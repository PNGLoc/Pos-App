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

                // --- CÁCH CŨ (Gây lỗi hiển thị như bạn thấy) ---
                // return $"{span.Hours}h {span.Minutes}p";

                // --- CÁCH MỚI (Làm tròn phút) ---
                // Math.Round: Làm tròn (>= 30s lên 1p, < 30s xuống 0p)
                // Math.Ceiling: Luôn làm tròn lên (1p 1s cũng tính là 2p - Tốt cho nhân viên hơn)

                int totalMinutes = (int)Math.Round(span.TotalMinutes);

                int h = totalMinutes / 60;
                int m = totalMinutes % 60;

                return $"{h}h {m}p";
            }
        }
    }
}