using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Models
{
    public class SaveAndSendQuoteDto
    {
        public QuoteDto Quote { get; set; }
        public SendQuoteToClientDto Send { get; set; }
    }
}
