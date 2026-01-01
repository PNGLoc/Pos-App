using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Server.Dtos;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR; // Thêm
using PosSystem.Main.Server.Hubs;   // Thêm
namespace PosSystem.Main.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrderController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<PosHub> _hubContext;
        public OrderController(AppDbContext context, IHubContext<PosHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // POST: api/order/create
        // Hàm này xử lý khi nhân viên bấm nút "Gửi Bếp" trên Mobile
        [HttpPost("create")]
        public async Task<IActionResult> CreateOrder([FromBody] OrderRequest request)
        {
            if (request.Items.Count == 0) return BadRequest("Chưa chọn món nào!");

            // 1. Kiểm tra xem bàn này đang có đơn nào chưa thanh toán (Pending) không?
            var currentOrder = await _context.Orders
                .Include(o => o.OrderDetails) // Load kèm chi tiết để tính toán
                .FirstOrDefaultAsync(o => o.TableID == request.TableID && o.OrderStatus == "Pending");

            // 2. Nếu chưa có đơn -> Tạo đơn mới (Mở bàn)
            if (currentOrder == null)
            {
                currentOrder = new Order
                {
                    TableID = request.TableID,
                    AccID = request.AccID,
                    OrderTime = DateTime.Now,
                    OrderStatus = "Pending",
                    PaymentMethod = "Cash"
                };
                _context.Orders.Add(currentOrder);

                // Cập nhật trạng thái bàn thành "Có người"
                var table = await _context.Tables.FindAsync(request.TableID);
                if (table != null) table.TableStatus = "Occupied";
            }

            // 3. Thêm các món mới vào đơn
            foreach (var itemDto in request.Items)
            {
                // Lấy thông tin món để lấy Giá hiện tại (Tránh trường hợp sau này đổi giá món)
                var dish = await _context.Dishes.FindAsync(itemDto.DishID);
                if (dish != null)
                {
                    var newDetail = new OrderDetail
                    {
                        DishID = dish.DishID,
                        Quantity = itemDto.Quantity,
                        UnitPrice = dish.Price, // Lưu giá tại thời điểm bán (Snapshot)
                        Note = itemDto.Note,
                        ItemStatus = "New", // Quan trọng: Đánh dấu là "Mới" để Máy in Bếp nhận diện và in
                        DiscountRate = 0,
                        TotalAmount = itemDto.Quantity * dish.Price // Tạm tính chưa giảm giá
                    };

                    // Nếu là đơn cũ, phải add vào list OrderDetails có sẵn
                    if (currentOrder.OrderID > 0)
                    {
                        currentOrder.OrderDetails.Add(newDetail);
                    }
                    else
                    {
                        // Nếu là đơn mới tinh (chưa có ID) thì add kiểu này
                        _context.Add(newDetail); // EF Core sẽ tự link với currentOrder đang tạo
                        // Gán tạm để logic tính tổng ở dưới chạy đúng
                        currentOrder.OrderDetails.Add(newDetail);
                    }
                }
            }

            // 4. Tính lại tổng tiền đơn hàng (SubTotal)
            // Cộng tất cả các món trong đơn (cả cũ và mới)
            currentOrder.SubTotal = currentOrder.OrderDetails.Sum(d => d.TotalAmount);

            // Tính toán sơ bộ FinalAmount (Chưa tính thuế/giảm giá bill ở bước này)
            currentOrder.FinalAmount = currentOrder.SubTotal;

            // 5. Lưu vào Database
            await _context.SaveChangesAsync();
            await _hubContext.Clients.All.SendAsync("TableUpdated", request.TableID);
            return Ok(new { Message = "Đã gửi đơn xuống bếp thành công!", OrderID = currentOrder.OrderID });
        }

        // GET: api/order/{tableId}
        // Lấy thông tin đơn hàng hiện tại của bàn (để hiển thị lại khi nhân viên bấm vào bàn)
        [HttpGet("{tableId}")]
        public async Task<IActionResult> GetOrderDetails(int tableId)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(od => od.Dish) // Join 2 lần để lấy tên món
                .FirstOrDefaultAsync(o => o.TableID == tableId && o.OrderStatus == "Pending");

            if (order == null) return NotFound("Bàn này đang trống");

            // Trả về dữ liệu sạch
            return Ok(new
            {
                order.OrderID,
                order.OrderTime,
                order.SubTotal,
                order.FinalAmount,
                Details = order.OrderDetails.Select(d => new
                {
                    d.OrderDetailID,
                    d.DishID,
                    d.Dish!.DishName, // Dấu ! báo cho compiler biết Dish ko null
                    d.Quantity,
                    d.UnitPrice,
                    d.TotalAmount,
                    d.Note,
                    d.ItemStatus // New, Sent, Done...
                })
            });
        }

        // POST: api/order/{tableId}
        // Mobile gửi yêu cầu thêm công thức (dữ liệu từ giỏ hàng)
        // POST: api/order/{tableId}
        // Mobile gửi yêu cầu thêm công thức (dữ liệu từ giỏ hàng)
        [HttpPost("{tableId}")]
        public async Task<IActionResult> AddOrderItems(int tableId, [FromBody] AddOrderItemsRequest request)
        {
            if (request?.Details == null || request.Details.Count == 0)
                return BadRequest("Chưa chọn món nào!");

            // 1. Kiểm tra xem bàn này đang có đơn nào chưa thanh toán (Pending) không?
            var currentOrder = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(od => od.Dish).ThenInclude(d => d.Category)
                .FirstOrDefaultAsync(o => o.TableID == tableId && o.OrderStatus == "Pending");

            // 2. Nếu chưa có đơn -> Tạo đơn mới (Mở bàn)
            if (currentOrder == null)
            {
                currentOrder = new Order
                {
                    TableID = tableId,
                    AccID = 1, // Default nhân viên (có thể lấy từ header nếu cần)
                    OrderTime = DateTime.Now,
                    OrderStatus = "Pending",
                    PaymentMethod = "Cash"
                };
                _context.Orders.Add(currentOrder);

                // Cập nhật trạng thái bàn thành "Có người"
                var table = await _context.Tables.FindAsync(tableId);
                if (table != null) table.TableStatus = "Occupied";
            }

            // 3. Thêm các món mới vào đơn
            var itemsToPrint = new List<OrderDetail>();
            foreach (var itemDto in request.Details)
            {
                // Lấy thông tin món để lấy Giá hiện tại (Tránh trường hợp sau này đổi giá món)
                var dish = await _context.Dishes.Include(d => d.Category).FirstOrDefaultAsync(d => d.DishID == itemDto.DishID);
                if (dish != null)
                {
                    var newDetail = new OrderDetail
                    {
                        DishID = dish.DishID,
                        Quantity = itemDto.Quantity,
                        UnitPrice = dish.Price, // Lưu giá tại thời điểm bán (Snapshot)
                        Note = itemDto.Note ?? "",
                        ItemStatus = "Sent", // Đánh dấu là "Sent" vì user đã gửi từ mobile
                        PrintedQuantity = itemDto.Quantity, // ⭐ SET NGAY = Quantity để tránh double-print
                        DiscountRate = 0,
                        TotalAmount = itemDto.Quantity * dish.Price // Tạm tính chưa giảm giá
                    };

                    // Nếu là đơn cũ, phải add vào list OrderDetails có sẵn
                    if (currentOrder.OrderID > 0)
                    {
                        currentOrder.OrderDetails.Add(newDetail);
                    }
                    else
                    {
                        // Nếu là đơn mới tinh (chưa có ID) thì add kiểu này
                        _context.Add(newDetail); // EF Core sẽ tự link với currentOrder đang tạo
                        // Gán tạm để logic tính tổng ở dưới chạy đúng
                        currentOrder.OrderDetails.Add(newDetail);
                    }

                    itemsToPrint.Add(newDetail);
                }
            }

            // 4. Tính lại tổng tiền đơn hàng (SubTotal)
            // Cộng tất cả các món trong đơn (cả cũ và mới)
            currentOrder.SubTotal = currentOrder.OrderDetails.Sum(d => d.TotalAmount);

            // Tính toán sơ bộ FinalAmount (Chưa tính thuế/giảm giá bill ở bước này)
            currentOrder.FinalAmount = currentOrder.SubTotal;

            // ⭐ SET FirstSentTime nếu chưa set (lần đầu gửi bếp)
            if (!currentOrder.FirstSentTime.HasValue)
            {
                currentOrder.FirstSentTime = DateTime.Now;
            }

            // 5. Lưu vào Database
            await _context.SaveChangesAsync();

            // ⭐ GỌI IN BẾP NGAY (không chờ PC nhấn gửi bếp)
            if (itemsToPrint.Any())
            {
                var batchNumber = currentOrder.OrderDetails
                    .Where(d => d.KitchenBatch > 0)
                    .Max(d => (int?)d.KitchenBatch) ?? 0;
                batchNumber++;

                foreach (var item in itemsToPrint)
                {
                    item.KitchenBatch = batchNumber;
                }
                await _context.SaveChangesAsync();

                // In ngay không chờ
                Services.PrintService.PrintKitchen(currentOrder, itemsToPrint, batchNumber);
            }

            // Notify PC via SignalR
            await _hubContext.Clients.All.SendAsync("TableUpdated", tableId);  // ⭐ Use same event name as PC listens to
            return Ok(new { Message = "Đã gửi đơn xuống bếp thành công!", OrderID = currentOrder.OrderID });
        }
        // POST: api/order/checkout
        // CHỈ DÀNH CHO MÁY TÍNH THU NGÂN
        [HttpPost("checkout")]
        public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request)
        {
            // 1. Lấy đơn hàng
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.OrderID == request.OrderID);

            if (order == null) return NotFound("Không tìm thấy đơn hàng!");
            if (order.OrderStatus == "Paid") return BadRequest("Đơn này đã thanh toán rồi!");

            // 2. Cập nhật thông tin giảm giá và thanh toán
            order.PaymentMethod = request.PaymentMethod;
            order.DiscountPercent = request.DiscountPercent;
            order.DiscountAmount = request.DiscountAmount;
            order.CheckoutTime = DateTime.Now;

            // 3. Tính toán lại tiền nong (Logic quan trọng)
            // Tổng tiền hàng
            order.SubTotal = order.OrderDetails.Sum(d => d.TotalAmount);

            // Tính tiền giảm giá
            decimal discountValue = 0;
            if (order.DiscountPercent > 0)
            {
                discountValue = order.SubTotal * (order.DiscountPercent / 100);
            }
            else
            {
                discountValue = order.DiscountAmount;
            }

            // Tính tiền khách phải trả
            order.FinalAmount = order.SubTotal - discountValue;
            if (order.FinalAmount < 0) order.FinalAmount = 0;

            // 4. Chốt đơn và Giải phóng bàn
            order.OrderStatus = "Paid";

            if (order.Table != null)
            {
                order.Table.TableStatus = "Empty"; // Trả bàn về trạng thái trống
            }

            // 5. Lưu xuống DB
            await _context.SaveChangesAsync();

            // 6. TRẢ VỀ KẾT QUẢ ĐỂ IN HÓA ĐƠN
            // Sau bước này, WPF sẽ nhận được cục data này để vẽ Bill
            return Ok(new
            {
                Message = "Thanh toán thành công!",
                OrderID = order.OrderID,
                TableName = order.Table?.TableName,
                Total = order.FinalAmount,
                Method = order.PaymentMethod
            });
        }

    }

}