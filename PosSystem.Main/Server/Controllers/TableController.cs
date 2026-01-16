using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using System.Linq;
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
                .OrderBy(t => t.TableID)
                .Select(t => new 
                {
                    t.TableID,
                    t.TableName,
                    t.TableStatus,
                    t.CategoryID,
                    t.TableType,
                    // [NEW] Kiểm tra có đơn tạm tính không
                    // [NEW] Kiểm tra có đơn tạm tính không
                    HasProvisionalBill = _context.Orders.Any(o => o.TableID == t.TableID && o.OrderStatus == "Pending" && o.IsPreCalculated),
                    // [NEW] Kiểm tra có yêu cầu thanh toán không
                    IsRequestingPayment = _context.Orders.Any(o => o.TableID == t.TableID && o.OrderStatus == "Pending" && o.IsRequestingPayment)
                })
                .ToListAsync();

            return Ok(tables);
        }

        // Sau này nếu bạn làm chức năng "Chuyển Bàn", bạn có thể dùng _hubContext ở đây
        // để bắn event "TableUpdated" cho cả bàn cũ và bàn mới.
    }
}