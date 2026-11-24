
namespace BookManagerApi.DTOs
{
    public class UpdateCopyDto
    {
        // Inventory number
        public string? InventoryNumber { get; set; }
        public bool InventoryNumberSet { get; set; }

        // Location
        public string? Location { get; set; }
        public bool LocationSet { get; set; }

        // Condition
        public string? Condition { get; set; }
        public bool ConditionSet { get; set; }

        // Center
        public int? CenterId { get; set; }
        public bool CenterIdSet { get; set; }

        // Status (availability)
        public bool Status { get; set; }
    }
}
