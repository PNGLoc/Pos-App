using System.Linq;
using System.Windows;
using System.Windows.Input;
using PosSystem.Main.Database;

namespace PosSystem.Main
{
    public partial class LoginWindow : Window
    {
        private bool _isPasswordVisible;

        public LoginWindow()
        {
            InitializeComponent();
            txtUser.Focus(); // Tự động trỏ chuột vào ô nhập tên
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string u = txtUser.Text.Trim();
            string p = GetPassword().Trim();

            if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
            {
                ShowLoginError("Vui lòng nhập đầy đủ thông tin!");
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
                    MainWindow main = new MainWindow();// màn hình nào hiển thị khi login thành công
                    main.Show();

                    // Đóng màn hình đăng nhập
                    this.Close();
                }
                else
                {
                    ShowLoginError("Sai tên đăng nhập hoặc mật khẩu!");
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void BtnTogglePassword_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordVisible = !_isPasswordVisible;

            if (_isPasswordVisible)
            {
                txtPassVisible.Text = txtPass.Password;
                txtPass.Visibility = Visibility.Collapsed;
                txtPassVisible.Visibility = Visibility.Visible;
                iconEyeSlash.Visibility = Visibility.Visible;
                iconEyeOpen.Visibility = Visibility.Collapsed;
                iconEyePupil.Visibility = Visibility.Collapsed;
                btnTogglePassword.ToolTip = "Ẩn mật khẩu";
                txtPassVisible.Focus();
                txtPassVisible.CaretIndex = txtPassVisible.Text?.Length ?? 0;
            }
            else
            {
                txtPass.Password = txtPassVisible.Text ?? string.Empty;
                txtPassVisible.Visibility = Visibility.Collapsed;
                txtPass.Visibility = Visibility.Visible;
                iconEyeSlash.Visibility = Visibility.Collapsed;
                iconEyeOpen.Visibility = Visibility.Visible;
                iconEyePupil.Visibility = Visibility.Visible;
                btnTogglePassword.ToolTip = "Hiện mật khẩu";
                txtPass.Focus();
            }

            ClearLoginError();
        }

        private void TxtPass_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isPasswordVisible)
            {
                // Nếu đang hiện password dạng TextBox, tránh vòng lặp sync.
                return;
            }
            ClearLoginError();
        }

        private void TxtUser_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ClearLoginError();
        }

        private void Card_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private string GetPassword()
        {
            return _isPasswordVisible ? (txtPassVisible.Text ?? string.Empty) : (txtPass.Password ?? string.Empty);
        }

        private void ShowLoginError(string message)
        {
            txtLoginError.Text = message;
            txtLoginError.Visibility = Visibility.Visible;
        }

        private void ClearLoginError()
        {
            txtLoginError.Text = string.Empty;
            txtLoginError.Visibility = Visibility.Collapsed;
        }
    }
}