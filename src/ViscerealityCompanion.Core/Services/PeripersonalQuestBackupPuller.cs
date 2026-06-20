using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public interface IPeripersonalQuestBackupPuller
{
    Task<PeripersonalQuestBackupPullResult> PullSessionBackupAsync(
        PeripersonalQuestBackupPullRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record PeripersonalQuestBackupPullRequest(
    string UnityPackage,
    string SessionFolderName,
    string WindowsSessionDirectory,
    string RemoteRootRelativePath = PeripersonalAdbQuestBackupPuller.DefaultRemoteRootRelativePath,
    string WindowsPullSubfolder = PeripersonalAdbQuestBackupPuller.DefaultWindowsPullSubfolder,
    IReadOnlyList<string>? ExpectedFiles = null);

public sealed record PeripersonalQuestBackupPullResult(
    OperationOutcome Outcome,
    string LocalPullDirectory,
    IReadOnlyList<string> PulledFiles,
    IReadOnlyList<string> MissingExpectedFiles,
    IReadOnlyList<string> EmptyExpectedFiles);

public sealed class NoOpPeripersonalQuestBackupPuller : IPeripersonalQuestBackupPuller
{
    public static NoOpPeripersonalQuestBackupPuller Instance { get; } = new();

    private NoOpPeripersonalQuestBackupPuller()
    {
    }

    public Task<PeripersonalQuestBackupPullResult> PullSessionBackupAsync(
        PeripersonalQuestBackupPullRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new PeripersonalQuestBackupPullResult(
            new OperationOutcome(
                OperationOutcomeKind.Success,
                "Quest backup pullback skipped.",
                "No peripersonal Quest backup puller is configured for this workflow service instance."),
            string.Empty,
            [],
            [],
            []));
}

public sealed class PeripersonalAdbQuestBackupPuller : IPeripersonalQuestBackupPuller
{
    public const string DefaultRemoteRootRelativePath = "files/runtime_csv";
    public const string DefaultWindowsPullSubfolder = "device-session-pull";

    private static readonly Regex SafeSessionFolderPattern = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SafeFileNamePattern = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    private static readonly string[] DefaultExpectedFiles =
    [
        "session_settings.json",
        "session_snapshot.json",
        "session_schema.json",
        "legacy_outputs_manifest.json",
        "session_events.csv",
        "signals_long.csv",
        "breathing_trace.csv",
        "runtime_state_samples.csv",
        "clock_alignment_samples.csv",
        "timing_markers.csv",
        "questionnaire_results.jsonl"
    ];

    private static readonly HashSet<string> RequiredNonEmptyFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "session_settings.json",
        "session_snapshot.json",
        "session_schema.json",
        "legacy_outputs_manifest.json",
        "session_events.csv",
        "runtime_state_samples.csv",
        "timing_markers.csv"
    };

    private readonly string _adbPath;
    private readonly string _selector;
    private readonly TimeSpan _timeout;
    private readonly IPeripersonalAdbProcessRunner _processRunner;

    public PeripersonalAdbQuestBackupPuller(
        string adbPath,
        string selector,
        TimeSpan? timeout = null)
        : this(adbPath, selector, timeout, new PeripersonalAdbProcessRunner())
    {
    }

    internal PeripersonalAdbQuestBackupPuller(
        string adbPath,
        string selector,
        TimeSpan? timeout,
        IPeripersonalAdbProcessRunner processRunner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        _adbPath = adbPath;
        _selector = selector;
        _timeout = timeout ?? DefaultTimeout;
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public static IReadOnlyList<string> ExpectedSessionFiles => DefaultExpectedFiles;

    public async Task<PeripersonalQuestBackupPullResult> PullSessionBackupAsync(
        PeripersonalQuestBackupPullRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Directory.Exists(request.WindowsSessionDirectory))
        {
            return WarningResult(
                "Windows session folder is missing.",
                $"Cannot pull the Quest backup because the Windows session folder does not exist: {request.WindowsSessionDirectory}.",
                string.Empty,
                [],
                ExpectedFiles(request),
                []);
        }

        if (!IsSafeSessionFolderName(request.SessionFolderName))
        {
            return WarningResult(
                "Peripersonal session folder name is unsafe for run-as pullback.",
                $"Session folder `{request.SessionFolderName}` does not match the strict participant_session_timestamp convention.",
                string.Empty,
                [],
                ExpectedFiles(request),
                []);
        }

        var localPullDirectory = Path.Combine(request.WindowsSessionDirectory, request.WindowsPullSubfolder);
        Directory.CreateDirectory(localPullDirectory);

        var listFailures = new List<string>();
        PeripersonalQuestBackupRemoteRoot? selectedRoot = null;
        var remoteSessionDirectory = string.Empty;
        IReadOnlyList<PeripersonalQuestBackupRemoteFile> remoteFiles = Array.Empty<PeripersonalQuestBackupRemoteFile>();
        foreach (var candidateRoot in BuildRemoteRootCandidates(request))
        {
            remoteSessionDirectory = $"{candidateRoot.RootPath}/{request.SessionFolderName}";
            PeripersonalAdbProcessResult listResult;
            try
            {
                listResult = await _processRunner.RunTextAsync(
                        _adbPath,
                        BuildFindArguments(candidateRoot, request.UnityPackage, remoteSessionDirectory),
                        _timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                listFailures.Add($"{candidateRoot.Description}: listing exceeded {_timeout.TotalSeconds:0} seconds.");
                continue;
            }
            catch (Exception ex)
            {
                listFailures.Add($"{candidateRoot.Description}: {ex.Message}");
                continue;
            }

            if (listResult.ExitCode != 0)
            {
                listFailures.Add($"{candidateRoot.Description}: {listResult.CombinedOutput}");
                continue;
            }

            remoteFiles = ParseFindOutput(listResult.StdOut, remoteSessionDirectory);
            if (remoteFiles.Count > 0)
            {
                selectedRoot = candidateRoot;
                break;
            }

            listFailures.Add($"{candidateRoot.Description}: no files listed under {remoteSessionDirectory}.");
        }

        if (selectedRoot is null)
        {
            TryDeleteEmptyDirectory(localPullDirectory);
            return WarningResult(
                "Quest backup listing failed.",
                $"No pullable Quest backup folder was found. {string.Join(" ", listFailures)}",
                localPullDirectory,
                [],
                ExpectedFiles(request),
                []);
        }

        if (remoteFiles.Count == 0)
        {
            TryDeleteEmptyDirectory(localPullDirectory);
            return WarningResult(
                "Quest backup folder is empty.",
                $"No files were listed under {remoteSessionDirectory}.",
                localPullDirectory,
                [],
                ExpectedFiles(request),
                []);
        }

        var pulledFiles = new List<string>();
        var failures = new List<string>();
        foreach (var remoteFile in remoteFiles.OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase))
        {
            var localPath = Path.Combine(localPullDirectory, remoteFile.FileName);
            PeripersonalAdbProcessResult pullResult;
            try
            {
                pullResult = await _processRunner.RunStdoutToFileAsync(
                        _adbPath,
                        BuildCatArguments(selectedRoot, request.UnityPackage, remoteFile.RemotePath),
                        localPath,
                        _timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                failures.Add($"{remoteFile.FileName}: ADB run-as cat exceeded {_timeout.TotalSeconds:0} seconds.");
                break;
            }
            catch (Exception ex)
            {
                failures.Add($"{remoteFile.FileName}: {ex.Message}");
                continue;
            }

            if (pullResult.ExitCode == 0 && File.Exists(localPath))
            {
                pulledFiles.Add(remoteFile.FileName);
            }
            else
            {
                TryDeleteFile(localPath);
                failures.Add($"{remoteFile.FileName}: {pullResult.CombinedOutput}");
            }
        }

        var expectedFiles = ExpectedFiles(request);
        var missingExpected = expectedFiles
            .Where(file => !File.Exists(Path.Combine(localPullDirectory, file)))
            .ToArray();
        var emptyExpected = expectedFiles
            .Where(IsRequiredNonEmptyFile)
            .Where(file =>
            {
                var path = Path.Combine(localPullDirectory, file);
                return File.Exists(path) && new FileInfo(path).Length == 0;
            })
            .ToArray();

        if (pulledFiles.Count == 0)
        {
            TryDeleteEmptyDirectory(localPullDirectory);
            return WarningResult(
                "Quest backup files were not pulled.",
                BuildDetail(remoteSessionDirectory, localPullDirectory, pulledFiles, missingExpected, emptyExpected, failures),
                localPullDirectory,
                pulledFiles,
                missingExpected,
                emptyExpected);
        }

        var hasGaps = failures.Count > 0 || missingExpected.Length > 0 || emptyExpected.Length > 0;
        return new PeripersonalQuestBackupPullResult(
            new OperationOutcome(
                hasGaps ? OperationOutcomeKind.Warning : OperationOutcomeKind.Success,
                hasGaps ? "Quest backup pulled with gaps." : "Quest backup files pulled.",
                BuildDetail(remoteSessionDirectory, localPullDirectory, pulledFiles, missingExpected, emptyExpected, failures),
                Items: [localPullDirectory]),
            localPullDirectory,
            pulledFiles,
            missingExpected,
            emptyExpected);
    }

    internal static IReadOnlyList<PeripersonalQuestBackupRemoteFile> ParseFindOutput(
        string output,
        string remoteSessionDirectory)
    {
        var normalizedDirectory = NormalizeRemotePath(remoteSessionDirectory).TrimEnd('/');
        var files = new Dictionary<string, PeripersonalQuestBackupRemoteFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in (output ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var remotePath = NormalizeRemotePath(rawLine.Trim());
            if (string.IsNullOrWhiteSpace(remotePath) ||
                !remotePath.StartsWith($"{normalizedDirectory}/", StringComparison.Ordinal))
            {
                continue;
            }

            var fileName = remotePath[(normalizedDirectory.Length + 1)..];
            if (fileName.Contains('/', StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsSafeFileName(fileName))
            {
                continue;
            }

            files[fileName] = new PeripersonalQuestBackupRemoteFile(fileName, remotePath);
        }

        return files.Values.ToArray();
    }

    internal static IReadOnlyList<PeripersonalQuestBackupRemoteRoot> BuildRemoteRootCandidates(
        PeripersonalQuestBackupPullRequest request)
    {
        var requestedRoot = NormalizeRemoteRoot(request.RemoteRootRelativePath);
        var candidates = new List<PeripersonalQuestBackupRemoteRoot>();
        if (requestedRoot.StartsWith("/", StringComparison.Ordinal))
        {
            candidates.Add(new PeripersonalQuestBackupRemoteRoot(
                requestedRoot,
                UseRunAs: false,
                "external app-specific storage"));
        }
        else
        {
            var externalRelative = requestedRoot.StartsWith("files/", StringComparison.Ordinal)
                ? requestedRoot["files/".Length..]
                : requestedRoot;
            candidates.Add(new PeripersonalQuestBackupRemoteRoot(
                $"/sdcard/Android/data/{request.UnityPackage}/files/{externalRelative}",
                UseRunAs: false,
                "external app-specific storage"));
            candidates.Add(new PeripersonalQuestBackupRemoteRoot(
                requestedRoot,
                UseRunAs: true,
                "debuggable app-private run-as storage"));
        }

        return candidates
            .GroupBy(candidate => $"{candidate.UseRunAs}:{candidate.RootPath}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private IReadOnlyList<string> BuildFindArguments(
        PeripersonalQuestBackupRemoteRoot root,
        string unityPackage,
        string remoteSessionDirectory)
        => root.UseRunAs
            ? [
                "-s",
                _selector,
                "shell",
                "run-as",
                unityPackage,
                "find",
                remoteSessionDirectory,
                "-maxdepth",
                "1",
                "-type",
                "f",
                "-print"
            ]
            : [
                "-s",
                _selector,
                "shell",
                "find",
                remoteSessionDirectory,
                "-maxdepth",
                "1",
                "-type",
                "f",
                "-print"
            ];

    private IReadOnlyList<string> BuildCatArguments(
        PeripersonalQuestBackupRemoteRoot root,
        string unityPackage,
        string remoteFilePath)
        => root.UseRunAs
            ? [
                "-s",
                _selector,
                "exec-out",
                "run-as",
                unityPackage,
                "cat",
                remoteFilePath
            ]
            : [
                "-s",
                _selector,
                "exec-out",
                "cat",
                remoteFilePath
            ];

    private static IReadOnlyList<string> ExpectedFiles(PeripersonalQuestBackupPullRequest request)
        => request.ExpectedFiles is { Count: > 0 }
            ? request.ExpectedFiles
                .Where(IsSafeFileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : ExpectedSessionFiles;

    private static string NormalizeRemoteRoot(string value)
    {
        var normalized = NormalizeRemotePath(value);
        normalized = normalized.Trim('/');
        return string.IsNullOrWhiteSpace(normalized) ? DefaultRemoteRootRelativePath : normalized;
    }

    private static string NormalizeRemotePath(string value)
        => (value ?? string.Empty).Replace('\\', '/').Trim().Trim('"', '\'');

    private static bool IsSafeSessionFolderName(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           SafeSessionFolderPattern.IsMatch(value) &&
           !value.Contains("..", StringComparison.Ordinal);

    private static bool IsSafeFileName(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           SafeFileNamePattern.IsMatch(value) &&
           !value.Contains("..", StringComparison.Ordinal);

    private static bool IsRequiredNonEmptyFile(string fileName)
        => RequiredNonEmptyFiles.Contains(fileName);

    private static string BuildDetail(
        string remoteSessionDirectory,
        string localPullDirectory,
        IReadOnlyList<string> pulledFiles,
        IReadOnlyList<string> missingExpected,
        IReadOnlyList<string> emptyExpected,
        IReadOnlyList<string> failures)
    {
        var builder = new StringBuilder();
        builder.Append($"Quest source folder: {remoteSessionDirectory}. ");
        builder.Append($"Pulled {pulledFiles.Count} file(s) into {localPullDirectory}.");
        if (pulledFiles.Count > 0)
        {
            builder.Append($" Files: {string.Join(", ", pulledFiles)}.");
        }

        if (missingExpected.Count > 0)
        {
            builder.Append($" Missing expected files: {string.Join(", ", missingExpected)}.");
        }

        if (emptyExpected.Count > 0)
        {
            builder.Append($" Empty required files: {string.Join(", ", emptyExpected)}.");
        }

        if (failures.Count > 0)
        {
            builder.Append($" Pullback gaps: {string.Join(" ", failures.Where(static failure => !string.IsNullOrWhiteSpace(failure)))}");
        }

        return builder.ToString();
    }

    private static PeripersonalQuestBackupPullResult WarningResult(
        string summary,
        string detail,
        string localPullDirectory,
        IReadOnlyList<string> pulledFiles,
        IReadOnlyList<string> missingExpectedFiles,
        IReadOnlyList<string> emptyExpectedFiles)
        => new(
            new OperationOutcome(
                OperationOutcomeKind.Warning,
                summary,
                detail,
                Items: string.IsNullOrWhiteSpace(localPullDirectory) ? [] : [localPullDirectory]),
            localPullDirectory,
            pulledFiles,
            missingExpectedFiles,
            emptyExpectedFiles);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteEmptyDirectory(string folderPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(folderPath) &&
                Directory.Exists(folderPath) &&
                !Directory.EnumerateFileSystemEntries(folderPath).Any())
            {
                Directory.Delete(folderPath);
            }
        }
        catch
        {
        }
    }
}

internal sealed record PeripersonalQuestBackupRemoteFile(string FileName, string RemotePath);

internal sealed record PeripersonalQuestBackupRemoteRoot(
    string RootPath,
    bool UseRunAs,
    string Description);

internal interface IPeripersonalAdbProcessRunner
{
    Task<PeripersonalAdbProcessResult> RunTextAsync(
        string adbPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<PeripersonalAdbProcessResult> RunStdoutToFileAsync(
        string adbPath,
        IReadOnlyList<string> arguments,
        string outputPath,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed record PeripersonalAdbProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[] { StdOut, StdErr }.Where(static value => !string.IsNullOrWhiteSpace(value)));
}

internal sealed class PeripersonalAdbProcessRunner : IPeripersonalAdbProcessRunner
{
    public async Task<PeripersonalAdbProcessResult> RunTextAsync(
        string adbPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var process = CreateProcess(adbPath, arguments);
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return new PeripersonalAdbProcessResult(
                process.ExitCode,
                await stdoutTask.ConfigureAwait(false),
                await stderrTask.ConfigureAwait(false));
        }
        catch
        {
            TryKillProcessTree(process);
            throw;
        }
    }

    public async Task<PeripersonalAdbProcessResult> RunStdoutToFileAsync(
        string adbPath,
        IReadOnlyList<string> arguments,
        string outputPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var process = CreateProcess(adbPath, arguments);
        process.Start();
        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(output, timeoutSource.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            await copyTask.ConfigureAwait(false);
            return new PeripersonalAdbProcessResult(
                process.ExitCode,
                string.Empty,
                await stderrTask.ConfigureAwait(false));
        }
        catch
        {
            TryKillProcessTree(process);
            throw;
        }
    }

    private static Process CreateProcess(
        string adbPath,
        IReadOnlyList<string> arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = adbPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        return process;
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}
