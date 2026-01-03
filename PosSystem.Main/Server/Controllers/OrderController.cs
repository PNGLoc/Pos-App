using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Server.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR; // Import SignalR
using PosSystem.Main.Server.Hubs;   // Import Hub của bạn

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

        // 1. MỞ BÀN & TẠO ĐƠN (Chỉ lưu status New, CHƯA IN)
        [HttpPost("create")]
        public async Task<IActionResult> CreateOrder([FromBody] OrderRequest request)
        {
            if (request.Items.Count == 0) return BadRequest("Chưa chọn món nào!");

            // Kiểm tra bàn có đơn chưa
            var currentOrder = await _context.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.TableID == request.TableID && o.OrderStatus == "Pending");

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

                var table = await _context.Tables.FindAsync(request.TableID);
                if (table != null) table.TableStatus = "Occupied";
            }

            foreach (var itemDto in request.Items)
            {
                var dish = await _context.Dishes.FindAsync(itemDto.DishID);
                if (dish != null)
                {
                    currentOrder.OrderDetails.Add(new OrderDetail
                    {
                        DishID = dish.DishID,
                        Quantity = itemDto.Quantity,
                        UnitPrice = dish.Price,
                        Note = itemDto.Note,
                        ItemStatus = "New",      // ⭐ Quan trọng: Mới chỉ là New
                        PrintedQuantity = 0,     // ⭐ Chưa in
                        DiscountRate = 0,
                        TotalAmount = itemDto.Quantity * dish.Price
                    });
                }
            }

            currentOrder.SubTotal = currentOrder.OrderDetails.Sum(d => d.TotalAmount);
            currentOrder.FinalAmount = currentOrder.SubTotal;

            await _context.SaveChangesAsync();

            // Bắn SignalR: WPF sẽ thấy bàn chuyển màu đỏ và hiện món màu vàng
            await _hubContext.Clients.All.SendAsync("TableUpdated", request.TableID);

            return Ok(new { Message = "Đã mở bàn (chưa gửi bếp)", OrderID = currentOrder.OrderID });
        }

        // 2. THÊM MÓN VÀO ĐƠN (Lưu vào giỏ chung, CHƯA IN)
        [HttpPost("{tableId}/add")]
        public async Task<IActionResult> AddOrderItems(int tableId, [FromBody] AddOrderItemsRequest request)
        {
            if (request?.Details == null || request.Details.Count == 0) return BadRequest("Chưa chọn món!");

            var currentOrder = await _context.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.TableID == tableId && o.OrderStatus == "Pending");

            if (currentOrder == null) return BadRequest("Bàn chưa mở, vui lòng mở bàn trước!");

            foreach (var itemDto in request.Details)
            {
                var dish = await _context.Dishes.FindAsync(itemDto.DishID);
                if (dish != null)
                {
                    // Gộp vào dòng "New" nếu trùng món và note (để gọn bill)
                    var existingItem = currentOrder.OrderDetails
                        .FirstOrDefault(d => d.DishID == dish.DishID && d.ItemStatus == "New" && (d.Note ?? "") == (itemDto.Note ?? ""));

                    if (existingItem != null)
                    {
                        existingItem.Quantity += itemDto.Quantity;
                        existingItem.TotalAmount = existingItem.Quantity * existingItem.UnitPrice;
                    }
                    else
                    {
                        currentOrder.OrderDetails.Add(new OrderDetail
                        {
                            DishID = dish.DishID,
                            Quantity = itemDto.Quantity,
                            UnitPrice = dish.Price,
                            Note = itemDto.Note ?? "",
                            ItemStatus = "New",      // ⭐ Status New -> Hiện ở tab Cart Mobile và dòng Vàng WPF
                            PrintedQuantity = 0,
                            TotalAmount = itemDto.Quantity * dish.Price
                        });
                    }
                }
            }

            currentOrder.SubTotal = currentOrder.OrderDetails.Sum(d => d.TotalAmount);
            currentOrder.FinalAmount = currentOrder.SubTotal;

            await _context.SaveChangesAsync();
            await _hubContext.Clients.All.SendAsync("TableUpdated", tableId);

            return Ok(new { Message = "Đã thêm vào giỏ hàng chung" });
        }

        // 3. API GỬI BẾP (Tìm món New -> In -> Chuyển thành Sent)
        // Mobile nút "Gửi thực đơn" sẽ gọi cái này
        [HttpPost("{tableId}/send")]
        public async Task<IActionResult> SendToKitchen(int tableId)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Dish).ThenInclude(c => c.Category)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.TableID == tableId && o.OrderStatus == "Pending");

            if (order == null) return BadRequest("Bàn này không có đơn hàng!");

            // Lấy các món chưa in (New) hoặc số lượng tăng thêm
            var itemsToPrint = order.OrderDetails
                .Where(d => d.Quantity > d.PrintedQuantity)
                .ToList();

            if (!itemsToPrint.Any()) return Ok(new { Message = "Không có món mới cần gửi bếp" });

            // Tăng Batch Number
            var batchNumber = order.OrderDetails.Max(d => (int?)d.KitchenBatch) ?? 0;
            batchNumber++;

            if (!order.FirstSentTime.HasValue) order.FirstSentTime = DateTime.Now;

            // Danh sách tạm để gửi lệnh in
            var printQueue = new List<OrderDetail>();

            foreach (var item in itemsToPrint)
            {
                int quantityToSend = item.Quantity - item.PrintedQuantity;

                // Tạo bản copy để in đúng số lượng mới
                var printItem = new OrderDetail
                {
                    Dish = item.Dish,
                    DishID = item.DishID,
                    Quantity = quantityToSend,
                    Note = item.Note,
                    KitchenBatch = batchNumber
                };
                printQueue.Add(printItem);

                // Cập nhật DB
                item.PrintedQuantity = item.Quantity;
                item.ItemStatus = "Sent"; // ⭐ Chuyển sang Sent -> Mobile hiện tab Đã Xác Nhận, WPF hiện dòng Trắng/Xanh
                item.KitchenBatch = batchNumber;
            }

            await _context.SaveChangesAsync();

            // Gọi Service In Bếp (Code có sẵn của bạn)
            Services.PrintService.PrintKitchen(order, printQueue, batchNumber);

            // Bắn SignalR
            await _hubContext.Clients.All.SendAsync("TableUpdated", tableId);

            return Ok(new { Message = $"Đã gửi {printQueue.Count} món xuống bếp!" });
        }
        // POST: api/Order/{tableId}/update-item
        [HttpPost("{tableId}/update-item")]
        public async Task<IActionResult> UpdateItem(int tableId, [FromBody] UpdateItemRequest req)
        {
            var orderDetail = await _context.OrderDetails
                .Include(od => od.Order)
                .FirstOrDefaultAsync(od => od.OrderDetailID == req.OrderDetailID && od.Order.TableID == tableId);

            if (orderDetail == null) return NotFound("Món không tồn tại");

            // Chỉ cho phép sửa món trạng thái "New" (chưa gửi bếp)
            if (orderDetail.ItemStatus != "New") return BadRequest("Chỉ sửa được món chưa gửi bếp");

            if (req.Quantity <= 0)
            {
                _context.OrderDetails.Remove(orderDetail); // Xóa nếu số lượng = 0
            }
            else
            {
                orderDetail.Quantity = req.Quantity;
                orderDetail.TotalAmount = orderDetail.Quantity * orderDetail.UnitPrice;
                orderDetail.Note = req.Note;
            }

            // Lưu tạm để cập nhật dòng chi tiết
            await _context.SaveChangesAsync();

            // Tính lại tổng tiền đơn hàng
            var order = orderDetail.Order;
            order.SubTotal = _context.OrderDetails.Where(d => d.OrderID == order.OrderID).Sum(d => d.TotalAmount);
            order.FinalAmount = order.SubTotal;

            await _context.SaveChangesAsync();

            // Báo cho mọi người biết để cập nhật giao diện
            await _hubContext.Clients.All.SendAsync("TableUpdated", tableId);

            return Ok(new { Message = "Cập nhật thành công" });
        }

        // GET: api/order/{tableId} (API lấy dữ liệu cho Mobile)
        [HttpGet("{tableId}")]
        public async Task<IActionResult> GetOrderDetails(int tableId)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                .FirstOrDefaultAsync(o => o.TableID == tableId && o.OrderStatus == "Pending");

            if (order == null) return NotFound("Bàn này đang trống");

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
                    d.Dish!.DishName, // ! để bỏ cảnh báo null
                    d.Quantity,
                    d.UnitPrice,
                    d.TotalAmount,
                    d.Note,
                    d.ItemStatus // Quan trọng để JS phân loại tab
                })
            });
        }

        public class MobileCheckoutRequest : CheckoutRequest
        {
            public int AccID { get; set; } // Thêm trường này để check quyền
        }

        [HttpPost("checkout-mobile")]
        public async Task<IActionResult> CheckoutMobile([FromBody] MobileCheckoutRequest request)
        {
            // 1. Check quyền
            var acc = await _context.Accounts.FindAsync(request.AccID);
            if (acc == null || !acc.CanPayment) return StatusCode(403, "Bạn không có quyền thanh toán!");

            // 2. Logic thanh toán (Copy từ hàm Checkout cũ)
            var order = await _context.Orders.Include(o => o.Table).Include(o => o.OrderDetails).ThenInclude(d => d.Dish)
                .FirstOrDefaultAsync(o => o.OrderID == request.OrderID);

            if (order == null || order.OrderStatus == "Paid") return BadRequest("Đơn lỗi");

            order.PaymentMethod = request.PaymentMethod;
            order.DiscountPercent = request.DiscountPercent;
            order.DiscountAmount = request.DiscountAmount;
            order.CheckoutTime = DateTime.Now;

            // Tính tiền lại cho chắc
            order.SubTotal = order.OrderDetails.Sum(d => d.TotalAmount);
            decimal discountVal = (order.DiscountPercent > 0) ? order.SubTotal * (order.DiscountPercent / 100) : order.DiscountAmount;
            order.FinalAmount = order.SubTotal - discountVal;
            order.OrderStatus = "Paid";

            if (order.Table != null) order.Table.TableStatus = "Empty";

            await _context.SaveChangesAsync();

            // 3. GỌI IN HÓA ĐƠN TRÊN SERVER (DESKTOP)
            // Đây là điểm mấu chốt: Mobile kích hoạt, Desktop in.
            try
            {
                Services.PrintService.PrintBill(order.OrderID);
            }
            catch { }

            await _hubContext.Clients.All.SendAsync("TableUpdated", order.TableID);

            return Ok(new { Message = "Đã thanh toán & In hóa đơn!" });
        }

        // [POST] api/Order/{tableId}/request-payment
        [HttpPost("{tableId}/request-payment")]
        public async Task<IActionResult> RequestPayment(int tableId)
        {
            // Gửi tín hiệu SignalR tên là "TableRequestPayment"
            // Desktop sẽ lắng nghe sự kiện này để đổi màu bàn
            await _hubContext.Clients.All.SendAsync("TableRequestPayment", tableId);
            return Ok(new { Message = "Đã gửi yêu cầu thanh toán!" });
        }
        // DTO nhận dữ liệu chuyển bàn
        public class MoveTableRequest
        {
            public int AccID { get; set; } // ID người thực hiện để check quyền
            public int TargetTableID { get; set; }
        }

        [HttpPost("{sourceTableId}/move")]
        public async Task<IActionResult> MoveTable(int sourceTableId, [FromBody] MoveTableRequest req)
        {
            // 1. Check quyền
            var acc = await _context.Accounts.FindAsync(req.AccID);
            if (acc == null || !acc.CanMoveTable) return StatusCode(403, "Bạn không có quyền chuyển bàn!");

            // 2. Lấy đơn gốc
            var sourceOrder = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Dish).ThenInclude(c => c.Category)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.TableID == sourceTableId && o.OrderStatus == "Pending");

            if (sourceOrder == null) return BadRequest("Bàn gốc không có đơn!");
            if (sourceTableId == req.TargetTableID) return BadRequest("Trùng bàn đích!");

            string oldTableName = sourceOrder.Table?.TableName ?? sourceTableId.ToString();
            string newTableName = "";

            // 3. Kiểm tra bàn đích
            var targetOrder = await _context.Orders
                .Include(o => o.OrderDetails)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.TableID == req.TargetTableID && o.OrderStatus == "Pending");

            var targetTable = await _context.Tables.FindAsync(req.TargetTableID);
            if (targetTable != null) newTableName = targetTable.TableName;

            if (targetOrder != null)
            {
                // TRƯỜNG HỢP GỘP BÀN: Chuyển hết món sang đơn đích
                foreach (var detail in sourceOrder.OrderDetails)
                {
                    detail.OrderID = targetOrder.OrderID;
                }
                // Tính lại tiền đơn đích
                targetOrder.SubTotal += sourceOrder.SubTotal;
                targetOrder.FinalAmount += sourceOrder.FinalAmount;

                // Xóa đơn gốc
                _context.Orders.Remove(sourceOrder);
            }
            else
            {
                // TRƯỜNG HỢP CHUYỂN BÀN: Đổi TableID
                sourceOrder.TableID = req.TargetTableID;
                if (targetTable != null) targetTable.TableStatus = "Occupied";
            }

            // Trả bàn gốc về trống
            var sourceTable = await _context.Tables.FindAsync(sourceTableId);
            if (sourceTable != null) sourceTable.TableStatus = "Empty";

            await _context.SaveChangesAsync();

            // 4. IN PHIẾU BÁO BẾP (Quan trọng)
            // Gọi hàm in có sẵn của Desktop
            Services.PrintService.PrintMoveTableNotification(targetOrder ?? sourceOrder, oldTableName, newTableName);

            // 5. Cập nhật UI
            await _hubContext.Clients.All.SendAsync("TableUpdated", sourceTableId);
            await _hubContext.Clients.All.SendAsync("TableUpdated", req.TargetTableID);

            return Ok(new { Message = "Chuyển bàn thành công!" });
        }
        public class CancelItemRequest
        {
            public int AccID { get; set; }
            public long OrderDetailID { get; set; }
            public int Quantity { get; set; } // Số lượng muốn hủy
            public string Reason { get; set; }
        }

        [HttpPost("cancel-item")]
        public async Task<IActionResult> CancelItem([FromBody] CancelItemRequest req)
        {
            // 1. Check quyền
            var acc = await _context.Accounts.FindAsync(req.AccID);
            if (acc == null || !acc.CanCancelItem) return StatusCode(403, "Bạn không có quyền hủy món!");

            var detail = await _context.OrderDetails
                .Include(d => d.Dish).ThenInclude(c => c.Category) // Load category để biết máy in nào
                .Include(d => d.Order).ThenInclude(o => o.Table)
                .FirstOrDefaultAsync(d => d.OrderDetailID == req.OrderDetailID);

            if (detail == null) return NotFound();

            if (req.Quantity > detail.Quantity) return BadRequest("Không thể hủy quá số lượng hiện có");

            // 2. Giảm số lượng
            detail.Quantity -= req.Quantity;
            detail.PrintedQuantity -= req.Quantity; // Giảm luôn số đã in để đồng bộ
            detail.TotalAmount = detail.Quantity * detail.UnitPrice; // Tính lại tiền row

            // Nếu giảm về 0 thì xóa luôn dòng
            bool isRemoved = false;
            if (detail.Quantity <= 0)
            {
                _context.OrderDetails.Remove(detail);
                isRemoved = true;
            }

            // Cập nhật tổng tiền đơn hàng
            var order = detail.Order;
            // ... (Logic tính lại SubTotal/FinalAmount cho order) ...

            await _context.SaveChangesAsync();

            // 3. IN PHIẾU HỦY XUỐNG BẾP
            // Mẹo: Tạo một OrderDetail ảo với số lượng ÂM
            var cancelItem = new OrderDetail
            {
                Dish = detail.Dish,
                DishID = detail.DishID,
                Quantity = -req.Quantity, // Số âm để template bếp hiểu là trả món
                Note = $"HỦY MÓN: {req.Reason}",
                KitchenBatch = 0
            };

            // Gọi Service in bếp
            Services.PrintService.PrintKitchen(order, new List<OrderDetail> { cancelItem }, 0);

            await _hubContext.Clients.All.SendAsync("TableUpdated", order.TableID);

            return Ok(new { Message = "Đã hủy món & Báo bếp" });
        }
    }
}
