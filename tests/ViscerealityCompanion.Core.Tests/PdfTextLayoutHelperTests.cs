using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class PdfTextLayoutHelperTests
{
    [Fact]
    public void PrepareForParagraph_InsertsBreaksAfterPathSeparators()
    {
        var text = @"C:\Users\tillh\AppData\Local\ViscerealityCompanion\tooling\hzdb\current\hzdb.exe";

        var prepared = PdfTextLayoutHelper.PrepareForParagraph(text, preserveCodeShape: true);

        Assert.Contains("\\\u200BUsers\\\u200Btillh", prepared, StringComparison.Ordinal);
        Assert.Contains("current\\\u200Bhzdb.\u200Bexe", prepared, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareForParagraph_BreaksVeryLongRunsWithoutSeparators()
    {
        var text = "abcdefghijklmnopqrstuvwxyz1234567890";

        var prepared = PdfTextLayoutHelper.PrepareForParagraph(text);

        Assert.Contains("\u200B", prepared, StringComparison.Ordinal);
    }
}
