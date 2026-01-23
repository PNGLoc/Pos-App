using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using System.Linq;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR; // Thêm
using PosSystem.Main.Server.Hubs;   // Thêm

namespace PosSystem.Main.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TableController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<PosHub> _hubContext; // Chuẩn bị sẵn Hub

        public TableController(AppDbContext context, IHubContext<PosHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // GET: api/table
        [HttpGet]
        public async Task<IActionResult> GetTables()
        {
            var tables = await _context.Tables
                // Match WPF ordering: Category.DisplayOrder -> CategoryID -> TableName
                // (so "Tất cả" shows tables grouped by category, not by creation)
                .OrderBy(t => t.Category != null ? t.Category.DisplayOrder : int.MaxValue)
                .ThenBy(t => t.CategoryID ?? int.MaxValue)
                .ThenBy(t => t.TableName)
                .Select(t => new
                {
                    t.TableID,
                    t.TableName,
                    t.TableStatus,
                    t.CategoryID,
                    t.TableType,
                    // [NEW] Kiểm tra có đơn tạm tính không
                    HasProvisionalBill = _context.Orders.Any(o => o.TableID == t.TableID && o.OrderStatus == "Pending" && o.IsPreCalculated),
                    // [NEW] Kiểm tra có yêu cầu thanh toán không
                    IsRequestingPayment = _context.Orders.Any(o => o.TableID == t.TableID && o.OrderStatus == "Pending" && o.IsRequestingPayment),
                    // [NEW] Lấy thời gian tạo đơn để đếm giờ
                    OrderTime = _context.Orders
                        .Where(o => o.TableID == t.TableID && o.OrderStatus == "Pending")
                        .Select(o => (DateTime?)o.OrderTime)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(tables);
        }

        // Sau này nếu bạn làm chức năng "Chuyển Bàn", bạn có thể dùng _hubContext ở đây
        // để bắn event "TableUpdated" cho cả bàn cũ và bàn mới.
    }
}