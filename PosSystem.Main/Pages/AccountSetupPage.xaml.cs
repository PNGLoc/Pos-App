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

        // 1. Khi chọn dòng -> Đổ dữ liệu vào Form (Bao gồm CheckBox)
        private void DgAcc_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgAcc.SelectedItem is Account acc)
            {
                _selectedAccount = acc;
                txtName.Text = acc.AccName;
                txtUser.Text = acc.Username;
                txtPass.Text = acc.AccPass;
                cboRole.SelectedIndex = acc.AccRole == "Admin" ? 0 : 1;

                // --- CẬP NHẬT: Load quyền lên checkbox ---
                chkMoveTable.IsChecked = acc.CanMoveTable;
                chkPayment.IsChecked = acc.CanPayment;
                chkCancelItem.IsChecked = acc.CanCancelItem;
                // -----------------------------------------
            }
        }

        // 2. Thêm mới -> Lưu các quyền từ CheckBox vào DB
        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtUser.Text) || string.IsNullOrWhiteSpace(txtPass.Text))
            {
                MessageBox.Show("Vui lòng nhập Tên đăng nhập và Mật khẩu!");
                return;
            }

            using (var db = new AppDbContext())
            {
                if (db.Accounts.Any(a => a.Username == txtUser.Text))
                {
                    MessageBox.Show("Tên đăng nhập đã tồn tại!");
                    return;
                }

                var newAcc = new Account
                {
                    AccName = txtName.Text,
                    Username = txtUser.Text,
                    AccPass = txtPass.Text,
                    AccRole = (cboRole.SelectedIndex == 0) ? "Admin" : "Staff",

                    // --- CẬP NHẬT: Lấy giá trị từ CheckBox ---
                    CanMoveTable = chkMoveTable.IsChecked == true,
                    CanPayment = chkPayment.IsChecked == true,
                    CanCancelItem = chkCancelItem.IsChecked == true
                    // -----------------------------------------
                };

                db.Accounts.Add(newAcc);
                db.SaveChanges();
                LoadData();
                ClearForm();
            }
        }

        // 3. Cập nhật -> Lưu thay đổi quyền
        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAccount == null) return;

            using (var db = new AppDbContext())
            {
                var acc = db.Accounts.Find(_selectedAccount.AccID);
                if (acc != null)
                {
                    acc.AccName = txtName.Text;
                    acc.AccPass = txtPass.Text;
                    acc.AccRole = (cboRole.SelectedIndex == 0) ? "Admin" : "Staff";

                    // Lưu ý: Không cho sửa Username để tránh lỗi logic hệ thống
                    // acc.Username = txtUser.Text; 

                    // --- CẬP NHẬT: Lưu quyền ---
                    acc.CanMoveTable = chkMoveTable.IsChecked == true;
                    acc.CanPayment = chkPayment.IsChecked == true;
                    acc.CanCancelItem = chkCancelItem.IsChecked == true;
                    // ---------------------------

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
            if (_selectedAccount.AccID == 1) // Bảo vệ Admin gốc
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

        // 4. Reset Form -> Bỏ chọn CheckBox
        private void ClearForm()
        {
            _selectedAccount = null;
            txtName.Text = "";
            txtUser.Text = "";
            txtPass.Text = "";
            cboRole.SelectedIndex = 1;

            // --- CẬP NHẬT: Reset checkbox ---
            chkMoveTable.IsChecked = false;
            chkPayment.IsChecked = false;
            chkCancelItem.IsChecked = false;
            // --------------------------------

            dgAcc.SelectedItem = null;
        }
    }
}