using System.ComponentModel.DataAnnotations;

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
    }
}