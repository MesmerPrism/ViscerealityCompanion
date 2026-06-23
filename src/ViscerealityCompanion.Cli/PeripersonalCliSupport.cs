using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using System.Text.Json.Serialization;
using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Cli;

public static partial class Program
{
    private const string PeripersonalDefaultStudyId = "peripersonal-space";
    private const string PeripersonalDefaultSessionId = "session-001";
    private const string PeripersonalBlockOneId = "block-1";

    private static Command BuildPeripersonalCommand()
    {
        var command = new Command("peripersonal", "Peripersonal study workflow commands that mirror the WPF operator buttons");
        var studyOption = new Option<string>("--study", () => PeripersonalDefaultStudyId, "Study shell ID.");
        var rootOption = new Option<string?>("--root", "Study shell catalog root directory path.");
        var statePathOption = new Option<string?>("--state", "Peripersonal CLI state file. Defaults to the companion session root.");
        var receiptTimeoutOption = new Option<int>("--receipt-timeout-seconds", () => 25, "LSL command receipt timeout.");

        command.AddGlobalOption(studyOption);
        command.AddGlobalOption(rootOption);
        command.AddGlobalOption(statePathOption);
        command.AddGlobalOption(receiptTimeoutOption);

        command.AddCommand(BuildPeripersonalPrepareCommand(studyOption, rootOption, statePathOption, receiptTimeoutOption));
        command.AddCommand(BuildPeripersonalOpenQuestionnaireCommand(studyOption, rootOption, statePathOption, receiptTimeoutOption));
        command.AddCommand(BuildPeripersonalMarkBlockOneSubmittedCommand(studyOption, rootOption, statePathOption));
        command.AddCommand(BuildPeripersonalStartRecordingCommand(studyOption, rootOption, statePathOption, receiptTimeoutOption));
        command.AddCommand(BuildPeripersonalClockProbeCommand(studyOption, rootOption, statePathOption, receiptTimeoutOption));
        command.AddCommand(BuildPeripersonalParticlesCommand(studyOption, rootOption, statePathOption, receiptTimeoutOption));
        command.AddCommand(BuildPeripersonalMarkXrBlockEndCommand(studyOption, rootOption, statePathOption, receiptTimeoutOption));
        command.AddCommand(BuildPeripersonalStopRecordingCommand(studyOption, rootOption, statePathOption, receiptTimeoutOption));
        command.AddCommand(BuildPeripersonalStatusCommand(statePathOption));
        command.AddCommand(BuildPeripersonalRunWorkflowCommand(studyOption, rootOption, statePathOption, receiptTimeoutOption));
        return command;
    }

    private static Command BuildPeripersonalPrepareCommand(
        Option<string> studyOption,
        Option<string?> rootOption,
        Option<string?> statePathOption,
        Option<int> receiptTimeoutOption)
    {
        var participantOption = new Option<string>(["--participant", "--participant-ref"], "Participant ID/reference.") { IsRequired = true };
        var sessionOption = new Option<string>("--session", () => PeripersonalDefaultSessionId, "Session ID.");
        var handednessOption = new Option<string>("--handedness", () => "right-handed", "Participant handedness: right-handed or left-handed.");
        var languageOption = new Option<string?>("--language", "Language code sent to Unity and the questionnaire panel.");
        var conditionOption = new Option<string?>("--condition", "Initial condition id.");
        var cliCommand = new Command("prepare", "Prepare the Peripersonal Windows, Unity, and questionnaire-panel session")
        {
            participantOption,
            sessionOption,
            handednessOption,
            languageOption,
            conditionOption
        };

        cliCommand.Handler = CommandHandler.Create(async (
            string study,
            string? root,
            string? state,
            int receiptTimeoutSeconds,
            string participant,
            string session,
            string handedness,
            string? language,
            string? condition,
            string? device) =>
        {
            var definition = await ResolvePeripersonalStudyAsync(study, root).ConfigureAwait(false);
            using var context = CreatePeripersonalWorkflowContext(definition, device, receiptTimeoutSeconds);
            var result = await context.Workflow.PrepareSessionAsync(new PeripersonalSessionSetupRequest(
                    ParticipantRef: participant,
                    SessionId: session,
                    Handedness: handedness,
                    StudyId: definition.Id,
                    LanguageCode: language ?? string.Empty,
                    InitialConditionId: condition ?? string.Empty))
                .ConfigureAwait(false);

            if (result.Succeeded && result.Session is not null)
            {
                PeripersonalCliSessionState.FromWorkflow(
                        definition.Id,
                        result.Session,
                        context.Workflow.State,
                        questionnaireBlockOneSubmitted: false)
                    .Save(state);
            }

            PrintWorkflowResult(result);
        });
        return cliCommand;
    }

