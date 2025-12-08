public static class MimeTypeValidator
{
    private static readonly byte[] Jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
    private static readonly byte[] Png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
    private static readonly byte[] Pdf = new byte[] { 0x25, 0x50, 0x44, 0x46 };

    public static (string MimeType, string Extension) GetMimeType(byte[] fileBytes)
    {
        if (fileBytes == null || fileBytes.Length < 4)
        {
            throw new ArgumentException(
                "File byte array is null or too short to determine MIME type."
            );
        }

        var header = fileBytes.Take(4).ToArray();

        if (header.SequenceEqual(Jpeg))
            return ("image/jpeg", ".jpg");
        if (header.SequenceEqual(Png))
            return ("image/png", ".png");
        if (header.SequenceEqual(Pdf))
            return ("application/pdf", ".pdf");

        // Add more types as needed

        throw new NotSupportedException("File type is not supported for inline processing.");
    }
}
