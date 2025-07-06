using System.Collections.Generic;

// ProbuildBackend/Models/BillOfMaterials.cs (and BomItem.cs)
// Add these models if they don't exist, for the GenerateBomFromText method.
public class BillOfMaterials
{
    public List<BomItem> BillOfMaterialsItems { get; set; } = new List<BomItem>();
}

public class BomItem
{
    public string Item { get; set; }
    public string Description { get; set; }
    public string Quantity { get; set; }
}