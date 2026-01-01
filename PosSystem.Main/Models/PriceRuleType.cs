using System.ComponentModel.DataAnnotations;

namespace PosSystem.Main.Models
{
    public class PriceRuleType
    {
        [Key]
        public int PriceRuleTypeID { get; set; }

        [Required]
        public string RuleType { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}
