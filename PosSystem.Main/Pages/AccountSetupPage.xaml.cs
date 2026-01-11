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
                dgAcc.ItemsSource = db.Accounts.OrderBy(a => a.AccName).ToList();
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            _selectedAccount = null;
            lblModalTitle.Text = "THÊM TÀI KHOẢN MỚI";
            
            txtName.Text = "";
            txtUser.Text = "";
            txtPass.Text = "";
            cboRole.SelectedIndex = 1; // Staff default
            
            // Enable username for new account
            txtUser.IsEnabled = true;

            chkMoveTable.IsChecked = false;
            chkPayment.IsChecked = false;
            chkCancelItem.IsChecked = false;

            modalOverlay.Visibility = Visibility.Visible;
            txtName.Focus();
        }

        private void BtnEditRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Account acc)
            {
                _selectedAccount = acc;
                lblModalTitle.Text = "CHỈNH SỬA TÀI KHOẢN";

                txtName.Text = acc.AccName;
                txtUser.Text = acc.Username;
                txtPass.Text = acc.AccPass;
                cboRole.SelectedIndex = acc.AccRole == "Admin" ? 0 : 1;

                // Disable username editing to prevent system issues
                txtUser.IsEnabled = false; 

                chkMoveTable.IsChecked = acc.CanMoveTable;
                chkPayment.IsChecked = acc.CanPayment;
                chkCancelItem.IsChecked = acc.CanCancelItem;

                modalOverlay.Visibility = Visibility.Visible;
                txtName.Focus();
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Account acc)
            {
                if (acc.AccID == 1 || acc.Username == "admin") 
                {
                    MessageBox.Show("Không thể xóa tài khoản Admin gốc!");
                    return;
                }

                if (MessageBox.Show($"Bạn chắc chắn muốn xóa tài khoản '{acc.AccName}'?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    using (var db = new AppDbContext())
                    {
                        var item = db.Accounts.Find(acc.AccID);
                        if (item != null)
                        {
                            db.Accounts.Remove(item);
                            db.SaveChanges();
                            LoadData();
                        }
                    }
                }
            }
        }

        private void BtnSaveModal_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtUser.Text) || string.IsNullOrWhiteSpace(txtPass.Text))
            {
                MessageBox.Show("Vui lòng nhập Tên đăng nhập và Mật khẩu!");
                return;
            }

            using (var db = new AppDbContext())
            {
                if (_selectedAccount == null)
                {
                    // Add New
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
                        CanMoveTable = chkMoveTable.IsChecked == true,
                        CanPayment = chkPayment.IsChecked == true,
                        CanCancelItem = chkCancelItem.IsChecked == true
                    };
                    db.Accounts.Add(newAcc);
                }
                else
                {
                    // Update
                    var acc = db.Accounts.Find(_selectedAccount.AccID);
                    if (acc != null)
                    {
                        acc.AccName = txtName.Text;
                        acc.AccPass = txtPass.Text;
                        acc.AccRole = (cboRole.SelectedIndex == 0) ? "Admin" : "Staff";
                        
                        // Don't update Username
                        
                        acc.CanMoveTable = chkMoveTable.IsChecked == true;
                        acc.CanPayment = chkPayment.IsChecked == true;
                        acc.CanCancelItem = chkCancelItem.IsChecked == true;
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