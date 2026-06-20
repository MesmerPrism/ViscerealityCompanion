using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class PeripersonalQuestBackupPullerTests
{
    [Fact]
    public async Task AdbQuestBackupPuller_PullsTopLevelRunAsFilesIntoDeviceSessionPullFolder()
    {
        var root = CreateTempRoot();
        try
        {
            var sessionFolder = "P001_session-1_20260620-123456";
            var localSession = Path.Combine(root, sessionFolder);
            Directory.CreateDirectory(localSession);
            var runner = new FakeAdbProcessRunner(
                string.Join(
                    "\n",
                    [
                        $"files/runtime_csv/{sessionFolder}/session_settings.json",
                        $"files/runtime_csv/{sessionFolder}/session_events.csv",
                        $"files/runtime_csv/{sessionFolder}/nested/ignored.csv",
                        $"files/runtime_csv/{sessionFolder}/../unsafe.csv"
                    ]),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [$"files/runtime_csv/{sessionFolder}/session_settings.json"] = "{\"ok\":true}",
                    [$"files/runtime_csv/{sessionFolder}/session_events.csv"] = "participant_id,session_id"
                });
            var puller = new PeripersonalAdbQuestBackupPuller(
                "adb.exe",
                "QUEST123",
                TimeSpan.FromSeconds(1),
                runner);

            var result = await puller.PullSessionBackupAsync(new PeripersonalQuestBackupPullRequest(
                "com.Viscereality.ViscerealityPeriPersonal",
                sessionFolder,
                localSession,
                ExpectedFiles: ["session_settings.json", "session_events.csv"]));

            Assert.Equal(OperationOutcomeKind.Success, result.Outcome.Kind);
            Assert.Equal(Path.Combine(localSession, "device-session-pull"), result.LocalPullDirectory);
            Assert.Equal(["session_events.csv", "session_settings.json"], result.PulledFiles.Order(StringComparer.Ordinal).ToArray());
            Assert.True(File.Exists(Path.Combine(result.LocalPullDirectory, "session_settings.json")));
            Assert.True(File.Exists(Path.Combine(result.LocalPullDirectory, "session_events.csv")));
            Assert.Empty(result.MissingExpectedFiles);
            Assert.Empty(result.EmptyExpectedFiles);
            Assert.Contains(runner.TextInvocations, args => args.Contains("find"));
            Assert.Equal(2, runner.FileInvocations.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AdbQuestBackupPuller_WarnsWhenExpectedFilesAreMissing()
    {
        var root = CreateTempRoot();
        try
        {
            var sessionFolder = "P001_session-1_20260620-123456";
            var localSession = Path.Combine(root, sessionFolder);
            Directory.CreateDirectory(localSession);
            var runner = new FakeAdbProcessRunner(
                $"files/runtime_csv/{sessionFolder}/session_settings.json",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [$"files/runtime_csv/{sessionFolder}/session_settings.json"] = "{\"ok\":true}"
                });
            var puller = new PeripersonalAdbQuestBackupPuller(
                "adb.exe",
                "QUEST123",
                TimeSpan.FromSeconds(1),
                runner);

            var result = await puller.PullSessionBackupAsync(new PeripersonalQuestBackupPullRequest(
                "com.Viscereality.ViscerealityPeriPersonal",
                sessionFolder,
                localSession,
                ExpectedFiles: ["session_settings.json", "session_events.csv"]));

            Assert.Equal(OperationOutcomeKind.Warning, result.Outcome.Kind);
            Assert.Equal(["session_events.csv"], result.MissingExpectedFiles);
            Assert.Contains("Missing expected files", result.Outcome.Detail, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AdbQuestBackupPuller_PrefersExternalAppSpecificStorageWhenAvailable()
    {
        var root = CreateTempRoot();
        try
        {
            var packageId = "com.Viscereality.ViscerealityPeriPersonal";
            var sessionFolder = "P001_session-1_20260620-123456";
            var remoteSettingsPath = $"/sdcard/Android/data/{packageId}/files/runtime_csv/{sessionFolder}/session_settings.json";
            var localSession = Path.Combine(root, sessionFolder);
            Directory.CreateDirectory(localSession);
            var runner = new FakeAdbProcessRunner(
                remoteSettingsPath,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [remoteSettingsPath] = "{\"ok\":true}"
                });
            var puller = new PeripersonalAdbQuestBackupPuller(
                "adb.exe",
                "QUEST123",
                TimeSpan.FromSeconds(1),
                runner);

            var result = await puller.PullSessionBackupAsync(new PeripersonalQuestBackupPullRequest(
                packageId,
                sessionFolder,
                localSession,
                ExpectedFiles: ["session_settings.json"]));

            Assert.Equal(OperationOutcomeKind.Success, result.Outcome.Kind);
            Assert.Single(runner.TextInvocations);
            Assert.DoesNotContain("run-as", runner.TextInvocations[0]);
            Assert.Single(runner.FileInvocations);
            Assert.DoesNotContain("run-as", runner.FileInvocations[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ParseFindOutput_IgnoresNestedAndUnsafePaths()
    {
        var files = PeripersonalAdbQuestBackupPuller.ParseFindOutput(
            """
            files/runtime_csv/P001_session-1_20260620-123456/session_settings.json
            files/runtime_csv/P001_session-1_20260620-123456/nested/session_events.csv
            files/runtime_csv/P001_session-1_20260620-123456/../unsafe.csv
            other/session_events.csv
            """,
            "files/runtime_csv/P001_session-1_20260620-123456");

        var file = Assert.Single(files);
        Assert.Equal("session_settings.json", file.FileName);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakeAdbProcessRunner : IPeripersonalAdbProcessRunner
    {
        private readonly string _listOutput;
        private readonly IReadOnlyDictionary<string, string> _fileContents;

        public FakeAdbProcessRunner(
            string listOutput,
            IReadOnlyDictionary<string, string> fileContents)
        {
            _listOutput = listOutput;
            _fileContents = fileContents;
        }

        public List<IReadOnlyList<string>> TextInvocations { get; } = [];
        public List<IReadOnlyList<string>> FileInvocations { get; } = [];

        public Task<PeripersonalAdbProcessResult> RunTextAsync(
            string adbPath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            TextInvocations.Add(arguments.ToArray());
            return Task.FromResult(new PeripersonalAdbProcessResult(0, _listOutput, string.Empty));
        }

        public Task<PeripersonalAdbProcessResult> RunStdoutToFileAsync(
            string adbPath,
            IReadOnlyList<string> arguments,
            string outputPath,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            FileInvocations.Add(arguments.ToArray());
            var remotePath = arguments[^1];
            if (!_fileContents.TryGetValue(remotePath, out var contents))
            {
                return Task.FromResult(new PeripersonalAdbProcessResult(1, string.Empty, $"missing {remotePath}"));
            }

            File.WriteAllText(outputPath, contents);
            return Task.FromResult(new PeripersonalAdbProcessResult(0, string.Empty, string.Empty));
        }
    }
}
