using System.Windows;
using Microsoft.Extensions.Hosting;
using PosSystem.Main.Server;
using System;
using PosSystem.Main.Database; // Dùng để khởi tạo DB nếu cần

namespace PosSystem.Main
{
    public partial class App : Application
    {
        private IHost? _host;

        // Lưu ý: Phải là 'async void' vì đây là Event Handler
        protected override async void OnStartup(StartupEventArgs e)
        {
            // 1. Gọi base trước để WPF khởi động UI
            base.OnStartup(e);

            try
            {
                Console.WriteLine(">>> DANG KHOI DONG WEB SERVER..."); // Log thủ công để check

                // 2. Build Server
                _host = WebServer.CreateHostBuilder(e.Args).Build();

                // 3. Start Server (Quan trọng: phải có await)
                await _host.StartAsync();

                Console.WriteLine(">>> WEB SERVER DA CHAY THANH CONG!");
            }
            catch (Exception ex)
            {
                // Nếu lỗi, nó sẽ hiện ra đây
                MessageBox.Show($"Lỗi khởi động Server: {ex.Message}");
                Console.WriteLine(ex.ToString());
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
            base.OnExit(e);
        }
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Mẹo: Đảm bảo DB được tạo ngay khi mở app để tránh lỗi thiếu bảng
            using (var db = new AppDbContext())
            {
                db.Database.EnsureCreated();
            }

            // Mở màn hình đăng nhập
            LoginWindow login = new LoginWindow();
            login.Show();
        }
    }

}