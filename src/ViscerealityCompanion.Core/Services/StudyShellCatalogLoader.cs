using System.Text.Json;
using System.Text.Json.Serialization;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed class StudyShellCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<StudyShellCatalog> LoadAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var fullRoot = Path.GetFullPath(rootPath);
        var manifestPath = Path.Combine(fullRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Study shell manifest not found.", manifestPath);
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<StudyShellManifestDto>(manifestJson, JsonOptions)
            ?? throw new InvalidDataException("Could not deserialize study shell manifest.");

        var studies = new List<StudyShellDefinition>(manifest.Studies.Length);
        foreach (var item in manifest.Studies)
        {
            if (string.IsNullOrWhiteSpace(item.File))
            {
                continue;
            }

            var definitionPath = Path.Combine(fullRoot, item.File);
            if (!File.Exists(definitionPath))
            {
                throw new FileNotFoundException("Study shell definition not found.", definitionPath);
            }

            var definitionJson = await File.ReadAllTextAsync(definitionPath, cancellationToken).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<StudyShellDefinitionDto>(definitionJson, JsonOptions)
                ?? throw new InvalidDataException($"Could not deserialize study shell definition `{item.File}`.");

            var definitionDirectory = Path.GetDirectoryName(definitionPath) ?? fullRoot;
            studies.Add(new StudyShellDefinition(
                dto.Id ?? Path.GetFileNameWithoutExtension(item.File) ?? "study-shell",
                dto.Label ?? dto.Id ?? "Study Shell",
                dto.Partner ?? string.Empty,
                dto.Description ?? string.Empty,
                new StudyPinnedApp(
                    dto.App?.Label ?? "Study Runtime",
                    dto.App?.PackageId ?? string.Empty,
                    ResolveRelativePath(definitionDirectory, dto.App?.ApkPath),
                    dto.App?.LaunchComponent ?? string.Empty,
                    dto.App?.Sha256 ?? string.Empty,
                    dto.App?.VersionName ?? string.Empty,
                    dto.App?.Notes ?? string.Empty,
                    dto.App?.AllowManualSelection ?? true,
                    dto.App?.LaunchInKioskMode ?? false),
                new StudyPinnedDeviceProfile(
                    dto.DeviceProfile?.Id ?? "study-device-profile",
                    dto.DeviceProfile?.Label ?? "Study Device Profile",
                    dto.DeviceProfile?.Description ?? string.Empty,
                    new Dictionary<string, string>(dto.DeviceProfile?.Properties ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)),
                new StudyMonitoringProfile(
                    dto.Monitoring?.ExpectedBreathingLabel ?? string.Empty,
                    dto.Monitoring?.ExpectedHeartbeatLabel ?? string.Empty,
                    dto.Monitoring?.ExpectedCoherenceLabel ?? string.Empty,
                    dto.Monitoring?.ExpectedLslStreamName ?? string.Empty,
                    dto.Monitoring?.ExpectedLslStreamType ?? string.Empty,
                    dto.Monitoring?.RecenterDistanceThresholdUnits ?? 0.2d,
                    CloneList(dto.Monitoring?.LslConnectivityKeys),
                    CloneList(dto.Monitoring?.LslStreamNameKeys),
                    CloneList(dto.Monitoring?.LslStreamTypeKeys),
                    CloneList(dto.Monitoring?.LslValueKeys),
                    CloneList(dto.Monitoring?.ControllerValueKeys),
                    CloneList(dto.Monitoring?.ControllerStateKeys),
                    CloneList(dto.Monitoring?.ControllerTrackingKeys),
                    CloneList(dto.Monitoring?.HeartbeatValueKeys),
                    CloneList(dto.Monitoring?.HeartbeatStateKeys),
                    CloneList(dto.Monitoring?.CoherenceValueKeys),
                    CloneList(dto.Monitoring?.CoherenceStateKeys),
                    CloneList(dto.Monitoring?.PerformanceFpsKeys),
                    CloneList(dto.Monitoring?.PerformanceFrameTimeKeys),
                    CloneList(dto.Monitoring?.PerformanceTargetFpsKeys),
                    CloneList(dto.Monitoring?.PerformanceRefreshRateKeys),
                    CloneList(dto.Monitoring?.RecenterDistanceKeys),
                    CloneList(dto.Monitoring?.ParticleVisibilityKeys)),
                new StudyControlProfile(
                    dto.Controls?.RecenterCommandActionId ?? string.Empty,
                    dto.Controls?.ParticleVisibleOnActionId ?? string.Empty,
                    dto.Controls?.ParticleVisibleOffActionId ?? string.Empty)));
        }

        return new StudyShellCatalog(
            new StudyShellSource(manifest.Label ?? "Study shells", fullRoot),
            studies,
            new StudyShellLaunchOptions(
                manifest.StartupStudyId?.Trim() ?? string.Empty,
                manifest.LockToStartupStudy));
    }

    private static IReadOnlyList<string> CloneList(string[]? values)
        => values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray()
            ?? Array.Empty<string>();

    private static string ResolveRelativePath(string definitionDirectory, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(definitionDirectory, value));
    }

    private sealed class StudyShellManifestDto
    {
        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("startupStudyId")]
        public string? StartupStudyId { get; init; }

        [JsonPropertyName("lockToStartupStudy")]
        public bool LockToStartupStudy { get; init; }

        [JsonPropertyName("studies")]
        public StudyShellItemDto[] Studies { get; init; } = Array.Empty<StudyShellItemDto>();
    }

    private sealed class StudyShellItemDto
    {
        [JsonPropertyName("file")]
        public string? File { get; init; }
    }

    private sealed class StudyShellDefinitionDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("partner")]
        public string? Partner { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("app")]
        public StudyPinnedAppDto? App { get; init; }

        [JsonPropertyName("deviceProfile")]
        public StudyPinnedDeviceProfileDto? DeviceProfile { get; init; }

        [JsonPropertyName("monitoring")]
        public StudyMonitoringProfileDto? Monitoring { get; init; }

        [JsonPropertyName("controls")]
        public StudyControlProfileDto? Controls { get; init; }
    }

    private sealed class StudyPinnedAppDto
    {
        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("packageId")]
        public string? PackageId { get; init; }

        [JsonPropertyName("apkPath")]
        public string? ApkPath { get; init; }

        [JsonPropertyName("launchComponent")]
        public string? LaunchComponent { get; init; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; init; }

        [JsonPropertyName("versionName")]
        public string? VersionName { get; init; }

        [JsonPropertyName("notes")]
        public string? Notes { get; init; }

        [JsonPropertyName("allowManualSelection")]
        public bool? AllowManualSelection { get; init; }

        [JsonPropertyName("launchInKioskMode")]
        public bool? LaunchInKioskMode { get; init; }
    }

    private sealed class StudyPinnedDeviceProfileDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("properties")]
        public Dictionary<string, string>? Properties { get; init; }
    }

    private sealed class StudyMonitoringProfileDto
    {
        [JsonPropertyName("expectedBreathingLabel")]
        public string? ExpectedBreathingLabel { get; init; }

        [JsonPropertyName("expectedHeartbeatLabel")]
        public string? ExpectedHeartbeatLabel { get; init; }

        [JsonPropertyName("expectedCoherenceLabel")]
        public string? ExpectedCoherenceLabel { get; init; }

        [JsonPropertyName("expectedLslStreamName")]
        public string? ExpectedLslStreamName { get; init; }

        [JsonPropertyName("expectedLslStreamType")]
        public string? ExpectedLslStreamType { get; init; }

        [JsonPropertyName("recenterDistanceThresholdUnits")]
        public double? RecenterDistanceThresholdUnits { get; init; }

        [JsonPropertyName("lslConnectivityKeys")]
        public string[]? LslConnectivityKeys { get; init; }

        [JsonPropertyName("lslStreamNameKeys")]
        public string[]? LslStreamNameKeys { get; init; }

        [JsonPropertyName("lslStreamTypeKeys")]
        public string[]? LslStreamTypeKeys { get; init; }

        [JsonPropertyName("lslValueKeys")]
        public string[]? LslValueKeys { get; init; }

        [JsonPropertyName("controllerValueKeys")]
        public string[]? ControllerValueKeys { get; init; }

        [JsonPropertyName("controllerStateKeys")]
        public string[]? ControllerStateKeys { get; init; }

        [JsonPropertyName("controllerTrackingKeys")]
        public string[]? ControllerTrackingKeys { get; init; }

        [JsonPropertyName("heartbeatValueKeys")]
        public string[]? HeartbeatValueKeys { get; init; }

        [JsonPropertyName("heartbeatStateKeys")]
        public string[]? HeartbeatStateKeys { get; init; }

        [JsonPropertyName("coherenceValueKeys")]
        public string[]? CoherenceValueKeys { get; init; }

        [JsonPropertyName("coherenceStateKeys")]
        public string[]? CoherenceStateKeys { get; init; }

        [JsonPropertyName("performanceFpsKeys")]
        public string[]? PerformanceFpsKeys { get; init; }

        [JsonPropertyName("performanceFrameTimeKeys")]
        public string[]? PerformanceFrameTimeKeys { get; init; }

        [JsonPropertyName("performanceTargetFpsKeys")]
        public string[]? PerformanceTargetFpsKeys { get; init; }

        [JsonPropertyName("performanceRefreshRateKeys")]
        public string[]? PerformanceRefreshRateKeys { get; init; }

        [JsonPropertyName("recenterDistanceKeys")]
        public string[]? RecenterDistanceKeys { get; init; }

        [JsonPropertyName("particleVisibilityKeys")]
        public string[]? ParticleVisibilityKeys { get; init; }
    }

    private sealed class StudyControlProfileDto
    {
        [JsonPropertyName("recenterCommandActionId")]
        public string? RecenterCommandActionId { get; init; }

        [JsonPropertyName("particleVisibleOnActionId")]
        public string? ParticleVisibleOnActionId { get; init; }

        [JsonPropertyName("particleVisibleOffActionId")]
        public string? ParticleVisibleOffActionId { get; init; }
    }
}
