using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;
namespace PosSystem.Main.Pages
{
    public partial class PrinterSetupPage : UserControl
    {
        private Printer? _selectedPrinter = null;

        public PrinterSetupPage()
        {
            InitializeComponent();
            LoadData();
        }

        private void LoadData()
        {
            using (var db = new AppDbContext())
            {
                dgPrinters.ItemsSource = db.Printers.ToList();
            }
        }

        private void dgPrinters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgPrinters.SelectedItem is Printer prt)
            {
                _selectedPrinter = prt;
                txtName.Text = prt.PrinterName;
                txtString.Text = prt.ConnectionString;
                cboType.SelectedIndex = prt.ConnectionType == "LAN" ? 0 : 1;
                chkIsBill.IsChecked = prt.IsBillPrinter;
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            using (var db = new AppDbContext())
            {
                var p = new Printer
                {
                    PrinterName = txtName.Text,
                    ConnectionType = cboType.SelectedIndex == 0 ? "LAN" : "USB",
                    ConnectionString = txtString.Text,
                    IsBillPrinter = chkIsBill.IsChecked == true,
                    IsActive = true
                };
                db.Printers.Add(p);
                db.SaveChanges();
                LoadData();
                ClearForm();
            }
        }

        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPrinter == null) return;
            using (var db = new AppDbContext())
            {
                var p = db.Printers.Find(_selectedPrinter.PrinterID);
                if (p != null)
                {
                    p.PrinterName = txtName.Text;
                    p.ConnectionType = cboType.SelectedIndex == 0 ? "LAN" : "USB";
                    p.ConnectionString = txtString.Text;
                    p.IsBillPrinter = chkIsBill.IsChecked == true;
                    db.SaveChanges();
                    LoadData();
                    ClearForm();
                }
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPrinter == null) return;
            if (MessageBox.Show("Xóa máy in này?", "Cảnh báo", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                using (var db = new AppDbContext())
                {
                    var p = db.Printers.Find(_selectedPrinter.PrinterID);
                    if (p != null) db.Printers.Remove(p);
                    db.SaveChanges();
                    LoadData();
                    ClearForm();
                }
            }
        }

        private void ClearForm()
        {
            txtName.Text = "";
            txtString.Text = "";
            _selectedPrinter = null;
        }

        // --- HÀM XỬ LÝ NÚT IN THỬ ---
        private void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            // 1. Lấy dữ liệu từ Form nhập liệu
            string name = txtName.Text.Trim();
            string connStr = txtString.Text.Trim();
            bool isLan = cboType.SelectedIndex == 0; // Index 0 là LAN, 1 là USB

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(connStr))
            {
                MessageBox.Show("Vui lòng nhập Tên máy và Cổng kết nối (IP/USB) để test!");
                return;
            }

            // 2. Tạo một đối tượng Printer tạm thời (Không lưu vào DB, chỉ để Test)
            var tempPrinter = new Printer
            {
                PrinterName = name,
                ConnectionType = isLan ? "LAN" : "USB",
                ConnectionString = connStr,
                IsActive = true
            };

            // 3. Thực hiện in thử (Chạy Async để không đơ ứng dụng)
            Task.Run(() =>
            {
                try
                {
                    // Gọi hàm PrintTest trong PrintService
                    PrintService.PrintTest(tempPrinter);

                    // Thông báo thành công (Quay về luồng UI)
                    Dispatcher.Invoke(() => MessageBox.Show("✅ Đã gửi lệnh in test!\nKiểm tra máy in của bạn."));
                }
                catch (System.Exception ex)
                {
                    Dispatcher.Invoke(() => MessageBox.Show("❌ Lỗi in thử: " + ex.Message));
                }
            });
        }
    }
}