using System.Globalization;
using System.Text.Json;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes.Charts;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace ViscerealityCompanion.Core.Services;

public static class SussexValidationPdfRenderer
{
    private const string TitleStyleName = "ValidationTitle";
    private const string SectionStyleName = "ValidationSection";
    private const string BodyStyleName = "ValidationBody";
    private const string SmallStyleName = "ValidationSmall";
    private const string TableHeaderStyleName = "ValidationTableHeader";
    private const string DenseBodyStyleName = "ValidationDenseBody";
    private const string CodeStyleName = "ValidationCode";
    private const string DenseCodeStyleName = "ValidationDenseCode";

    private static readonly string[] ExpectedWindowsFiles =
    [
        "session_events.csv",
        "signals_long.csv",
        "breathing_trace.csv",
        "clock_alignment_roundtrip.csv",
        "upstream_lsl_monitor.csv",
        "timing_markers.csv",
        "session_settings.json"
    ];

    private static readonly string[] ExpectedQuestFiles =
    [
        "session_settings.json",
        "session_events.csv",
        "signals_long.csv",
        "breathing_trace.csv",
        "clock_alignment_samples.csv",
        "timing_markers.csv"
    ];

    public static void Render(string sessionFolderPath, string outputPdfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPdfPath);

        var sessionDirectory = Path.GetFullPath(sessionFolderPath);
        if (!Directory.Exists(sessionDirectory))
        {
            throw new DirectoryNotFoundException($"Session folder not found: {sessionDirectory}");
        }

        var data = ValidationSessionData.Load(sessionDirectory);

        PdfReportBootstrap.EnsureInitialized();

        var document = new Document();
        document.Info.Title = $"Sussex Session Review - {data.SessionId}";
        document.Info.Subject = "Sussex validation capture review";
        document.Info.Author = "Viscereality Companion";
        DefineStyles(document);

        AddCoverPage(document, data);
        AddExecutiveSummaryPage(document, data);
        AddCoveragePage(document, data);
        AddSignalInventoryPage(document, data);
        AddMilestonePage(document, data);
        AddSessionParameterStatePages(document, data);

