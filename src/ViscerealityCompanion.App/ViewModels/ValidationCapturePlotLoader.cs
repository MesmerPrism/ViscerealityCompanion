using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace ViscerealityCompanion.App.ViewModels;

internal static class ValidationCapturePlotLoader
{
    private const double CanvasWidth = 300d;
    private const double CanvasHeight = 120d;

    public static ValidationCapturePlotLoadResult LoadBreathing(string localSessionFolderPath)
        => Load(
            localSessionFolderPath,
            "breathing_trace.csv",
            "breath_volume01",
            "Breathing",
            "breathing samples");

    public static ValidationCapturePlotLoadResult LoadCoherence(string localSessionFolderPath)
        => Load(
            localSessionFolderPath,
            "signals_long.csv",
            "coherence.value01",
            "Coherence",
            "coherence samples",
            signalNameColumnName: "signal_name",
            numericValueColumnName: "value_numeric");

    private static ValidationCapturePlotLoadResult Load(
        string localSessionFolderPath,
        string fileName,
        string targetColumnOrSignalName,
        string label,
        string sampleLabel,
        string? signalNameColumnName = null,
        string? numericValueColumnName = null)
    {
        if (string.IsNullOrWhiteSpace(localSessionFolderPath) || !Directory.Exists(localSessionFolderPath))
        {
            return new ValidationCapturePlotLoadResult(false, $"{label} plot unavailable because the Windows session folder is missing.", CreateFrozenPointCollection());
        }

        var filePath = Path.Combine(localSessionFolderPath, fileName);
        if (!File.Exists(filePath))
        {
            return new ValidationCapturePlotLoadResult(false, $"{label} plot unavailable because {fileName} was not written.", CreateFrozenPointCollection());
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(filePath);
        }
        catch (Exception ex)
        {
            return new ValidationCapturePlotLoadResult(false, $"{label} plot could not be loaded from {fileName}: {ex.Message}", CreateFrozenPointCollection());
        }

        if (lines.Length < 2)
        {
            return new ValidationCapturePlotLoadResult(false, $"{label} plot unavailable because {fileName} has no recorded rows yet.", CreateFrozenPointCollection());
        }

        var headers = lines[0].Split(',');
        var values = signalNameColumnName is null
            ? ReadColumnValues(lines, headers, targetColumnOrSignalName)
            : ReadFilteredSignalValues(lines, headers, signalNameColumnName, targetColumnOrSignalName, numericValueColumnName ?? "value_numeric");

        if (values.Count == 0)
        {
            return new ValidationCapturePlotLoadResult(false, $"{label} plot unavailable because no {sampleLabel} were found in {fileName}.", CreateFrozenPointCollection());
        }

        var points = BuildPlotPoints(values, CanvasWidth, CanvasHeight);
        return new ValidationCapturePlotLoadResult(true, $"{values.Count} {sampleLabel} loaded from {fileName}.", points);
    }

    private static List<double> ReadColumnValues(IReadOnlyList<string> lines, IReadOnlyList<string> headers, string columnName)
    {
        var columnIndex = FindCsvColumnIndex(headers, columnName);
        if (columnIndex < 0)
        {
            return [];
        }

        var values = new List<double>(lines.Count - 1);
        for (var index = 1; index < lines.Count; index++)
        {
            var cells = lines[index].Split(',');
            if (columnIndex >= cells.Length)
            {
                continue;
            }

            if (TryParseUnitInterval(cells[columnIndex], out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static List<double> ReadFilteredSignalValues(
        IReadOnlyList<string> lines,
        IReadOnlyList<string> headers,
        string signalNameColumnName,
        string targetSignalName,
        string numericValueColumnName)
    {
        var signalNameIndex = FindCsvColumnIndex(headers, signalNameColumnName);
        var valueIndex = FindCsvColumnIndex(headers, numericValueColumnName);
        if (signalNameIndex < 0 || valueIndex < 0)
        {
            return [];
        }

        var values = new List<double>(lines.Count - 1);
        for (var index = 1; index < lines.Count; index++)
        {
            var cells = lines[index].Split(',');
            if (signalNameIndex >= cells.Length || valueIndex >= cells.Length)
            {
                continue;
            }

            if (!string.Equals(cells[signalNameIndex], targetSignalName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParseUnitInterval(cells[valueIndex], out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static int FindCsvColumnIndex(IReadOnlyList<string> headers, string columnName)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (string.Equals(headers[index], columnName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryParseUnitInterval(string raw, out double value)
    {
        if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            value = Math.Clamp(parsed, 0d, 1d);
            return true;
        }

        value = 0d;
        return false;
    }

    private static PointCollection BuildPlotPoints(IReadOnlyList<double> values, double width, double height)
    {
        if (values.Count == 0)
        {
            return CreateFrozenPointCollection();
        }

        if (values.Count == 1)
        {
            var y = (1d - values[0]) * height;
            return CreateFrozenPointCollection(
            [
                new Point(width * 0.5d, y)
            ]);
        }

        var points = new List<Point>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var x = index * width / (values.Count - 1d);
            var y = (1d - values[index]) * height;
            points.Add(new Point(x, y));
        }

        return CreateFrozenPointCollection(points);
    }

    private static PointCollection CreateFrozenPointCollection(IEnumerable<Point>? points = null)
    {
        var collection = points is null ? [] : new PointCollection(points);
        if (collection.CanFreeze)
        {
            collection.Freeze();
        }

        return collection;
    }
}
