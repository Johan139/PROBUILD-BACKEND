using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    [Table("JobNotificationRecipients")]
    public class JobNotificationRecipient
    {
        [Key]
        public Guid id { get; set; }
        public Guid? original_id { get; set; }
        [StringLength(50)]
        public string lead_type { get; set; }
        [StringLength(255)]
        public string name { get; set; }
        [StringLength(255)]
        public string email { get; set; }
        [StringLength(50)]
        public string messyphone { get; set; }
        [StringLength(100)]
        public string city { get; set; }
        [StringLength(50)]
        public string state { get; set; }
        [StringLength(20)]
        public string postal_code { get; set; }
        [StringLength(500)]
        public string full_address { get; set; }
        [Column(TypeName = "decimal(10, 7)")]
        public decimal? latitude { get; set; }
        [Column(TypeName = "decimal(10, 7)")]
        public decimal? longitude { get; set; }
        [StringLength(255)]
        public string site { get; set; }
        [Column(TypeName = "decimal(3, 2)")]
        public decimal? rating { get; set; }
        public int? reviews { get; set; }
        [StringLength(500)]
        public string query { get; set; }
        [StringLength(100)]
        public string category { get; set; }
        public string subtypes { get; set; }
        [StringLength(255)]
        public string linkedin { get; set; }
        public DateTime? created_at { get; set; }
        public DateTime? updated_at { get; set; }
        [StringLength(100)]
        public string source_table { get; set; }
        [StringLength(255)]
        public string facebook { get; set; }
        [StringLength(255)]
        public string instagram { get; set; }
        [StringLength(255)]
        public string twitter { get; set; }
        [StringLength(255)]
        public string youtube { get; set; }
        [StringLength(255)]
        public string tiktok { get; set; }
        [StringLength(255)]
        public string snapchat { get; set; }
        [StringLength(255)]
        public string telegram { get; set; }
        [StringLength(255)]
        public string whatsapp { get; set; }
        [StringLength(255)]
        public string reddit { get; set; }
        [StringLength(255)]
        public string skype { get; set; }
        [StringLength(255)]
        public string medium { get; set; }
        [StringLength(255)]
        public string vimeo { get; set; }
        [StringLength(255)]
        public string github { get; set; }
        [StringLength(255)]
        public string crunchbase { get; set; }
        [StringLength(100)]
        public string first_name { get; set; }
        [StringLength(100)]
        public string last_name { get; set; }
        public string notes { get; set; }
        [StringLength(50)]
        public string sms_opt_in_status { get; set; }
        public DateTime? sms_opt_in_date { get; set; }
        public DateTime? last_sms_sent_at { get; set; }
        [StringLength(50)]
        public string phone { get; set; }
        [StringLength(50)]
        public string call_status { get; set; }
        [StringLength(100)]
        public string subtype_primary { get; set; }
        public bool? notification_enabled { get; set; }
        public int? notification_radius_miles { get; set; }
        public DateTime? last_job_notification { get; set; }
        public int? total_notifications_sent { get; set; }
        public NetTopologySuite.Geometries.Point location_geo { get; set; }
    }
}