    private static Command BuildPeripersonalOpenQuestionnaireCommand(
        Option<string> studyOption,
        Option<string?> rootOption,
        Option<string?> statePathOption,
        Option<int> receiptTimeoutOption)
    {
        var blockOption = new Option<string>("--block", () => PeripersonalBlockOneId, "Questionnaire block id.");
        var conditionNumberOption = new Option<int?>("--condition-number", "Questionnaire condition number.");
        var participantCommandScriptOption = new Option<string?>(
            "--participant-command-script",
            "Semicolon-separated in-panel participant automation commands for evidence runs.");
        var participantCommandIntervalOption = new Option<int?>(
            "--participant-command-interval-ms",
            "Delay between in-panel participant automation commands.");
        var cliCommand = new Command("open-questionnaire", "Open a questionnaire block through the same Unity command used by the WPF button")
        {
            blockOption,
            conditionNumberOption,
            participantCommandScriptOption,
            participantCommandIntervalOption
        };

        cliCommand.Handler = CommandHandler.Create(async (
            string study,
            string? root,
            string? state,
            int receiptTimeoutSeconds,
            string block,
            int? conditionNumber,
            string? participantCommandScript,
            int? participantCommandIntervalMs,
            string? device) =>
        {
            var (definition, workflow, savedState, context) = await RestorePeripersonalWorkflowAsync(
                    study,
                    root,
                    state,
                    device,
                    receiptTimeoutSeconds)
                .ConfigureAwait(false);
            using (context)
            {
                var result = await workflow.OpenQuestionnaireBlockAsync(
                        $"cli-{block}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
                        block,
                        [block],
                        conditionNumber,
                        participantCommandScript,
                        participantCommandIntervalMs)
                    .ConfigureAwait(false);
                SaveStateIfSessionPresent(definition.Id, result, workflow, savedState, state);
                PrintWorkflowResult(result);
            }
        });
        return cliCommand;
    }

    private static Command BuildPeripersonalMarkBlockOneSubmittedCommand(
        Option<string> studyOption,
        Option<string?> rootOption,
        Option<string?> statePathOption)
    {
        var cliCommand = new Command("mark-block1-submitted", "Mark Questionnaire Block 1 submitted, enabling global Start Recording");
        cliCommand.Handler = CommandHandler.Create(async (
            string study,
            string? root,
            string? state) =>
        {
            var definition = await ResolvePeripersonalStudyAsync(study, root).ConfigureAwait(false);
            var savedState = PeripersonalCliSessionState.LoadRequired(state);
            var workflow = new PeripersonalOperatorWorkflowService(
                definition,
                new NoOpPeripersonalCommandTransport());
            workflow.RestorePreparedSession(
                savedState.ToPreparedSession(),
                savedState.WorkflowState,
                savedState.QuestionnaireBlockOneSubmitted);
            var result = workflow.RecordQuestionnaireSubmitted(PeripersonalBlockOneId);

            (savedState with { QuestionnaireBlockOneSubmitted = result.Succeeded || savedState.QuestionnaireBlockOneSubmitted })
            .Save(state);
            PrintWorkflowResult(result);
        });
        return cliCommand;
    }

    private static Command BuildPeripersonalStartRecordingCommand(
        Option<string> studyOption,
        Option<string?> rootOption,
        Option<string?> statePathOption,
        Option<int> receiptTimeoutOption)
    {
        var cliCommand = new Command("start-recording", "Start the one global Peripersonal recording after Block 1");
        cliCommand.Handler = CommandHandler.Create(async (
            string study,
            string? root,
            string? state,
            int receiptTimeoutSeconds,
            string? device) =>
        {
            var (definition, workflow, savedState, context) = await RestorePeripersonalWorkflowAsync(
                    study,
                    root,
                    state,
                    device,
                    receiptTimeoutSeconds)
                .ConfigureAwait(false);
            using (context)
            {
                var result = await workflow.StartRecordingAsync().ConfigureAwait(false);
                SaveStateIfSessionPresent(definition.Id, result, workflow, savedState, state);
                PrintWorkflowResult(result);
            }
        });
        return cliCommand;
    }

