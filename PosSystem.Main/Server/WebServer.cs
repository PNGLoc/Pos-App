using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection; // Cần cái này
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Http;
using PosSystem.Main.Database; // <--- QUAN TRỌNG: Phải using thư mục Database
using PosSystem.Main.Helpers;
using PosSystem.Main.Server.Hubs;
using PosSystem.Main.Server;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PosSystem.Main.Server
{
    public class WebServer
    {
        private static string BuildWebAssetVersion(string webRootPath)
        {
            try
            {
                var versionFiles = new[]
                {
                    Path.Combine(webRootPath, "css", "mobile-style.css"),
                    Path.Combine(webRootPath, "js", "mobile-app.js"),
                    Path.Combine(webRootPath, "js", "signalr.min.js"),
                    Path.Combine(webRootPath, "vendor", "fontawesome", "css", "all.min.css")
                };

                long maxTicks = versionFiles
                    .Where(File.Exists)
                    .Select(f => File.GetLastWriteTimeUtc(f).Ticks)
                    .DefaultIfEmpty(DateTime.UtcNow.Ticks)
                    .Max();

                return maxTicks.ToString("x");
            }
            catch
            {
                return DateTime.UtcNow.Ticks.ToString("x");
            }
        }

        private static bool IsLongCacheStaticPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

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
                        services.AddHostedService<IdempotencyCleanupService>();
                        // -------------------------
                    });

                    webBuilder.Configure(app =>
                    {
                        app.UseDeveloperExceptionPage();

                        // Ensure data folders exist early
                        try { AppPaths.EnsureInitialized(); } catch { }

                        var webRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");

                        // Serve mobile.html dynamically so JS/CSS query hash updates automatically per deploy
                        app.Use(async (context, next) =>
                        {
                            if (HttpMethods.IsGet(context.Request.Method))
                            {
                                var reqPath = context.Request.Path.Value ?? string.Empty;
                                if (reqPath.Equals("/mobile", StringComparison.OrdinalIgnoreCase) ||
                                    reqPath.Equals("/mobile.html", StringComparison.OrdinalIgnoreCase))
                                {
                                    var mobileHtmlPath = Path.Combine(webRootPath, "mobile.html");
                                    if (File.Exists(mobileHtmlPath))
                                    {
                                        var html = await File.ReadAllTextAsync(mobileHtmlPath, Encoding.UTF8);
                                        var assetVersion = BuildWebAssetVersion(webRootPath);
                                        html = html.Replace("__ASSET_VER__", assetVersion);

                                        context.Response.StatusCode = 200;
                                        context.Response.ContentType = "text/html; charset=utf-8";
                                        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
                                        context.Response.Headers["Pragma"] = "no-cache";
                                        context.Response.Headers["Expires"] = "0";
                                        await context.Response.WriteAsync(html);
                                        return;
                                    }
                                }
                            }

                            await next();
                        });

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
                                    RequestPath = "/assets",
                                    OnPrepareResponse = ctx =>
                                    {
                                        var path = ctx.Context.Request.Path.Value ?? string.Empty;
                                        if (IsLongCacheStaticPath(path))
                                        {
                                            ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=2592000, immutable";
                                        }
                                    }
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
                                RequestPath = "/images",
                                OnPrepareResponse = ctx =>
                                {
                                    ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=2592000";
                                }
                            });
                        }
                        catch { }

                        // Ensure correct MIME type for manifest.webmanifest (Android/Chrome)
                        var contentTypeProvider = new FileExtensionContentTypeProvider();
                        contentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json";

                        app.UseStaticFiles(new StaticFileOptions
                        {
                            ContentTypeProvider = contentTypeProvider,
                            OnPrepareResponse = ctx =>
                            {
                                var path = ctx.Context.Request.Path.Value ?? string.Empty;
                                if (path.EndsWith("/mobile.html", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith("/sw.js", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith("/manifest.webmanifest", StringComparison.OrdinalIgnoreCase))
                                {
                                    ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
                                    ctx.Context.Response.Headers["Pragma"] = "no-cache";
                                    ctx.Context.Response.Headers["Expires"] = "0";
                                }
                                else if (IsLongCacheStaticPath(path))
                                {
                                    ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=2592000, immutable";
                                }
                            }
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