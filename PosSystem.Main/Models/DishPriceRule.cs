using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PosSystem.Main.Models
{
    public class DishPriceRule
    {
        [Key]
        public int PriceRuleID { get; set; }

        [ForeignKey(nameof(Dish))]
        public int DishID { get; set; }

        [Required]
        public string RuleName { get; set; } = string.Empty; // Ví dụ: "Giá Tết 2026"

        [Required]
        public string RuleType { get; set; } = "holiday"; // Loại: 'holiday', 'event', 'vip', v.v

        [Required]
        public decimal Price { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Navigation property
        public Dish? Dish { get; set; }
    }
}