    private static Command BuildPeripersonalClockProbeCommand(
        Option<string> studyOption,
        Option<string?> rootOption,
        Option<string?> statePathOption,
        Option<int> receiptTimeoutOption)
    {
        var durationOption = new Option<double>("--duration-seconds", () => StudyClockAlignmentStreamContract.DefaultDurationSeconds, "Clock probe duration in seconds.");
        var intervalOption = new Option<int>("--probe-interval-ms", () => StudyClockAlignmentStreamContract.DefaultProbeIntervalMilliseconds, "Clock probe interval in milliseconds.");
        var cliCommand = new Command("clock-probe", "Run the operator clock-alignment probe while the global Peripersonal recording is active")
        {
            durationOption,
            intervalOption
        };
        cliCommand.Handler = CommandHandler.Create(async (
            string study,
            string? root,
            string? state,
            int receiptTimeoutSeconds,
            double durationSeconds,
            int probeIntervalMs,
            string? device) =>
        {
            var (_, workflow, _, context) = await RestorePeripersonalWorkflowAsync(
                    study,
                    root,
                    state,
                    device,
                    receiptTimeoutSeconds)
                .ConfigureAwait(false);
            using (context)
            using (var clockAlignment = StudyClockAlignmentServiceFactory.CreateDefault())
            {
                var result = await workflow.RunClockProbeAsync(
                        clockAlignment,
                        duration: TimeSpan.FromSeconds(Math.Max(0.5d, durationSeconds)),
                        probeInterval: TimeSpan.FromMilliseconds(Math.Max(50, probeIntervalMs)))
                    .ConfigureAwait(false);
                PrintClockProbeResult(result);
            }
        });
        return cliCommand;
    }

    private static Command BuildPeripersonalParticlesCommand(
        Option<string> studyOption,
        Option<string?> rootOption,
        Option<string?> statePathOption,
        Option<int> receiptTimeoutOption)
    {
        var visibleOption = new Option<bool>("--visible", "Set true for Particles-ON, false for Particles-OFF.");
        var onCommand = new Command("particles-on", "Turn particles on and emit the Particles-ON marker");
        onCommand.Handler = CommandHandler.Create(async (
            string study,
            string? root,
            string? state,
            int receiptTimeoutSeconds,
            string? device) =>
        {
            await RunParticlesCommandAsync(study, root, state, receiptTimeoutSeconds, device, visible: true).ConfigureAwait(false);
        });

        var offCommand = new Command("particles-off", "Turn particles off and emit the Particles-OFF marker");
        offCommand.Handler = CommandHandler.Create(async (
            string study,
            string? root,
            string? state,
            int receiptTimeoutSeconds,
            string? device) =>
        {
            await RunParticlesCommandAsync(study, root, state, receiptTimeoutSeconds, device, visible: false).ConfigureAwait(false);
        });

        var setCommand = new Command("set-particles", "Set particle visibility") { visibleOption };
        setCommand.Handler = CommandHandler.Create(async (
            string study,
            string? root,
            string? state,
            int receiptTimeoutSeconds,
            bool visible,
            string? device) =>
        {
            await RunParticlesCommandAsync(study, root, state, receiptTimeoutSeconds, device, visible).ConfigureAwait(false);
        });

        var group = new Command("particles", "Particle visibility commands that mirror the WPF particles buttons");
        group.AddCommand(onCommand);
        group.AddCommand(offCommand);
        group.AddCommand(setCommand);
        return group;
    }

