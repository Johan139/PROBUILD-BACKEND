namespace ProbuildBackend.Interface
{
    public interface IPdfImageConverter
    {
        Task<List<(int PageIndex, byte[] ImageBytes)>> ConvertPdfToImagesAsync(
            string blobUrl,
            Stream contentStream
        );
    }
}
