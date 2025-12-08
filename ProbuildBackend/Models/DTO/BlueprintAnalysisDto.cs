namespace ProbuildBackend.Models.DTO
{
    public class BlueprintAnalysisDto
    {
        public int Id { get; set; }
        public int JobId { get; set; }
        public string Name { get; set; }
        public string PdfUrl { get; set; }
        public List<string> PageImageUrls { get; set; }
        public string AnalysisJson { get; set; }
        public int TotalPages { get; set; }
    }
}
