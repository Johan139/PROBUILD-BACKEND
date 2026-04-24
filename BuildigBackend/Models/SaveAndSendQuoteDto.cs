using BuildigBackend.Models.DTO;

namespace BuildigBackend.Models
{
    public class SaveAndSendQuoteDto
    {
        public QuoteDto Quote { get; set; }
        public SendQuoteToClientDto Send { get; set; }
    }
}

