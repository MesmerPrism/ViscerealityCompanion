using System.IO;
using System.Text.Json;

namespace ViscerealityCompanion.App;

internal sealed record AppSessionState(
    string? ActiveEndpoint,
    string? LastUsbSerial,
    string? LastProximitySelector = null,
    bool? LastProximityExpectedEnabled = null,
    DateTimeOffset? LastProximityDisableUntilUtc = null,
    DateTimeOffset? LastProximityUpdatedAtUtc = null,
    bool RegularAdbSnapshotEnabled = false)
{
    private static readonly string SessionDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ViscerealityCompanion",
        "session");

    private static readonly string StatePath = Path.Combine(SessionDirectory, "app-state.json");
    private static readonly string CliFallbackPath = Path.Combine(SessionDirectory, "cli-state.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSessionState Load()
        => TryLoad(StatePath)
            ?? TryLoad(CliFallbackPath)
            ?? new AppSessionState(null, null);

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SessionDirectory);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(StatePath, json);
        }
        catch
        {
            // Best-effort persistence only.
        }
    }

    public AppSessionState WithEndpoint(string? endpoint)
        => string.IsNullOrWhiteSpace(endpoint)
            ? this
            : this with { ActiveEndpoint = endpoint };

    public AppSessionState WithUsbSerial(string? serial)
        => string.IsNullOrWhiteSpace(serial)
            ? this
            : this with { LastUsbSerial = serial };

    public AppSessionState WithTrackedProximity(string? selector, bool expectedEnabled, DateTimeOffset? disableUntilUtc)
        => string.IsNullOrWhiteSpace(selector)
            ? this
            : this with
            {
                LastProximitySelector = selector,
                LastProximityExpectedEnabled = expectedEnabled,
                LastProximityDisableUntilUtc = disableUntilUtc?.ToUniversalTime(),
                LastProximityUpdatedAtUtc = DateTimeOffset.UtcNow
            };

    public TrackedQuestProximityState GetTrackedProximity(string? selector, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(selector) ||
            string.IsNullOrWhiteSpace(LastProximitySelector) ||
            !string.Equals(selector, LastProximitySelector, StringComparison.OrdinalIgnoreCase) ||
            LastProximityExpectedEnabled is null)
        {
            return default;
        }

        var currentTime = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var disableUntilUtc = LastProximityDisableUntilUtc?.ToUniversalTime();
        if (LastProximityExpectedEnabled == false &&
            disableUntilUtc.HasValue &&
            disableUntilUtc.Value <= currentTime)
        {
            return new TrackedQuestProximityState(
                Known: true,
                ExpectedEnabled: true,
                DisableUntilUtc: null,
                UpdatedAtUtc: LastProximityUpdatedAtUtc,
                DisableWindowExpired: true);
        }

        return new TrackedQuestProximityState(
            Known: true,
            ExpectedEnabled: LastProximityExpectedEnabled.Value,
            DisableUntilUtc: disableUntilUtc,
            UpdatedAtUtc: LastProximityUpdatedAtUtc,
            DisableWindowExpired: false);
    }

    public AppSessionState WithRegularAdbSnapshotEnabled(bool enabled)
        => this with { RegularAdbSnapshotEnabled = enabled };

    private static AppSessionState? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSessionState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

internal readonly record struct TrackedQuestProximityState(
    bool Known,
    bool ExpectedEnabled,
    DateTimeOffset? DisableUntilUtc,
    DateTimeOffset? UpdatedAtUtc,
    bool DisableWindowExpired);
