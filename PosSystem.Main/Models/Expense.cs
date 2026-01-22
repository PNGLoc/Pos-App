using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PosSystem.Main.Models
{
    public class Expense
    {
        [Key]
        public long ExpenseID { get; set; }

        public decimal Amount { get; set; }
        public string Note { get; set; } // Lý do chi
        public DateTime ExpenseDate { get; set; }
        public string CreatedBy { get; set; } // Người nhập
    }
}
