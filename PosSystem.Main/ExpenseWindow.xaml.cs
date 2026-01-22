using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main
{
    public partial class ExpenseWindow : Window
    {
        public bool IsSuccess { get; private set; } = false;

        public ExpenseWindow()
        {
            InitializeComponent();
            txtAmount.Focus();
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void TxtAmount_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Remove previous formatting to get raw number
            string rawText = txtAmount.Text.Replace(".", "").Replace(",", "");

            if (decimal.TryParse(rawText, out decimal amount))
            {
                // Format with thousand separators (e.g. 1.000)
                // Use CultureInfo.InvariantCulture or specific culture if needed, here N0 uses system default
                // For "1.000" style (dot as thousand sep), we can force Vietnamese culture or simple Replace
                
                string formatted = string.Format("{0:N0}", amount); 
                
                // If system uses comma for thousand, we might need to replace. 
                // Assuming N0 output is suitable. If user wants strictly "1.000", let's check:
                
                // Avoid infinite loop by checking if text actually changed
                if (txtAmount.Text != formatted)
                {
                    txtAmount.Text = formatted;
                    txtAmount.CaretIndex = formatted.Length; // Move caret to end
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Reset message
            lblMessage.Text = "";

            if (!decimal.TryParse(txtAmount.Text, out decimal amount) || amount <= 0)
            {
                lblMessage.Text = "⚠️ Vui lòng nhập số tiền hợp lệ!";
                lblMessage.Foreground = Brushes.Red;
                return;
            }

            string note = txtNote.Text.Trim();
            if (string.IsNullOrEmpty(note))
            {
                lblMessage.Text = "⚠️ Vui lòng nhập lý do chi!";
                lblMessage.Foreground = Brushes.Red;
                return;
            }

            try
            {
                using (var db = new AppDbContext())
                {
                    var expense = new Expense
                    {
                        Amount = amount,
                        Note = note,
                        ExpenseDate = DateTime.Now,
                        CreatedBy = UserSession.AccName ?? "Unknown"
                    };

                    db.Expenses.Add(expense);
                    await db.SaveChangesAsync(); // Async save
                }

                IsSuccess = true;
                lblMessage.Text = "✅ Đã lưu phiếu chi thành công!";
                lblMessage.Foreground = Brushes.Green;

                // Disable button to prevent double click
                ((Button)sender).IsEnabled = false;

                // Wait then close
                await Task.Delay(1500);
                Close();
            }
            catch (Exception ex)
            {
                lblMessage.Text = "❌ Lỗi: " + ex.Message;
                lblMessage.Foreground = Brushes.Red;
            }
        }
    }
}