    private static Command BuildPeripersonalMarkXrBlockEndCommand(
        Option<string> studyOption,
        Option<string?> rootOption,
        Option<string?> statePathOption,
        Option<int> receiptTimeoutOption)
    {
        var blockOption = new Option<string>("--block", () => "xr-block-1", "XR block id.");
        var conditionOption = new Option<string?>("--condition", "Condition id to include in the XR block-end marker.");
        var cliCommand = new Command("mark-xr-block-end", "Record an XR block-end marker without stopping the global recording")
        {
            blockOption,
            conditionOption
        };
        cliCommand.Handler = CommandHandler.Create(async (
            string study,
            string? root,
            string? state,
            int receiptTimeoutSeconds,
            string block,
            string? condition,
            string? device) =>
        {
            var (definition, workflow, savedState, context) = await RestorePeripersonalWorkflowAsync(
                    study,
                    root,
                    state,
                    device,
                    receiptTimeoutSeconds)
                .ConfigureAwait(false);
            using (context)
            {
                var result = await workflow.MarkXrBlockEndAsync(block, condition ?? string.Empty).ConfigureAwait(false);
                SaveStateIfSessionPresent(definition.Id, result, workflow, savedState, state);
                PrintWorkflowResult(result);
            }
        });
        return cliCommand;
    }

    private static Command BuildPeripersonalStopRecordingCommand(
        Option<string> studyOption,
        Option<string?> rootOption,
        Option<string?> statePathOption,
        Option<int> receiptTimeoutOption)
    {
        var cliCommand = new Command("stop-recording", "Stop the global recording, pull Quest backup files, and close Quest apps");
        cliCommand.Handler = CommandHandler.Create(async (
            string study,
            string? root,
            string? state,
            int receiptTimeoutSeconds,
            string? device) =>
        {
            var (definition, workflow, savedState, context) = await RestorePeripersonalWorkflowAsync(
                    study,
                    root,
                    state,
                    device,
                    receiptTimeoutSeconds)
                .ConfigureAwait(false);
            using (context)
            {
                var result = await workflow.StopRecordingAndCloseAppsAsync().ConfigureAwait(false);
                SaveStateIfSessionPresent(definition.Id, result, workflow, savedState, state);
                PrintWorkflowResult(result);
            }
        });
        return cliCommand;
    }

    private static Command BuildPeripersonalStatusCommand(Option<string?> statePathOption)
    {
        var cliCommand = new Command("workflow-status", "Show the persisted Peripersonal CLI workflow state") { statePathOption };
        cliCommand.Handler = CommandHandler.Create((string? state) =>
        {
            var saved = PeripersonalCliSessionState.Load(state);
            if (saved is null)
            {
                Console.WriteLine("No Peripersonal CLI workflow state has been prepared yet.");
                return;
            }

            Console.WriteLine($"Study:                 {saved.StudyId}");
            Console.WriteLine($"Participant:           {saved.ParticipantRef}");
            Console.WriteLine($"Session ID:            {saved.SessionId}");
            Console.WriteLine($"Session folder:        {saved.SessionFolderName}");
            Console.WriteLine($"Workflow state:        {saved.WorkflowState}");
            Console.WriteLine($"Block 1 submitted:     {saved.QuestionnaireBlockOneSubmitted}");
            Console.WriteLine($"Breath controller:     {saved.BreathTrackingControllerSide}");
            Console.WriteLine($"Language:              {saved.LanguageCode}");
            Console.WriteLine($"Initial condition:     {saved.InitialConditionId}");
            Console.WriteLine($"Windows folder:        {saved.WindowsSessionDirectory}");
            Console.WriteLine($"Ledger:                {saved.LedgerPath}");
            Console.WriteLine($"Pulled Quest backup:   {saved.LastQuestBackupPullDirectory}");
        });
        return cliCommand;
    }

