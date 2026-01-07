using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PosSystem.Main.Models
{
    public class TimeLog
    {
        [Key]
        public int LogID { get; set; }

        // Liên kết với Employee thay vì Account
        public int EmpID { get; set; }

        [ForeignKey("EmpID")]
        public virtual Employee? Employee { get; set; }

        public DateTime CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }

        [NotMapped]
        public string DurationDisplay
        {
            get
            {
                if (CheckOutTime == null) return "Đang làm việc";
                var span = CheckOutTime.Value - CheckInTime;
                return $"{span.Hours}h {span.Minutes}p";
            }
        }
    }
}