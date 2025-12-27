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
            contentArea.Children.Clear();
            contentArea.Children.Add(new Pages.PrinterSetupPage());
        }

        private void Menu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton btn && btn.Tag is string tag)
            {
                contentArea.Children.Clear();
                switch (tag)
                {
                    case "Printer": contentArea.Children.Add(new Pages.PrinterSetupPage()); break;
                    case "LayoutDesigner": contentArea.Children.Add(new Pages.LayoutDesignerPage()); break;

                    // --- CÁC TRANG MỚI ---
                    case "Table": contentArea.Children.Add(new Pages.TableSetupPage()); break;
                    case "Account": contentArea.Children.Add(new Pages.AccountSetupPage()); break;
                    case "Menu": contentArea.Children.Add(new Pages.MenuSetupPage()); break;
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