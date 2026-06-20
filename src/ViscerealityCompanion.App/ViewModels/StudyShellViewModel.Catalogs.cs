namespace ViscerealityCompanion.App.ViewModels;

public sealed partial class StudyShellViewModel
{
    private static readonly StudyValueSection[] SectionCatalog =
    [
        new("lsl", "LSL Routing", "Track the stream target and current LSL input connectivity."),
        new("controller", "Controller Breathing", "Follow controller breathing state, calibration, and live control value."),
        new("heartbeat", "Heartbeat", "Inspect heartbeat route selection and the latest incoming heartbeat value."),
        new("coherence", "Coherence", "Inspect coherence routing and the current coherence value."),
        new("performance", "Performance", "Track current fps, frame time, and the runtime target."),
        new("controls", "Recenter + Particles", "Study-specific controls and the runtime telemetry that backs them."),
        new("all", "All Pinned Keys", "Every live key this study shell is currently watching.")
    ];

    private WorkflowGuideStepDefinition[] WorkflowGuideCatalog =>
        _isPeripersonalWorkflow ? PeripersonalWorkflowGuideCatalog : SussexWorkflowGuideCatalog;

    private static readonly WorkflowGuideStepDefinition[] SussexWorkflowGuideCatalog =
    [
        new(1, "Verify USB visibility", "Start with a real USB connection. The headset must be visible over USB ADB before the guide can bootstrap remote control."),
        new(2, "Enable Wi-Fi ADB", "Turn on Wi-Fi ADB so the headset can stay reachable on its current Wi-Fi network. This exposes ADB over the headset's active Wi-Fi connection."),
        new(3, "Confirm the router path", "Confirm the headset is on a network path that Windows can really reach. Matching Wi-Fi names are fine, but the host can also be on the same router over Ethernet or another valid routed link."),
        new(4, "Confirm Wi-Fi ADB stays active", "Make sure the companion keeps reaching the headset over Wi-Fi ADB and does not silently fall back to USB. The later guided steps should keep showing the Wi-Fi endpoint as the active transport."),
        new(5, "Verify the Sussex APK", "Check whether the approved Sussex Experiment APK is installed. If not, install the bundled study APK before moving on."),
        new(6, "Review the device profile", "Apply the pinned CPU, GPU, brightness, and media-volume profile, then review the battery and remaining bench advisories before leaving the bench. These checks stay visible, but they do not block the Sussex flow."),
        new(7, "Draw the boundary", "Have the experimenter draw a comfortable boundary that covers the participant position, experimenter position, and the full experiment area. This step is manual and not enforced by the app."),
        new(8, "Launch the runtime", "Wake the headset to enable launching, then launch the Sussex runtime. On the current Meta OS build the launch path still applies best-effort task pinning, but the controller Meta/menu button may remain active."),
        new(9, "Verify LSL reaches the headset", "Confirm the external heartbeat/biofeedback LSL stream is reaching the Sussex runtime and that the resulting live state is visible back in the companion."),
        new(10, "Test particle commands", "Use the companion controls to turn particles on and then off again. This is still recommended for bench confidence, but it does not block the Sussex flow."),
        new(11, "Try controller calibration", "Controller-volume breathing calibration is available here, but the current Sussex APK path is still unstable. Try it if useful, but it is not required before continuing."),
        new(12, "Run an optional 20 second validation capture", "Enter a temporary subject id, record a short validation run, let the start and end clock-alignment bursts plus sparse background probes run automatically, then pull the Quest-side backup files so both Windows and headset data are available for inspection."),
        new(13, "Reset for the real participant", "Reset calibration, make sure particles are off, then hand off into the dedicated experiment-session window for the real participant.")
    ];

    private static readonly WorkflowGuideStepDefinition[] PeripersonalWorkflowGuideCatalog =
    [
        new(1, "Verify USB visibility", "Start with a real USB connection. The headset must be visible over USB ADB before the guide can bootstrap remote control."),
        new(2, "Enable Wi-Fi ADB", "Turn on Wi-Fi ADB so the headset can stay reachable on its current Wi-Fi network while the participant is in the headset."),
        new(3, "Confirm the router path", "Confirm the headset is on a network path that Windows can reach reliably. Matching Wi-Fi names are fine, but a routed Ethernet path is also valid if the diagnostics accept it."),
        new(4, "Verify the Peripersonal APK", "Check whether the approved Peripersonal Unity runtime is installed. If not, install the bundled or selected study APK before continuing."),
        new(5, "Launch the XR runtime", "Launch the Peripersonal Unity runtime and confirm it is foregrounded before session setup. The questionnaire panel is launched later from this runtime under the current implementation."),
        new(6, "Prepare the session", "Enter participant metadata, session id, handedness, and MAIA language. Prepare Session creates the Windows folder, sends setup metadata, and fixes the breath-tracking controller side for this session."),
        new(7, "Run Questionnaire Block 1", "Open the first MAIA spatial questionnaire block. After the participant submits it, mark Block 1 submitted so the single global recording can start."),
        new(8, "Start global recording", "Start the one continuous recording session after Block 1 and run a clock probe. Questionnaire blocks 2 and 3 happen while this recording remains active."),
        new(9, "XR Block 1 marker", "Use particle visibility controls and then mark the XR block end. These are markers and condition transitions inside the continuous recording, not recorder stop/start boundaries."),
        new(10, "Run Questionnaire Block 2", "Open the second MAIA spatial questionnaire block while recording continues. Participant submit should return the XR runtime to foreground."),
        new(11, "XR Block 2 marker", "Use the required runtime controls for the second XR block and mark the block end while recording continues."),
        new(12, "Run Questionnaire Block 3", "Open the final MAIA spatial questionnaire block while the same recording is still active."),
        new(13, "Stop and close", "Use the final central Stop Recording command. It ends the recording session, pulls the Quest backup, and asks the Quest apps to close.")
    ];

    private static readonly string[] WorkflowGuideExpectedDeviceRecordingFiles =
    [
        "session_settings.json",
        "session_events.csv",
        "signals_long.csv",
        "breathing_trace.csv",
        "clock_alignment_samples.csv",
        "timing_markers.csv"
    ];

    private static readonly string[] WorkflowExpectedWindowsRecordingFiles =
    [
        "session_events.csv",
        "signals_long.csv",
        "breathing_trace.csv",
        "clock_alignment_roundtrip.csv",
        "upstream_lsl_monitor.csv",
        "timing_markers.csv"
    ];
}
