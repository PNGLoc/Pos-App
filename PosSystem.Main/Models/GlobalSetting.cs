using System.ComponentModel.DataAnnotations;

namespace PosSystem.Main.Models
{
    public class GlobalSetting
    {
        [Key]
        public int SettingID { get; set; }

        [Required]
        public string Key { get; set; } = string.Empty; // Ví dụ: "activePriceRule"

        [Required]
        public string Value { get; set; } = string.Empty; // Ví dụ: "holiday", "event", null (tức giá thường)

        public string Description { get; set; } = string.Empty;

        public DateTime ModifiedDate { get; set; } = DateTime.Now;
    }
}
