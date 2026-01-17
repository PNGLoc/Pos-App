using System.Windows;
using Microsoft.Extensions.Hosting;
using PosSystem.Main.Server;
using System;
using PosSystem.Main.Database; // Dùng để khởi tạo DB nếu cần
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
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

                // [MIGRATION] Thêm cột CanPrintProvisional nếu chưa có (cho DB cũ)
                try
                {
                    db.Database.ExecuteSqlRaw("ALTER TABLE Accounts ADD COLUMN CanPrintProvisional INTEGER NOT NULL DEFAULT 0;");
                    db.Database.ExecuteSqlRaw("UPDATE Accounts SET CanPrintProvisional = 1 WHERE AccRole = 'Admin';");
                }
                catch { }

                // [NEW] Tạo bảng CancelledLogs nếu chưa có
                try
                {
                    db.Database.ExecuteSqlRaw(@"
                        CREATE TABLE IF NOT EXISTS ""CancelledLogs"" (
                            ""LogID"" INTEGER NOT NULL CONSTRAINT ""PK_CancelledLogs"" PRIMARY KEY AUTOINCREMENT,
                            ""TableID"" INTEGER NULL,
                            ""OrderID"" INTEGER NULL,
                            ""DishName"" TEXT NOT NULL,
                            ""Quantity"" INTEGER NOT NULL,
                            ""Amount"" TEXT NOT NULL DEFAULT '0',
                            -- Reason removed
                            ""DeletedBy"" TEXT NULL,
                            ""CancelTime"" TEXT NOT NULL DEFAULT (datetime('now')),
                            CONSTRAINT ""FK_CancelledLogs_Tables_TableID"" FOREIGN KEY (""TableID"") REFERENCES ""Tables"" (""TableID"") ON DELETE RESTRICT
                        );
                    ");
                }
                catch { }

                // [EXISTING CODE]
                try
                {
                    db.Database.ExecuteSqlRaw("ALTER TABLE Orders ADD COLUMN IsRequestingPayment INTEGER NOT NULL DEFAULT 0;");
                }
                catch { }

                // [NEW] Activity logs (persist "Hoạt động mới" in DB, keep last 200)
                try
                {
                    db.Database.ExecuteSqlRaw(@"
                        CREATE TABLE IF NOT EXISTS ""ActivityLogs"" (
                            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ActivityLogs"" PRIMARY KEY AUTOINCREMENT,
                            ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now')),
                            ""Message"" TEXT NOT NULL
                        );
                    ");

                    db.Database.ExecuteSqlRaw(@"
                        DELETE FROM ""ActivityLogs""
                        WHERE ""Id"" NOT IN (
                            SELECT ""Id"" FROM ""ActivityLogs"" ORDER BY ""Id"" DESC LIMIT 200
                        );
                    ");
                }
                catch { }
            }

            // Mở màn hình đăng nhập
            LoginWindow login = new LoginWindow();
            login.Show();
        }
    }

}