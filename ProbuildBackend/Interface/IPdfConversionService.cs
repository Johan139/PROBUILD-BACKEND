using System.Collections.Generic;
using System.IO;

namespace ProbuildBackend.Interface
{
    public interface IPdfConversionService
    {
        List<string> ConvertPdfToImages(Stream pdfStream, string outputFileNamePrefix, int dpi = 300);
    }
}