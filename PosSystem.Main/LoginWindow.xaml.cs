using System.Linq;
using System.Windows;
using PosSystem.Main.Database;

namespace PosSystem.Main
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            txtUser.Focus(); // Tự động trỏ chuột vào ô nhập tên
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string u = txtUser.Text.Trim();
            string p = txtPass.Password.Trim();

            if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
            {
                MessageBox.Show("Vui lòng nhập đầy đủ thông tin!");
                return;
            }

            using (var db = new AppDbContext())
            {
                // Kiểm tra database
                var acc = db.Accounts.FirstOrDefault(a => a.Username == u && a.AccPass == p);

                if (acc != null)
                {
                    // Đăng nhập thành công -> Lưu vào Session
                    UserSession.AccID = acc.AccID;
                    UserSession.AccName = acc.AccName;
                    UserSession.AccRole = acc.AccRole;

                    // Mở màn hình chính
                    AdminWindow admin = new AdminWindow();// màn hình nào hiển thị khi login thành công
                    admin.Show();

                    // Đóng màn hình đăng nhập
                    this.Close();
                }
                else
                {
                    MessageBox.Show("Sai tên đăng nhập hoặc mật khẩu!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}