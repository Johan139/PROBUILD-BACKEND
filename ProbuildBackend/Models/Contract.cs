using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class Contract
    {
        [Key]
        public Guid Id { get; set; }
        public int JobId { get; set; }
        public string GcId { get; set; }
        public string ScVendorId { get; set; }
        public string ContractText { get; set; }
        public string GcSignature { get; set; }
        public string ScVendorSignature { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
