using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PosSystem.Main.Models
{
    [Table("CancelledLogs")]
    public class CancelledLog
    {
        [Key]
        public int LogID { get; set; }

        public int? TableID { get; set; }
        
        [ForeignKey("TableID")]
        public virtual Table Table { get; set; }

        public long? OrderID { get; set; } // Liên kết lỏng lẻo (vì Order có thể bị xóa)

        [Required]
        public string DishName { get; set; }

        public int Quantity { get; set; }
        public decimal Amount { get; set; } // Giá trị hủy

        // Removed Reason property as per user request

        public string DeletedBy { get; set; } // Người hủy (Acc Name)

        public DateTime CancelTime { get; set; } = DateTime.Now;
    }
}
