using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main.Pages
{
    public partial class TableSetupPage : UserControl
    {
        private Table? _selected = null;
        public TableSetupPage() { InitializeComponent(); LoadData(); }

        // Mapping giữa hiển thị tiếng Việt và giá trị database
        private string ConvertDisplayToDb(string display) => display switch
        {
            "Bàn ăn tại quán" => "DineIn",
            "Mang về" => "TakeAway",
            "Khách lấy" => "Pickup",
            "Ship" => "Delivery",
            _ => display
        };

        private string ConvertDbToDisplay(string dbValue) => dbValue switch
        {
            "DineIn" => "Bàn ăn tại quán",
            "TakeAway" => "Mang về",
            "Pickup" => "Khách lấy",
            "Delivery" => "Ship",
            _ => dbValue
        };

        void LoadData() 
        { 
            try
            {
                using (var db = new AppDbContext()) 
                    dgTables.ItemsSource = db.Tables.OrderBy(t => t.TableID).ToList();
            }
            catch { }
        }

        // --- CÁC HÀM XỬ LÝ MODAL ---

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            // Mở form thêm mới
            _selected = null;
            txtName.Text = "";
            cboType.SelectedIndex = 0;
            
            lblModalTitle.Text = "THÊM BÀN MỚI";
            modalOverlay.Visibility = Visibility.Visible;
            txtName.Focus();
        }

        private void BtnEditRow_Click(object sender, RoutedEventArgs e)
        {
            // Mở form sửa từ nút trên dòng
            if (sender is Button btn && btn.Tag is Table t)
            {
                _selected = t;
                txtName.Text = t.TableName;
                cboType.Text = ConvertDbToDisplay(t.TableType);

                lblModalTitle.Text = "SỬA THÔNG TIN BÀN";
                modalOverlay.Visibility = Visibility.Visible;
                txtName.Focus();
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Table t)
            {
                if (MessageBox.Show($"Bạn có chắc muốn xóa bàn '{t.TableName}'?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    using (var db = new AppDbContext())
                    {
                        var item = db.Tables.Find(t.TableID);
                        if (item != null)
                        {
                            db.Tables.Remove(item);
                            db.SaveChanges();
                            LoadData();
                        }
                    }
                }
            }
        }

        private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
        {
            modalOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnSaveModal_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Vui lòng nhập tên bàn!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (var db = new AppDbContext())
            {
                if (_selected == null)
                {
                    // Thêm mới
                    db.Tables.Add(new Table 
                    { 
                        TableName = txtName.Text, 
                        TableType = ConvertDisplayToDb(cboType.Text), 
                        TableStatus = "Empty" // Mặc định bàn trống 
                    });
                }
                else
                {
                    // Cập nhật
                    var t = db.Tables.Find(_selected.TableID);
                    if (t != null)
                    {
                        t.TableName = txtName.Text;
                        t.TableType = ConvertDisplayToDb(cboType.Text);
                    }
                }
                db.SaveChanges();
            }

            // Đóng modal và tải lại dữ liệu
            modalOverlay.Visibility = Visibility.Collapsed;
            LoadData();
        }
    }
}