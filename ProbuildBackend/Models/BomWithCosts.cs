namespace ProbuildBackend.Models
{
    public class BomWithCosts
    {
        public List<BomItemWithCost> BillOfMaterials { get; set; } = new();
        public decimal TotalCost { get; set; }
    }

    public class BomItemWithCost
    {
        public string Item { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal CostPerUnit { get; set; }
        public decimal TotalItemCost { get; set; }
    }
}