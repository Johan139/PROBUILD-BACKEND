namespace BuildigBackend.Models.DTO
{
    public class AnalyzePreviewBidsRequestDto
    {
        public string? ComparisonType { get; set; }

        public PreviewTradePackageDto? TradePackage { get; set; }

        public List<PreviewBidDto> Bids { get; set; } = new();
    }

    public class PreviewTradePackageDto
    {
        public int? Id { get; set; }

        public string? TradeName { get; set; }

        public string? Category { get; set; }

        public string? ScopeOfWork { get; set; }

        public string? CsiCode { get; set; }

        public decimal? Budget { get; set; }

        public decimal? LaborBudget { get; set; }

        public decimal? MaterialBudget { get; set; }

        public string? LaborType { get; set; }
    }

    public class PreviewBidDto
    {
        public int BidId { get; set; }

        public decimal Amount { get; set; }

        public string? Status { get; set; }

        public float? BuildigRating { get; set; }

        public float? GoogleRating { get; set; }
    }
}

