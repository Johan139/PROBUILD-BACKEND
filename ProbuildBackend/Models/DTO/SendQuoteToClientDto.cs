namespace ProbuildBackend.Models.DTO
{
    public class SendQuoteToClientDto
    {
        public string ClientEmail { get; set; } = string.Empty;
        public string? ClientName { get; set; }
        public string? PersonalMessage { get; set; }
        public bool AttachPdf { get; set; } = true;
    }
}