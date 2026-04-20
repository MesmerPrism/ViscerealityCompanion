using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public static class SussexDiagnosticsPdfRenderer
{
    private const string TitleStyleName = "ReportTitle";
    private const string SectionStyleName = "ReportSection";
    private const string BodyStyleName = "ReportBody";
    private const string MetaStyleName = "ReportMeta";
    private const string TableHeaderStyleName = "ReportTableHeader";
    private const string DenseBodyStyleName = "ReportDenseBody";
    private const string CodeStyleName = "ReportCode";
    private const string DenseCodeStyleName = "ReportDenseCode";

    public static void Render(SussexDiagnosticsReport report, string outputPdfPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPdfPath);

        PdfReportBootstrap.EnsureInitialized();

        var document = new Document();
        document.Info.Title = $"{report.StudyLabel} LSL/Twin Diagnostics";
        document.Info.Subject = "Sussex diagnostics report";
        document.Info.Author = "Viscereality Companion";
        DefineStyles(document);

        var overviewSection = AddSection(document);
        AddTitle(overviewSection, report);
        AddKeyValueTable(overviewSection, "Summary",
        [
            ("overall", $"{RenderLevel(report.Level)} - {report.Summary}"),
            ("detail", report.Detail),
            ("study", report.StudyId),
            ("package", report.PackageId),
            ("expected upstream", $"{report.ExpectedLslStreamName} / {report.ExpectedLslStreamType}"),
            ("report folder", report.ReportDirectory)
        ]);

        AddKeyValueTable(overviewSection, "Quest Setup",
        [
            ("selector", report.QuestSetup.Selector),
            ("foreground", report.QuestSetup.ForegroundAndSnapshot),
            ("pinned build", report.QuestSetup.PinnedBuild),
            ("device profile", report.QuestSetup.DeviceProfileSummary)
        ]);

        AddCheckSection(
            document,
            "Windows Environment",
            report.WindowsEnvironment.Checks.Select(check => new SussexMachineLslCheckResult(check.Label, check.Level, check.Summary, check.Detail)));

        AddCheckSection(document, "Machine LSL State", report.MachineLslState.Checks);

        var detailSection = AddSection(document);
        AddKeyValueTable(detailSection, "Quest Wi-Fi Transport",
        [
            ("level", RenderLevel(report.QuestWifiTransport.Level)),
            ("summary", report.QuestWifiTransport.Summary),
            ("selector", report.QuestWifiTransport.Selector),
            ("headset wifi", report.QuestWifiTransport.HeadsetWifi),
            ("host wifi", report.QuestWifiTransport.HostWifi),
            ("topology", report.QuestWifiTransport.Topology),
            ("ping", report.QuestWifiTransport.Ping),
            ("tcp", report.QuestWifiTransport.Tcp)
        ]);

        AddKeyValueTable(detailSection, "Quest Twin Return Path",
        [
            ("twin-state outlet", QuestTwinStatePublisherInventoryService.RenderForOperator(report.TwinStatePublisherInventory)),
            ("expected inlet", report.TwinConnection.ExpectedInlet),
            ("runtime target", report.TwinConnection.RuntimeTarget),
            ("connected inlet", report.TwinConnection.ConnectedInlet),
            ("counts", report.TwinConnection.Counts),
            ("quest status", report.TwinConnection.QuestStatus),
            ("quest echo", report.TwinConnection.QuestEcho),
            ("return path", report.TwinConnection.ReturnPath),
            ("transport", report.TwinConnection.TransportDetail)
        ]);

        AddKeyValueTable(detailSection, "Command Acceptance Probe",
        [
            ("level", RenderLevel(report.CommandAcceptance.Level)),
            ("summary", report.CommandAcceptance.Summary),
            ("action id", report.CommandAcceptance.ActionId),
            ("sequence", report.CommandAcceptance.Sequence?.ToString(CultureInfo.InvariantCulture) ?? "n/a"),
            ("accepted", report.CommandAcceptance.Accepted ? "yes" : "no"),
            ("detail", report.CommandAcceptance.Detail)
        ]);

        if (report.TwinTelemetry.Count > 0)
        {
            AddKeyValueTable(
                detailSection,
                "Key Twin Telemetry",
                report.TwinTelemetry.Select(item => (item.Key, item.Value)),
                monospaceValue: true);
        }

        AddKeyValueTable(detailSection, "Artifacts", report.Artifacts.Select(item => (item.Key, item.Value)), monospaceValue: true);

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

        var meta = document.Styles.AddStyle(MetaStyleName, StyleNames.Normal);
        meta.Font.Size = Unit.FromPoint(7.5);
        meta.Font.Color = Colors.Gray;
        meta.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);

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

    private static void AddTitle(Section section, SussexDiagnosticsReport report)
    {
        var title = AddParagraph(section, $"{report.StudyLabel} LSL/Twin Diagnostics", TitleStyleName);
        title.Format.SpaceAfter = Unit.FromPoint(3);

        var subtitle = AddParagraph(
            section,
            $"Generated {report.GeneratedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz} | schema {report.SchemaVersion}",
            MetaStyleName);
        subtitle.Format.SpaceAfter = Unit.FromPoint(8);
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
        table.AddColumn(Unit.FromCentimeter(13.7));

        var rowIndex = 0;
        foreach (var row in rows)
        {
            var tableRow = table.AddRow();
            StyleBodyRow(tableRow, rowIndex++);
            AddParagraph(tableRow.Cells[0], row.Key, BodyStyleName).Format.Font.Bold = true;
            var paragraph = AddParagraph(tableRow.Cells[1], row.Value ?? "n/a", monospaceValue ? DenseCodeStyleName : BodyStyleName);
            paragraph.Format.SpaceAfter = 0;
        }
    }

    private static void AddCheckSection(
        Document document,
        string heading,
        IEnumerable<SussexMachineLslCheckResult> checks)
    {
        var section = AddSection(document, Orientation.Landscape);
        AddParagraph(section, heading, SectionStyleName);

        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(2.4));
        table.AddColumn(Unit.FromCentimeter(5.2));
        table.AddColumn(Unit.FromCentimeter(18.3));

        var header = table.AddRow();
        StyleHeaderRow(header);
        AddParagraph(header.Cells[0], "Level", TableHeaderStyleName);
        AddParagraph(header.Cells[1], "Check", TableHeaderStyleName);
        AddParagraph(header.Cells[2], "Result", TableHeaderStyleName);

        var rowIndex = 0;
        foreach (var check in checks)
        {
            var row = table.AddRow();
            StyleBodyRow(row, rowIndex++);
            AddParagraph(row.Cells[0], RenderLevel(check.Level), BodyStyleName);
            AddParagraph(row.Cells[1], check.Label, BodyStyleName);
            AddParagraph(row.Cells[2], BuildCheckResultText(check.Summary, check.Detail), DenseBodyStyleName);
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

    private static string BuildCheckResultText(string summary, string detail)
    {
        var safeSummary = summary?.Trim() ?? string.Empty;
        var safeDetail = detail?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(safeDetail)
            ? safeSummary
            : string.IsNullOrWhiteSpace(safeSummary)
                ? safeDetail
                : $"{safeSummary}\n{safeDetail}";
    }

    private static Section AddSection(Document document, Orientation orientation = Orientation.Portrait)
    {
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.4);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.Orientation = orientation;
        AddFooter(section);
        return section;
    }

    private static void AddFooter(Section section)
    {
        var paragraph = section.Footers.Primary.AddParagraph();
        paragraph.Style = MetaStyleName;
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

    private static string RenderLevel(OperationOutcomeKind level)
        => level switch
        {
            OperationOutcomeKind.Success => "OK",
            OperationOutcomeKind.Warning => "WARN",
            OperationOutcomeKind.Failure => "FAIL",
            OperationOutcomeKind.Preview => "PREVIEW",
            _ => "INFO"
        };
}
