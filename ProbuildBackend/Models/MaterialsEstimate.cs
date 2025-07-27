namespace ProbuildBackend.Models
{
    public class MaterialsEstimate
    {
        public List<MaterialEstimateItem> Materials { get; set; } = new();
    }

    public class MaterialEstimateItem
    {
        public string Item { get; set; }
        public decimal TotalQuantity { get; set; }
        public string Unit { get; set; }
    }
}