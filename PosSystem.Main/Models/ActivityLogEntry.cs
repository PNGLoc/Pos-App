using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PosSystem.Main.Models
{
    [Table("ActivityLogs")]
    public class ActivityLogEntry
    {
        [Key]
        public long Id { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string Message { get; set; } = "";
    }
}