        var plottedSignalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plottedBreathingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddBreathingChartPage(document, data, "Breathing Trace", "Windows and Quest recorder traces shown by sample order.", "breath_volume01", yMinimum: 0, yMaximum: 1);
        plottedBreathingNames.Add("breath_volume01");
        AddSignalChartPage(document, data, "Runtime Breathing Mirror", "Runtime breathing mirror returned through twin-state by sample order.", "breathing.value01", yMinimum: 0, yMaximum: 1);
        plottedSignalNames.Add("breathing.value01");
        AddSignalChartPage(document, data, "Coherence", "Held twin-state coherence mirror by sample order.", "coherence.value01", yMinimum: 0, yMaximum: 1);
        plottedSignalNames.Add("coherence.value01");
        AddSignalChartPage(document, data, "Heartbeat Envelope", "Normalized heartbeat envelope by sample order.", "heartbeat.value01", yMinimum: 0, yMaximum: 1);
        plottedSignalNames.Add("heartbeat.value01");
        AddSignalChartPage(document, data, "Heartbeat Packet Value", "Latest upstream packet value held in twin-state by sample order.", "heartbeat.packet_value01", yMinimum: 0, yMaximum: 1);
        plottedSignalNames.Add("heartbeat.packet_value01");
        AddSignalChartPage(document, data, "Heartbeat Real-Beat Ramp", "Beat-trigger ramp mirrored from the Quest runtime by sample order.", "heartbeat.real_beat_value01", yMinimum: 0, yMaximum: 1);
        plottedSignalNames.Add("heartbeat.real_beat_value01");
        AddSignalChartPage(document, data, "Orbit Radius Visual", "Runtime orbit-distance multiplier by sample order.", "orbit.radius_visual01", yMinimum: 0, yMaximum: 1);
        plottedSignalNames.Add("orbit.radius_visual01");
        AddSignalChartPage(document, data, "Orbit Envelope Weight", "Orbit envelope weight by sample order.", "orbit.radius_envelope_weight01", yMinimum: 0, yMaximum: 1);
        plottedSignalNames.Add("orbit.radius_envelope_weight01");
        AddSignalChartPage(document, data, "Orbit Radius Phase", "Orbit phase mirror by sample order.", "orbit.radius_phase01", yMinimum: 0, yMaximum: 1);
        plottedSignalNames.Add("orbit.radius_phase01");
        AddSignalChartPage(document, data, "Orbit Peak Active", "Near-peak orbit flag by sample order.", "orbit.radius_peak_active", yMinimum: 0, yMaximum: 1);
        plottedSignalNames.Add("orbit.radius_peak_active");
        AddBreathingChartPage(document, data, "Recorder Sphere Progress", "Breathing-recorder sphere progress by sample order.", "sphere_radius_progress01", yMinimum: 0, yMaximum: 1);
        plottedBreathingNames.Add("sphere_radius_progress01");
        AddSignalChartPage(document, data, "Runtime Sphere Progress Mirror", "Runtime sphere progress mirrored through twin-state by sample order.", "sphere_radius.progress01", yMinimum: 0, yMaximum: 1);
        plottedSignalNames.Add("sphere_radius.progress01");
        AddBreathingChartPage(document, data, "Recorder Sphere Radius", "Breathing-recorder raw sphere radius by sample order.", "sphere_radius_raw");
        plottedBreathingNames.Add("sphere_radius_raw");
        AddSignalChartPage(document, data, "Runtime Sphere Radius Mirror", "Runtime raw sphere radius mirrored through twin-state by sample order.", "sphere_radius.raw");
        plottedSignalNames.Add("sphere_radius.raw");
        AddBreathingChartPage(document, data, "Controller Calibrated", "Breathing-recorder controller-calibrated flag by sample order.", "controller_calibrated", yMinimum: 0, yMaximum: 1);
        plottedBreathingNames.Add("controller_calibrated");
        AddSignalChartPage(document, data, "LSL Sample Count", "Cumulative LSL sample counter by sample order.", "lsl.sample_count");
        plottedSignalNames.Add("lsl.sample_count");
        AddSignalChartPage(document, data, "LSL Latest Timestamp", "Latest LSL timestamp seen by the Quest runtime by sample order.", "lsl.latest_timestamp_seconds");
        plottedSignalNames.Add("lsl.latest_timestamp_seconds");
        AddAdditionalSignalChartPages(document, data, plottedSignalNames);
        AddAdditionalBreathingChartPages(document, data, plottedBreathingNames);
        AddPacketTimingPage(document, data);
        AddClockAlignmentChartPage(
            document,
            "Clock Alignment Round-Trip",
            "Probe echo return time in milliseconds from the Windows recorder.",
            data.ClockAlignmentPoints.Select(point => point.RoundTripMs).ToArray(),
            Colors.MediumSeaGreen);
        AddClockAlignmentChartPage(
            document,
            "Clock Alignment Offset Residual",
            "Quest-minus-Windows offset after subtracting the median estimate, in milliseconds.",
            data.ClockAlignmentPoints.Select(point => point.OffsetResidualMs).ToArray(),
            Colors.MediumVioletRed);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPdfPath))!);
        var renderer = new PdfDocumentRenderer
        {
            Document = document
        };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(outputPdfPath);
    }

    private static void DefineStyles(Document document)
    {
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = Unit.FromPoint(8.5);
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);

        var title = document.Styles.AddStyle(TitleStyleName, StyleNames.Normal);
        title.Font.Size = Unit.FromPoint(18);
        title.Font.Bold = true;
        title.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);

        var section = document.Styles.AddStyle(SectionStyleName, StyleNames.Normal);
        section.Font.Size = Unit.FromPoint(11);
        section.Font.Bold = true;
        section.ParagraphFormat.SpaceBefore = Unit.FromPoint(10);
        section.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);
        section.ParagraphFormat.KeepWithNext = true;

        var body = document.Styles.AddStyle(BodyStyleName, StyleNames.Normal);
        body.Font.Size = Unit.FromPoint(8.5);
        body.ParagraphFormat.SpaceAfter = Unit.FromPoint(1.5);

        var small = document.Styles.AddStyle(SmallStyleName, StyleNames.Normal);
        small.Font.Size = Unit.FromPoint(7.5);
        small.Font.Color = Colors.Gray;
        small.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);

        var tableHeader = document.Styles.AddStyle(TableHeaderStyleName, StyleNames.Normal);
        tableHeader.Font.Size = Unit.FromPoint(8.1);
        tableHeader.Font.Bold = true;

        var denseBody = document.Styles.AddStyle(DenseBodyStyleName, StyleNames.Normal);
        denseBody.Font.Size = Unit.FromPoint(7.6);
        denseBody.ParagraphFormat.SpaceAfter = Unit.FromPoint(1);

        var code = document.Styles.AddStyle(CodeStyleName, StyleNames.Normal);
        code.Font.Name = "Courier New";
        code.Font.Size = Unit.FromPoint(6.6);
        code.ParagraphFormat.SpaceAfter = Unit.FromPoint(1);

        var denseCode = document.Styles.AddStyle(DenseCodeStyleName, StyleNames.Normal);
        denseCode.Font.Name = "Courier New";
        denseCode.Font.Size = Unit.FromPoint(6.0);
        denseCode.ParagraphFormat.SpaceAfter = Unit.FromPoint(1);
    }

    private static void AddCoverPage(Document document, ValidationSessionData data)
    {
        var section = AddSection(document);

        AddParagraph(section, "Sussex Session Review", TitleStyleName);
        var subtitle = AddParagraph(section, $"Generated {DateTimeOffset.UtcNow.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}", BodyStyleName);
        subtitle.Format.Font.Color = Colors.Gray;
        AddParagraph(
            section,
            "This report compares the Windows-side recorder with the Quest pullback from the same session. Read the executive summary first, then the coverage pages, then the charts.",
            SmallStyleName);

        AddKeyValueTable(section, "Session Identity",
        [
            ("participant", data.ParticipantId),
            ("session", data.SessionId),
            ("started", FormatTimestamp(data.SessionStartedAtUtc)),
            ("ended", FormatTimestamp(data.SessionEndedAtUtc)),
            ("duration", FormatDuration(data.SessionDuration)),
            ("package", data.PackageId),
            ("version", data.AppVersionName),
            ("quest selector", data.QuestSelector)
        ]);

        AddKeyValueTable(section, "Runtime Baseline",
        [
            ("apk hash", ShortHash(data.ApkSha256, 20)),
            ("headset build", data.HeadsetBuildId),
            ("headset display", data.HeadsetDisplayId),
            ("lsl stream", $"{data.LslStreamName} / {data.LslStreamType}"),
            ("windows machine", data.WindowsMachineName),
            ("session folder", data.SessionFolderPath)
        ], monospaceValue: true);
    }

    private static void AddExecutiveSummaryPage(Document document, ValidationSessionData data)
    {
        var section = AddSection(document, Orientation.Landscape);
        AddParagraph(section, "Executive Summary", SectionStyleName);
        AddParagraph(
            section,
            "These pages prioritize operator interpretation over raw artifacts: what completed, what looks partial, and what the current session can and cannot prove.",
            SmallStyleName);

        var presentQuestFiles = ExpectedQuestFiles.Count(fileName => data.DevicePullFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase));
        AddKeyValueTable(section, "Capture Snapshot",
        [
            ("quest pullback files", $"{presentQuestFiles}/{ExpectedQuestFiles.Length} expected files present"),
            ("signal rows", $"{data.WindowsSignalRowCount} Windows | {data.QuestSignalRowCount} Quest"),
            ("breathing rows", $"{data.WindowsBreathingRowCount} Windows | {data.QuestBreathingRowCount} Quest"),
            ("event rows", $"{data.WindowsEvents.Count} Windows | {data.QuestEvents.Count} Quest"),
            ("packet timing matches", data.PacketTiming.Matches.Count.ToString(CultureInfo.InvariantCulture)),
            ("clock samples", data.ClockAlignmentPoints.Count.ToString(CultureInfo.InvariantCulture)),
            ("tuning changes", data.SessionParameterChangeCount.ToString(CultureInfo.InvariantCulture))
        ], keyColumnWidthCm: 4.8);

        AddSessionAssessmentTable(section, BuildSessionAssessmentRows(data));

        var findings = BuildSessionFindings(data);
        if (findings.Count > 0)
        {
            AddSessionFindingTable(section, findings);
        }
    }

    private static void AddCoveragePage(Document document, ValidationSessionData data)
    {
        var section = AddSection(document);
        AddFileCoverageTable(
            section,
            "Windows Recorder Coverage",
            data.SessionFolderPath,
            ExpectedWindowsFiles.Select(fileName => new FileCoverageRow(fileName, data.LocalFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase), fileName)).ToArray());

        AddFileCoverageTable(
            section,
            "Quest Pullback Coverage",
            data.DevicePullFolderPath,
            ExpectedQuestFiles.Select(fileName => new FileCoverageRow(fileName, data.DevicePullFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase), fileName)).ToArray());

        AddKeyValueTable(section, "Recorder Summary",
        [
            ("Windows signals", data.WindowsSignalRowCount.ToString(CultureInfo.InvariantCulture)),
            ("Quest signals", data.QuestSignalRowCount.ToString(CultureInfo.InvariantCulture)),
            ("Windows breathing", data.WindowsBreathingRowCount.ToString(CultureInfo.InvariantCulture)),
            ("Quest breathing", data.QuestBreathingRowCount.ToString(CultureInfo.InvariantCulture)),
            ("windows events", data.WindowsEvents.Count.ToString(CultureInfo.InvariantCulture)),
            ("quest events", data.QuestEvents.Count.ToString(CultureInfo.InvariantCulture)),
            ("clock samples", data.ClockAlignmentPoints.Count.ToString(CultureInfo.InvariantCulture))
        ], keyColumnWidthCm: 4.8);

        AddKeyValueTable(section, "Timing Coverage",
        [
            ("windows signals span", FormatCoverageSpan(data.WindowsSignalCoverage)),
            ("quest signals span", FormatCoverageSpan(data.QuestSignalCoverage)),
            ("windows breathing span", FormatCoverageSpan(data.WindowsBreathingCoverage)),
            ("quest breathing span", FormatCoverageSpan(data.QuestBreathingCoverage))
        ]);

        AddCoverageComparisonTable(section, data);
    }

    private static void AddSessionAssessmentTable(
        Section section,
        IReadOnlyList<SessionAssessmentRow> rows)
    {
        AddParagraph(section, "Session Assessment", SectionStyleName);
        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(3.4));
        table.AddColumn(Unit.FromCentimeter(1.9));
        table.AddColumn(Unit.FromCentimeter(8.0));
        table.AddColumn(Unit.FromCentimeter(12.4));

        var header = table.AddRow();
        StyleHeaderRow(header);
        AddParagraph(header.Cells[0], "Area", TableHeaderStyleName);
        AddParagraph(header.Cells[1], "Status", TableHeaderStyleName);
        AddParagraph(header.Cells[2], "Evidence", TableHeaderStyleName);
        AddParagraph(header.Cells[3], "Interpretation", TableHeaderStyleName);

        var rowIndex = 0;
        foreach (var row in rows)
        {
            var tableRow = table.AddRow();
            StyleBodyRow(tableRow, rowIndex++);
            AddParagraph(tableRow.Cells[0], row.Area, BodyStyleName);
            AddParagraph(tableRow.Cells[1], row.Status, BodyStyleName);
            AddParagraph(tableRow.Cells[2], row.Evidence, DenseBodyStyleName);
            AddParagraph(tableRow.Cells[3], row.Interpretation, DenseBodyStyleName);
        }
    }

    private static void AddSessionFindingTable(
        Section section,
        IReadOnlyList<SessionFindingRow> rows)
    {
        AddParagraph(section, "Open Questions And Caveats", SectionStyleName);
        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(1.8));
        table.AddColumn(Unit.FromCentimeter(4.0));
        table.AddColumn(Unit.FromCentimeter(19.9));

        var header = table.AddRow();
        StyleHeaderRow(header);
        AddParagraph(header.Cells[0], "Level", TableHeaderStyleName);
        AddParagraph(header.Cells[1], "Topic", TableHeaderStyleName);
        AddParagraph(header.Cells[2], "Caveat", TableHeaderStyleName);

        var rowIndex = 0;
        foreach (var row in rows)
        {
            var tableRow = table.AddRow();
            StyleBodyRow(tableRow, rowIndex++);
            AddParagraph(tableRow.Cells[0], row.Level, BodyStyleName);
            AddParagraph(tableRow.Cells[1], row.Topic, BodyStyleName);
            AddParagraph(tableRow.Cells[2], row.Detail, DenseBodyStyleName);
        }
    }

    private static IReadOnlyList<SessionAssessmentRow> BuildSessionAssessmentRows(ValidationSessionData data)
    {
        var missingQuestFiles = ExpectedQuestFiles
            .Where(fileName => !data.DevicePullFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var rows = new List<SessionAssessmentRow>
        {
            new(
                "Quest pullback",
                missingQuestFiles.Length == 0 ? "OK" : "WARN",
                $"{ExpectedQuestFiles.Length - missingQuestFiles.Length}/{ExpectedQuestFiles.Length} expected Quest files present.",
                missingQuestFiles.Length == 0
                    ? "Quest-side plots and event review are backed by a full pullback snapshot."
                    : $"Missing {string.Join(", ", missingQuestFiles)}. Treat Quest-side evidence as partial."),
            BuildCoverageAssessmentRow(
                "Signals parity",
                data.WindowsSignalRowCount,
                data.QuestSignalRowCount,
                data.WindowsSignalCoverage,
                data.QuestSignalCoverage,
                "Windows and Quest signals_long.csv should usually remain broadly aligned through the run."),
            BuildCoverageAssessmentRow(
                "Breathing parity",
                data.WindowsBreathingRowCount,
                data.QuestBreathingRowCount,
                data.WindowsBreathingCoverage,
                data.QuestBreathingCoverage,
                "Breathing trace parity matters most for calibration and controller-side breathing analysis."),
            BuildPacketTimingAssessmentRow(data),
            BuildClockAlignmentAssessmentRow(data),
            BuildParameterAuditAssessmentRow(data)
        };

        return rows;
    }

    private static SessionAssessmentRow BuildCoverageAssessmentRow(
        string area,
        int windowsRows,
        int questRows,
        CoverageSpan windowsCoverage,
        CoverageSpan questCoverage,
        string healthyInterpretation)
    {
        if (windowsRows == 0 && questRows == 0)
        {
            return new SessionAssessmentRow(
                area,
                "INFO",
                "No rows were recorded on either side.",
                "This report cannot compare Windows and Quest parity for this dataset.");
        }

        if (windowsRows > 0 && questRows == 0)
        {
            return new SessionAssessmentRow(
                area,
                "WARN",
                $"Windows recorded {windowsRows} row(s) while Quest recorded none.",
                "The Quest-side recorder or pullback is missing for this dataset, so only the Windows-side trace can be trusted.");
        }

        var ratio = windowsRows > 0 ? (double)questRows / windowsRows : 1d;
        var evidence = $"Windows {windowsRows} row(s), Quest {questRows} row(s), Quest/Windows {FormatRatio(ratio)}. Windows span {FormatCoverageSpan(windowsCoverage)}. Quest span {FormatCoverageSpan(questCoverage)}.";
        var durationDeltaSeconds = ComputeDurationDeltaSeconds(questCoverage, windowsCoverage);

        if (ratio < 0.5d || durationDeltaSeconds < -2d)
        {
            return new SessionAssessmentRow(
                area,
                "WARN",
                evidence,
                "Quest-side capture is materially shorter or sparser than Windows, so side-by-side plots should be read as partial parity rather than full runtime equivalence.");
        }

        if (ratio < 0.9d || Math.Abs(durationDeltaSeconds) > 1d)
        {
            return new SessionAssessmentRow(
                area,
                "INFO",
                evidence,
                "The two sides are directionally aligned, but one recorder appears to have fewer samples or a shorter visible span.");
        }

        return new SessionAssessmentRow(area, "OK", evidence, healthyInterpretation);
    }

    private static SessionAssessmentRow BuildPacketTimingAssessmentRow(ValidationSessionData data)
    {
        var analysis = data.PacketTiming;
        if (analysis.Matches.Count > 0)
        {
            return new SessionAssessmentRow(
                "Packet timing",
                "OK",
                $"{analysis.Matches.Count} matched packet(s). Send -> orbit peak: {SummarizeMilliseconds(analysis.Matches.Select(match => match.OrbitPeakLatencyMs))}.",
                "The report can quantify upstream-send to Quest-peak timing from matched Windows upstream packets and Quest timing markers.");
        }

        if (analysis.WindowsOverlapCount > 0 && analysis.QuestOverlapCount > 0)
        {
            return new SessionAssessmentRow(
                "Packet timing",
                "WARN",
                $"Windows overlap rows {analysis.WindowsOverlapCount}, Quest overlap packet groups {analysis.QuestOverlapCount}, but zero matches.",
                "Both sides recorded overlapping timing windows, but the data could not be stitched into packet-level latency measurements.");
        }

        return new SessionAssessmentRow(
            "Packet timing",
            "INFO",
            "No matched packet timing evidence was available.",
            "One side was missing the required upstream or timing-marker overlap, so send-to-peak latency could not be computed for this session.");
    }

    private static SessionAssessmentRow BuildClockAlignmentAssessmentRow(ValidationSessionData data)
    {
        if (data.ClockAlignmentPoints.Count == 0)
        {
            return new SessionAssessmentRow(
                "Clock alignment",
                "WARN",
                "No clock-alignment samples were recorded.",
                "Packet timing and Quest-vs-Windows timestamp interpretation are weaker without probe-based clock-alignment evidence.");
        }

        return new SessionAssessmentRow(
            "Clock alignment",
            data.ClockAlignmentPoints.Count >= 3 ? "OK" : "INFO",
            $"{data.ClockAlignmentPoints.Count} sample(s), round-trip {SummarizePlainMilliseconds(data.ClockAlignmentPoints.Select(point => point.RoundTripMs))}.",
            "These samples anchor the Quest-minus-Windows offset used for the packet-timing pages.");
    }

    private static SessionAssessmentRow BuildParameterAuditAssessmentRow(ValidationSessionData data)
    {
        if (string.IsNullOrWhiteSpace(data.InitialSessionParameterStateHash) &&
            string.IsNullOrWhiteSpace(data.LatestSessionParameterStateHash))
        {
            return new SessionAssessmentRow(
                "Session tuning audit",
                "INFO",
                "This session did not persist the newer parameter-state payload.",
                "Profile names may still be present, but the per-parameter audit trail is incomplete on older builds.");
        }

        return new SessionAssessmentRow(
            "Session tuning audit",
            data.SessionParameterChangeCount > 0 ? "INFO" : "OK",
            $"Initial {ShortHash(data.InitialSessionParameterStateHash, 16)} | latest {ShortHash(data.LatestSessionParameterStateHash, 16)} | changes {data.SessionParameterChangeCount}.",
            data.SessionParameterChangeCount > 0
                ? "Review the tuning timeline and latest parameter pages to see exactly which settings changed during the run."
                : "No live parameter edits were recorded after the session started.");
    }

    private static IReadOnlyList<SessionFindingRow> BuildSessionFindings(ValidationSessionData data)
    {
        var findings = new List<SessionFindingRow>();
        var missingQuestFiles = ExpectedQuestFiles
            .Where(fileName => !data.DevicePullFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (missingQuestFiles.Length > 0)
        {
            findings.Add(new SessionFindingRow(
                "WARN",
                "Quest pullback",
                $"The Quest pullback is incomplete: missing {string.Join(", ", missingQuestFiles)}."));
        }

        if (data.WindowsSignalRowCount > 0 && data.QuestSignalRowCount == 0)
        {
            findings.Add(new SessionFindingRow(
                "WARN",
                "Quest signals",
                "Windows recorded signals_long.csv rows but the Quest pullback contains no Quest-side signal rows."));
        }

        if (data.WindowsBreathingRowCount > 0 && data.QuestBreathingRowCount == 0)
        {
            findings.Add(new SessionFindingRow(
                "WARN",
                "Quest breathing trace",
                "Windows recorded breathing_trace.csv rows but the Quest pullback contains no Quest-side breathing rows."));
        }

        if (data.PacketTiming.Matches.Count == 0 &&
            data.PacketTiming.WindowsOverlapCount > 0 &&
            data.PacketTiming.QuestOverlapCount > 0)
        {
            findings.Add(new SessionFindingRow(
                "WARN",
                "Packet timing",
                "Timing windows overlapped, but the report could not match Windows upstream samples to Quest timing markers."));
        }

        if (data.SessionParameterChangeCount > 0)
        {
            findings.Add(new SessionFindingRow(
                "INFO",
                "Live tuning changes",
                $"{data.SessionParameterChangeCount} parameter-change event(s) were recorded during the session."));
        }

        return findings
            .Take(8)
            .ToArray();
    }

    private static void AddCoverageComparisonTable(Section section, ValidationSessionData data)
    {
        AddParagraph(section, "Windows Vs Quest Coverage Delta", SectionStyleName);
        var rows = new[]
        {
            new CoverageComparisonRow(
                "signals_long.csv",
                data.WindowsSignalRowCount,
                data.QuestSignalRowCount,
                data.WindowsSignalCoverage,
                data.QuestSignalCoverage),
            new CoverageComparisonRow(
                "breathing_trace.csv",
                data.WindowsBreathingRowCount,
                data.QuestBreathingRowCount,
                data.WindowsBreathingCoverage,
                data.QuestBreathingCoverage),
            new CoverageComparisonRow(
                "session_events.csv",
                data.WindowsEvents.Count,
                data.QuestEvents.Count,
                BuildEventCoverageSpan(data.WindowsEvents),
                BuildEventCoverageSpan(data.QuestEvents))
        };

        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(4.0));
        table.AddColumn(Unit.FromCentimeter(2.0));
        table.AddColumn(Unit.FromCentimeter(2.0));
        table.AddColumn(Unit.FromCentimeter(2.3));
        table.AddColumn(Unit.FromCentimeter(5.6));
        table.AddColumn(Unit.FromCentimeter(5.6));
        table.AddColumn(Unit.FromCentimeter(4.3));

        var header = table.AddRow();
        StyleHeaderRow(header);
        AddParagraph(header.Cells[0], "Dataset", TableHeaderStyleName);
        AddParagraph(header.Cells[1], "Win", TableHeaderStyleName);
        AddParagraph(header.Cells[2], "Quest", TableHeaderStyleName);
        AddParagraph(header.Cells[3], "Quest/Win", TableHeaderStyleName);
        AddParagraph(header.Cells[4], "Windows span", TableHeaderStyleName);
        AddParagraph(header.Cells[5], "Quest span", TableHeaderStyleName);
        AddParagraph(header.Cells[6], "Interpretation", TableHeaderStyleName);

        var rowIndex = 0;
        foreach (var row in rows)
        {
            var tableRow = table.AddRow();
            StyleBodyRow(tableRow, rowIndex++);
            AddParagraph(tableRow.Cells[0], row.Dataset, DenseCodeStyleName);
            AddParagraph(tableRow.Cells[1], row.WindowsRows.ToString(CultureInfo.InvariantCulture), BodyStyleName);
            AddParagraph(tableRow.Cells[2], row.QuestRows.ToString(CultureInfo.InvariantCulture), BodyStyleName);
            AddParagraph(tableRow.Cells[3], FormatRatio(row.WindowsRows > 0 ? (double)row.QuestRows / row.WindowsRows : row.QuestRows > 0 ? 1d : 0d), BodyStyleName);
            AddParagraph(tableRow.Cells[4], FormatCoverageSpan(row.WindowsCoverage), DenseBodyStyleName);
            AddParagraph(tableRow.Cells[5], FormatCoverageSpan(row.QuestCoverage), DenseBodyStyleName);
            AddParagraph(tableRow.Cells[6], InterpretCoverageDelta(row), DenseBodyStyleName);
        }
    }

    private static CoverageSpan BuildEventCoverageSpan(IReadOnlyList<EventData> events)
    {
        if (events.Count == 0)
        {
            return new CoverageSpan(null, null);
        }

        return new CoverageSpan(events[0].TimestampUtc, events[^1].TimestampUtc);
    }

    private static string InterpretCoverageDelta(CoverageComparisonRow row)
    {
        if (row.WindowsRows == 0 && row.QuestRows == 0)
        {
            return "No rows recorded on either side.";
        }

        if (row.WindowsRows > 0 && row.QuestRows == 0)
        {
            return "Quest-side evidence missing.";
        }

        var ratio = row.WindowsRows > 0 ? (double)row.QuestRows / row.WindowsRows : 1d;
        var durationDelta = ComputeDurationDeltaSeconds(row.QuestCoverage, row.WindowsCoverage);
        if (ratio < 0.5d || durationDelta < -2d)
        {
            return "Quest-side coverage is materially shorter or sparser.";
        }

        if (ratio < 0.9d || Math.Abs(durationDelta) > 1d)
        {
            return "Coverage is usable but not tightly aligned.";
        }

        return "Coverage is broadly aligned.";
    }

    private static void AddSignalInventoryPage(Document document, ValidationSessionData data)
    {
        var section = AddSection(document, Orientation.Landscape);
        AddParagraph(section, "Recorded Signal Inventory", SectionStyleName);
        AddParagraph(
            section,
            "Every numeric series currently present in the Windows recorder or Quest pullback is listed below. The standard pages keep the core Sussex charts stable, and any remaining numeric series are auto-plotted afterward.",
            SmallStyleName);

        var rows = BuildSignalInventoryRows(data);
        if (rows.Count == 0)
        {
            AddParagraph(section, "No numeric signal inventory was available.", BodyStyleName);
            return;
        }

        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(2.3));
        table.AddColumn(Unit.FromCentimeter(6.0));
        table.AddColumn(Unit.FromCentimeter(2.0));
        table.AddColumn(Unit.FromCentimeter(2.2));
        table.AddColumn(Unit.FromCentimeter(2.2));
        table.AddColumn(Unit.FromCentimeter(5.6));
        table.AddColumn(Unit.FromCentimeter(5.6));

        var header = table.AddRow();
        StyleHeaderRow(header);
        AddParagraph(header.Cells[0], "Source", TableHeaderStyleName);
        AddParagraph(header.Cells[1], "Series", TableHeaderStyleName);
        AddParagraph(header.Cells[2], "Unit", TableHeaderStyleName);
        AddParagraph(header.Cells[3], "Win rows", TableHeaderStyleName);
        AddParagraph(header.Cells[4], "Quest rows", TableHeaderStyleName);
        AddParagraph(header.Cells[5], "Windows span", TableHeaderStyleName);
        AddParagraph(header.Cells[6], "Quest span", TableHeaderStyleName);

        var rowIndex = 0;
        foreach (var row in rows)
        {
            var tableRow = table.AddRow();
            StyleBodyRow(tableRow, rowIndex++);
            AddParagraph(tableRow.Cells[0], row.Source, BodyStyleName);
            AddParagraph(tableRow.Cells[1], row.Name, DenseCodeStyleName);
            AddParagraph(tableRow.Cells[2], row.Unit, BodyStyleName);
            AddParagraph(tableRow.Cells[3], row.WindowsSamples.ToString(CultureInfo.InvariantCulture), BodyStyleName);
            AddParagraph(tableRow.Cells[4], row.QuestSamples.ToString(CultureInfo.InvariantCulture), BodyStyleName);
            AddParagraph(tableRow.Cells[5], FormatCoverageSpan(row.WindowsCoverage), DenseBodyStyleName);
            AddParagraph(tableRow.Cells[6], FormatCoverageSpan(row.QuestCoverage), DenseBodyStyleName);
        }
    }

    private static IReadOnlyList<SignalInventoryRow> BuildSignalInventoryRows(ValidationSessionData data)
    {
        var rows = new List<SignalInventoryRow>();
        rows.AddRange(BuildSignalInventoryRows("signals_long", data.WindowsSignals, data.QuestSignals));
        rows.AddRange(BuildSignalInventoryRows("breathing_trace", data.WindowsBreathing, data.QuestBreathing));
        return rows
            .OrderBy(row => row.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<SignalInventoryRow> BuildSignalInventoryRows(
        string source,
        IReadOnlyDictionary<string, SeriesData> windowsSeries,
        IReadOnlyDictionary<string, SeriesData> questSeries)
    {
        var names = windowsSeries.Keys
            .Concat(questSeries.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            windowsSeries.TryGetValue(name, out var windows);
            questSeries.TryGetValue(name, out var quest);
            yield return new SignalInventoryRow(
                source,
                name,
                !string.IsNullOrWhiteSpace(windows?.Unit)
                    ? windows.Unit
                    : quest?.Unit ?? string.Empty,
                windows?.Values.Count ?? 0,
                quest?.Values.Count ?? 0,
                BuildSeriesCoverageSpan(windows),
                BuildSeriesCoverageSpan(quest));
        }
    }

    private static void AddSessionParameterStatePages(Document document, ValidationSessionData data)
    {
        var latestState = data.LatestSessionParameterState ?? data.InitialSessionParameterState;
        var section = AddSection(document);
        AddParagraph(section, "Session Parameter State", SectionStyleName);
        AddParagraph(
            section,
            "Participant-session recordings now persist the full Sussex tuning state, not just profile names. The summary below comes from session_settings.json, and any live edits or apply actions recorded during the session are listed afterward.",
            SmallStyleName);

        AddKeyValueTable(section, "State Capture Summary",
        [
            ("initial state hash", ShortHash(data.InitialSessionParameterStateHash, 24)),
            ("latest state hash", ShortHash(data.LatestSessionParameterStateHash, 24)),
            ("state updated", FormatTimestamp(data.SessionParameterStateUpdatedAtUtc)),
            ("tuning changes", data.SessionParameterChangeCount.ToString(CultureInfo.InvariantCulture))
        ], monospaceValue: true, keyColumnWidthCm: 4.8);

        if (latestState.HasValue &&
            TryGetNestedProperty(latestState.Value, out var runtimeHotload, "RuntimeHotloadProfile"))
        {
            AddKeyValueTable(section, "Runtime Hotload Profile",
            [
                ("id", GetPropertyString(runtimeHotload, "Id")),
                ("version", GetPropertyString(runtimeHotload, "Version")),
                ("channel", GetPropertyString(runtimeHotload, "Channel")),
                ("runtime config hash", ShortHash(GetPropertyString(runtimeHotload, "RuntimeConfigHash"), 24))
            ], monospaceValue: true);
        }

        if (!latestState.HasValue)
        {
            AddParagraph(
                section,
                "This session did not persist the newer parameter-state payload. Re-run the session with a newer companion build if you need a per-parameter audit trail in the PDF.",
                BodyStyleName);
        }

        var tuningEvents = BuildSessionParameterActivityRows(data.WindowsEvents);
        if (tuningEvents.Count > 0)
        {
            AddSessionParameterActivityPage(document, tuningEvents);
        }

        if (latestState.HasValue &&
            TryGetNestedProperty(latestState.Value, out var visualTuning, "VisualTuning"))
        {
            AddSessionParameterSurfacePage(document, "Visual Tuning", visualTuning, includeGroupColumn: false);
        }

        if (latestState.HasValue &&
            TryGetNestedProperty(latestState.Value, out var controllerTuning, "ControllerBreathingTuning"))
        {
            AddSessionParameterSurfacePage(document, "Controller Breathing Tuning", controllerTuning, includeGroupColumn: true);
        }
    }

    private static void AddSessionParameterActivityPage(
        Document document,
        IReadOnlyList<SessionParameterActivityRow> activityRows)
    {
        var section = AddSection(document, Orientation.Landscape);
        AddParagraph(section, "Tuning Change Timeline", SectionStyleName);
        AddParagraph(
            section,
            "These rows come from session_events.csv. They show draft edits and explicit apply actions captured while the participant-session recorder was running.",
            SmallStyleName);

        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(2.8));
        table.AddColumn(Unit.FromCentimeter(2.8));
        table.AddColumn(Unit.FromCentimeter(3.3));
        table.AddColumn(Unit.FromCentimeter(1.5));
        table.AddColumn(Unit.FromCentimeter(13.7));

        var header = table.AddRow();
        StyleHeaderRow(header);
        AddParagraph(header.Cells[0], "Recorded", TableHeaderStyleName);
        AddParagraph(header.Cells[1], "Surface / kind", TableHeaderStyleName);
        AddParagraph(header.Cells[2], "Profile", TableHeaderStyleName);
        AddParagraph(header.Cells[3], "Changes", TableHeaderStyleName);
        AddParagraph(header.Cells[4], "Summary", TableHeaderStyleName);

        var rowIndex = 0;
        foreach (var activity in activityRows)
        {
            var row = table.AddRow();
            StyleBodyRow(row, rowIndex++);
            AddParagraph(row.Cells[0], FormatTimestamp(activity.TimestampUtc), BodyStyleName);
            AddParagraph(row.Cells[1], $"{activity.Surface} / {activity.Kind}", DenseBodyStyleName);
            AddParagraph(row.Cells[2], string.IsNullOrWhiteSpace(activity.ProfileName) ? "n/a" : activity.ProfileName, DenseBodyStyleName);
            AddParagraph(row.Cells[3], activity.ChangeCount.ToString(CultureInfo.InvariantCulture), BodyStyleName);
            AddParagraph(row.Cells[4], activity.Summary, DenseBodyStyleName);
        }
    }

    private static void AddSessionParameterSurfacePage(
        Document document,
        string title,
        JsonElement tuningElement,
        bool includeGroupColumn)
    {
        var section = AddSection(document, Orientation.Landscape);
        AddParagraph(section, title, SectionStyleName);
        AddParagraph(
            section,
            "The table below records the latest known editor state, plus baseline, last-applied, and last-reported runtime values when available.",
            SmallStyleName);

        AddKeyValueTable(section, "Surface Summary",
        [
            ("current profile", GetNestedString(tuningElement, "CurrentProfile", "Document", "Profile", "Name")),
            ("effective profile", GetNestedString(tuningElement, "EffectiveProfile", "Document", "Profile", "Name")),
            ("startup profile", GetNestedString(tuningElement, "StartupProfile", "ProfileName")),
            ("last applied profile", GetNestedString(tuningElement, "LastApplyRecord", "ProfileName")),
            ("apply summary", GetPropertyString(tuningElement, "ApplySummary")),
            ("selected == applied", FormatBooleanLabel(GetPropertyBoolean(tuningElement, "SelectedMatchesLastApplied"))),
            ("has unapplied edits", FormatBooleanLabel(GetPropertyBoolean(tuningElement, "HasUnappliedEdits")))
        ], keyColumnWidthCm: 5.0);

        var applyDetail = GetPropertyString(tuningElement, "ApplyDetail");
        if (!string.IsNullOrWhiteSpace(applyDetail))
        {
            AddParagraph(section, "Apply Detail", SectionStyleName);
            AddParagraph(section, applyDetail, DenseBodyStyleName);
        }

        var controls = BuildSessionParameterControlRows(tuningElement);
        if (controls.Count == 0)
        {
            AddParagraph(section, "No control payload was available for this surface.", BodyStyleName);
            return;
        }

        AddParameterControlTable(section, controls, includeGroupColumn);
    }

    private static void AddParameterControlTable(
        Section section,
        IReadOnlyList<SessionParameterControlRow> controls,
        bool includeGroupColumn)
    {
        AddParagraph(section, "Recorded Parameters", SectionStyleName);
        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(6.2));
        if (includeGroupColumn)
        {
            table.AddColumn(Unit.FromCentimeter(2.6));
        }

        table.AddColumn(Unit.FromCentimeter(2.2));
        table.AddColumn(Unit.FromCentimeter(2.2));
        table.AddColumn(Unit.FromCentimeter(2.2));
        table.AddColumn(Unit.FromCentimeter(2.2));
        table.AddColumn(Unit.FromCentimeter(4.0));
        table.AddColumn(Unit.FromCentimeter(1.6));

        var header = table.AddRow();
        StyleHeaderRow(header);
        var cellIndex = 0;
        AddParagraph(header.Cells[cellIndex++], "Parameter", TableHeaderStyleName);
        if (includeGroupColumn)
        {
            AddParagraph(header.Cells[cellIndex++], "Group", TableHeaderStyleName);
        }

        AddParagraph(header.Cells[cellIndex++], "Current", TableHeaderStyleName);
        AddParagraph(header.Cells[cellIndex++], "Baseline", TableHeaderStyleName);
        AddParagraph(header.Cells[cellIndex++], "Applied", TableHeaderStyleName);
        AddParagraph(header.Cells[cellIndex++], "Reported", TableHeaderStyleName);
        AddParagraph(header.Cells[cellIndex++], "Safe range", TableHeaderStyleName);
        AddParagraph(header.Cells[cellIndex], "Unit", TableHeaderStyleName);

        var rowIndex = 0;
        foreach (var control in controls)
        {
            var row = table.AddRow();
            StyleBodyRow(row, rowIndex++);
            cellIndex = 0;
            AddParagraph(row.Cells[cellIndex++], $"{control.Label}\n{control.Id}", DenseBodyStyleName);
            if (includeGroupColumn)
            {
                AddParagraph(row.Cells[cellIndex++], string.IsNullOrWhiteSpace(control.Group) ? "n/a" : control.Group, DenseBodyStyleName);
            }

            AddParagraph(row.Cells[cellIndex++], FormatParameterValue(control.CurrentValue, control.Type), BodyStyleName);
            AddParagraph(row.Cells[cellIndex++], FormatParameterValue(control.BaselineValue, control.Type), BodyStyleName);
            AddParagraph(row.Cells[cellIndex++], FormatParameterValue(control.AppliedValue, control.Type), BodyStyleName);
            AddParagraph(row.Cells[cellIndex++], FormatParameterValue(control.ReportedValue, control.Type), BodyStyleName);
            AddParagraph(row.Cells[cellIndex++], $"{FormatParameterValue(control.SafeMinimum, control.Type)} .. {FormatParameterValue(control.SafeMaximum, control.Type)}", DenseBodyStyleName);
            AddParagraph(row.Cells[cellIndex], string.IsNullOrWhiteSpace(control.Units) ? "n/a" : control.Units, DenseBodyStyleName);
        }
    }

    private static void AddAdditionalSignalChartPages(
        Document document,
        ValidationSessionData data,
        ISet<string> plottedSignalNames)
    {
        var remainingSignals = data.WindowsSignals.Keys
            .Concat(data.QuestSignals.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !plottedSignalNames.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var signalName in remainingSignals)
        {
            data.WindowsSignals.TryGetValue(signalName, out var windowsSeries);
            data.QuestSignals.TryGetValue(signalName, out var questSeries);
            var unit = !string.IsNullOrWhiteSpace(windowsSeries?.Unit)
                ? windowsSeries.Unit
                : questSeries?.Unit ?? string.Empty;
            var windowsCount = windowsSeries?.Values.Count ?? 0;
            var questCount = questSeries?.Values.Count ?? 0;
            if (Math.Max(windowsCount, questCount) < 2)
            {
                continue;
            }

            var clampToUnitRange = ShouldClampSeriesToUnitRange(signalName, unit);

            AddSignalChartPage(
                document,
                data,
                HumanizeSignalName(signalName),
                $"Additional telemetry from signals_long.csv for {signalName}. Windows {windowsCount} sample(s), Quest {questCount} sample(s), unit {HumanizeUnitLabel(unit)}.",
                signalName,
                clampToUnitRange ? 0d : null,
                clampToUnitRange ? 1d : null);
        }
    }

    private static void AddAdditionalBreathingChartPages(
        Document document,
        ValidationSessionData data,
        ISet<string> plottedBreathingNames)
    {
        var remainingSignals = data.WindowsBreathing.Keys
            .Concat(data.QuestBreathing.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !plottedBreathingNames.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var signalName in remainingSignals)
        {
            data.WindowsBreathing.TryGetValue(signalName, out var windowsSeries);
            data.QuestBreathing.TryGetValue(signalName, out var questSeries);
            var unit = !string.IsNullOrWhiteSpace(windowsSeries?.Unit)
                ? windowsSeries.Unit
                : questSeries?.Unit ?? string.Empty;
            var windowsCount = windowsSeries?.Values.Count ?? 0;
            var questCount = questSeries?.Values.Count ?? 0;
            if (Math.Max(windowsCount, questCount) < 2)
            {
                continue;
            }

            var clampToUnitRange = ShouldClampSeriesToUnitRange(signalName, unit);

            AddBreathingChartPage(
                document,
                data,
                HumanizeSignalName(signalName),
                $"Additional telemetry from breathing_trace.csv for {signalName}. Windows {windowsCount} sample(s), Quest {questCount} sample(s), unit {HumanizeUnitLabel(unit)}.",
                signalName,
                clampToUnitRange ? 0d : null,
                clampToUnitRange ? 1d : null);
        }
    }

    private static IReadOnlyList<SessionParameterControlRow> BuildSessionParameterControlRows(JsonElement tuningElement)
    {
        if (!TryGetNestedProperty(tuningElement, out var documentElement, "CurrentProfile", "Document") &&
            !TryGetNestedProperty(tuningElement, out documentElement, "EffectiveProfile", "Document"))
        {
            return Array.Empty<SessionParameterControlRow>();
        }

        if (!TryGetNestedProperty(documentElement, out var controlsElement, "Controls") ||
            controlsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SessionParameterControlRow>();
        }

        var reportedValues = ReadObjectNumericDictionary(tuningElement, "ReportedValues");
        var appliedValues = ReadNestedObjectNumericDictionary(tuningElement, "LastApplyRecord", "RequestedValues");
        var rows = new List<SessionParameterControlRow>();

        foreach (var control in controlsElement.EnumerateArray())
        {
            if (control.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = GetPropertyString(control, "Id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var type = GetPropertyString(control, "Type");
            rows.Add(new SessionParameterControlRow(
                Group: GetPropertyString(control, "Group"),
                Label: string.IsNullOrWhiteSpace(GetPropertyString(control, "Label")) ? id : GetPropertyString(control, "Label"),
                Id: id,
                Type: type,
                CurrentValue: GetPropertyDouble(control, "Value"),
                BaselineValue: GetPropertyDouble(control, "BaselineValue"),
                AppliedValue: appliedValues.TryGetValue(id, out var appliedValue) ? appliedValue : null,
                ReportedValue: reportedValues.TryGetValue(id, out var reportedValue) ? reportedValue : null,
                SafeMinimum: GetPropertyDouble(control, "SafeMinimum"),
                SafeMaximum: GetPropertyDouble(control, "SafeMaximum"),
                Units: GetPropertyString(control, "Units")));
        }

        return rows
            .OrderBy(row => row.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SessionParameterActivityRow> BuildSessionParameterActivityRows(
        IReadOnlyList<EventData> windowsEvents)
    {
        var rows = new List<SessionParameterActivityRow>();
        foreach (var evt in windowsEvents.Where(evt => evt.EventName.StartsWith("tuning.", StringComparison.OrdinalIgnoreCase)))
        {
            var tokens = evt.EventName.Split('.', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var fallbackSurface = tokens.Length >= 2 ? tokens[1] : "unknown";
            var fallbackKind = tokens.Length >= 3 ? tokens[2] : "activity";
            var profileName = string.Empty;
            var summary = string.Empty;
            var changeCount = 0;
            var surface = fallbackSurface;
            var kind = fallbackKind;

            if (!string.IsNullOrWhiteSpace(evt.Detail))
            {
                try
                {
                    using var document = JsonDocument.Parse(evt.Detail);
                    var root = document.RootElement;
                    surface = string.IsNullOrWhiteSpace(GetPropertyString(root, "Surface"))
                        ? fallbackSurface
                        : GetPropertyString(root, "Surface");
                    kind = string.IsNullOrWhiteSpace(GetPropertyString(root, "Kind"))
                        ? fallbackKind
                        : GetPropertyString(root, "Kind");
                    profileName = GetPropertyString(root, "ProfileName");
                    summary = GetPropertyString(root, "Summary");
                    changeCount = TryGetProperty(root, "Changes", out var changesElement) && changesElement.ValueKind == JsonValueKind.Array
                        ? changesElement.GetArrayLength()
                        : 0;

                    if (string.IsNullOrWhiteSpace(summary))
                    {
                        summary = BuildActivityChangeSummary(root, evt.Detail);
                    }
                }
                catch (JsonException)
                {
                    summary = evt.Detail;
                }
            }

            if (string.IsNullOrWhiteSpace(summary))
            {
                summary = "Recorded session tuning activity.";
            }

            rows.Add(new SessionParameterActivityRow(evt.TimestampUtc, surface, kind, profileName, changeCount, summary));
        }

        return rows
            .OrderBy(row => row.TimestampUtc)
            .ToArray();
    }

    private static string BuildActivityChangeSummary(JsonElement root, string fallbackDetail)
    {
        if (TryGetProperty(root, "Changes", out var changesElement) &&
            changesElement.ValueKind == JsonValueKind.Array)
        {
            var parts = changesElement.EnumerateArray()
                .Take(3)
                .Select(change =>
                {
                    var label = string.IsNullOrWhiteSpace(GetPropertyString(change, "Label"))
                        ? GetPropertyString(change, "Id")
                        : GetPropertyString(change, "Label");
                    var previous = GetPropertyString(change, "PreviousLabel");
                    var current = GetPropertyString(change, "CurrentLabel");
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        return string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(previous) || string.Equals(previous, "n/a", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"{label}={current}";
                    }

                    return $"{label}: {previous} -> {current}";
                })
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();

            if (parts.Length > 0)
            {
                var extraCount = Math.Max(0, changesElement.GetArrayLength() - parts.Length);
                return extraCount > 0
                    ? $"{string.Join("; ", parts)} (+{extraCount} more)"
                    : string.Join("; ", parts);
            }
        }

        var detail = GetPropertyString(root, "Detail");
        return string.IsNullOrWhiteSpace(detail) ? fallbackDetail : detail;
    }

    private static IReadOnlyDictionary<string, double?> ReadObjectNumericDictionary(
        JsonElement parent,
        string propertyName)
    {
        if (!TryGetProperty(parent, propertyName, out var objectElement) ||
            objectElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in objectElement.EnumerateObject())
        {
            values[property.Name] = GetElementDouble(property.Value);
        }

        return values;
    }

    private static IReadOnlyDictionary<string, double?> ReadNestedObjectNumericDictionary(
        JsonElement parent,
        params string[] path)
    {
        if (!TryGetNestedProperty(parent, out var objectElement, path) ||
            objectElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in objectElement.EnumerateObject())
        {
            values[property.Name] = GetElementDouble(property.Value);
        }

        return values;
    }

    private static CoverageSpan BuildSeriesCoverageSpan(SeriesData? series)
    {
        if (series is null)
        {
            return new CoverageSpan(null, null);
        }

        var timestamps = series.Points
            .Select(point => point.TimestampUtc)
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .OrderBy(timestamp => timestamp)
            .ToArray();

        return timestamps.Length == 0
            ? new CoverageSpan(null, null)
            : new CoverageSpan(timestamps[0], timestamps[^1]);
    }

    private static bool ShouldClampSeriesToUnitRange(string signalName, string unit)
        => string.Equals(unit, "unit01", StringComparison.OrdinalIgnoreCase)
           || string.Equals(unit, "bool", StringComparison.OrdinalIgnoreCase)
           || signalName.EndsWith("value01", StringComparison.OrdinalIgnoreCase)
           || signalName.EndsWith("progress01", StringComparison.OrdinalIgnoreCase)
           || signalName.EndsWith("_active", StringComparison.OrdinalIgnoreCase)
           || signalName.EndsWith("_calibrated", StringComparison.OrdinalIgnoreCase);

    private static string HumanizeSignalName(string signalName)
        => string.Join(
            " ",
            signalName
                .Split(['.', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(token => token.ToLowerInvariant() switch
                {
                    "lsl" => "LSL",
                    "fps" => "FPS",
                    "ms" => "Ms",
                    "qx" => "QX",
                    "qy" => "QY",
                    "qz" => "QZ",
                    "qw" => "QW",
                    _ => char.ToUpperInvariant(token[0]) + token[1..]
                }));

    private static string FormatParameterValue(double? value, string? type)
    {
        if (!value.HasValue)
        {
            return "n/a";
        }

        if (string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return value.Value >= 0.5d ? "On" : "Off";
        }

        if (string.Equals(type, "int", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Round(value.Value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
        }

        return value.Value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatBooleanLabel(bool? value)
        => value.HasValue
            ? (value.Value ? "Yes" : "No")
            : "n/a";

    private static bool TryGetNestedProperty(JsonElement element, out JsonElement value, params string[] path)
    {
        value = element;
        foreach (var segment in path)
        {
            if (!TryGetProperty(value, segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string GetNestedString(JsonElement element, params string[] path)
        => TryGetNestedProperty(element, out var value, path)
            ? GetElementString(value)
            : string.Empty;

    private static string GetPropertyString(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var value)
            ? GetElementString(value)
            : string.Empty;

    private static bool? GetPropertyBoolean(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var value)
            ? GetElementBoolean(value)
            : null;

    private static double? GetPropertyDouble(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var value)
            ? GetElementDouble(value)
            : null;

    private static string GetElementString(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => element.ToString()
        };

    private static bool? GetElementBoolean(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetDouble(out var numeric) => Math.Abs(numeric) >= 0.5d,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null
        };

    private static double? GetElementDouble(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDouble(out var numeric) => numeric,
            JsonValueKind.True => 1d,
            JsonValueKind.False => 0d,
            JsonValueKind.String when double.TryParse(
                element.GetString(),
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };

    private static void AddMilestonePage(Document document, ValidationSessionData data)
    {
        var section = AddSection(document);
        AddParagraph(section, "Run Milestones", SectionStyleName);

        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(3.7));
        table.AddColumn(Unit.FromCentimeter(2.7));
        table.AddColumn(Unit.FromCentimeter(11.0));

        var header = table.AddRow();
        StyleHeaderRow(header);
        AddParagraph(header.Cells[0], "Event", TableHeaderStyleName);
        AddParagraph(header.Cells[1], "Offset", TableHeaderStyleName);
        AddParagraph(header.Cells[2], "Detail", TableHeaderStyleName);

        var rowIndex = 0;
        foreach (var milestone in data.BuildMilestones())
        {
            var row = table.AddRow();
            StyleBodyRow(row, rowIndex++);
            AddParagraph(row.Cells[0], milestone.Label, BodyStyleName);
            AddParagraph(row.Cells[1], milestone.OffsetLabel, BodyStyleName);
            AddParagraph(row.Cells[2], milestone.Detail, DenseBodyStyleName);
        }

        if (data.QuestEvents.Count > 0)
        {
            AddParagraph(section, "Quest Event Snapshot", SectionStyleName);
            AddQuestEventTable(section, data.QuestEvents.Take(8).ToArray());
        }
    }

    private static bool AddSignalChartPage(
        Document document,
        ValidationSessionData data,
        string title,
        string subtitle,
        string signalName,
        double? yMinimum = null,
        double? yMaximum = null)
    {
        var windowsSeries = data.WindowsSignals.TryGetValue(signalName, out var localSeries)
            ? localSeries
            : SeriesData.Empty(signalName, string.Empty);
        var questSeries = data.QuestSignals.TryGetValue(signalName, out var questSignalSeries)
            ? questSignalSeries
            : SeriesData.Empty(signalName, string.Empty);
        if (windowsSeries.Values.Count == 0 && questSeries.Values.Count == 0)
        {
            return false;
        }

        AddSeriesChartPage(
            document,
            title,
            subtitle,
            CreateSeriesSpec($"Windows {signalName}", windowsSeries, Colors.DodgerBlue),
            CreateSeriesSpec($"Quest {signalName}", questSeries, Colors.Orange),
            yMinimum,
            yMaximum);
        return true;
    }

    private static bool AddBreathingChartPage(
        Document document,
        ValidationSessionData data,
        string title,
        string subtitle,
        string signalName,
        double? yMinimum = null,
        double? yMaximum = null)
    {
        var windowsSeries = data.WindowsBreathing.TryGetValue(signalName, out var localSeries)
            ? localSeries
            : SeriesData.Empty(signalName, string.Empty);
        var questSeries = data.QuestBreathing.TryGetValue(signalName, out var questBreathingSeries)
            ? questBreathingSeries
            : SeriesData.Empty(signalName, string.Empty);
        if (windowsSeries.Values.Count == 0 && questSeries.Values.Count == 0)
        {
            return false;
        }

        AddSeriesChartPage(
            document,
            title,
            subtitle,
            CreateSeriesSpec($"Windows {signalName}", windowsSeries, Colors.DodgerBlue),
            CreateSeriesSpec($"Quest {signalName}", questSeries, Colors.Orange),
            yMinimum,
            yMaximum);
        return true;
    }

    private static void AddPacketTimingPage(Document document, ValidationSessionData data)
    {
        var section = AddSection(document, Orientation.Landscape);
        AddParagraph(section, "Packet Delivery To Orbit Peak", SectionStyleName);
        AddParagraph(
            section,
            "These timings use Windows upstream LSL packet timestamps plus Windows-captured Quest timing markers when present, with pulled Quest timing markers as the backup. They do not use the held mirror values in signals_long.csv.",
            SmallStyleName);

        var analysis = data.PacketTiming;
        AddKeyValueTable(section, "Packet Timing Summary",
        [
            ("Windows upstream", analysis.WindowsOverlapCount.ToString(CultureInfo.InvariantCulture)),
            ("Quest packet groups", analysis.QuestOverlapCount.ToString(CultureInfo.InvariantCulture)),
            ("matched packets", analysis.Matches.Count.ToString(CultureInfo.InvariantCulture)),
            ("unmatched Windows", analysis.WindowsOnlyCount.ToString(CultureInfo.InvariantCulture)),
            ("unmatched Quest", analysis.QuestOnlyCount.ToString(CultureInfo.InvariantCulture)),
            ("Quest minus Windows", FormatSeconds(analysis.QuestMinusWindowsClockSeconds)),
            ("send -> quest receive", SummarizeMilliseconds(analysis.Matches.Select(match => match.QuestReceiveLatencyMs))),
            ("send -> coherence publish", SummarizeMilliseconds(analysis.Matches.Select(match => match.CoherencePublishLatencyMs))),
            ("send -> orbit peak", SummarizeMilliseconds(analysis.Matches.Select(match => match.OrbitPeakLatencyMs))),
            ("quest receive -> orbit peak", SummarizeMilliseconds(analysis.Matches.Select(match => match.QuestReceiveToOrbitPeakMs)))
        ], keyColumnWidthCm: 5.2);

        AddSeriesChartPage(
            document,
            "Send To Orbit Peak Latency",
            "Milliseconds from the Windows-side LSL packet timestamp to the Quest orbit peak and Quest receive-to-peak pipeline.",
            CreateSeriesSpec(
                "send -> orbit peak ms",
                BuildPacketTimingSeries("orbit_peak_latency_ms", "milliseconds", analysis.Matches, match => match.OrbitPeakLatencyMs),
                Colors.MediumSeaGreen),
            CreateSeriesSpec(
                "quest receive -> orbit peak ms",
                BuildPacketTimingSeries("quest_receive_to_orbit_peak_ms", "milliseconds", analysis.Matches, match => match.QuestReceiveToOrbitPeakMs),
                Colors.DodgerBlue),
            yMinimum: 0);
    }

    private static void AddSeriesChartPage(
        Document document,
        string title,
        string subtitle,
        ChartSeriesSpec firstSeries,
        ChartSeriesSpec secondSeries,
        double? yMinimum = null,
        double? yMaximum = null)
    {
        var section = AddSection(document, Orientation.Landscape);
        AddParagraph(section, title, SectionStyleName);
        AddParagraph(section, subtitle, SmallStyleName);

        if (firstSeries.Data.Values.Count == 0 && secondSeries.Data.Values.Count == 0)
        {
            AddParagraph(section, "No samples were available for this chart.", BodyStyleName);
            return;
        }

        var chart = section.AddChart(ChartType.Line);
        chart.Width = Unit.FromCentimeter(24);
        chart.Height = Unit.FromCentimeter(11.3);
        chart.LineFormat.Width = Unit.FromPoint(0.75);
        chart.LineFormat.Color = Colors.LightGray;
        chart.PlotArea.LineFormat.Width = Unit.FromPoint(0.75);
        chart.PlotArea.LineFormat.Color = Colors.LightGray;
        chart.XAxis.Title.Caption = "Sample";
        chart.YAxis.Title.Caption = HumanizeUnitLabel(string.IsNullOrWhiteSpace(firstSeries.Data.Unit) ? secondSeries.Data.Unit : firstSeries.Data.Unit);
        chart.YAxis.MajorTickMark = TickMarkType.Outside;
        chart.YAxis.MinorTickMark = TickMarkType.None;
        chart.YAxis.HasMajorGridlines = true;
        chart.YAxis.HasMinorGridlines = false;

        var reducedFirst = ReduceSeries(firstSeries.Data.Values, 180);
        var reducedSecond = ReduceSeries(secondSeries.Data.Values, 180);
        var chartableFirst = reducedFirst.Count >= 2;
        var chartableSecond = reducedSecond.Count >= 2;
        var maxPointCount = Math.Max(chartableFirst ? reducedFirst.Count : 0, chartableSecond ? reducedSecond.Count : 0);
        if (maxPointCount == 0)
        {
            AddParagraph(
                section,
                BuildInsufficientSampleMessage(
                    "Not enough samples were recorded to draw a line chart.",
                    [
                        (firstSeries.Label, reducedFirst.Count),
                        (secondSeries.Label, reducedSecond.Count)
                    ]),
                BodyStyleName);
            return;
        }

        ConfigureChartYAxis(
            chart.YAxis,
            reducedFirst.Concat(reducedSecond),
            yMinimum,
            yMaximum,
            string.IsNullOrWhiteSpace(firstSeries.Data.Unit) ? secondSeries.Data.Unit : firstSeries.Data.Unit);

        var xSeries = chart.XValues.AddXSeries();
        foreach (var label in BuildSampleLabels(maxPointCount))
        {
            xSeries.Add(label);
        }

        if (chartableFirst)
        {
            AddLineSeries(chart, firstSeries.Label, reducedFirst, firstSeries.Color);
        }

        if (chartableSecond)
        {
            AddLineSeries(chart, secondSeries.Label, reducedSecond, secondSeries.Color);
        }

        var legend = chart.RightArea.AddLegend();
        legend.LineFormat.Color = Colors.LightGray;
        legend.LineFormat.Width = Unit.FromPoint(0.5);

        if (!chartableFirst || !chartableSecond)
        {
            AddParagraph(
                section,
                BuildInsufficientSampleMessage(
                    "Skipped line-series rendering for data with fewer than 2 samples.",
                    [
                        (firstSeries.Label, reducedFirst.Count),
                        (secondSeries.Label, reducedSecond.Count)
                    ]),
                SmallStyleName);
        }

        var sampleStrength = DescribeSampleStrength(reducedFirst.Count, reducedSecond.Count);
        if (!string.IsNullOrWhiteSpace(sampleStrength))
        {
            AddParagraph(section, sampleStrength, SmallStyleName);
        }

        AddSeriesStatisticsTable(section, firstSeries, secondSeries);
        AddParagraph(
            section,
            BuildSeriesCoverageSummary(firstSeries, secondSeries),
            SmallStyleName);
    }

    private static void AddClockAlignmentChartPage(
        Document document,
        string title,
        string subtitle,
        IReadOnlyList<double> values,
        Color color)
    {
        var section = AddSection(document, Orientation.Landscape);
        AddParagraph(section, title, SectionStyleName);
        AddParagraph(section, subtitle, SmallStyleName);

        if (values.Count == 0)
        {
            AddParagraph(section, "No clock-alignment samples were recorded.", BodyStyleName);
            return;
        }

        var reduced = ReduceSeries(values, 180);
        if (reduced.Count < 2)
        {
            AddParagraph(
                section,
                $"Only {reduced.Count} clock-alignment sample was recorded, so the line chart was skipped.",
                BodyStyleName);
            AddKeyValueTable(section, "Clock Alignment Summary",
            [
                ("sample count", values.Count.ToString(CultureInfo.InvariantCulture)),
                ("mean", values.Average().ToString("0.###", CultureInfo.InvariantCulture)),
                ("min", values.Min().ToString("0.###", CultureInfo.InvariantCulture)),
                ("max", values.Max().ToString("0.###", CultureInfo.InvariantCulture))
            ]);
            return;
        }

        var chart = section.AddChart(ChartType.Line);
        chart.Width = Unit.FromCentimeter(24);
        chart.Height = Unit.FromCentimeter(11.3);
        chart.LineFormat.Width = Unit.FromPoint(0.75);
        chart.LineFormat.Color = Colors.LightGray;
        chart.PlotArea.LineFormat.Width = Unit.FromPoint(0.75);
        chart.PlotArea.LineFormat.Color = Colors.LightGray;
        chart.XAxis.Title.Caption = "Probe Sequence";
        chart.YAxis.Title.Caption = title.Contains("Round-Trip", StringComparison.OrdinalIgnoreCase) ? "ms" : "Residual ms";
        chart.YAxis.MajorTickMark = TickMarkType.Outside;
        chart.YAxis.MinorTickMark = TickMarkType.None;
        chart.YAxis.HasMajorGridlines = true;
        chart.YAxis.HasMinorGridlines = false;
        ConfigureChartYAxis(chart.YAxis, reduced, null, null, "milliseconds");

        var xSeries = chart.XValues.AddXSeries();
        foreach (var label in BuildSampleLabels(reduced.Count))
        {
            xSeries.Add(label);
        }

        AddLineSeries(chart, title, reduced, color);
        AddSeriesStatisticsTable(section, CreateSeriesSpec(title, new SeriesData(title, "milliseconds", values.Select(value => new SeriesPoint(null, value)).ToArray()), color));

        AddKeyValueTable(section, "Clock Alignment Summary",
        [
            ("sample count", values.Count.ToString(CultureInfo.InvariantCulture)),
            ("mean", values.Count == 0 ? "n/a" : values.Average().ToString("0.###", CultureInfo.InvariantCulture)),
            ("min", values.Count == 0 ? "n/a" : values.Min().ToString("0.###", CultureInfo.InvariantCulture)),
            ("max", values.Count == 0 ? "n/a" : values.Max().ToString("0.###", CultureInfo.InvariantCulture))
        ], keyColumnWidthCm: 4.8);
    }

    private static void AddLineSeries(Chart chart, string label, IReadOnlyList<double> values, Color color)
    {
        if (values.Count == 0)
        {
            return;
        }

        var series = chart.SeriesCollection.AddSeries();
        series.Name = label;
        series.ChartType = ChartType.Line;
        series.MarkerStyle = MarkerStyle.None;
        series.LineFormat.Color = color;
        series.LineFormat.Width = Unit.FromPoint(1.25);
        foreach (var value in values)
        {
            series.Add(value);
        }
    }

    private static void AddFileCoverageTable(
        Section section,
        string heading,
        string folderPath,
        IReadOnlyList<FileCoverageRow> rows)
    {
        AddParagraph(section, heading, SectionStyleName);
        AddParagraph(section, folderPath, CodeStyleName);

        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(4.2));
        table.AddColumn(Unit.FromCentimeter(2.1));
        table.AddColumn(Unit.FromCentimeter(11.0));

        var header = table.AddRow();
        StyleHeaderRow(header);
        AddParagraph(header.Cells[0], "Artifact", TableHeaderStyleName);
        AddParagraph(header.Cells[1], "Status", TableHeaderStyleName);
        AddParagraph(header.Cells[2], "Detail", TableHeaderStyleName);

        var rowIndex = 0;
        foreach (var coverage in rows)
        {
            var row = table.AddRow();
            StyleBodyRow(row, rowIndex++);
            AddParagraph(row.Cells[0], coverage.FileName, DenseCodeStyleName);
            AddParagraph(row.Cells[1], coverage.Present ? "present" : "missing", BodyStyleName);
            AddParagraph(row.Cells[2], coverage.Detail, DenseBodyStyleName);
        }
    }

    private static void AddKeyValueTable(
        Section section,
        string heading,
        IEnumerable<(string Key, string Value)> rows,
        bool monospaceValue = false,
        double keyColumnWidthCm = 4.6)
    {
        AddParagraph(section, heading, SectionStyleName);
        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(keyColumnWidthCm));
        table.AddColumn(Unit.FromCentimeter(17.2 - keyColumnWidthCm));

        var rowIndex = 0;
        foreach (var (key, value) in rows)
        {
            var row = table.AddRow();
            StyleBodyRow(row, rowIndex++);
            AddParagraph(row.Cells[0], key, BodyStyleName).Format.Font.Bold = true;
            AddParagraph(row.Cells[1], value ?? "n/a", monospaceValue ? DenseCodeStyleName : BodyStyleName);
        }
    }

    private static void AddQuestEventTable(Section section, IReadOnlyList<EventData> events)
    {
        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(3.4));
        table.AddColumn(Unit.FromCentimeter(3.0));
        table.AddColumn(Unit.FromCentimeter(10.4));

        var header = table.AddRow();
        StyleHeaderRow(header);
        AddParagraph(header.Cells[0], "Event", TableHeaderStyleName);
        AddParagraph(header.Cells[1], "Recorded", TableHeaderStyleName);
        AddParagraph(header.Cells[2], "Detail", TableHeaderStyleName);

        var rowIndex = 0;
        foreach (var evt in events)
        {
            var row = table.AddRow();
            StyleBodyRow(row, rowIndex++);
            AddParagraph(row.Cells[0], evt.EventName, DenseCodeStyleName);
            AddParagraph(row.Cells[1], FormatTimestamp(evt.TimestampUtc), BodyStyleName);
            AddParagraph(row.Cells[2], string.IsNullOrWhiteSpace(evt.Detail) ? "Recorded." : evt.Detail, DenseBodyStyleName);
        }
    }

    private static Table CreateTable(Section section)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Colors.LightGray;
        table.Rows.LeftIndent = 0;
        table.TopPadding = Unit.FromPoint(3);
        table.BottomPadding = Unit.FromPoint(3);
        return table;
    }

    private static void StyleHeaderRow(Row row)
    {
        row.HeadingFormat = true;
        row.Shading.Color = Colors.Gainsboro;
        row.Format.Font.Bold = true;
        row.VerticalAlignment = VerticalAlignment.Center;
        row.TopPadding = Unit.FromPoint(3);
        row.BottomPadding = Unit.FromPoint(3);
    }

    private static void StyleBodyRow(Row row, int rowIndex)
    {
        row.VerticalAlignment = VerticalAlignment.Top;
        row.TopPadding = Unit.FromPoint(2);
        row.BottomPadding = Unit.FromPoint(2);
        if (rowIndex % 2 == 1)
        {
            row.Shading.Color = Colors.WhiteSmoke;
        }
    }

    private static Section AddSection(Document document, Orientation orientation = Orientation.Portrait)
    {
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.3);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.1);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.4);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.4);
        section.PageSetup.Orientation = orientation;
        AddFooter(section);
        return section;
    }

    private static void AddFooter(Section section)
    {
        var paragraph = section.Footers.Primary.AddParagraph();
        paragraph.Style = SmallStyleName;
        paragraph.Format.Alignment = ParagraphAlignment.Right;
        paragraph.AddText("Page ");
        paragraph.AddPageField();
        paragraph.AddText(" of ");
        paragraph.AddNumPagesField();
    }

    private static Paragraph AddParagraph(Section section, string text, string styleName)
    {
        var paragraph = section.AddParagraph(PrepareParagraphText(text, styleName));
        paragraph.Style = styleName;
        return paragraph;
    }

    private static Paragraph AddParagraph(Cell cell, string text, string styleName)
    {
        var paragraph = cell.AddParagraph(PrepareParagraphText(text, styleName));
        paragraph.Style = styleName;
        return paragraph;
    }

    private static string PrepareParagraphText(string text, string styleName)
        => PdfTextLayoutHelper.PrepareForParagraph(
            text,
            preserveCodeShape:
                string.Equals(styleName, CodeStyleName, StringComparison.Ordinal) ||
                string.Equals(styleName, DenseCodeStyleName, StringComparison.Ordinal));

    private static void ConfigureChartYAxis(
        Axis axis,
        IEnumerable<double> values,
        double? minimumScale,
        double? maximumScale,
        string unit)
    {
        var finite = values
            .Where(double.IsFinite)
            .ToArray();
        axis.Title.Caption = HumanizeUnitLabel(unit);
        axis.MajorTickMark = TickMarkType.Outside;
        axis.MinorTickMark = TickMarkType.None;
        axis.HasMajorGridlines = true;
        axis.HasMinorGridlines = false;
        axis.TickLabels.Font.Size = Unit.FromPoint(7);

        if (finite.Length == 0 && (!minimumScale.HasValue || !maximumScale.HasValue))
        {
            return;
        }

        var minimum = minimumScale ?? finite.Min();
        var maximum = maximumScale ?? finite.Max();

        if (!double.IsFinite(minimum) || !double.IsFinite(maximum))
        {
            return;
        }

        if (Math.Abs(maximum - minimum) < 0.000001d)
        {
            var padding = Math.Max(1d, Math.Abs(maximum) * 0.1d);
            minimum -= padding;
            maximum += padding;
        }

        if (!minimumScale.HasValue || !maximumScale.HasValue)
        {
            var range = maximum - minimum;
            var padding = range <= 0d ? 0.1d : Math.Max(range * 0.08d, 0.02d);
            if (!minimumScale.HasValue)
            {
                minimum -= padding;
            }

            if (!maximumScale.HasValue)
            {
                maximum += padding;
            }
        }

        var majorTick = BuildNiceStep((maximum - minimum) / 6d);
        if (!minimumScale.HasValue)
        {
            minimum = Math.Floor(minimum / majorTick) * majorTick;
        }

        if (!maximumScale.HasValue)
        {
            maximum = Math.Ceiling(maximum / majorTick) * majorTick;
        }

        if (Math.Abs(maximum - minimum) < majorTick * 2d)
        {
            maximum = minimum + majorTick * 2d;
        }

        axis.MinimumScale = minimum;
        axis.MaximumScale = maximum;
        axis.MajorTick = majorTick;
        axis.TickLabels.Format = DetermineTickLabelFormat(majorTick);
    }

    private static double BuildNiceStep(double rawStep)
    {
        if (!double.IsFinite(rawStep) || rawStep <= 0d)
        {
            return 1d;
        }

        var exponent = Math.Floor(Math.Log10(rawStep));
        var magnitude = Math.Pow(10d, exponent);
        var normalized = rawStep / magnitude;
        var nice = normalized switch
        {
            <= 1d => 1d,
            <= 2d => 2d,
            <= 2.5d => 2.5d,
            <= 5d => 5d,
            _ => 10d
        };
        return nice * magnitude;
    }

    private static string DetermineTickLabelFormat(double majorTick)
    {
        if (majorTick >= 1d)
        {
            return "0.0";
        }

        if (majorTick >= 0.1d)
        {
            return "0.0";
        }

        if (majorTick >= 0.01d)
        {
            return "0.00";
        }

        return "0.000";
    }

    private static void AddSeriesStatisticsTable(Section section, params ChartSeriesSpec[] series)
    {
        var rows = series
            .Where(item => item.Data.Values.Count > 0)
            .ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(7.2));
        table.AddColumn(Unit.FromCentimeter(2.2));
        table.AddColumn(Unit.FromCentimeter(2.8));
        table.AddColumn(Unit.FromCentimeter(2.8));
        table.AddColumn(Unit.FromCentimeter(2.8));

        var header = table.AddRow();
        StyleHeaderRow(header);
        AddParagraph(header.Cells[0], "Series", TableHeaderStyleName);
        AddParagraph(header.Cells[1], "Samples", TableHeaderStyleName);
        AddParagraph(header.Cells[2], "Min", TableHeaderStyleName);
        AddParagraph(header.Cells[3], "Mean", TableHeaderStyleName);
        AddParagraph(header.Cells[4], "Max", TableHeaderStyleName);

        var rowIndex = 0;
        foreach (var item in rows)
        {
            var finite = item.Data.Values
                .Where(double.IsFinite)
                .ToArray();
            if (finite.Length == 0)
            {
                continue;
            }

            var row = table.AddRow();
            StyleBodyRow(row, rowIndex++);
            AddParagraph(row.Cells[0], item.Label, DenseBodyStyleName);
            AddParagraph(row.Cells[1], finite.Length.ToString(CultureInfo.InvariantCulture), BodyStyleName);
            AddParagraph(row.Cells[2], FormatNumericValue(finite.Min()), BodyStyleName);
            AddParagraph(row.Cells[3], FormatNumericValue(finite.Average()), BodyStyleName);
            AddParagraph(row.Cells[4], FormatNumericValue(finite.Max()), BodyStyleName);
        }
    }

    private static string DescribeSampleStrength(int firstCount, int secondCount)
    {
        var maxCount = Math.Max(firstCount, secondCount);
        return maxCount switch
        {
            < 3 and > 0 => "Only two plotted points were available. Use this page as an endpoint comparison, not as waveform-shape evidence.",
            < 6 and > 0 => "Only a small number of plotted points were available. Treat the chart as a coarse trend check.",
            _ => string.Empty
        };
    }

    private static string HumanizeUnitLabel(string unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return "n/a";
        }

        return unit.ToLowerInvariant() switch
        {
            "unit01" => "0..1",
            "bool" => "0/1",
            "hz" => "Hz",
            "milliseconds" => "ms",
            "meters" => "meters",
            "quaternion" => "quaternion",
            _ => unit
        };
    }

    private static string FormatNumericValue(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static ChartSeriesSpec CreateSeriesSpec(string label, SeriesData data, Color color)
        => new(label, data, color);

    private static string BuildInsufficientSampleMessage(string prefix, IReadOnlyList<(string Label, int Count)> series)
    {
        var details = series
            .Select(item => $"{item.Label}: {item.Count}")
            .ToArray();
        return details.Length == 0
            ? prefix
            : $"{prefix} Sample counts: {string.Join("; ", details)}.";
    }

    private static SeriesData BuildPacketTimingSeries(
        string name,
        string unit,
        IReadOnlyList<PacketTimingMatch> matches,
        Func<PacketTimingMatch, double?> selector)
    {
        var points = matches
            .Select(selector)
            .Where(value => value.HasValue)
            .Select(value => new SeriesPoint(null, value!.Value))
            .ToArray();

        return points.Length == 0
            ? SeriesData.Empty(name, unit)
            : new SeriesData(name, unit, points);
    }

    private static string BuildSeriesCoverageSummary(ChartSeriesSpec firstSeries, ChartSeriesSpec secondSeries)
        => $"{DescribeSeriesCoverage(firstSeries)} | {DescribeSeriesCoverage(secondSeries)}";

    private static string DescribeSeriesCoverage(ChartSeriesSpec series)
    {
        var timestamps = series.Data.Points
            .Select(point => point.TimestampUtc)
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .ToArray();

        if (timestamps.Length == 0)
        {
            return $"{series.Label}: {series.Data.Values.Count.ToString(CultureInfo.InvariantCulture)} sample(s)";
        }

        var first = timestamps[0];
        var last = timestamps[^1];
        var duration = last - first;
        return $"{series.Label}: {series.Data.Values.Count.ToString(CultureInfo.InvariantCulture)} sample(s), {FormatTimestamp(first)} to {FormatTimestamp(last)} ({FormatDuration(duration)})";
    }

    private static IReadOnlyList<double> ReduceSeries(IReadOnlyList<double> values, int maxPoints)
    {
        if (values.Count <= maxPoints)
        {
            return values;
        }

        var reduced = new List<double>(maxPoints);
        for (var index = 0; index < maxPoints; index++)
        {
            var sourceIndex = (int)Math.Round(index * (values.Count - 1d) / Math.Max(1d, maxPoints - 1d));
            reduced.Add(values[sourceIndex]);
        }

        return reduced;
    }

    private static IReadOnlyList<string> BuildSampleLabels(int count)
    {
        if (count <= 0)
        {
            return Array.Empty<string>();
        }

        var labels = Enumerable.Repeat(string.Empty, count).ToArray();
        var desiredTickCount = Math.Min(8, count);
        var tickIndexes = new HashSet<int>();

        for (var tick = 0; tick < desiredTickCount; tick++)
        {
            var index = desiredTickCount == 1
                ? 0
                : (int)Math.Round(tick * (count - 1d) / (desiredTickCount - 1d));
            tickIndexes.Add(index);
        }

        foreach (var index in tickIndexes)
        {
            labels[index] = (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        return labels;
    }

    private static string FormatTimestamp(DateTimeOffset? timestamp)
        => timestamp.HasValue
            ? timestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture)
            : "n/a";

    private static string FormatDuration(TimeSpan? duration)
    {
        if (!duration.HasValue)
        {
            return "n/a";
        }

        var totalSeconds = duration.Value.TotalSeconds;
        return totalSeconds < 60
            ? $"{totalSeconds:0.0}s"
            : $"{(int)duration.Value.TotalMinutes}m {duration.Value.Seconds:00}s";
    }

    private static string ShortHash(string value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "n/a";
        }

        return value.Length <= length ? value : value[..length];
    }

    private static string FormatCoverageSpan(CoverageSpan span)
        => span.FirstTimestampUtc.HasValue && span.LastTimestampUtc.HasValue
            ? $"{FormatTimestamp(span.FirstTimestampUtc)} to {FormatTimestamp(span.LastTimestampUtc)} ({FormatDuration(span.Duration)})"
            : "n/a";

    private static string FormatSeconds(double? value)
        => value.HasValue
            ? $"{value.Value.ToString("0.000000", CultureInfo.InvariantCulture)} s"
            : "n/a";

    private static string SummarizeMilliseconds(IEnumerable<double?> values)
    {
        var finite = values
            .Where(value => value.HasValue && double.IsFinite(value.Value))
            .Select(value => value!.Value)
            .ToArray();

        if (finite.Length == 0)
        {
            return "n/a";
        }

        Array.Sort(finite);
        var mean = finite.Average();
        var median = finite[finite.Length / 2];
        var min = finite[0];
        var max = finite[^1];
        return $"{mean.ToString("0.0", CultureInfo.InvariantCulture)} mean | {median.ToString("0.0", CultureInfo.InvariantCulture)} median | {min.ToString("0.0", CultureInfo.InvariantCulture)}..{max.ToString("0.0", CultureInfo.InvariantCulture)} ms";
    }

    private static string SummarizePlainMilliseconds(IEnumerable<double> values)
    {
        var finite = values
            .Where(double.IsFinite)
            .ToArray();

        if (finite.Length == 0)
        {
            return "n/a";
        }

        Array.Sort(finite);
        var mean = finite.Average();
        var median = finite[finite.Length / 2];
        var min = finite[0];
        var max = finite[^1];
        return $"{mean.ToString("0.0", CultureInfo.InvariantCulture)} mean | {median.ToString("0.0", CultureInfo.InvariantCulture)} median | {min.ToString("0.0", CultureInfo.InvariantCulture)}..{max.ToString("0.0", CultureInfo.InvariantCulture)} ms";
    }

    private static string FormatRatio(double ratio)
        => ratio <= 0d
            ? "0%"
            : $"{(ratio * 100d).ToString("0", CultureInfo.InvariantCulture)}%";

    private static double ComputeDurationDeltaSeconds(CoverageSpan left, CoverageSpan right)
    {
        var leftDuration = left.Duration?.TotalSeconds;
        var rightDuration = right.Duration?.TotalSeconds;
        return leftDuration.HasValue && rightDuration.HasValue
            ? leftDuration.Value - rightDuration.Value
            : 0d;
    }

    private sealed record ChartSeriesSpec(string Label, SeriesData Data, Color Color);
    private sealed record FileCoverageRow(string FileName, bool Present, string Detail);
    private sealed record SessionAssessmentRow(string Area, string Status, string Evidence, string Interpretation);
    private sealed record SessionFindingRow(string Level, string Topic, string Detail);
    private sealed record CoverageComparisonRow(
        string Dataset,
        int WindowsRows,
        int QuestRows,
        CoverageSpan WindowsCoverage,
        CoverageSpan QuestCoverage);

    private sealed record EventData(DateTimeOffset TimestampUtc, string EventName, string Detail);

    private sealed record SeriesPoint(DateTimeOffset? TimestampUtc, double Value);

    private sealed record SeriesData(string Name, string Unit, IReadOnlyList<SeriesPoint> Points)
    {
        public IReadOnlyList<double> Values => Points.Select(point => point.Value).ToArray();

        public static SeriesData Empty(string name, string unit)
            => new(name, unit, Array.Empty<SeriesPoint>());
    }

    private sealed record ClockAlignmentPoint(
        int ProbeSequence,
        string WindowKind,
        double RoundTripMs,
        double OffsetResidualMs,
        double QuestMinusWindowsClockSeconds);

    private sealed record MilestoneRow(string Label, string OffsetLabel, string Detail);

    private sealed record CoverageSpan(DateTimeOffset? FirstTimestampUtc, DateTimeOffset? LastTimestampUtc)
    {
        public TimeSpan? Duration
            => FirstTimestampUtc.HasValue && LastTimestampUtc.HasValue
                ? LastTimestampUtc.Value - FirstTimestampUtc.Value
                : null;
    }

    private sealed record UpstreamLslSample(
        DateTimeOffset RecordedAtUtc,
        double ObservedLocalClockSeconds,
        double StreamSampleTimestampSeconds,
        double ValueNumeric,
        int Sequence);

    private sealed record TimingMarker(
        DateTimeOffset RecordedAtUtc,
        string MarkerName,
        int? SampleSequence,
        double SourceLslTimestampSeconds,
        double QuestLocalClockSeconds,
        double? Value01,
        double? AuxValue);

    private sealed record QuestPacketTiming(
        double SourceLslTimestampSeconds,
        int? SampleSequence,
        double? PacketValue01,
        double? HeartbeatPacketReceiveAtQuestLocal,
        double? CoherenceValuePublishAtQuestLocal,
        double? OrbitRadiusPeakAtQuestLocal,
        double? OrbitRadiusPeakValue01,
        double? OrbitRadiusPeakAuxValue);

    private sealed record PacketTimingMatch(
        double SourceLslTimestampSeconds,
        double RelativeSeconds,
        int? WindowsSequence,
        int? QuestSampleSequence,
        double? WindowsPacketValue01,
        double? QuestPacketValue01,
        double? WindowsReceiveLatencyMs,
        double? QuestReceiveLatencyMs,
        double? CoherencePublishLatencyMs,
        double? OrbitPeakLatencyMs,
        double? QuestReceiveToOrbitPeakMs);

    private sealed record PacketTimingAnalysis(
        IReadOnlyList<PacketTimingMatch> Matches,
        int WindowsOverlapCount,
        int QuestOverlapCount,
        int WindowsOnlyCount,
        int QuestOnlyCount,
        double? QuestMinusWindowsClockSeconds);

    private sealed record SignalInventoryRow(
        string Source,
        string Name,
        string Unit,
        int WindowsSamples,
        int QuestSamples,
        CoverageSpan WindowsCoverage,
        CoverageSpan QuestCoverage);

    private sealed record SessionParameterControlRow(
        string Group,
        string Label,
        string Id,
        string Type,
        double? CurrentValue,
        double? BaselineValue,
        double? AppliedValue,
        double? ReportedValue,
        double? SafeMinimum,
        double? SafeMaximum,
        string Units);

    private sealed record SessionParameterActivityRow(
        DateTimeOffset TimestampUtc,
        string Surface,
        string Kind,
        string ProfileName,
        int ChangeCount,
        string Summary);

    private sealed class ValidationSessionData
    {
        private ValidationSessionData()
        {
        }

        public required string SessionFolderPath { get; init; }
        public required string DevicePullFolderPath { get; init; }
        public required string ParticipantId { get; init; }
        public required string SessionId { get; init; }
        public required string PackageId { get; init; }
        public required string AppVersionName { get; init; }
        public required string ApkSha256 { get; init; }
        public required string HeadsetBuildId { get; init; }
        public required string HeadsetDisplayId { get; init; }
        public required string QuestSelector { get; init; }
        public required string LslStreamName { get; init; }
        public required string LslStreamType { get; init; }
        public required string WindowsMachineName { get; init; }
        public required DateTimeOffset SessionStartedAtUtc { get; init; }
        public required DateTimeOffset? SessionEndedAtUtc { get; init; }
        public required string InitialSessionParameterStateHash { get; init; }
        public required JsonElement? InitialSessionParameterState { get; init; }
        public required string LatestSessionParameterStateHash { get; init; }
        public required JsonElement? LatestSessionParameterState { get; init; }
        public required DateTimeOffset? SessionParameterStateUpdatedAtUtc { get; init; }
        public required int SessionParameterChangeCount { get; init; }
        public required IReadOnlyList<string> LocalFiles { get; init; }
        public required IReadOnlyList<string> DevicePullFiles { get; init; }
        public required IReadOnlyDictionary<string, SeriesData> WindowsSignals { get; init; }
        public required IReadOnlyDictionary<string, SeriesData> QuestSignals { get; init; }
        public required IReadOnlyDictionary<string, SeriesData> WindowsBreathing { get; init; }
        public required IReadOnlyDictionary<string, SeriesData> QuestBreathing { get; init; }
        public required IReadOnlyList<EventData> WindowsEvents { get; init; }
        public required IReadOnlyList<EventData> QuestEvents { get; init; }
        public required IReadOnlyList<ClockAlignmentPoint> ClockAlignmentPoints { get; init; }
        public required int WindowsSignalRowCount { get; init; }
        public required int QuestSignalRowCount { get; init; }
        public required int WindowsBreathingRowCount { get; init; }
        public required int QuestBreathingRowCount { get; init; }
        public required CoverageSpan WindowsSignalCoverage { get; init; }
        public required CoverageSpan QuestSignalCoverage { get; init; }
        public required CoverageSpan WindowsBreathingCoverage { get; init; }
        public required CoverageSpan QuestBreathingCoverage { get; init; }
        public required PacketTimingAnalysis PacketTiming { get; init; }

        public TimeSpan? SessionDuration
            => SessionEndedAtUtc.HasValue ? SessionEndedAtUtc.Value - SessionStartedAtUtc : null;

        public static ValidationSessionData Load(string sessionFolderPath)
        {
            var settingsPath = Path.Combine(sessionFolderPath, "session_settings.json");
            if (!File.Exists(settingsPath))
            {
                throw new FileNotFoundException($"Session settings not found: {settingsPath}");
            }

            var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(settingsPath))
                ?? throw new InvalidOperationException($"Session settings could not be read: {settingsPath}");

            var devicePullFolderPath = Path.Combine(sessionFolderPath, "device-session-pull");
            var localFiles = Directory.EnumerateFiles(sessionFolderPath, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OfType<string>()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var devicePullFiles = Directory.Exists(devicePullFolderPath)
                ? Directory.EnumerateFiles(devicePullFolderPath, "*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();

            var windowsSignals = ReadSignalSeries(Path.Combine(sessionFolderPath, "signals_long.csv"), out var windowsSignalRows);
            var questSignals = ReadSignalSeries(Path.Combine(devicePullFolderPath, "signals_long.csv"), out var questSignalRows);
            var windowsBreathing = ReadBreathingSeries(Path.Combine(sessionFolderPath, "breathing_trace.csv"), out var windowsBreathingRows);
            var questBreathing = ReadBreathingSeries(Path.Combine(devicePullFolderPath, "breathing_trace.csv"), out var questBreathingRows);
            var windowsEvents = ReadEvents(Path.Combine(sessionFolderPath, "session_events.csv"));
            var questEvents = ReadEvents(Path.Combine(devicePullFolderPath, "session_events.csv"));
            var clockAlignment = ReadClockAlignment(Path.Combine(sessionFolderPath, "clock_alignment_roundtrip.csv"));
            var windowsUpstream = ReadUpstreamLslSamples(Path.Combine(sessionFolderPath, "upstream_lsl_monitor.csv"));
            var windowsTimingMarkers = ReadTimingMarkers(Path.Combine(sessionFolderPath, "timing_markers.csv"));
            var questTimingMarkers = ReadTimingMarkers(Path.Combine(devicePullFolderPath, "timing_markers.csv"));
            var selectedTimingMarkers = windowsTimingMarkers.Count > 0
                ? windowsTimingMarkers
                : questTimingMarkers;
            var questPackets = GroupQuestPacketTimings(selectedTimingMarkers);

            return new ValidationSessionData
            {
                SessionFolderPath = sessionFolderPath,
                DevicePullFolderPath = devicePullFolderPath,
                ParticipantId = GetString(settings, "ParticipantId"),
                SessionId = GetString(settings, "SessionId"),
                PackageId = GetString(settings, "PackageId"),
                AppVersionName = GetString(settings, "AppVersionName"),
                ApkSha256 = GetString(settings, "ApkSha256"),
                HeadsetBuildId = GetString(settings, "HeadsetBuildId"),
                HeadsetDisplayId = GetString(settings, "HeadsetDisplayId"),
                QuestSelector = GetString(settings, "QuestSelector"),
                LslStreamName = GetString(settings, "LslStreamName"),
                LslStreamType = GetString(settings, "LslStreamType"),
                WindowsMachineName = GetString(settings, "WindowsMachineName"),
                SessionStartedAtUtc = GetDateTimeOffset(settings, "SessionStartedAtUtc") ?? DateTimeOffset.UtcNow,
                SessionEndedAtUtc = GetDateTimeOffset(settings, "SessionEndedAtUtc"),
                InitialSessionParameterStateHash = GetString(settings, "InitialSessionParameterStateHash"),
                InitialSessionParameterState = TryGetJsonElement(settings, "InitialSessionParameterState"),
                LatestSessionParameterStateHash = GetString(settings, "LatestSessionParameterStateHash"),
                LatestSessionParameterState = TryGetJsonElement(settings, "LatestSessionParameterState")
                    ?? TryGetJsonElement(settings, "InitialSessionParameterState"),
                SessionParameterStateUpdatedAtUtc = GetDateTimeOffset(settings, "SessionParameterStateUpdatedAtUtc"),
                SessionParameterChangeCount = TryParseInt(GetString(settings, "SessionParameterChangeCount")) ?? 0,
                LocalFiles = localFiles,
                DevicePullFiles = devicePullFiles,
                WindowsSignals = windowsSignals,
                QuestSignals = questSignals,
                WindowsBreathing = windowsBreathing,
                QuestBreathing = questBreathing,
                WindowsEvents = windowsEvents,
                QuestEvents = questEvents,
                ClockAlignmentPoints = clockAlignment,
                WindowsSignalRowCount = windowsSignalRows,
                QuestSignalRowCount = questSignalRows,
                WindowsBreathingRowCount = windowsBreathingRows,
                QuestBreathingRowCount = questBreathingRows,
                WindowsSignalCoverage = BuildCoverageSpan(windowsSignals),
                QuestSignalCoverage = BuildCoverageSpan(questSignals),
                WindowsBreathingCoverage = BuildCoverageSpan(windowsBreathing),
                QuestBreathingCoverage = BuildCoverageSpan(questBreathing),
                PacketTiming = AnalyzePacketTimings(settings, windowsUpstream, questPackets, clockAlignment)
            };
        }

        public IReadOnlyList<MilestoneRow> BuildMilestones()
        {
            if (WindowsEvents.Count == 0)
            {
                return
                [
                    new MilestoneRow("Windows events", "n/a", "session_events.csv did not contain any rows.")
                ];
            }

            return new[]
                {
                    ("experiment.start_command", "Start command"),
                    ("recording.device_confirmation", "Quest recorder confirmed"),
                    ("clock_alignment.result", "Clock alignment finished"),
                    ("experiment.end_command", "End command"),
                    ("recording.device_stop_confirmation", "Quest recorder stopped")
                }
                .Select(mapping =>
                {
                    var matched = WindowsEvents.FirstOrDefault(evt => string.Equals(evt.EventName, mapping.Item1, StringComparison.OrdinalIgnoreCase));
                    if (matched is null)
                    {
                        return new MilestoneRow(mapping.Item2, "n/a", "Event not present in session_events.csv.");
                    }

                    return new MilestoneRow(
                        mapping.Item2,
                        $"+{(matched.TimestampUtc - SessionStartedAtUtc).TotalSeconds:0.0}s",
                        string.IsNullOrWhiteSpace(matched.Detail) ? "Recorded." : matched.Detail);
                })
                .ToArray();
        }

        private static IReadOnlyDictionary<string, SeriesData> ReadSignalSeries(string path, out int rowCount)
        {
            var grouped = new Dictionary<string, List<SeriesPoint>>(StringComparer.OrdinalIgnoreCase);
            var units = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            rowCount = 0;

            foreach (var row in ReadCsvRows(path))
            {
                rowCount++;
                var signalName = GetCsvValue(row, "signal_name");
                if (string.IsNullOrWhiteSpace(signalName))
                {
                    continue;
                }

                if (!TryParseNumericLike(GetCsvValue(row, "value_numeric"), out var value))
                {
                    continue;
                }

                if (!grouped.TryGetValue(signalName, out var values))
                {
                    values = new List<SeriesPoint>();
                    grouped[signalName] = values;
                }

                values.Add(new SeriesPoint(
                    GetDateTimeOffset(row, "source_timestamp_utc") ?? GetDateTimeOffset(row, "recorded_at_utc"),
                    value));
                units[signalName] = GetCsvValue(row, "unit");
            }

            return grouped.ToDictionary(
                pair => pair.Key,
                pair => new SeriesData(pair.Key, units.TryGetValue(pair.Key, out var unit) ? unit : string.Empty, pair.Value),
                StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyDictionary<string, SeriesData> ReadBreathingSeries(string path, out int rowCount)
        {
            rowCount = 0;
            var rows = ReadCsvRows(path).ToArray();
            if (rows.Length == 0)
            {
                return new Dictionary<string, SeriesData>(StringComparer.OrdinalIgnoreCase);
            }

            var grouped = new Dictionary<string, List<SeriesPoint>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                rowCount++;
                var timestamp = GetDateTimeOffset(row, "source_timestamp_utc") ?? GetDateTimeOffset(row, "recorded_at_utc");
                foreach (var field in row.Keys)
                {
                    if (field is "participant_id" or "session_id" or "dataset_id" or "recorded_at_utc" or "source_timestamp_utc")
                    {
                        continue;
                    }

                    if (!TryParseNumericLike(GetCsvValue(row, field), out var value))
                    {
                        continue;
                    }

                    if (!grouped.TryGetValue(field, out var values))
                    {
                        values = new List<SeriesPoint>();
                        grouped[field] = values;
                    }

                    values.Add(new SeriesPoint(timestamp, value));
                }
            }

            return grouped.ToDictionary(
                pair => pair.Key,
                pair => new SeriesData(pair.Key, InferBreathingUnit(pair.Key), pair.Value),
                StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<EventData> ReadEvents(string path)
            => ReadCsvRows(path)
                .Select(row => new EventData(
                    GetDateTimeOffset(row, "recorded_at_utc") ?? DateTimeOffset.MinValue,
                    GetCsvValue(row, "event_name"),
                    GetCsvValue(row, "event_detail")))
                .Where(evt => evt.TimestampUtc != DateTimeOffset.MinValue && !string.IsNullOrWhiteSpace(evt.EventName))
                .OrderBy(evt => evt.TimestampUtc)
                .ToArray();

        private static IReadOnlyList<ClockAlignmentPoint> ReadClockAlignment(string path)
        {
            var raw = ReadCsvRows(path)
                .Select(row => new
                {
                    ProbeSequence = TryParseInt(GetCsvValue(row, "probe_sequence")),
                    WindowKind = GetCsvValue(row, "window_kind"),
                    RoundTripSeconds = TryParseDouble(GetCsvValue(row, "roundtrip_seconds")),
                    OffsetSeconds = TryParseDouble(GetCsvValue(row, "quest_minus_windows_clock_seconds"))
                })
                .Where(item => item.ProbeSequence.HasValue && item.RoundTripSeconds.HasValue && item.OffsetSeconds.HasValue)
                .ToArray();

            if (raw.Length == 0)
            {
                return Array.Empty<ClockAlignmentPoint>();
            }

            var medianOffsetSeconds = raw.Select(item => item.OffsetSeconds!.Value).OrderBy(value => value).ElementAt(raw.Length / 2);
            return raw
                .Select(item => new ClockAlignmentPoint(
                    item.ProbeSequence!.Value,
                    item.WindowKind,
                    item.RoundTripSeconds!.Value * 1000d,
                    (item.OffsetSeconds!.Value - medianOffsetSeconds) * 1000d,
                    item.OffsetSeconds!.Value))
                .OrderBy(point => point.ProbeSequence)
                .ToArray();
        }

        private static IReadOnlyList<UpstreamLslSample> ReadUpstreamLslSamples(string path)
            => ReadCsvRows(path)
                .Select(row => new
                {
                    RecordedAtUtc = GetDateTimeOffset(row, "recorded_at_utc"),
                    ObservedLocalClockSeconds = TryParseDouble(GetCsvValue(row, "observed_local_clock_seconds")),
                    StreamSampleTimestampSeconds = TryParseDouble(GetCsvValue(row, "stream_sample_timestamp_seconds")),
                    ValueNumeric = TryParseDouble(GetCsvValue(row, "value_numeric")),
                    Sequence = TryParseInt(GetCsvValue(row, "sequence"))
                })
                .Where(item =>
                    item.RecordedAtUtc.HasValue
                    && item.ObservedLocalClockSeconds.HasValue
                    && item.StreamSampleTimestampSeconds.HasValue
                    && item.ValueNumeric.HasValue
                    && item.Sequence.HasValue)
                .Select(item => new UpstreamLslSample(
                    item.RecordedAtUtc!.Value,
                    item.ObservedLocalClockSeconds!.Value,
                    item.StreamSampleTimestampSeconds!.Value,
                    item.ValueNumeric!.Value,
                    item.Sequence!.Value))
                .OrderBy(item => item.StreamSampleTimestampSeconds)
                .ToArray();

        private static IReadOnlyList<TimingMarker> ReadTimingMarkers(string path)
            => ReadCsvRows(path)
                .Select(row => new TimingMarker(
                    GetDateTimeOffset(row, "recorded_at_utc") ?? DateTimeOffset.MinValue,
                    GetCsvValue(row, "marker_name"),
                    TryParseInt(GetCsvValue(row, "sample_sequence")),
                    TryParseDouble(GetCsvValue(row, "source_lsl_timestamp_seconds")) ?? double.NaN,
                    TryParseDouble(GetCsvValue(row, "quest_local_clock_seconds")) ?? double.NaN,
                    TryParseDouble(GetCsvValue(row, "value01")),
                    TryParseDouble(GetCsvValue(row, "aux_value"))))
                .Where(item =>
                    item.RecordedAtUtc != DateTimeOffset.MinValue
                    && !string.IsNullOrWhiteSpace(item.MarkerName)
                    && double.IsFinite(item.SourceLslTimestampSeconds)
                    && double.IsFinite(item.QuestLocalClockSeconds))
                .OrderBy(item => item.SourceLslTimestampSeconds)
                .ThenBy(item => item.QuestLocalClockSeconds)
                .ThenBy(item => item.MarkerName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        private static IReadOnlyList<QuestPacketTiming> GroupQuestPacketTimings(IReadOnlyList<TimingMarker> markers)
        {
            if (markers.Count == 0)
            {
                return Array.Empty<QuestPacketTiming>();
            }

            var grouped = new Dictionary<long, List<TimingMarker>>();
            foreach (var marker in markers)
            {
                var key = checked((long)Math.Round(marker.SourceLslTimestampSeconds * 10000d));
                if (!grouped.TryGetValue(key, out var bucket))
                {
                    bucket = [];
                    grouped[key] = bucket;
                }

                bucket.Add(marker);
            }

            return grouped
                .OrderBy(pair => pair.Key)
                .Select(pair =>
                {
                    var bucket = pair.Value;
                    var heartbeatReceive = bucket.FirstOrDefault(item => string.Equals(item.MarkerName, "heartbeat_packet_receive", StringComparison.OrdinalIgnoreCase));
                    var coherencePublish = bucket.FirstOrDefault(item => string.Equals(item.MarkerName, "coherence_value_publish", StringComparison.OrdinalIgnoreCase));
                    var orbitPeak = bucket.FirstOrDefault(item => string.Equals(item.MarkerName, "orbit_radius_peak", StringComparison.OrdinalIgnoreCase));
                    var coherenceReceive = bucket.FirstOrDefault(item => string.Equals(item.MarkerName, "coherence_packet_receive", StringComparison.OrdinalIgnoreCase));
                    var sourceTimestamp = bucket[0].SourceLslTimestampSeconds;
                    var packetValue = heartbeatReceive?.Value01 ?? coherenceReceive?.Value01;
                    var sampleSequence = heartbeatReceive?.SampleSequence
                        ?? coherenceReceive?.SampleSequence
                        ?? coherencePublish?.SampleSequence
                        ?? orbitPeak?.SampleSequence;

                    return new QuestPacketTiming(
                        sourceTimestamp,
                        sampleSequence,
                        packetValue,
                        heartbeatReceive?.QuestLocalClockSeconds,
                        coherencePublish?.QuestLocalClockSeconds,
                        orbitPeak?.QuestLocalClockSeconds,
                        orbitPeak?.Value01,
                        orbitPeak?.AuxValue);
                })
                .ToArray();
        }

        private static PacketTimingAnalysis AnalyzePacketTimings(
            IReadOnlyDictionary<string, JsonElement> settings,
            IReadOnlyList<UpstreamLslSample> windowsSamples,
            IReadOnlyList<QuestPacketTiming> questPackets,
            IReadOnlyList<ClockAlignmentPoint> clockAlignment)
        {
            if (windowsSamples.Count == 0 || questPackets.Count == 0)
            {
                return new PacketTimingAnalysis(Array.Empty<PacketTimingMatch>(), 0, 0, windowsSamples.Count, questPackets.Count, ResolveQuestMinusWindowsClockSeconds(settings, clockAlignment));
            }

            var overlapStart = Math.Max(windowsSamples[0].StreamSampleTimestampSeconds, questPackets[0].SourceLslTimestampSeconds);
            var overlapEnd = Math.Min(windowsSamples[^1].StreamSampleTimestampSeconds, questPackets[^1].SourceLslTimestampSeconds);
            if (overlapStart > overlapEnd)
            {
                return new PacketTimingAnalysis(Array.Empty<PacketTimingMatch>(), 0, 0, windowsSamples.Count, questPackets.Count, ResolveQuestMinusWindowsClockSeconds(settings, clockAlignment));
            }

            var windowsOverlap = windowsSamples
                .Where(sample => sample.StreamSampleTimestampSeconds >= overlapStart && sample.StreamSampleTimestampSeconds <= overlapEnd)
                .ToArray();
            var questOverlap = questPackets
                .Where(packet => packet.SourceLslTimestampSeconds >= overlapStart && packet.SourceLslTimestampSeconds <= overlapEnd)
                .ToArray();
            var questMinusWindowsClockSeconds = ResolveQuestMinusWindowsClockSeconds(settings, clockAlignment);

            var matches = new List<PacketTimingMatch>();
            var windowsOnlyCount = 0;
            var questOnlyCount = 0;
            var windowsIndex = 0;
            var questIndex = 0;

            while (windowsIndex < windowsOverlap.Length && questIndex < questOverlap.Length)
            {
                var windowsSample = windowsOverlap[windowsIndex];
                var questPacket = questOverlap[questIndex];
                var deltaSeconds = windowsSample.StreamSampleTimestampSeconds - questPacket.SourceLslTimestampSeconds;

                if (Math.Abs(deltaSeconds) <= 0.001d)
                {
                    var sourceTimestamp = 0.5d * (windowsSample.StreamSampleTimestampSeconds + questPacket.SourceLslTimestampSeconds);
                    var sendTimeInQuestClock = questMinusWindowsClockSeconds.HasValue
                        ? sourceTimestamp + questMinusWindowsClockSeconds.Value
                        : (double?)null;

                    matches.Add(new PacketTimingMatch(
                        sourceTimestamp,
                        sourceTimestamp - overlapStart,
                        windowsSample.Sequence,
                        questPacket.SampleSequence,
                        windowsSample.ValueNumeric,
                        questPacket.PacketValue01,
                        (windowsSample.ObservedLocalClockSeconds - windowsSample.StreamSampleTimestampSeconds) * 1000d,
                        ComputeLatencyMs(sendTimeInQuestClock, questPacket.HeartbeatPacketReceiveAtQuestLocal),
                        ComputeLatencyMs(sendTimeInQuestClock, questPacket.CoherenceValuePublishAtQuestLocal),
                        ComputeLatencyMs(sendTimeInQuestClock, questPacket.OrbitRadiusPeakAtQuestLocal),
                        ComputeLatencyMs(questPacket.HeartbeatPacketReceiveAtQuestLocal, questPacket.OrbitRadiusPeakAtQuestLocal)));

                    windowsIndex++;
                    questIndex++;
                }
                else if (deltaSeconds < 0d)
                {
                    windowsOnlyCount++;
                    windowsIndex++;
                }
                else
                {
                    questOnlyCount++;
                    questIndex++;
                }
            }

            windowsOnlyCount += windowsOverlap.Length - windowsIndex;
            questOnlyCount += questOverlap.Length - questIndex;

            return new PacketTimingAnalysis(
                matches,
                windowsOverlap.Length,
                questOverlap.Length,
                windowsOnlyCount,
                questOnlyCount,
                questMinusWindowsClockSeconds);
        }

        private static IEnumerable<Dictionary<string, string>> ReadCsvRows(string path)
        {
            if (!File.Exists(path))
            {
                yield break;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch
            {
                yield break;
            }

            if (lines.Length < 2)
            {
                yield break;
            }

            var headers = ParseCsvLine(lines[0]);
            for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                if (string.IsNullOrWhiteSpace(lines[lineIndex]))
                {
                    continue;
                }

                var cells = ParseCsvLine(lines[lineIndex], headers.Count);

                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < headers.Count; index++)
                {
                    row[headers[index]] = index < cells.Count ? cells[index] : string.Empty;
                }

                yield return row;
            }
        }

        private static List<string> ParseCsvLine(string line, int expectedCellCount = int.MaxValue)
        {
            var cells = new List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;

            for (var index = 0; index < line.Length; index++)
            {
                if (cells.Count == Math.Max(0, expectedCellCount - 1))
                {
                    current.Append(line.AsSpan(index));
                    break;
                }

                var ch = line[index];
                if (ch == '"')
                {
                    if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    cells.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            cells.Add(NormalizeCsvCell(current.ToString()));
            return cells;
        }

        private static string NormalizeCsvCell(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                return value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
            }

            return value;
        }

        private static string GetCsvValue(IReadOnlyDictionary<string, string> row, string key)
            => row.TryGetValue(key, out var value) ? value?.Trim() ?? string.Empty : string.Empty;

        private static bool TryParseNumericLike(string raw, out double value)
        {
            var trimmed = raw?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                value = 0;
                return false;
            }

            if (bool.TryParse(trimmed, out var parsedBool))
            {
                value = parsedBool ? 1d : 0d;
                return true;
            }

            return double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
        }

        private static double? TryParseDouble(string raw)
            => double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

        private static int? TryParseInt(string raw)
            => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

        private static double? ComputeLatencyMs(double? earlierSeconds, double? laterSeconds)
            => earlierSeconds.HasValue && laterSeconds.HasValue
                ? (laterSeconds.Value - earlierSeconds.Value) * 1000d
                : null;

        private static string GetString(IReadOnlyDictionary<string, JsonElement> values, string key)
        {
            foreach (var pair in values)
            {
                if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return pair.Value.ValueKind switch
                {
                    JsonValueKind.String => pair.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => pair.Value.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => pair.Value.ToString()
                };
            }

            return string.Empty;
        }

        private static JsonElement? TryGetJsonElement(IReadOnlyDictionary<string, JsonElement> values, string key)
        {
            foreach (var pair in values)
            {
                if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return pair.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                    ? null
                    : pair.Value.Clone();
            }

            return null;
        }

        private static DateTimeOffset? GetDateTimeOffset(IReadOnlyDictionary<string, JsonElement> values, string key)
        {
            var raw = GetString(values, key);
            return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;
        }

        private static DateTimeOffset? GetDateTimeOffset(IReadOnlyDictionary<string, string> values, string key)
        {
            var raw = GetCsvValue(values, key);
            return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;
        }

        private static string InferBreathingUnit(string key)
            => key.EndsWith("01", StringComparison.OrdinalIgnoreCase) || key.EndsWith("_progress01", StringComparison.OrdinalIgnoreCase)
                ? "unit01"
                : key.EndsWith("_active", StringComparison.OrdinalIgnoreCase) || key.EndsWith("_calibrated", StringComparison.OrdinalIgnoreCase)
                    ? "bool"
                    : "value";

        private static double? ResolveQuestMinusWindowsClockSeconds(
            IReadOnlyDictionary<string, JsonElement> settings,
            IReadOnlyList<ClockAlignmentPoint> clockAlignment)
        {
            var direct = GetString(settings, "ClockAlignmentRecommendedQuestMinusWindowsClockSeconds");
            if (double.TryParse(direct, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedDirect))
            {
                return parsedDirect;
            }

            var startBurstOffsets = clockAlignment
                .Where(point => string.Equals(point.WindowKind, "StartBurst", StringComparison.OrdinalIgnoreCase))
                .Select(point => point.QuestMinusWindowsClockSeconds)
                .ToArray();
            var allOffsets = startBurstOffsets.Length > 0
                ? startBurstOffsets
                : clockAlignment.Select(point => point.QuestMinusWindowsClockSeconds).ToArray();
            if (allOffsets.Length == 0)
            {
                return null;
            }

            Array.Sort(allOffsets);
            return allOffsets[allOffsets.Length / 2];
        }

        private static CoverageSpan BuildCoverageSpan(IReadOnlyDictionary<string, SeriesData> series)
        {
            var timestamps = series.Values
                .SelectMany(item => item.Points)
                .Select(point => point.TimestampUtc)
                .Where(timestamp => timestamp.HasValue)
                .Select(timestamp => timestamp!.Value)
                .OrderBy(timestamp => timestamp)
                .ToArray();

            return timestamps.Length == 0
                ? new CoverageSpan(null, null)
                : new CoverageSpan(timestamps[0], timestamps[^1]);
        }
    }
}
