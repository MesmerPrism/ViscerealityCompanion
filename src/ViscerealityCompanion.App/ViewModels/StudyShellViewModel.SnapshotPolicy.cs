using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViscerealityCompanion.App;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.App.ViewModels;

public sealed partial class StudyShellViewModel
{
    private static bool HasFreshCommittedTwinSnapshot(
        TwinSnapshotGate gate,
        DateTimeOffset commandIssuedAtUtc,
        string? currentRevision,
        DateTimeOffset? currentCommittedAtUtc)
    {
        if (currentCommittedAtUtc is null)
        {
            return false;
        }

        if (gate.CommittedAtUtc is null)
        {
            return currentCommittedAtUtc.Value >= commandIssuedAtUtc;
        }

        return currentCommittedAtUtc.Value > gate.CommittedAtUtc.Value ||
               !string.Equals(currentRevision, gate.Revision, StringComparison.Ordinal);
    }

    internal static bool HasReportedStudyRuntimeConfigJson(IReadOnlyDictionary<string, string>? reportedTwinState)
    {
        if (reportedTwinState is null || reportedTwinState.Count == 0)
        {
            return false;
        }

        return reportedTwinState.TryGetValue("showcase_active_runtime_config_json", out var directRuntimeConfigJson) &&
               !string.IsNullOrWhiteSpace(directRuntimeConfigJson) ||
               reportedTwinState.TryGetValue("hotload.showcase_active_runtime_config_json", out var hotloadRuntimeConfigJson) &&
               !string.IsNullOrWhiteSpace(hotloadRuntimeConfigJson);
    }

