namespace ProbuildBackend.Models
{

    public class SaveSubtasksRequest
    {
        public List<JobSubtasksModel> Subtasks { get; set; }
        public string UserId { get; set; }
    }
    public class JobSubtasksModel
    {
        public int Id { get; set; }
        public int JobId { get; set; }
        public string GroupTitle { get; set; }  // e.g. "Foundation Subtasks"
        public string Task { get; set; }
        public int Days { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; }

        public bool Deleted { get; set; }
    }

}
