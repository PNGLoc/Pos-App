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

        // Loại bàn: DineIn (Tại quán), TakeAway (Mang về), Delivery (Ship)
        public string TableType { get; set; } = "DineIn";

        // Trạng thái: Empty (Trống), Occupied (Có khách)
        public string TableStatus { get; set; } = "Empty";

        // Navigation property
        public ICollection<Order> Orders { get; set; } = new List<Order>();
    }
}