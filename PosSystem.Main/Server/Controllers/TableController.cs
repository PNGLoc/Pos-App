using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using System.Linq;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR; // Thêm
using PosSystem.Main.Server.Hubs;   // Thêm
using PosSystem.Main.Server;

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
            try
            {
                var tables = await _context.Tables
                    .Select(t => new
                    {
                        t.TableID,
                        t.TableName,
                        t.TableStatus,
                        t.CategoryID,
                        CategoryIconClass = t.Category != null ? t.Category.IconClass : "fas fa-chair",
                        CategoryDisplayOrder = t.Category != null ? t.Category.DisplayOrder : int.MaxValue,
                        // [NEW] Kiểm tra có đơn tạm tính không
                        HasProvisionalBill = _context.Orders.Any(o => o.TableID == t.TableID && o.OrderStatus == "Pending" && o.IsPreCalculated),
                        // [NEW] Kiểm tra có yêu cầu thanh toán không
                        IsRequestingPayment = _context.Orders.Any(o => o.TableID == t.TableID && o.OrderStatus == "Pending" && o.IsRequestingPayment),
                        // [NEW] Lấy thời gian tạo đơn để đếm giờ
                        OrderTime = _context.Orders
                            .Where(o => o.TableID == t.TableID && o.OrderStatus == "Pending")
                            .Select(o => (DateTime?)o.OrderTime)
                            .FirstOrDefault(),

                        // [NEW] Kiểm tra có món chưa gửi (New) -> Trạng thái Ordering
                        HasNewItems = _context.Orders.Any(o => o.TableID == t.TableID && o.OrderStatus == "Pending" && o.OrderDetails.Any(d => d.ItemStatus == "New"))
                    })
                    .ToListAsync();

                tables = tables
                    .OrderBy(t => t.CategoryDisplayOrder)
                    .ThenBy(t => t.CategoryID ?? int.MaxValue)
                    .ThenBy(t => t.TableID)
                    .ToList();

                return Ok(tables);
            }
            catch
            {
                return ApiError.Result(500, "TABLE_LIST_FAILED", "Lỗi tải danh sách bàn");
            }
        }

        // Sau này nếu bạn làm chức năng "Chuyển Bàn", bạn có thể dùng _hubContext ở đây
        // để bắn event "TableUpdated" cho cả bàn cũ và bàn mới.
    }
}