using System.Windows;
using Microsoft.Extensions.Hosting;
using PosSystem.Main.Server;
using System;
using PosSystem.Main.Database; // Dùng để khởi tạo DB nếu cần
using Microsoft.Extensions.DependencyInjection;
namespace PosSystem.Main
{
    public partial class App : Application
    {
        public static IHost? WebHost { get; private set; }

        // Lưu ý: Phải là 'async void' vì đây là Event Handler
        protected override async void OnStartup(StartupEventArgs e)
        {
            // 1. Gọi base trước để WPF khởi động UI
            base.OnStartup(e);

            try
            {
                // 1. Tạo Web Server (nhưng chưa chạy)
                WebHost = WebServer.CreateHostBuilder(e.Args).Build();

                // 2. Chạy Server (Async để không đơ UI)
                await WebHost.StartAsync();
            }
            catch (Exception ex)
            {
                // Nếu lỗi, nó sẽ hiện ra đây
                MessageBox.Show($"Lỗi khởi động Server: {ex.Message}");
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (WebHost != null)
            {
                await WebHost.StopAsync();
                WebHost.Dispose();
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