    private static Command BuildPeripersonalRunWorkflowCommand(
        Option<string> studyOption,
        Option<string?> rootOption,
        Option<string?> statePathOption,
        Option<int> receiptTimeoutOption)
    {
        var participantOption = new Option<string>(["--participant", "--participant-ref"], "Participant ID/reference.") { IsRequired = true };
        var sessionOption = new Option<string>("--session", () => PeripersonalDefaultSessionId, "Session ID.");
        var handednessOption = new Option<string>("--handedness", () => "right-handed", "Participant handedness.");
        var blockOption = new Option<string>("--xr-block", () => "xr-block-1", "XR block id for the marker step.");
        var conditionOption = new Option<string?>("--condition", "Condition id for marker/setup.");
        var launchOption = new Option<bool>("--launch", "Launch the pinned study runtime before running the workflow.");
        var cliCommand = new Command("run-workflow", "Run a representative button-sequence workflow from the CLI")
        {
            participantOption,
            sessionOption,
            handednessOption,
            blockOption,
            conditionOption,
            launchOption
        };

        cliCommand.Handler = CommandHandler.Create(async (
            string study,
            string? root,
            string? state,
            int receiptTimeoutSeconds,
            string participant,
            string session,
            string handedness,
            string xrBlock,
            string? condition,
            bool launch,
            string? device) =>
        {
            var definition = await ResolvePeripersonalStudyAsync(study, root).ConfigureAwait(false);
            if (launch)
            {
                var service = CreateQuestService(device);
                var launchResult = await service.LaunchAppAsync(
                        StudyShellOperatorBindings.CreateQuestTarget(definition),
                        kioskMode: definition.App.LaunchInKioskMode)
                    .ConfigureAwait(false);
                PrintOutcome(launchResult);
                await Task.Delay(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
            }

            using var context = CreatePeripersonalWorkflowContext(definition, device, receiptTimeoutSeconds);
            var prepare = await context.Workflow.PrepareSessionAsync(new PeripersonalSessionSetupRequest(
                    ParticipantRef: participant,
                    SessionId: session,
                    Handedness: handedness,
                    StudyId: definition.Id,
                    InitialConditionId: condition ?? string.Empty))
                .ConfigureAwait(false);
            PrintWorkflowResult(prepare);
            if (!prepare.Succeeded)
            {
                return;
            }

            context.Workflow.RecordQuestionnaireSubmitted(PeripersonalBlockOneId);
            var start = await context.Workflow.StartRecordingAsync().ConfigureAwait(false);
            PrintWorkflowResult(start);
            if (!start.Succeeded)
            {
                return;
            }

            PrintWorkflowResult(await context.Workflow.SetParticlesVisibleAsync(true).ConfigureAwait(false));
            PrintWorkflowResult(await context.Workflow.SetParticlesVisibleAsync(false).ConfigureAwait(false));
            PrintWorkflowResult(await context.Workflow.MarkXrBlockEndAsync(xrBlock, condition ?? string.Empty).ConfigureAwait(false));
            var stop = await context.Workflow.StopRecordingAndCloseAppsAsync().ConfigureAwait(false);
            PrintWorkflowResult(stop);
            SaveStateIfSessionPresent(
                definition.Id,
                stop,
                context.Workflow,
                PeripersonalCliSessionState.FromWorkflow(definition.Id, prepare.Session!, PeripersonalOperatorWorkflowState.Prepared, true),
                state);
        });
        return cliCommand;
    }

    private static async Task RunParticlesCommandAsync(
        string study,
        string? root,
        string? state,
        int receiptTimeoutSeconds,
        string? device,
        bool visible)
    {
        var (definition, workflow, savedState, context) = await RestorePeripersonalWorkflowAsync(
                study,
                root,
                state,
                device,
                receiptTimeoutSeconds)
            .ConfigureAwait(false);
        using (context)
        {
            var result = await workflow.SetParticlesVisibleAsync(visible).ConfigureAwait(false);
            SaveStateIfSessionPresent(definition.Id, result, workflow, savedState, state);
            PrintWorkflowResult(result);
        }
    }

    private static async Task<(StudyShellDefinition Definition, PeripersonalOperatorWorkflowService Workflow, PeripersonalCliSessionState State, PeripersonalWorkflowContext Context)>
        RestorePeripersonalWorkflowAsync(
            string study,
            string? root,
            string? statePath,
            string? device,
            int receiptTimeoutSeconds)
    {
        var definition = await ResolvePeripersonalStudyAsync(study, root).ConfigureAwait(false);
        var savedState = PeripersonalCliSessionState.LoadRequired(statePath);
        var context = CreatePeripersonalWorkflowContext(definition, device, receiptTimeoutSeconds);
        context.Workflow.RestorePreparedSession(
            savedState.ToPreparedSession(),
            savedState.WorkflowState,
            savedState.QuestionnaireBlockOneSubmitted);
        return (definition, context.Workflow, savedState, context);
    }

    private static async Task<StudyShellDefinition> ResolvePeripersonalStudyAsync(string study, string? root)
    {
        var definition = await ResolveStudyShellAsync(study, root).ConfigureAwait(false);
        if (!string.Equals(definition.Id, PeripersonalDefaultStudyId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Study shell `{definition.Id}` is not the Peripersonal study shell.");
        }

        return definition;
    }

    private static PeripersonalWorkflowContext CreatePeripersonalWorkflowContext(
        StudyShellDefinition definition,
        string? device,
        int receiptTimeoutSeconds)
    {
        var selector = ResolveDeviceSerial(device);
        var adbPath = ResolvePeripersonalAdbPath();
        var lslTransport = new PeripersonalLslCommandTransport(
            LslOutletServiceFactory.CreateDefault(),
            LslMonitorServiceFactory.CreateDefault(),
            TimeSpan.FromSeconds(Math.Max(1, receiptTimeoutSeconds)));
        var panelTransport = new PeripersonalAndroidBroadcastCommandTransport(
            adbPath,
            selector,
            timeout: TimeSpan.FromSeconds(Math.Max(1, receiptTimeoutSeconds)));
        var workflow = new PeripersonalOperatorWorkflowService(
            definition,
            new PeripersonalCompositeCommandTransport(lslTransport, panelTransport),
            questAppCloser: new PeripersonalAdbQuestAppCloser(adbPath, selector),
            questBackupPuller: new PeripersonalAdbQuestBackupPuller(adbPath, selector),
            questHttpForwarder: new PeripersonalAdbQuestHttpForwarder(adbPath, selector));
        return new PeripersonalWorkflowContext(workflow, lslTransport);
    }

    private static string ResolvePeripersonalAdbPath()
        => ResolveFirstExistingPath(EnumeratePeripersonalAdbCandidates())
           ?? throw new InvalidOperationException(
               $"adb.exe is unavailable. Run `viscereality tooling install-official` or set VISCEREALITY_ADB_EXE. Expected managed path: {OfficialQuestToolingLayout.AdbExecutablePath}");

    private static string? ResolveFirstExistingPath(IEnumerable<string?> candidatePaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawCandidate in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(rawCandidate))
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = Path.GetFullPath(rawCandidate.Trim().Trim('"'));
            }
            catch
            {
                continue;
            }

            if (seen.Add(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string?> EnumeratePeripersonalAdbCandidates()
    {
        yield return Environment.GetEnvironmentVariable("VISCEREALITY_ADB_EXE");
        yield return OfficialQuestToolingLayout.AdbExecutablePath;
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Android",
            "Sdk",
            "platform-tools",
            "adb.exe");

        foreach (var envVar in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return Path.Combine(value, "platform-tools", "adb.exe");
            }
        }

        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(entry, "adb.exe");
            yield return Path.Combine(entry, "adb");
        }
    }

