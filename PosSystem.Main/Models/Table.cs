using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace PosSystem.Main.Models
{
    public class Table
    {
        [Key]
        public int TableID { get; set; }

        [Required]
        public string TableName { get; set; } = string.Empty; // Vd: Bàn 1, Bàn 2

        // Loại bàn (Legacy - sẽ bỏ sau này)
        public string TableType { get; set; } = "DineIn";

        // Category Link
        public int? CategoryID { get; set; }
        public TableCategory? Category { get; set; }

        // Trạng thái: Empty (Trống), Occupied (Có khách)
        public string TableStatus { get; set; } = "Empty";

        // Navigation property
        public ICollection<Order> Orders { get; set; } = new List<Order>();
    }
}