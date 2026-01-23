using System.Linq;
using System.Threading.Tasks;
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

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            _selectedPrinter = null;
            lblModalTitle.Text = "THÊM MÁY IN MỚI";
            txtName.Text = "";
            txtString.Text = "";
            cboType.SelectedIndex = 0;
            chkIsBill.IsChecked = false;
            cboBeepCount.SelectedIndex = 0;

            modalOverlay.Visibility = Visibility.Visible;
            txtName.Focus();
        }

        private void BtnEditRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Printer prt)
            {
                _selectedPrinter = prt;
                lblModalTitle.Text = "CẬP NHẬT MÁY IN";
                txtName.Text = prt.PrinterName;
                txtString.Text = prt.ConnectionString;
                cboType.SelectedIndex = prt.ConnectionType == "LAN" ? 0 : 1;
                chkIsBill.IsChecked = prt.IsBillPrinter;
                int beepCount = prt.BeepCount;
                if (beepCount < 0) beepCount = 0;
                if (beepCount > 3) beepCount = 3;
                cboBeepCount.SelectedIndex = beepCount;

                modalOverlay.Visibility = Visibility.Visible;
                txtName.Focus();
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Printer prt)
            {
                if (MessageBox.Show($"Bạn chắc chắn muốn xóa máy in '{prt.PrinterName}'?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    using (var db = new AppDbContext())
                    {
                        var item = db.Printers.Find(prt.PrinterID);
                        if (item != null)
                        {
                            db.Printers.Remove(item);
                            db.SaveChanges();
                            LoadData();
                        }
                    }
                }
            }
        }

        private void BtnTestRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Printer prt)
            {
                // In thử (Async)
                Task.Run(() =>
                {
                    try
                    {
                        PrintService.PrintTest(prt);
                        Dispatcher.Invoke(() => MessageBox.Show($"✅ Đã gửi lệnh in test tới '{prt.PrinterName}'!"));
                    }
                    catch (System.Exception ex)
                    {
                        Dispatcher.Invoke(() => MessageBox.Show("❌ Lỗi in thử: " + ex.Message));
                    }
                });
            }
        }

        private void BtnSaveModal_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text) || string.IsNullOrWhiteSpace(txtString.Text))
            {
                MessageBox.Show("Vui lòng nhập Tên máy và Cổng kết nối (IP/USB)!");
                return;
            }

            using (var db = new AppDbContext())
            {
                int beepCount = cboBeepCount.SelectedIndex;
                if (beepCount < 0) beepCount = 0;
                if (beepCount > 3) beepCount = 3;
                bool beepOnPrint = beepCount > 0;

                if (_selectedPrinter == null)
                {
                    // Add
                    var p = new Printer
                    {
                        PrinterName = txtName.Text,
                        ConnectionType = cboType.SelectedIndex == 0 ? "LAN" : "USB",
                        ConnectionString = txtString.Text,
                        IsBillPrinter = chkIsBill.IsChecked == true,
                        BeepOnPrint = beepOnPrint,
                        BeepCount = beepCount,
                        IsActive = true
                    };
                    db.Printers.Add(p);
                }
                else
                {
                    // Update
                    var p = db.Printers.Find(_selectedPrinter.PrinterID);
                    if (p != null)
                    {
                        p.PrinterName = txtName.Text;
                        p.ConnectionType = cboType.SelectedIndex == 0 ? "LAN" : "USB";
                        p.ConnectionString = txtString.Text;
                        p.IsBillPrinter = chkIsBill.IsChecked == true;
                        p.BeepOnPrint = beepOnPrint;
                        p.BeepCount = beepCount;
                    }
                }
                db.SaveChanges();
            }

            modalOverlay.Visibility = Visibility.Collapsed;
            LoadData();
        }

        private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
        {
            modalOverlay.Visibility = Visibility.Collapsed;
        }
    }
}