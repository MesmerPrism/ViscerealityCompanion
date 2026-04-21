using System.Globalization;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed record SussexExpectedUpstreamState(
    string ExpectedStreamName,
    string ExpectedStreamType,
    bool DiscoveryAvailable,
    bool ProbeFailed,
    bool VisibleOnWindows,
    bool VisibleViaCompanionTestSender,
    int VisibleMatchCount,
    IReadOnlyList<LslVisibleStreamInfo> VisibleMatches,
    string Summary,
    string Detail);

public sealed record SussexConnectionAssessmentInput(
    bool PinnedBuildReady,
    bool DeviceProfileReady,
    bool HeadsetForeground,
    bool WifiTransportReady,
    bool InletReady,
    bool ReturnPathReady,
    bool AnyTwinStatePublisherVisible,
    bool ExpectedTwinStatePublisherVisible,
    SussexExpectedUpstreamState ExpectedUpstream,
    bool HeadsetAsleep = false,
    bool HeadsetInWakeLimbo = false);

public sealed record SussexConnectionAssessmentResult(
    OperationOutcomeKind Level,
    string Summary,
    IReadOnlyList<string> MissingLinks,
    string FocusNext);

public static class SussexConnectionAssessmentService
{
    public static SussexExpectedUpstreamState InspectExpectedUpstream(
        ILslStreamDiscoveryService streamDiscoveryService,
        string expectedStreamName,
        string expectedStreamType,
        string companionTestSourceId)
    {
        ArgumentNullException.ThrowIfNull(streamDiscoveryService);

        var streamName = string.IsNullOrWhiteSpace(expectedStreamName)
            ? HrvBiofeedbackStreamContract.StreamName
            : expectedStreamName.Trim();
        var streamType = string.IsNullOrWhiteSpace(expectedStreamType)
            ? HrvBiofeedbackStreamContract.StreamType
            : expectedStreamType.Trim();

        if (!streamDiscoveryService.RuntimeState.Available)
        {
            return new SussexExpectedUpstreamState(
                streamName,
                streamType,
                DiscoveryAvailable: false,
                ProbeFailed: false,
                VisibleOnWindows: false,
                VisibleViaCompanionTestSender: false,
                VisibleMatchCount: 0,
                VisibleMatches: [],
                Summary: $"{streamName} / {streamType} could not be probed on Windows.",
                Detail: streamDiscoveryService.RuntimeState.Detail);
        }

        try
        {
            var matches = streamDiscoveryService.Discover(new LslStreamDiscoveryRequest(streamName, streamType));
            var visibleViaCompanionTestSender = matches.Any(stream =>
                string.Equals(stream.SourceId, companionTestSourceId, StringComparison.Ordinal));

            var summary = matches.Count switch
            {
                0 => $"{streamName} / {streamType} is not currently visible on Windows.",
                1 when visibleViaCompanionTestSender => $"{streamName} / {streamType} is visible on Windows via the companion TEST sender.",
                1 => $"{streamName} / {streamType} is visible on Windows.",
                _ when visibleViaCompanionTestSender => $"Multiple {streamName} / {streamType} sources are visible on Windows, including the companion TEST sender.",
                _ => $"Multiple {streamName} / {streamType} sources are visible on Windows."
            };

            var detail = matches.Count == 0
                ? "No matching visible streams."
                : FormatVisibleStreamInventory(matches);

            return new SussexExpectedUpstreamState(
                streamName,
                streamType,
                DiscoveryAvailable: true,
                ProbeFailed: false,
                VisibleOnWindows: matches.Count > 0,
                VisibleViaCompanionTestSender: visibleViaCompanionTestSender,
                VisibleMatchCount: matches.Count,
                VisibleMatches: matches,
                Summary: summary,
                Detail: detail);
        }
        catch (Exception ex)
        {
            return new SussexExpectedUpstreamState(
                streamName,
                streamType,
                DiscoveryAvailable: true,
                ProbeFailed: true,
                VisibleOnWindows: false,
                VisibleViaCompanionTestSender: false,
                VisibleMatchCount: 0,
                VisibleMatches: [],
                Summary: $"{streamName} / {streamType} could not be probed on Windows.",
                Detail: ex.Message);
        }
    }

    public static SussexConnectionAssessmentResult Assess(SussexConnectionAssessmentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.ExpectedUpstream);

        var expectedStreamLabel = $"{input.ExpectedUpstream.ExpectedStreamName} / {input.ExpectedUpstream.ExpectedStreamType}";
        var missingLinks = new List<string>();

        if (!input.PinnedBuildReady)
        {
            missingLinks.Add("Pinned Sussex APK is not installed or does not match the expected build.");
        }

        if (!input.DeviceProfileReady)
        {
            missingLinks.Add("Required Sussex device profile is not active.");
        }

        if (!input.WifiTransportReady)
        {
            missingLinks.Add("Active Quest transport is not Wi-Fi ADB.");
        }

        if (!input.ExpectedUpstream.DiscoveryAvailable || input.ExpectedUpstream.ProbeFailed)
        {
            missingLinks.Add($"Windows could not verify visibility of {expectedStreamLabel}.");
        }
        else if (!input.ExpectedUpstream.VisibleOnWindows)
        {
            missingLinks.Add($"Windows cannot currently see {expectedStreamLabel}.");
        }

        if (!input.HeadsetForeground)
        {
            missingLinks.Add("Sussex is not confirmed in the foreground.");
        }

        if (!input.InletReady)
        {
            missingLinks.Add($"Sussex has not confirmed an active {expectedStreamLabel} inlet.");
        }

        if (!input.ReturnPathReady)
        {
            missingLinks.Add("Windows has not received a fresh quest_twin_state / quest.twin.state frame.");
        }

