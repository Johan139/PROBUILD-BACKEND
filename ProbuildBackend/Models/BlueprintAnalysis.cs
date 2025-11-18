using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class BlueprintAnalysis
{
    [Key]
    public int Id { get; set; }
    public int? JobId { get; set; }
    public string SessionId { get; set; }

    [Required]
    public string OriginalFileName { get; set; }

    [Required]
    public string PdfUrl { get; set; } // Original PDF

    public string PageImageUrlsJson { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string AnalysisJson { get; set; }

    public int TotalPages { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}