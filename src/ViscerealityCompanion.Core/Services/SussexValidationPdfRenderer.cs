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
        "session_settings.json"
    ];

    private static readonly string[] ExpectedQuestFiles =
    [
        "session_settings.json",
        "session_events.csv",
        "signals_long.csv",
        "breathing_trace.csv",
        "clock_alignment_samples.csv",
        "timing_markers.csv",
        "lsl_samples.csv"
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
        AddCoveragePage(document, data);
        AddMilestonePage(document, data);
        AddSeriesChartPage(
            document,
            "Breathing Trace",
            "Windows and Quest recorder traces shown by sample order.",
            CreateSeriesSpec("Windows breath_volume01", data.WindowsBreathing.TryGetValue("breath_volume01", out var localBreathing) ? localBreathing : SeriesData.Empty("breath_volume01", "unit01"), Colors.DodgerBlue),
            CreateSeriesSpec("Quest breath_volume01", data.QuestBreathing.TryGetValue("breath_volume01", out var questBreathing) ? questBreathing : SeriesData.Empty("breath_volume01", "unit01"), Colors.Orange),
            yMinimum: 0,
            yMaximum: 1);
        AddSeriesChartPage(
            document,
            "Coherence",
            "Held twin-state coherence mirror by sample order.",
            CreateSeriesSpec("Windows coherence.value01", data.WindowsSignals.TryGetValue("coherence.value01", out var localCoherence) ? localCoherence : SeriesData.Empty("coherence.value01", "unit01"), Colors.DodgerBlue),
            CreateSeriesSpec("Quest coherence.value01", data.QuestSignals.TryGetValue("coherence.value01", out var questCoherence) ? questCoherence : SeriesData.Empty("coherence.value01", "unit01"), Colors.Orange),
            yMinimum: 0,
            yMaximum: 1);
        AddSeriesChartPage(
            document,
            "Heartbeat Packet Value",
            "Latest upstream packet value held in twin-state by sample order.",
            CreateSeriesSpec("Windows heartbeat.packet_value01", data.WindowsSignals.TryGetValue("heartbeat.packet_value01", out var localPacket) ? localPacket : SeriesData.Empty("heartbeat.packet_value01", "unit01"), Colors.DodgerBlue),
            CreateSeriesSpec("Quest heartbeat.packet_value01", data.QuestSignals.TryGetValue("heartbeat.packet_value01", out var questPacket) ? questPacket : SeriesData.Empty("heartbeat.packet_value01", "unit01"), Colors.Orange),
            yMinimum: 0,
            yMaximum: 1);
        AddSeriesChartPage(
            document,
            "Orbit Radius Visual",
            "Runtime orbit-distance multiplier by sample order.",
            CreateSeriesSpec("Windows orbit.radius_visual01", data.WindowsSignals.TryGetValue("orbit.radius_visual01", out var localOrbit) ? localOrbit : SeriesData.Empty("orbit.radius_visual01", "unit01"), Colors.DodgerBlue),
            CreateSeriesSpec("Quest orbit.radius_visual01", data.QuestSignals.TryGetValue("orbit.radius_visual01", out var questOrbit) ? questOrbit : SeriesData.Empty("orbit.radius_visual01", "unit01"), Colors.Orange),
            yMinimum: 0,
            yMaximum: 1);
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
            ("windows signals rows", data.WindowsSignalRowCount.ToString(CultureInfo.InvariantCulture)),
            ("quest signals rows", data.QuestSignalRowCount.ToString(CultureInfo.InvariantCulture)),
            ("windows breathing rows", data.WindowsBreathingRowCount.ToString(CultureInfo.InvariantCulture)),
            ("quest breathing rows", data.QuestBreathingRowCount.ToString(CultureInfo.InvariantCulture)),
            ("windows events", data.WindowsEvents.Count.ToString(CultureInfo.InvariantCulture)),
            ("quest events", data.QuestEvents.Count.ToString(CultureInfo.InvariantCulture)),
            ("clock-alignment samples", data.ClockAlignmentPoints.Count.ToString(CultureInfo.InvariantCulture))
        ]);
    }

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
            AddKeyValueTable(
                section,
                "Quest Events",
                data.QuestEvents.Take(8).Select(evt =>
                    (evt.EventName, $"{FormatTimestamp(evt.TimestampUtc)} | {evt.Detail}")));
        }
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
        chart.Height = Unit.FromCentimeter(12.5);
        chart.LineFormat.Width = Unit.FromPoint(0.75);
        chart.LineFormat.Color = Colors.LightGray;
        chart.PlotArea.LineFormat.Width = Unit.FromPoint(0.75);
        chart.PlotArea.LineFormat.Color = Colors.LightGray;
        chart.XAxis.Title.Caption = "Sample";
        chart.YAxis.Title.Caption = string.IsNullOrWhiteSpace(firstSeries.Data.Unit) ? secondSeries.Data.Unit : firstSeries.Data.Unit;
        chart.YAxis.MajorTickMark = TickMarkType.Outside;
        chart.YAxis.HasMajorGridlines = true;

        if (yMinimum.HasValue)
        {
            chart.YAxis.MinimumScale = yMinimum.Value;
        }

        if (yMaximum.HasValue)
        {
            chart.YAxis.MaximumScale = yMaximum.Value;
        }

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
        chart.Height = Unit.FromCentimeter(12.5);
        chart.LineFormat.Width = Unit.FromPoint(0.75);
        chart.LineFormat.Color = Colors.LightGray;
        chart.PlotArea.LineFormat.Width = Unit.FromPoint(0.75);
        chart.PlotArea.LineFormat.Color = Colors.LightGray;
        chart.XAxis.Title.Caption = "Probe Sequence";
        chart.YAxis.Title.Caption = title.Contains("Round-Trip", StringComparison.OrdinalIgnoreCase) ? "Milliseconds" : "Residual ms";
        chart.YAxis.MajorTickMark = TickMarkType.Outside;
        chart.YAxis.HasMajorGridlines = true;

        var xSeries = chart.XValues.AddXSeries();
        foreach (var label in BuildSampleLabels(reduced.Count))
        {
            xSeries.Add(label);
        }

        AddLineSeries(chart, title, reduced, color);

        AddKeyValueTable(section, "Clock Alignment Summary",
        [
            ("sample count", values.Count.ToString(CultureInfo.InvariantCulture)),
            ("mean", values.Count == 0 ? "n/a" : values.Average().ToString("0.###", CultureInfo.InvariantCulture)),
            ("min", values.Count == 0 ? "n/a" : values.Min().ToString("0.###", CultureInfo.InvariantCulture)),
            ("max", values.Count == 0 ? "n/a" : values.Max().ToString("0.###", CultureInfo.InvariantCulture))
        ]);
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
        bool monospaceValue = false)
    {
        AddParagraph(section, heading, SectionStyleName);
        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(3.8));
        table.AddColumn(Unit.FromCentimeter(13.4));

        var rowIndex = 0;
        foreach (var (key, value) in rows)
        {
            var row = table.AddRow();
            StyleBodyRow(row, rowIndex++);
            AddParagraph(row.Cells[0], key, BodyStyleName).Format.Font.Bold = true;
            AddParagraph(row.Cells[1], value ?? "n/a", monospaceValue ? DenseCodeStyleName : BodyStyleName);
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
            preserveCodeShape: string.Equals(styleName, CodeStyleName, StringComparison.Ordinal));

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

    private sealed record ChartSeriesSpec(string Label, SeriesData Data, Color Color);
    private sealed record FileCoverageRow(string FileName, bool Present, string Detail);

    private sealed record EventData(DateTimeOffset TimestampUtc, string EventName, string Detail);

    private sealed record SeriesData(string Name, string Unit, IReadOnlyList<double> Values)
    {
        public static SeriesData Empty(string name, string unit)
            => new(name, unit, Array.Empty<double>());
    }

    private sealed record ClockAlignmentPoint(int ProbeSequence, double RoundTripMs, double OffsetResidualMs);

    private sealed record MilestoneRow(string Label, string OffsetLabel, string Detail);

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
                QuestBreathingRowCount = questBreathingRows
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
            var grouped = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
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
                    values = new List<double>();
                    grouped[signalName] = values;
                }

                values.Add(value);
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

            var grouped = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                rowCount++;
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
                        values = new List<double>();
                        grouped[field] = values;
                    }

                    values.Add(value);
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
                    item.RoundTripSeconds!.Value * 1000d,
                    (item.OffsetSeconds!.Value - medianOffsetSeconds) * 1000d))
                .OrderBy(point => point.ProbeSequence)
                .ToArray();
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

                var cells = ParseCsvLine(lines[lineIndex]);
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < headers.Count; index++)
                {
                    row[headers[index]] = index < cells.Count ? cells[index] : string.Empty;
                }

                yield return row;
            }
        }

        private static List<string> ParseCsvLine(string line)
        {
            var cells = new List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;

            for (var index = 0; index < line.Length; index++)
            {
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

            cells.Add(current.ToString());
            return cells;
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
    }
}