        if (!input.ExpectedTwinStatePublisherVisible)
        {
            missingLinks.Add(input.AnyTwinStatePublisherVisible
                ? "The expected Sussex quest_twin_state publisher is not visible on Windows."
                : "No Sussex quest_twin_state publisher is visible on Windows.");
        }

        var summary = !input.PinnedBuildReady
            ? "Pinned Sussex APK is not installed or does not match the study shell baseline."
            : !input.WifiTransportReady
                ? input.InletReady && input.ReturnPathReady
                    ? "Quest inlet and return path are live, but the session is still on USB ADB."
                    : "Quest is still not on Wi-Fi ADB, so this diagnostic cannot turn green yet."
                : input.InletReady && input.ReturnPathReady
                    ? input.DeviceProfileReady
                        ? "Quest inlet is connected and the Windows return path is live."
                        : "Quest path is live, but the required Sussex device profile still needs attention."
                    : input.InletReady
                        ? input.HeadsetAsleep
                            ? "Quest inlet is connected, but the headset is asleep and the Windows return path is stale."
                            : input.HeadsetInWakeLimbo
                                ? "Quest inlet is connected, but Guardian or tracking-loss is blocking a fresh Windows return path."
                                : input.HeadsetForeground && !input.ExpectedTwinStatePublisherVisible
                                    ? "Quest inlet is connected, but the Quest twin-state publisher stalled or became undiscoverable."
                                    : !input.HeadsetForeground
                                        ? "Quest inlet is connected, but Sussex is not foregrounded and the Windows return path is stale."
                                        : "Quest inlet is connected, but Windows is not receiving a fresh return path yet."
                        : input.ReturnPathReady
                            ? "Windows is receiving quest_twin_state, but Sussex has not confirmed an LSL inlet yet."
                            : !input.HeadsetForeground
                                ? "Sussex is not foregrounded, so neither the LSL inlet nor the Windows return path is confirmed."
                                : !input.AnyTwinStatePublisherVisible
                                    ? "Sussex is in front, but the Quest twin-state publisher is absent and the inlet is not confirmed."
                                    : "Quest is reachable, but neither the LSL inlet nor the Windows return path is confirmed.";

        var level = !input.PinnedBuildReady
            ? OperationOutcomeKind.Failure
            : !input.WifiTransportReady
                ? OperationOutcomeKind.Warning
                : input.InletReady && input.ReturnPathReady
                    ? input.DeviceProfileReady
                        ? OperationOutcomeKind.Success
                        : OperationOutcomeKind.Warning
                    : input.InletReady || input.ReturnPathReady
                        ? OperationOutcomeKind.Warning
                        : input.HeadsetForeground
                            ? OperationOutcomeKind.Failure
                            : OperationOutcomeKind.Warning;

        var focusNext = !input.PinnedBuildReady
            ? "Install or restage the pinned Sussex APK before debugging transport or LSL."
            : !input.WifiTransportReady
                ? "Switch the active Quest transport to Wi-Fi ADB. This probe cannot turn green while the session is still on USB."
                : !input.ExpectedUpstream.DiscoveryAvailable || input.ExpectedUpstream.ProbeFailed
                    ? "Restore the Windows liblsl discovery runtime before relying on expected-stream checks."
                    : !input.ExpectedUpstream.VisibleOnWindows
                        ? "Start the expected upstream sender on Windows, or run the companion TEST sender and confirm that Windows can see it."
                        : !input.HeadsetForeground
                            ? "Bring Sussex back to the foreground, confirm the visible headset scene, and rerun the probe."
                            : !input.InletReady && input.ReturnPathReady
                                ? "The return path is already alive, so focus next on why Sussex has not subscribed the expected inlet."
                                : input.InletReady && !input.ReturnPathReady
                                    ? input.ExpectedTwinStatePublisherVisible
                                        ? "The inlet is already connected, so focus next on why fresh quest_twin_state frames are not reaching Windows."
                                        : "The inlet is already connected, so focus next on the Sussex quest_twin_state publisher and return path."
                                    : input.ExpectedUpstream.VisibleViaCompanionTestSender && !input.InletReady
                                        ? "Windows already sees the companion TEST sender, so focus next on the headset-side Sussex scene, inlet subscription, or Wi-Fi client-isolation."
                                        : !input.ExpectedTwinStatePublisherVisible
                                            ? "Focus next on why the Sussex runtime is not publishing its quest_twin_state stream on Windows."
                                            : input.ExpectedUpstream.VisibleOnWindows
                                                ? "Windows already sees the expected upstream stream, so focus next on Sussex runtime state and twin telemetry."
                                                : "Re-run Windows Environment and the study probe together so the missing transport link is explicit.";

        return new SussexConnectionAssessmentResult(level, summary, missingLinks, focusNext);
    }

    public static string BuildCompanionTestSenderSourceId(StudyShellDefinition study)
    {
        ArgumentNullException.ThrowIfNull(study);
        return BuildCompanionTestSenderSourceId(study.Id);
    }

    public static string BuildCompanionTestSenderSourceId(string studyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studyId);
        return $"viscereality.companion.study-shell.test.{studyId.Trim()}";
    }

    private static string FormatVisibleStreamInventory(IReadOnlyList<LslVisibleStreamInfo> streams)
        => streams.Count == 0
            ? "No matching visible streams."
            : string.Join(
                Environment.NewLine,
                streams.Select(static stream =>
                    $"{stream.Name} / {stream.Type} | source_id `{(string.IsNullOrWhiteSpace(stream.SourceId) ? "n/a" : stream.SourceId)}` | channels {stream.ChannelCount.ToString(CultureInfo.InvariantCulture)} | nominal {stream.SampleRateHz.ToString("0.###", CultureInfo.InvariantCulture)} Hz"));
}
