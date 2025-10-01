namespace ProbuildBackend.Models.DTO
{
    public class ConnectionDto
    {
        public string Id { get; set; }
        public string? OtherUserId { get; set; }
        public string? OtherUserEmail { get; set; }
        public string Status { get; set; }
        public bool IsInSystem { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? RequesterId { get; set; }
        public string? ReceiverId { get; set; }
    }
}