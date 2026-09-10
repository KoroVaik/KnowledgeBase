using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace KnowledgeBase.Worker.Extraction;

public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public string Extract(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);

        // ContentOrderTextExtractor over page.Text: the latter concatenates glyphs with no word
        // spacing on many PDFs. This reconstructs reading order and whitespace.
        var pages = document.GetPages()
            .Select(page => ContentOrderTextExtractor.GetText(page).Trim())
            .Where(text => text.Length > 0);

        return string.Join("\n\n", pages);
    }
}
