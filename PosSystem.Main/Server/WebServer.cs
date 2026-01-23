using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection; // Cần cái này
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.StaticFiles;
using PosSystem.Main.Database; // <--- QUAN TRỌNG: Phải using thư mục Database
using PosSystem.Main.Helpers;
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

                        // Ensure data folders exist early
                        try { AppPaths.EnsureInitialized(); } catch { }

                        // Cho phép phục vụ file index.html, css, js
                        app.UseDefaultFiles();

                        // Serve app icon from <appRoot>/Assets at /assets (used by manifest + iOS A2HS)
                        try
                        {
                            var assetsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets");
                            if (System.IO.Directory.Exists(assetsDir))
                            {
                                app.UseStaticFiles(new StaticFileOptions
                                {
                                    FileProvider = new PhysicalFileProvider(assetsDir),
                                    RequestPath = "/assets"
                                });
                            }
                        }
                        catch { }

                        // Serve dish images from <appRoot>/data/image at /images
                        try
                        {
                            app.UseStaticFiles(new StaticFileOptions
                            {
                                FileProvider = new PhysicalFileProvider(AppPaths.ImagesDir),
                                RequestPath = "/images"
                            });
                        }
                        catch { }

                        // Ensure correct MIME type for manifest.webmanifest (Android/Chrome)
                        var contentTypeProvider = new FileExtensionContentTypeProvider();
                        contentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json";

                        app.UseStaticFiles(new StaticFileOptions
                        {
                            ContentTypeProvider = contentTypeProvider
                        });

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