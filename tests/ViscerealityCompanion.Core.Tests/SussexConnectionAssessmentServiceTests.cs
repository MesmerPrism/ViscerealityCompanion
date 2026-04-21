using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class SussexConnectionAssessmentServiceTests
{
    [Fact]
    public void InspectExpectedUpstream_RecognizesCompanionTestSenderSource()
    {
        var discovery = new FakeLslStreamDiscoveryService(
            new LslRuntimeState(true, "Fake discovery runtime ready."),
            [
                new LslVisibleStreamInfo(
                    "HRV_Biofeedback",
                    "HRV",
                    SussexConnectionAssessmentService.BuildCompanionTestSenderSourceId("sussex-university"),
                    1,
                    10f,
                    42d)
            ]);

        var result = SussexConnectionAssessmentService.InspectExpectedUpstream(
            discovery,
            "HRV_Biofeedback",
            "HRV",
            SussexConnectionAssessmentService.BuildCompanionTestSenderSourceId("sussex-university"));

        Assert.True(result.VisibleOnWindows);
        Assert.True(result.VisibleViaCompanionTestSender);
        Assert.Equal(1, result.VisibleMatchCount);
        Assert.Contains("companion TEST sender", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source_id", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_CallsOutHeadsetSideWork_WhenWindowsAlreadySeesCompanionTestSender()
    {
        var assessment = SussexConnectionAssessmentService.Assess(new SussexConnectionAssessmentInput(
            PinnedBuildReady: true,
            DeviceProfileReady: true,
            HeadsetForeground: true,
            WifiTransportReady: true,
            InletReady: false,
            ReturnPathReady: false,
            AnyTwinStatePublisherVisible: false,
            ExpectedTwinStatePublisherVisible: false,
            ExpectedUpstream: new SussexExpectedUpstreamState(
                "HRV_Biofeedback",
                "HRV",
                DiscoveryAvailable: true,
                ProbeFailed: false,
                VisibleOnWindows: true,
                VisibleViaCompanionTestSender: true,
                VisibleMatchCount: 1,
                VisibleMatches:
                [
                    new LslVisibleStreamInfo(
                        "HRV_Biofeedback",
                        "HRV",
                        SussexConnectionAssessmentService.BuildCompanionTestSenderSourceId("sussex-university"),
                        1,
                        10f,
                        42d)
                ],
                Summary: "HRV_Biofeedback / HRV is visible on Windows via the companion TEST sender.",
                Detail: "HRV_Biofeedback / HRV | source_id `viscereality.companion.study-shell.test.sussex-university` | channels 1 | nominal 10 Hz")));

        Assert.Equal(OperationOutcomeKind.Failure, assessment.Level);
        Assert.Equal("Sussex is in front, but the Quest twin-state publisher is absent and the inlet is not confirmed.", assessment.Summary);
        Assert.DoesNotContain(assessment.MissingLinks, item => item.Contains("Windows cannot currently see", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assessment.MissingLinks, item => item.Contains("Sussex has not confirmed an active HRV_Biofeedback / HRV inlet.", StringComparison.Ordinal));
        Assert.Contains("companion TEST sender", assessment.FocusNext, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("headset-side Sussex scene", assessment.FocusNext, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_CallsOutInletSpecifically_WhenReturnPathIsAlreadyAlive()
    {
        var assessment = SussexConnectionAssessmentService.Assess(new SussexConnectionAssessmentInput(
            PinnedBuildReady: true,
            DeviceProfileReady: true,
            HeadsetForeground: true,
            WifiTransportReady: true,
            InletReady: false,
            ReturnPathReady: true,
            AnyTwinStatePublisherVisible: true,
            ExpectedTwinStatePublisherVisible: true,
            ExpectedUpstream: new SussexExpectedUpstreamState(
                "HRV_Biofeedback",
                "HRV",
                DiscoveryAvailable: true,
                ProbeFailed: false,
                VisibleOnWindows: true,
                VisibleViaCompanionTestSender: false,
                VisibleMatchCount: 1,
                VisibleMatches: [],
                Summary: "HRV_Biofeedback / HRV is visible on Windows.",
                Detail: "Visible from an external sender.")));

        Assert.Equal(OperationOutcomeKind.Warning, assessment.Level);
        Assert.Equal("Windows is receiving quest_twin_state, but Sussex has not confirmed an LSL inlet yet.", assessment.Summary);
        Assert.Contains(assessment.MissingLinks, item => item.Contains("active HRV_Biofeedback / HRV inlet", StringComparison.Ordinal));
        Assert.DoesNotContain(assessment.MissingLinks, item => item.Contains("quest_twin_state publisher", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("return path is already alive", assessment.FocusNext, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_CallsOutReturnPath_WhenInletIsConnectedButTwinFramesAreMissing()
    {
        var assessment = SussexConnectionAssessmentService.Assess(new SussexConnectionAssessmentInput(
            PinnedBuildReady: true,
            DeviceProfileReady: true,
            HeadsetForeground: true,
            WifiTransportReady: true,
            InletReady: true,
            ReturnPathReady: false,
            AnyTwinStatePublisherVisible: false,
            ExpectedTwinStatePublisherVisible: false,
            ExpectedUpstream: new SussexExpectedUpstreamState(
                "HRV_Biofeedback",
                "HRV",
                DiscoveryAvailable: true,
                ProbeFailed: false,
                VisibleOnWindows: true,
                VisibleViaCompanionTestSender: false,
                VisibleMatchCount: 1,
                VisibleMatches: [],
                Summary: "HRV_Biofeedback / HRV is visible on Windows.",
                Detail: "Visible from an external sender.")));

        Assert.Equal(OperationOutcomeKind.Warning, assessment.Level);
        Assert.Equal("Quest inlet is connected, but the Quest twin-state publisher stalled or became undiscoverable.", assessment.Summary);
        Assert.Contains(assessment.MissingLinks, item => item.Contains("fresh quest_twin_state / quest.twin.state frame", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assessment.MissingLinks, item => item.Contains("No Sussex quest_twin_state publisher", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("quest_twin_state publisher", assessment.FocusNext, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeLslStreamDiscoveryService(
        LslRuntimeState runtimeState,
        IReadOnlyList<LslVisibleStreamInfo> matches) : ILslStreamDiscoveryService
    {
        public LslRuntimeState RuntimeState { get; } = runtimeState;

        public IReadOnlyList<LslVisibleStreamInfo> Discover(LslStreamDiscoveryRequest request)
            => matches
                .Where(stream =>
                    (string.IsNullOrWhiteSpace(request.StreamName) || string.Equals(stream.Name, request.StreamName, StringComparison.Ordinal)) &&
                    (string.IsNullOrWhiteSpace(request.StreamType) || string.Equals(stream.Type, request.StreamType, StringComparison.Ordinal)))
                .ToArray();
    }
}
