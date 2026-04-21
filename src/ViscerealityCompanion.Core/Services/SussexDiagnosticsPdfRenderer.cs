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

        AddOverviewPage(document, report);
        AddActionPlanPage(document, report);
        AddQuestSetupPage(document, report);
        AddCheckSection(
            document,
            "Windows Environment",
            report.WindowsEnvironment.Level,
            report.WindowsEnvironment.Summary,
            report.WindowsEnvironment.Detail,
            report.WindowsEnvironment.CompletedAtUtc,
            report.WindowsEnvironment.Checks.Select(check => new SussexMachineLslCheckResult(check.Label, check.Level, check.Summary, check.Detail)));
        AddCheckSection(
            document,
            "Machine LSL State",
            report.MachineLslState.Level,
            report.MachineLslState.Summary,
            report.MachineLslState.Detail,
            report.MachineLslState.CompletedAtUtc,
            report.MachineLslState.Checks);
        AddTransportAndTwinPage(document, report);
        AddArtifactsPage(document, report);

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

    private static void AddOverviewPage(Document document, SussexDiagnosticsReport report)
    {
        var section = AddSection(document, Orientation.Landscape);
        AddTitle(section, report);

        AddParagraph(
            section,
            "Read this report in order: overall verdict, immediate follow-up items, Quest baseline, then the detailed subsystem evidence. The first pages are designed for operator triage; later pages preserve the raw check output.",
            MetaStyleName);

        AddKeyValueTable(section, "Executive Summary",
        [
            ("overall verdict", $"{RenderLevel(report.Level)} - {report.Summary}"),
            ("operator detail", report.Detail),
            ("study", report.StudyId),
            ("package", report.PackageId),
            ("expected upstream", $"{report.ExpectedLslStreamName} / {report.ExpectedLslStreamType}"),
            ("report folder", report.ReportDirectory)
        ]);

        AddStatusTable(section, "Critical Path", BuildCriticalPathRows(report));
    }

    private static void AddActionPlanPage(Document document, SussexDiagnosticsReport report)
    {
        var section = AddSection(document, Orientation.Landscape);
        AddParagraph(section, "Immediate Follow-Up", SectionStyleName);
        AddParagraph(
            section,
            "These rows keep only the actionable advisories from the current snapshot. A clean run should collapse this page down to either no rows or only informational notes.",
            MetaStyleName);

        var findings = BuildActionPlanRows(report);
        if (findings.Count == 0)
        {
            AddParagraph(
                section,
                "No blockers or advisories were recorded in this snapshot. The current diagnostics output is internally consistent.",
                BodyStyleName);
            return;
        }

        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(1.8));
        table.AddColumn(Unit.FromCentimeter(4.4));
        table.AddColumn(Unit.FromCentimeter(18.4));

        var header = table.AddRow();
        StyleHeaderRow(header);
        AddParagraph(header.Cells[0], "Level", TableHeaderStyleName);
        AddParagraph(header.Cells[1], "Topic", TableHeaderStyleName);
        AddParagraph(header.Cells[2], "Actionable evidence", TableHeaderStyleName);

        var rowIndex = 0;
        foreach (var finding in findings)
        {
            var row = table.AddRow();
            StyleBodyRow(row, rowIndex++);
            AddParagraph(row.Cells[0], RenderLevel(finding.Level), BodyStyleName);
            AddParagraph(row.Cells[1], finding.Topic, BodyStyleName);
            AddParagraph(row.Cells[2], finding.Finding, DenseBodyStyleName);
        }
    }

    private static void AddQuestSetupPage(Document document, SussexDiagnosticsReport report)
    {
        var section = AddSection(document);
        AddParagraph(section, "Quest Baseline", SectionStyleName);

        AddKeyValueTable(section, "Headset Status",
        [
            ("selector", report.QuestSetup.Selector),
            ("connection", report.QuestSetup.Headset.ConnectionLabel),
            ("device model", report.QuestSetup.Headset.DeviceModel),
            ("battery", report.QuestSetup.Headset.BatteryLevel?.ToString(CultureInfo.InvariantCulture) ?? "n/a"),
            ("software version", report.QuestSetup.Headset.SoftwareVersion),
            ("software build", report.QuestSetup.Headset.SoftwareBuildId),
            ("foreground package", report.QuestSetup.Headset.ForegroundPackageId),
            ("foreground component", report.QuestSetup.Headset.ForegroundComponent ?? string.Empty),
            ("foreground verdict", report.QuestSetup.ForegroundAndSnapshot),
            ("headset summary", BuildCheckResultText(report.QuestSetup.Headset.Summary, report.QuestSetup.Headset.Detail))
        ], monospaceValue: false);

        AddKeyValueTable(section, "Pinned App Snapshot",
        [
            ("package id", report.QuestSetup.InstalledApp.PackageId),
            ("installed", report.QuestSetup.InstalledApp.IsInstalled ? "yes" : "no"),
            ("version", $"{report.QuestSetup.InstalledApp.VersionName} ({report.QuestSetup.InstalledApp.VersionCode})"),
            ("sha256", report.QuestSetup.InstalledApp.InstalledSha256),
            ("install path", report.QuestSetup.InstalledApp.InstalledPath),
            ("pinned build", report.QuestSetup.PinnedBuild),
            ("app summary", BuildCheckResultText(report.QuestSetup.InstalledApp.Summary, report.QuestSetup.InstalledApp.Detail))
        ], monospaceValue: true);

        AddKeyValueTable(section, "Device Profile Snapshot",
        [
            ("profile id", report.QuestSetup.DeviceProfile.ProfileId),
            ("label", report.QuestSetup.DeviceProfile.Label),
            ("active", report.QuestSetup.DeviceProfile.IsActive ? "yes" : "no"),
            ("profile verdict", report.QuestSetup.DeviceProfileSummary),
            ("profile summary", BuildCheckResultText(report.QuestSetup.DeviceProfile.Summary, report.QuestSetup.DeviceProfile.Detail))
        ]);

        var mismatchedProperties = report.QuestSetup.DeviceProfile.Properties
            .Where(property => !property.Matches)
            .ToArray();
        if (mismatchedProperties.Length > 0)
        {
            AddParagraph(section, "Profile Property Mismatches", SectionStyleName);

            var table = CreateTable(section);
            table.AddColumn(Unit.FromCentimeter(4.8));
            table.AddColumn(Unit.FromCentimeter(3.0));
            table.AddColumn(Unit.FromCentimeter(6.0));
            table.AddColumn(Unit.FromCentimeter(2.4));

            var header = table.AddRow();
            StyleHeaderRow(header);
            AddParagraph(header.Cells[0], "Property", TableHeaderStyleName);
            AddParagraph(header.Cells[1], "Expected", TableHeaderStyleName);
            AddParagraph(header.Cells[2], "Reported", TableHeaderStyleName);
            AddParagraph(header.Cells[3], "Blocks", TableHeaderStyleName);

            var rowIndex = 0;
            foreach (var property in mismatchedProperties)
            {
                var row = table.AddRow();
                StyleBodyRow(row, rowIndex++);
                AddParagraph(row.Cells[0], property.Key, DenseCodeStyleName);
                AddParagraph(row.Cells[1], property.ExpectedValue, DenseBodyStyleName);
                AddParagraph(row.Cells[2], property.ReportedValue, DenseBodyStyleName);
                AddParagraph(row.Cells[3], property.BlocksActivation ? "yes" : "no", BodyStyleName);
            }
        }
    }

    private static void AddCheckSection(
        Document document,
        string heading,
        OperationOutcomeKind overallLevel,
        string summary,
        string detail,
        DateTimeOffset completedAtUtc,
        IEnumerable<SussexMachineLslCheckResult> checks)
    {
        var section = AddSection(document, Orientation.Landscape);
        AddParagraph(section, heading, SectionStyleName);
        AddKeyValueTable(section, "Section Summary",
        [
            ("overall level", RenderLevel(overallLevel)),
            ("summary", summary),
            ("detail", detail),
            ("checked at", completedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture))
        ]);

        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(2.2));
        table.AddColumn(Unit.FromCentimeter(5.0));
        table.AddColumn(Unit.FromCentimeter(18.7));

        var header = table.AddRow();
        StyleHeaderRow(header);
        AddParagraph(header.Cells[0], "Level", TableHeaderStyleName);
        AddParagraph(header.Cells[1], "Check", TableHeaderStyleName);
        AddParagraph(header.Cells[2], "Evidence", TableHeaderStyleName);

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

    private static void AddTransportAndTwinPage(Document document, SussexDiagnosticsReport report)
    {
        var section = AddSection(document, Orientation.Landscape);
        AddParagraph(section, "Transport And Twin Evidence", SectionStyleName);

        AddKeyValueTable(section, "Quest Wi-Fi Transport",
        [
            ("level", RenderLevel(report.QuestWifiTransport.Level)),
            ("summary", report.QuestWifiTransport.Summary),
            ("detail", report.QuestWifiTransport.Detail),
            ("selector", report.QuestWifiTransport.Selector),
            ("headset wifi", report.QuestWifiTransport.HeadsetWifi),
            ("host wifi", report.QuestWifiTransport.HostWifi),
            ("topology", report.QuestWifiTransport.Topology),
            ("ping", report.QuestWifiTransport.Ping),
            ("tcp", report.QuestWifiTransport.Tcp),
            ("bootstrap", string.IsNullOrWhiteSpace(report.QuestWifiTransport.Bootstrap) ? "n/a" : report.QuestWifiTransport.Bootstrap)
        ]);

        AddKeyValueTable(section, "Quest Twin Return Path",
        [
            ("level", RenderLevel(report.TwinConnection.Level)),
            ("summary", report.TwinConnection.Summary),
            ("detail", report.TwinConnection.Detail),
            ("twin-state outlet", QuestTwinStatePublisherInventoryService.RenderForOperator(report.TwinStatePublisherInventory)),
            ("expected inlet", report.TwinConnection.ExpectedInlet),
            ("windows expected stream", report.TwinConnection.WindowsExpectedStream),
            ("missing links", FormatDiagnosticList(report.TwinConnection.MissingLinks)),
            ("focus next", report.TwinConnection.FocusNext),
            ("runtime target", report.TwinConnection.RuntimeTarget),
            ("connected inlet", report.TwinConnection.ConnectedInlet),
            ("counts", report.TwinConnection.Counts),
            ("quest status", report.TwinConnection.QuestStatus),
            ("quest echo", report.TwinConnection.QuestEcho),
            ("return path", report.TwinConnection.ReturnPath),
            ("transport detail", report.TwinConnection.TransportDetail)
        ]);

        AddKeyValueTable(section, "Command Acceptance Probe",
        [
            ("level", RenderLevel(report.CommandAcceptance.Level)),
            ("summary", report.CommandAcceptance.Summary),
            ("detail", report.CommandAcceptance.Detail),
            ("attempted", report.CommandAcceptance.Attempted ? "yes" : "no"),
            ("sent", report.CommandAcceptance.Sent ? "yes" : "no"),
            ("accepted", report.CommandAcceptance.Accepted ? "yes" : "no"),
            ("action id", report.CommandAcceptance.ActionId),
            ("sequence", report.CommandAcceptance.Sequence?.ToString(CultureInfo.InvariantCulture) ?? "n/a"),
            ("last action id", report.CommandAcceptance.LastReportedActionId),
            ("last action sequence", report.CommandAcceptance.LastReportedActionSequence),
            ("last particle sequence", report.CommandAcceptance.LastReportedParticleSequence)
        ]);

        if (report.TwinStatePublisherInventory.VisiblePublishers.Count > 0)
        {
            AddParagraph(section, "Visible Twin-State Publishers", SectionStyleName);
            var table = CreateTable(section);
            table.AddColumn(Unit.FromCentimeter(3.4));
            table.AddColumn(Unit.FromCentimeter(3.6));
            table.AddColumn(Unit.FromCentimeter(7.8));
            table.AddColumn(Unit.FromCentimeter(2.2));
            table.AddColumn(Unit.FromCentimeter(2.5));

            var header = table.AddRow();
            StyleHeaderRow(header);
            AddParagraph(header.Cells[0], "Name", TableHeaderStyleName);
            AddParagraph(header.Cells[1], "Type", TableHeaderStyleName);
            AddParagraph(header.Cells[2], "Source Id", TableHeaderStyleName);
            AddParagraph(header.Cells[3], "Channels", TableHeaderStyleName);
            AddParagraph(header.Cells[4], "Rate Hz", TableHeaderStyleName);

            var rowIndex = 0;
            foreach (var publisher in report.TwinStatePublisherInventory.VisiblePublishers)
            {
                var row = table.AddRow();
                StyleBodyRow(row, rowIndex++);
                AddParagraph(row.Cells[0], publisher.Name, DenseBodyStyleName);
                AddParagraph(row.Cells[1], publisher.Type, DenseBodyStyleName);
                AddParagraph(row.Cells[2], publisher.SourceId, DenseCodeStyleName);
                AddParagraph(row.Cells[3], publisher.ChannelCount.ToString(CultureInfo.InvariantCulture), BodyStyleName);
                AddParagraph(row.Cells[4], publisher.SampleRateHz.ToString("0.###", CultureInfo.InvariantCulture), BodyStyleName);
            }
        }

        if (report.TwinTelemetry.Count > 0)
        {
            AddKeyValueTable(
                section,
                "Key Twin Telemetry",
                report.TwinTelemetry.Select(item => (item.Key, item.Value)),
                monospaceValue: true);
        }
    }

    private static void AddArtifactsPage(Document document, SussexDiagnosticsReport report)
    {
        var section = AddSection(document);
        AddParagraph(section, "Artifacts And Paths", SectionStyleName);
        AddKeyValueTable(section, "Artifacts",
        new[]
        {
            ("operator data root", report.OperatorDataRoot),
            ("report directory", report.ReportDirectory)
        }.Concat(report.Artifacts.Select(item => (item.Key, item.Value))), monospaceValue: true);
    }

    private static IReadOnlyList<DiagnosticStatusRow> BuildCriticalPathRows(SussexDiagnosticsReport report)
    {
        var rows = new List<DiagnosticStatusRow>
        {
            new(
                "Windows environment",
                report.WindowsEnvironment.Level,
                BuildTriageSummary(report.WindowsEnvironment.Summary, report.WindowsEnvironment.Detail),
                FirstActionableCheckFocus(report.WindowsEnvironment.Checks) ?? "No Windows-side blocker is currently highlighted."),
            new(
                "Machine LSL state",
                report.MachineLslState.Level,
                BuildTriageSummary(report.MachineLslState.Summary, report.MachineLslState.Detail),
                FirstActionableCheckFocus(report.MachineLslState.Checks) ?? "Machine-visible LSL state looks internally consistent."),
            new(
                "Quest Wi-Fi transport",
                report.QuestWifiTransport.Level,
                BuildTriageSummary(report.QuestWifiTransport.Summary, report.QuestWifiTransport.Detail),
                BuildTriageFocusNext(report.QuestWifiTransport.Detail)
                    ?? (string.IsNullOrWhiteSpace(report.QuestWifiTransport.Bootstrap)
                        ? NormalizeInline(report.QuestWifiTransport.Topology)
                        : NormalizeInline($"{report.QuestWifiTransport.Topology} {report.QuestWifiTransport.Bootstrap}"))),
            new(
                "Twin return path",
                report.TwinConnection.Level,
                BuildTriageSummary(report.TwinConnection.Summary, report.TwinConnection.Detail),
                string.IsNullOrWhiteSpace(report.TwinConnection.FocusNext)
                    ? "No further focus suggestion was recorded."
                    : report.TwinConnection.FocusNext),
            new(
                "Command acceptance",
                report.CommandAcceptance.Level,
                BuildTriageSummary(report.CommandAcceptance.Summary, report.CommandAcceptance.Detail),
                report.CommandAcceptance.Accepted
                    ? "Quest echoed the probe action."
                    : report.CommandAcceptance.Attempted
                        ? "Probe was attempted but not confirmed."
                        : "Probe was skipped in this run.")
        };

        return rows;
    }

    private static IReadOnlyList<DiagnosticFindingRow> BuildActionPlanRows(SussexDiagnosticsReport report)
    {
        var findings = new List<DiagnosticFindingRow>();

        AppendActionableFindings(findings, "Windows environment", report.WindowsEnvironment.Checks);
        AppendActionableFindings(findings, "Machine LSL state", report.MachineLslState.Checks);

        if (report.QuestWifiTransport.Level is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure)
        {
            findings.Add(new DiagnosticFindingRow(
                report.QuestWifiTransport.Level,
                "Quest Wi-Fi transport",
                BuildActionableEvidence(report.QuestWifiTransport.Summary, report.QuestWifiTransport.Detail)));
        }

        foreach (var missingLink in report.TwinConnection.MissingLinks)
        {
            findings.Add(new DiagnosticFindingRow(
                report.TwinConnection.Level,
                "Twin return path",
                missingLink));
        }

        if (!string.IsNullOrWhiteSpace(report.TwinConnection.FocusNext))
        {
            findings.Add(new DiagnosticFindingRow(
                OperationOutcomeKind.Preview,
                "Focus next",
                report.TwinConnection.FocusNext));
        }

        if (report.CommandAcceptance.Level is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure || !report.CommandAcceptance.Attempted)
        {
            findings.Add(new DiagnosticFindingRow(
                report.CommandAcceptance.Level,
                "Command acceptance",
                BuildActionableEvidence(report.CommandAcceptance.Summary, report.CommandAcceptance.Detail)));
        }

        return findings
            .Take(14)
            .ToArray();
    }

    private static void AppendActionableFindings(
        ICollection<DiagnosticFindingRow> findings,
        string topic,
        IEnumerable<WindowsEnvironmentCheckResult> checks)
    {
        foreach (var check in checks.Where(check => check.Level is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure))
        {
            findings.Add(new DiagnosticFindingRow(
                check.Level,
                topic,
                $"{check.Label}: {BuildActionableEvidence(check.Summary, check.Detail)}"));
        }
    }

    private static void AppendActionableFindings(
        ICollection<DiagnosticFindingRow> findings,
        string topic,
        IEnumerable<SussexMachineLslCheckResult> checks)
    {
        foreach (var check in checks.Where(check => check.Level is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure))
        {
            findings.Add(new DiagnosticFindingRow(
                check.Level,
                topic,
                $"{check.Label}: {BuildActionableEvidence(check.Summary, check.Detail)}"));
        }
    }

    private static string? FirstActionableCheckFocus(IEnumerable<WindowsEnvironmentCheckResult> checks)
        => checks
            .FirstOrDefault(check => check.Level is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure)
            is { } check
                ? BuildTriageFocusNext(check.Detail) ?? $"{check.Label}: {BuildTriageSummary(check.Summary, check.Detail)}"
                : null;

    private static string? FirstActionableCheckFocus(IEnumerable<SussexMachineLslCheckResult> checks)
        => checks
            .FirstOrDefault(check => check.Level is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure)
            is { } check
                ? BuildTriageFocusNext(check.Detail) ?? $"{check.Label}: {BuildTriageSummary(check.Summary, check.Detail)}"
                : null;

    private static void AddStatusTable(
        Section section,
        string heading,
        IReadOnlyList<DiagnosticStatusRow> rows)
    {
        AddParagraph(section, heading, SectionStyleName);
        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(4.0));
        table.AddColumn(Unit.FromCentimeter(2.0));
        table.AddColumn(Unit.FromCentimeter(9.4));
        table.AddColumn(Unit.FromCentimeter(9.4));

        var header = table.AddRow();
        StyleHeaderRow(header);
        AddParagraph(header.Cells[0], "Stage", TableHeaderStyleName);
        AddParagraph(header.Cells[1], "Level", TableHeaderStyleName);
        AddParagraph(header.Cells[2], "Evidence", TableHeaderStyleName);
        AddParagraph(header.Cells[3], "Focus next", TableHeaderStyleName);

        var rowIndex = 0;
        foreach (var row in rows)
        {
            var tableRow = table.AddRow();
            StyleBodyRow(tableRow, rowIndex++);
            AddParagraph(tableRow.Cells[0], row.Stage, BodyStyleName);
            AddParagraph(tableRow.Cells[1], RenderLevel(row.Level), BodyStyleName);
            AddParagraph(tableRow.Cells[2], row.Evidence, DenseBodyStyleName);
            AddParagraph(tableRow.Cells[3], row.FocusNext, DenseBodyStyleName);
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
        table.AddColumn(Unit.FromCentimeter(17.5 - keyColumnWidthCm));

        var rowIndex = 0;
        foreach (var row in rows)
        {
            var tableRow = table.AddRow();
            StyleBodyRow(tableRow, rowIndex++);
            AddParagraph(tableRow.Cells[0], row.Key, BodyStyleName).Format.Font.Bold = true;
            var paragraph = AddParagraph(
                tableRow.Cells[1],
                row.Value ?? "n/a",
                monospaceValue ? DenseCodeStyleName : BodyStyleName);
            paragraph.Format.SpaceAfter = 0;
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

    private static string FormatDiagnosticList(IReadOnlyList<string> values)
        => values.Count == 0
            ? "none"
            : string.Join(Environment.NewLine, values.Select(static value => $"- {value}"));

    private static string MergeEvidence(string summary, string detail)
    {
        var combined = BuildCheckResultText(summary, detail);
        return string.IsNullOrWhiteSpace(combined) ? "n/a" : combined;
    }

    private static string BuildTriageSummary(string summary, string detail)
    {
        var parts = new List<string>();
        AppendDistinctSnippet(parts, summary);

        var signalLine = ExtractSignalLine(detail);
        AppendDistinctSnippet(parts, signalLine);

        return parts.Count == 0
            ? "n/a"
            : string.Join(" ", parts);
    }

    private static string BuildActionableEvidence(string summary, string detail)
    {
        var parts = new List<string>();
        AppendDistinctSnippet(parts, summary);
        AppendDistinctSnippet(parts, ExtractSignalLine(detail));
        AppendDistinctSnippet(parts, BuildTriageFocusNext(detail));

        return parts.Count == 0
            ? "n/a"
            : string.Join(" ", parts);
    }

    private static string? BuildTriageFocusNext(string detail)
    {
        foreach (var line in EnumerateMeaningfulLines(detail))
        {
            if (line.StartsWith("Fix:", StringComparison.OrdinalIgnoreCase))
            {
                return TakeSentences(NormalizeInline(line["Fix:".Length..]), 2);
            }

            if (line.StartsWith("Focus next:", StringComparison.OrdinalIgnoreCase))
            {
                return TakeSentences(NormalizeInline(line["Focus next:".Length..]), 2);
            }
        }

        var flattened = NormalizeInline(detail);
        var fixIndex = flattened.IndexOf("Fix:", StringComparison.OrdinalIgnoreCase);
        if (fixIndex >= 0)
        {
            return TakeSentences(NormalizeInline(flattened[(fixIndex + 4)..]), 2);
        }

        var focusIndex = flattened.IndexOf("Focus next:", StringComparison.OrdinalIgnoreCase);
        if (focusIndex >= 0)
        {
            return TakeSentences(NormalizeInline(flattened[(focusIndex + "Focus next:".Length)..]), 2);
        }

        return null;
    }

    private static string? ExtractSignalLine(string detail)
    {
        var snippets = EnumerateMeaningfulLines(detail)
            .SelectMany(EnumerateMeaningfulSentences)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (snippets.Length == 0)
        {
            return null;
        }

        var preferred = snippets.FirstOrDefault(static line =>
            line.StartsWith("Checks ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Hazards:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Missing links:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Windows expected stream inventory:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("No visible source", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Expected upstream sources:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Quest and host", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("TCP port 5555", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("SSIDs match", StringComparison.OrdinalIgnoreCase));
        return preferred ?? snippets[0];
    }

    private static IEnumerable<string> EnumerateMeaningfulLines(string? text)
        => (text ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => !string.IsNullOrWhiteSpace(line));

    private static IEnumerable<string> EnumerateMeaningfulSentences(string text)
    {
        var normalized = NormalizeInline(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            yield break;
        }

        foreach (var sentence in normalized.Split(". ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var cleaned = sentence.Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                continue;
            }

            yield return cleaned.EndsWith('.') ? cleaned : $"{cleaned}.";
        }
    }

    private static void AppendDistinctSnippet(ICollection<string> parts, string? snippet)
    {
        var normalized = NormalizeInline(snippet);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (parts.Any(existing =>
                string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase) ||
                existing.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(existing, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        parts.Add(normalized);
    }

    private static string NormalizeInline(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string TakeSentences(string text, int maxSentenceCount)
    {
        var sentences = EnumerateMeaningfulSentences(text)
            .Take(Math.Max(1, maxSentenceCount))
            .ToArray();
        return sentences.Length == 0
            ? NormalizeInline(text)
            : string.Join(" ", sentences);
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

    private static string RenderLevel(OperationOutcomeKind level)
        => level switch
        {
            OperationOutcomeKind.Success => "OK",
            OperationOutcomeKind.Warning => "WARN",
            OperationOutcomeKind.Failure => "FAIL",
            OperationOutcomeKind.Preview => "INFO",
            _ => "INFO"
        };

    private sealed record DiagnosticStatusRow(
        string Stage,
        OperationOutcomeKind Level,
        string Evidence,
        string FocusNext);

    private sealed record DiagnosticFindingRow(
        OperationOutcomeKind Level,
        string Topic,
        string Finding);
}
