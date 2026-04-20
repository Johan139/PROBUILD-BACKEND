namespace BuildigBackend.Interface
{
    public interface IPdfTextExtractionService
    {
        Task<string> ExtractTextAsync(Stream pdfStream);
    }
}

