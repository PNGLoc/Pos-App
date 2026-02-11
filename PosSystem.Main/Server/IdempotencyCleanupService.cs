using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PosSystem.Main.Database;

namespace PosSystem.Main.Server
{
    public sealed class IdempotencyCleanupService : BackgroundService
    {
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RecordTtl = TimeSpan.FromMinutes(10);

        private readonly IServiceScopeFactory _scopeFactory;

        public IdempotencyCleanupService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(CleanupInterval, stoppingToken);

                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var threshold = DateTime.UtcNow - RecordTtl;
                    var stale = await db.IdempotencyRecords
                        .Where(r => r.CreatedAt < threshold)
                        .ToListAsync(stoppingToken);

                    if (stale.Count > 0)
                    {
                        db.IdempotencyRecords.RemoveRange(stale);
                        await db.SaveChangesAsync(stoppingToken);
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch
                {
                    // Swallow errors to keep service alive.
                }
            }
        }
    }
}
