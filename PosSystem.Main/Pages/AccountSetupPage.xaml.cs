using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main.Pages
{
    public partial class AccountSetupPage : UserControl
    {
        private Account? _selectedAccount = null;

        public AccountSetupPage()
        {
            InitializeComponent();
            LoadData();
        }

        private void LoadData()
        {
            using (var db = new AppDbContext())
            {
                dgAcc.ItemsSource = db.Accounts.ToList();
            }
        }

        private void DgAcc_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgAcc.SelectedItem is Account acc)
            {
                _selectedAccount = acc;
                txtName.Text = acc.AccName;
                txtUser.Text = acc.Username;
                txtPass.Text = acc.AccPass;

                // Chọn combobox (0: Admin, 1: Staff)
                cboRole.SelectedIndex = acc.AccRole == "Admin" ? 0 : 1;
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            // Validate cơ bản
            if (string.IsNullOrWhiteSpace(txtUser.Text) || string.IsNullOrWhiteSpace(txtPass.Text))
            {
                MessageBox.Show("Vui lòng nhập Tên đăng nhập và Mật khẩu!");
                return;
            }

            using (var db = new AppDbContext())
            {
                // Kiểm tra trùng username
                if (db.Accounts.Any(a => a.Username == txtUser.Text))
                {
                    MessageBox.Show("Tên đăng nhập này đã tồn tại!");
                    return;
                }

                // Lấy giá trị từ ComboBoxItem
                string role = ((ComboBoxItem)cboRole.SelectedItem).Content.ToString() ?? "Staff";

                var newAcc = new Account
                {
                    AccName = txtName.Text,
                    Username = txtUser.Text,
                    AccPass = txtPass.Text,
                    AccRole = role
                };

                db.Accounts.Add(newAcc);
                db.SaveChanges();

                LoadData();
                ClearForm();
                MessageBox.Show("Thêm nhân viên thành công!");
            }
        }

        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAccount == null)
            {
                MessageBox.Show("Vui lòng chọn nhân viên để sửa!");
                return;
            }

            using (var db = new AppDbContext())
            {
                var acc = db.Accounts.Find(_selectedAccount.AccID);
                if (acc != null)
                {
                    string role = ((ComboBoxItem)cboRole.SelectedItem).Content.ToString() ?? "Staff";

                    acc.AccName = txtName.Text;
                    acc.Username = txtUser.Text; // Có thể chặn sửa username nếu muốn
                    acc.AccPass = txtPass.Text;
                    acc.AccRole = role;

                    db.SaveChanges();
                    LoadData();
                    ClearForm();
                    MessageBox.Show("Cập nhật thành công!");
                }
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAccount == null) return;

            // Không cho xóa chính mình (nếu đang đăng nhập) hoặc xóa Admin gốc (ID=1)
            if (_selectedAccount.AccID == 1)
            {
                MessageBox.Show("Không thể xóa tài khoản Admin gốc!");
                return;
            }

            if (MessageBox.Show($"Bạn chắc chắn muốn xóa nhân viên '{_selectedAccount.AccName}'?",
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                using (var db = new AppDbContext())
                {
                    var acc = db.Accounts.Find(_selectedAccount.AccID);
                    if (acc != null)
                    {
                        db.Accounts.Remove(acc);
                        db.SaveChanges();
                        LoadData();
                        ClearForm();
                    }
                }
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        private void ClearForm()
        {
            _selectedAccount = null;
            txtName.Text = "";
            txtUser.Text = "";
            txtPass.Text = "";
            cboRole.SelectedIndex = 1; // Mặc định Staff
            dgAcc.SelectedItem = null;
        }
    }
}