    internal static bool HasReportedParticipantSessionRuntimeConfig(
        IReadOnlyDictionary<string, string>? reportedTwinState,
        string expectedSessionId,
        string expectedDatasetHash)
    {
        if (string.IsNullOrWhiteSpace(expectedSessionId) || string.IsNullOrWhiteSpace(expectedDatasetHash))
        {
            return false;
        }

        if (HasReportedParticipantSessionMetadataFields(
                reportedTwinState,
                expectedSessionId,
                expectedDatasetHash))
        {
            return true;
        }

        foreach (var runtimeConfigJson in EnumerateReportedStudyRuntimeConfigJson(reportedTwinState))
        {
            if (TryRuntimeConfigContainsSessionMetadata(runtimeConfigJson, expectedSessionId, expectedDatasetHash))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasFreshRuntimeConfigTwinBaseline(
        string? previousRevision,
        DateTimeOffset? previousCommittedAtUtc,
        DateTimeOffset commandIssuedAtUtc,
        string? currentRevision,
        DateTimeOffset? currentCommittedAtUtc,
        IReadOnlyDictionary<string, string>? reportedTwinState)
        => HasFreshCommittedTwinSnapshot(
               new TwinSnapshotGate(previousRevision, previousCommittedAtUtc),
               commandIssuedAtUtc,
               currentRevision,
               currentCommittedAtUtc) &&
           HasReportedStudyRuntimeConfigJson(reportedTwinState);

    internal static bool HasFreshParticipantSessionRuntimeConfigBaseline(
        string? previousRevision,
        DateTimeOffset? previousCommittedAtUtc,
        DateTimeOffset commandIssuedAtUtc,
        string? currentRevision,
        DateTimeOffset? currentCommittedAtUtc,
        IReadOnlyDictionary<string, string>? reportedTwinState,
        string expectedSessionId,
        string expectedDatasetHash)
        => HasFreshCommittedTwinSnapshot(
               new TwinSnapshotGate(previousRevision, previousCommittedAtUtc),
               commandIssuedAtUtc,
               currentRevision,
               currentCommittedAtUtc) &&
           HasReportedParticipantSessionRuntimeConfig(
               reportedTwinState,
               expectedSessionId,
               expectedDatasetHash);

    internal static AppSessionState NormalizeStartupSessionState(
        AppSessionState sessionState,
        out bool clearedPersistedRegularSnapshots)
    {
        ArgumentNullException.ThrowIfNull(sessionState);

        if (!sessionState.RegularAdbSnapshotEnabled)
        {
            clearedPersistedRegularSnapshots = false;
            return sessionState;
        }

        // Regular snapshot polling is a bench-only debug mode. Auto-restoring it can
        // trap the locked startup study shell in a failing launch loop before the
        // operator can reach the toggle again, so require an explicit opt-in each run.
        clearedPersistedRegularSnapshots = true;
        return sessionState.WithRegularAdbSnapshotEnabled(false);
    }

    internal static bool ShouldRefreshInstalledAppStatusForSnapshot(
        bool forceRefresh,
        InstalledAppStatus? currentStatus,
        string? currentStagedApkPath,
        string? lastQueriedStagedApkPath)
    {
        if (forceRefresh || currentStatus is null)
        {
            return true;
        }

        var normalizedCurrentPath = NormalizeInstalledAppStatusStagedApkPath(currentStagedApkPath);
        var normalizedLastPath = NormalizeInstalledAppStatusStagedApkPath(lastQueriedStagedApkPath);
        return !string.Equals(normalizedCurrentPath, normalizedLastPath, StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeInstalledAppStatusStagedApkPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    private static bool HasReportedParticipantSessionMetadataFields(
        IReadOnlyDictionary<string, string>? reportedTwinState,
        string expectedSessionId,
        string expectedDatasetHash)
    {
        if (reportedTwinState is null || reportedTwinState.Count == 0)
        {
            return false;
        }

        return KeysMatch(
            reportedTwinState,
            expectedSessionId,
            expectedDatasetHash,
            "hotload.study_session_id",
            "hotload.study_session_dataset_hash") ||
               KeysMatch(
                   reportedTwinState,
                   expectedSessionId,
                   expectedDatasetHash,
                   "study.session.id",
                   "study.session.dataset_hash") ||
               KeysMatch(
                   reportedTwinState,
                   expectedSessionId,
                   expectedDatasetHash,
                   "study_session_id",
                   "study_session_dataset_hash");
    }

    private static bool KeysMatch(
        IReadOnlyDictionary<string, string> reportedTwinState,
        string expectedSessionId,
        string expectedDatasetHash,
        string sessionIdKey,
        string datasetHashKey)
        => reportedTwinState.TryGetValue(sessionIdKey, out var sessionId) &&
           reportedTwinState.TryGetValue(datasetHashKey, out var datasetHash) &&
           string.Equals(sessionId, expectedSessionId, StringComparison.Ordinal) &&
           string.Equals(datasetHash, expectedDatasetHash, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetReportedStudyRuntimeConfigJson(
        IReadOnlyDictionary<string, string>? reportedTwinState,
        out string runtimeConfigJson)
    {
        foreach (var candidate in EnumerateReportedStudyRuntimeConfigJson(reportedTwinState))
        {
            runtimeConfigJson = candidate;
            return true;
        }

        runtimeConfigJson = string.Empty;
        return false;
    }

    private static IEnumerable<string> EnumerateReportedStudyRuntimeConfigJson(IReadOnlyDictionary<string, string>? reportedTwinState)
    {
        if (reportedTwinState is null || reportedTwinState.Count == 0)
        {
            yield break;
        }

        if (reportedTwinState.TryGetValue("showcase_active_runtime_config_json", out var directRuntimeConfigJson) &&
            !string.IsNullOrWhiteSpace(directRuntimeConfigJson))
        {
            yield return directRuntimeConfigJson;
        }

        if (reportedTwinState.TryGetValue("hotload.showcase_active_runtime_config_json", out var hotloadRuntimeConfigJson) &&
            !string.IsNullOrWhiteSpace(hotloadRuntimeConfigJson))
        {
            yield return hotloadRuntimeConfigJson;
        }
    }

    private static bool TryRuntimeConfigContainsSessionMetadata(
        string runtimeConfigJson,
        string expectedSessionId,
        string expectedDatasetHash)
    {
        try
        {
            var root = JsonNode.Parse(runtimeConfigJson);
            if (root is null)
            {
                return false;
            }

            var sessionIdFound = false;
            var datasetHashFound = false;
            foreach (var node in EnumerateJsonNodes(root))
            {
                if (node is not JsonValue value ||
                    !value.TryGetValue<string>(out var stringValue) ||
                    string.IsNullOrWhiteSpace(stringValue))
                {
                    continue;
                }

                if (!sessionIdFound &&
                    string.Equals(stringValue, expectedSessionId, StringComparison.Ordinal))
                {
                    sessionIdFound = true;
                }

                if (!datasetHashFound &&
                    string.Equals(stringValue, expectedDatasetHash, StringComparison.OrdinalIgnoreCase))
                {
                    datasetHashFound = true;
                }

                if (sessionIdFound && datasetHashFound)
                {
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return runtimeConfigJson.Contains(expectedSessionId, StringComparison.Ordinal) &&
                   runtimeConfigJson.Contains(expectedDatasetHash, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<JsonNode> EnumerateJsonNodes(JsonNode node)
    {
        yield return node;

        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                if (property.Value is null)
                {
                    continue;
                }

                foreach (var child in EnumerateJsonNodes(property.Value))
                {
                    yield return child;
                }
            }

            yield break;
        }

        if (node is not JsonArray array)
        {
            yield break;
        }

        foreach (var childNode in array)
        {
            if (childNode is null)
            {
                continue;
            }

            foreach (var child in EnumerateJsonNodes(childNode))
            {
                yield return child;
            }
        }
    }
}