    private static void SaveStateIfSessionPresent(
        string studyId,
        PeripersonalWorkflowOperationResult result,
        PeripersonalOperatorWorkflowService workflow,
        PeripersonalCliSessionState previous,
        string? statePath)
    {
        if (result.Session is null)
        {
            return;
        }

        PeripersonalCliSessionState.FromWorkflow(
                studyId,
                result.Session,
                workflow.State,
                previous.QuestionnaireBlockOneSubmitted,
                result.Outcome.SafeItems.FirstOrDefault() ?? previous.LastQuestBackupPullDirectory)
            .Save(statePath);
    }

    private static void PrintWorkflowResult(PeripersonalWorkflowOperationResult result)
    {
        PrintOutcome(result.Outcome);
        if (!result.Succeeded)
        {
            Environment.ExitCode = 1;
        }

        if (result.Session is not null)
        {
            Console.WriteLine($"       Session folder: {result.Session.SessionFolderName}");
            Console.WriteLine($"       Ledger: {result.Session.LedgerPath}");
        }

        if (result.CommandSnapshots.Count > 0)
        {
            var latest = result.CommandSnapshots[^1];
            Console.WriteLine($"       Ledger snapshots: {result.CommandSnapshots.Count}; latest {latest.Action} => {latest.Status}");
        }
    }

