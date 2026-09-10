namespace KnowledgeBase.Worker.Extraction;

// Pulls the text layer out of a PDF. An interface of its own so PdfSourceExtractor's policy
// (empty layer -> skip, later: fall back to rendering pages) stays free of PdfPig specifics
// and can be unit-tested with a fake.
public interface IPdfTextExtractor
{
    string Extract(byte[] pdf);
}
