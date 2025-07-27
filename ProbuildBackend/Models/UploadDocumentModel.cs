namespace ProbuildBackend.Models
{
    public class UploadDocumentModel
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public List<string> FileUrls { get; set; }
        public List<string> FileNames { get; set; }
        public string Message { get; set; }
        public List<BomWithCosts> BillOfMaterials { get; set; } // Add this
    }
}