    private static void PrintClockProbeResult(PeripersonalClockProbeOperationResult result)
    {
        PrintOutcome(result.Outcome);
        if (!result.Succeeded)
        {
            Environment.ExitCode = 1;
        }

        Console.WriteLine($"       Clock probes sent: {result.ClockAlignment.Summary.ProbesSent}");
        Console.WriteLine($"       Clock echoes received: {result.ClockAlignment.Summary.EchoesReceived}");
        Console.WriteLine($"       Clock samples persisted: {result.ClockAlignment.Samples.Count}");
        if (result.ClockAlignment.Summary.RecommendedQuestMinusWindowsClockSeconds.HasValue)
        {
            Console.WriteLine($"       Recommended Quest-Windows offset: {result.ClockAlignment.Summary.RecommendedQuestMinusWindowsClockSeconds.Value.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)} s");
        }

        if (!string.IsNullOrWhiteSpace(result.WindowsRoundTripCsvPath))
        {
            Console.WriteLine($"       Windows clock CSV: {result.WindowsRoundTripCsvPath}");
        }
    }

    private sealed class NoOpPeripersonalCommandTransport : IPeripersonalCommandTransport
    {
        public Task<PeripersonalCommandReceiptEnvelope> SendAsync(
            PeripersonalCommandEnvelope command,
            CancellationToken cancellationToken = default)
            => Task.FromResult(PeripersonalCommandReceiptEnvelope.Create(
                command,
                accepted: true,
                executed: true,
                completed: true,
                transportReceived: command.Transport,
                message: "No-op CLI state command.",
                observedState: null,
                stateRevision: command.Sequence));
    }

    private sealed class PeripersonalWorkflowContext : IDisposable
    {
        private readonly IDisposable _lslTransport;

        public PeripersonalWorkflowContext(
            PeripersonalOperatorWorkflowService workflow,
            IDisposable lslTransport)
        {
            Workflow = workflow;
            _lslTransport = lslTransport;
        }

        public PeripersonalOperatorWorkflowService Workflow { get; }

        public void Dispose()
        {
            _lslTransport.Dispose();
        }
    }
}

internal sealed record PeripersonalCliSessionState(
    string StudyId,
    string ParticipantRef,
    string SessionId,
    string Handedness,
    string BreathTrackingControllerSide,
    string SessionFolderName,
    string WindowsSessionDirectory,
    string LedgerPath,
    DateTimeOffset PreparedAtUtc,
    PeripersonalOperatorWorkflowState WorkflowState,
    bool QuestionnaireBlockOneSubmitted,
    string LastQuestBackupPullDirectory = "",
    string LanguageCode = "",
    string InitialConditionId = "")
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static PeripersonalCliSessionState FromWorkflow(
        string studyId,
        PeripersonalPreparedSession session,
        PeripersonalOperatorWorkflowState workflowState,
        bool questionnaireBlockOneSubmitted,
        string lastQuestBackupPullDirectory = "")
        => new(
            studyId,
            session.ParticipantRef,
            session.SessionId,
            session.Handedness,
            session.BreathTrackingControllerSide,
            session.SessionFolderName,
            session.WindowsSessionDirectory,
            session.LedgerPath,
            session.PreparedAtUtc,
            workflowState,
            questionnaireBlockOneSubmitted,
            lastQuestBackupPullDirectory,
            session.LanguageCode,
            session.InitialConditionId);

    public static PeripersonalCliSessionState? Load(string? path)
    {
        var resolvedPath = ResolvePath(path);
        if (!File.Exists(resolvedPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<PeripersonalCliSessionState>(
            File.ReadAllText(resolvedPath),
            JsonOptions);
    }

    public static PeripersonalCliSessionState LoadRequired(string? path)
        => Load(path)
           ?? throw new InvalidOperationException($"No Peripersonal CLI workflow state exists yet. Run `viscereality peripersonal prepare` first. State path: {ResolvePath(path)}");

    public void Save(string? path)
    {
        var resolvedPath = ResolvePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath) ?? ".");
        File.WriteAllText(resolvedPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public PeripersonalPreparedSession ToPreparedSession()
        => new(
            StudyId,
            ParticipantRef,
            SessionId,
            Handedness,
            BreathTrackingControllerSide,
            SessionFolderName,
            WindowsSessionDirectory,
            LedgerPath,
            PreparedAtUtc,
            LanguageCode,
            InitialConditionId);

    private static string ResolvePath(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? Path.Combine(CompanionOperatorDataLayout.SessionRootPath, "peripersonal-cli-state.json")
            : Path.GetFullPath(path);
}

