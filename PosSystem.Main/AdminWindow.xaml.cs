using System;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;

namespace PosSystem.Main
{
    public partial class AdminWindow : Window
    {
        public AdminWindow()
        {
            InitializeComponent();
            // Mặc định load trang Printer
            mainFrame.Navigate(new Pages.PrinterSetupPage());

            // Hiển thị IP Address
            DisplayIPAddress();
        }

        private void DisplayIPAddress()
        {
            try
            {
                // Cách 1: Lấy tất cả IP của máy
                string hostname = Dns.GetHostName();
                IPAddress[] addresses = Dns.GetHostAddresses(hostname);

                // Lấy IPv4 đầu tiên
                string ipAddress = "Không tìm thấy";
                foreach (var addr in addresses)
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ipAddress = addr.ToString();
                        break;
                    }
                }

                lblIPAddress.Text = ipAddress +":5000";
            }
            catch (Exception ex)
            {
                lblIPAddress.Text = $"Lỗi: {ex.Message}";
            }
        }

        private void Menu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton btn && btn.Tag is string tag)
            {
                // Sử dụng Navigate cho Frame
                switch (tag)
                {
                    case "Printer":
                        mainFrame.Navigate(new Pages.PrinterSetupPage());
                        break;
                    case "Table":
                        mainFrame.Navigate(new Pages.TableSetupPage());
                        break;
                    case "Account":
                        mainFrame.Navigate(new Pages.AccountSetupPage());
                        break;
                    case "Menu":
                        mainFrame.Navigate(new Pages.MenuSetupPage());
                        break;
                    case "PriceRule":
                        mainFrame.Navigate(new Pages.PriceRuleSetupPage());
                        break;
                    // Case mới cho Lịch sử đơn hàng
                    case "OrderHistory":
                        mainFrame.Navigate(new Pages.OrderHistoryPage());
                        break;
                }
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            MainWindow pos = new MainWindow();
            pos.Show();
            this.Close();
        }
        private void BtnGoToPOS_Click(object sender, RoutedEventArgs e)
        {
            MainWindow pos = new MainWindow();
            pos.Show();

            // Tùy chọn: Có muốn đóng Admin lại không? 
            // Nếu muốn giữ Admin chạy nền thì không cần Close(), nhưng thường thì nên Close cho nhẹ máy.
            this.Close();
        }
    }
}