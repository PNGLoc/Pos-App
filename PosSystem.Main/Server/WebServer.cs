using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection; // Cần cái này
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PosSystem.Main.Database; // <--- QUAN TRỌNG: Phải using thư mục Database
using PosSystem.Main.Server.Hubs;
using System;

namespace PosSystem.Main.Server
{
    public class WebServer
    {
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    // Ẩn log console của ASP.NET Core
                    logging.ClearProviders();
                    logging.AddFilter(level => false); // Tắt tất cả log
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    // Cấu hình IP và Port
                    webBuilder.UseUrls("http://0.0.0.0:5000");

                    // Cấu hình đường dẫn file tĩnh (quan trọng cho PWA/Mobile)
                    webBuilder.UseContentRoot(AppContext.BaseDirectory);
                    webBuilder.UseWebRoot("wwwroot");

                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddControllers();
                        services.AddSignalR(hubOptions =>
{
    // Thực tế mạng Wifi/mobile + UI thread có thể bận ngắn hạn.
    // KeepAlive quá thấp làm tăng overhead; timeout quá thấp dễ bị đá nhầm khi tải cao.
    hubOptions.KeepAliveInterval = TimeSpan.FromSeconds(5);
    hubOptions.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

                        // --- [SỬA LỖI TẠI ĐÂY] ---
                        // Đăng ký AppDbContext để Controller có thể sử dụng
                        services.AddDbContext<AppDbContext>();
                        // -------------------------
                    });

                    webBuilder.Configure(app =>
                    {
                        app.UseDeveloperExceptionPage();

                        // Cho phép phục vụ file index.html, css, js
                        app.UseDefaultFiles();
                        app.UseStaticFiles();

                        // Enable WebSockets (SignalR will use this transport)
                        app.UseWebSockets();

                        app.UseRouting();

                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapControllers();
                            endpoints.MapHub<PosHub>("/posHub");
                        });
                    });
                });
    }
}