using System;
using System.ComponentModel.DataAnnotations;

namespace PosSystem.Main.Models
{
    public class IdempotencyRecord
    {
        [Key]
        [MaxLength(200)]
        public string Key { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool Completed { get; set; } = false;

        public int StatusCode { get; set; } = 0;

        [MaxLength(100)]
        public string? ContentType { get; set; }

        public string? ResponseBody { get; set; }
    }
}
