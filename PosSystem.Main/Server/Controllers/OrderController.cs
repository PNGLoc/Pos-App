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
using System.Text.Json;
using System.Text.Json.Serialization;
using PosSystem.Main.Server;
using PosSystem.Main.Services;

namespace PosSystem.Main.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrderController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<PosHub> _hubContext;

        private const string IdempotencyHeaderName = "X-Idempotency-Key";
        private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromMinutes(10);
        private const string IdempotencyInProgressCode = "IDEMPOTENCY_IN_PROGRESS";
        private static readonly JsonSerializerOptions IdempotencyJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public OrderController(AppDbContext context, IHubContext<PosHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        private string? BuildIdempotencyKey()
        {
            if (!Request.Headers.TryGetValue(IdempotencyHeaderName, out var headerValue)) return null;

            var raw = headerValue.ToString();
            if (string.IsNullOrWhiteSpace(raw)) return null;

            return $"{Request.Path}:{raw}";
        }

        private IActionResult BuildIdempotencyResult(IdempotencyRecord record)
        {
            if (!record.Completed)
            {
                return BuildIdempotencyInProgressResult();
            }

            if (string.IsNullOrWhiteSpace(record.ResponseBody))
            {
                return StatusCode(record.StatusCode > 0 ? record.StatusCode : 200);
            }

            var contentType = string.IsNullOrWhiteSpace(record.ContentType)
                ? "text/plain"
                : record.ContentType;

            var response = Content(record.ResponseBody, contentType, System.Text.Encoding.UTF8);
            response.StatusCode = record.StatusCode > 0 ? record.StatusCode : 200;
            return response;
        }

        private IActionResult BuildIdempotencyInProgressResult()
        {
            return ApiError.Result(409, IdempotencyInProgressCode, "Request is already in progress");
        }

        private IActionResult BuildErrorResult(int statusCode, string errorCode, string message)
        {
            return ApiError.Result(statusCode, errorCode, message);
        }

        private string? TryBeginIdempotent(out IActionResult? earlyResult)
        {
            earlyResult = null;
            var key = BuildIdempotencyKey();
            if (string.IsNullOrWhiteSpace(key)) return null;

            CleanupIdempotency();

            var existing = _context.IdempotencyRecords.AsNoTracking().FirstOrDefault(r => r.Key == key);
            if (existing != null)
            {
                earlyResult = BuildIdempotencyResult(existing);
                return null;
            }

            _context.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = key,
                CreatedAt = DateTime.UtcNow,
                Completed = false,
                StatusCode = 0,
                ContentType = null,
                ResponseBody = null
            });
            try
            {
                _context.SaveChanges();
            }
            catch (DbUpdateException)
            {
                earlyResult = BuildIdempotencyInProgressResult();
                return null;
            }

            return key;
        }

        private IActionResult FinishIdempotent(string? key, IActionResult result)
        {
            if (string.IsNullOrWhiteSpace(key)) return result;

            var record = _context.IdempotencyRecords.FirstOrDefault(r => r.Key == key);
            if (record == null)
            {
                record = new IdempotencyRecord { Key = key, CreatedAt = DateTime.UtcNow };
                _context.IdempotencyRecords.Add(record);
            }

            if (result is ObjectResult objectResult)
            {
                record.StatusCode = objectResult.StatusCode ?? 200;
                if (objectResult.Value is string textValue)
                {
                    record.ContentType = "text/plain";
                    record.ResponseBody = textValue;
                }
                else
                {
                    record.ContentType = "application/json";
                    record.ResponseBody = JsonSerializer.Serialize(objectResult.Value, IdempotencyJsonOptions);
                }
            }
            else if (result is StatusCodeResult statusResult)
            {
                record.StatusCode = statusResult.StatusCode;
                record.ContentType = null;
                record.ResponseBody = null;
            }
            else
            {
                record.StatusCode = 200;
                record.ContentType = null;
                record.ResponseBody = null;
            }

            record.Completed = true;
            _context.SaveChanges();

            return result;
        }

        private void AbandonIdempotent(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            var record = _context.IdempotencyRecords.FirstOrDefault(r => r.Key == key);
            if (record != null)
            {
                _context.IdempotencyRecords.Remove(record);
                _context.SaveChanges();
            }
        }

        private void CleanupIdempotency()
        {
            var threshold = DateTime.UtcNow - IdempotencyTtl;
            var stale = _context.IdempotencyRecords.Where(r => r.CreatedAt < threshold).ToList();
            if (stale.Count == 0) return;
            _context.IdempotencyRecords.RemoveRange(stale);
            _context.SaveChanges();
        }

        private async Task<string> GetAccountNameAsync(int accId, string fallback = "Admin")
        {
            if (accId <= 0) return fallback;
            var acc = await _context.Accounts.FindAsync(accId);
            return acc?.AccName ?? fallback;
        }

        private async Task<string> GetTableNameAsync(int tableId)
        {
            var table = await _context.Tables.FindAsync(tableId);
            return table?.TableName ?? $"Bàn {tableId}";
        }

        private static string FormatItem(string dishName, int quantity, string? note)
        {
            var baseText = $"{dishName} x{quantity}";
            var n = (note ?? string.Empty).Trim();
            return string.IsNullOrEmpty(n) ? baseText : $"{baseText} ({n})";
        }

        private static string SummarizeItems(IEnumerable<(string dishName, int quantity, string note)> items, int maxItems = 6)
        {
            var list = items.Where(i => i.quantity != 0).ToList();
            if (list.Count == 0) return "(không có món)";

            var shown = list.Take(maxItems).Select(i => FormatItem(i.dishName, i.quantity, i.note)).ToList();
            if (list.Count > maxItems)
            {
                shown.Add($"+{list.Count - maxItems} món khác");
            }
            return string.Join("\n", shown);
        }

        // 1. MỞ BÀN & TẠO ĐƠN (Chỉ lưu status New, CHƯA IN)
        [HttpPost("create")]
        public async Task<IActionResult> CreateOrder([FromBody] OrderRequest request)
        {
            var idempotencyKey = TryBeginIdempotent(out var idempotentResult);
            if (idempotentResult != null) return idempotentResult;

            try
            {
                if (request.Items.Count == 0)
                {
                    return FinishIdempotent(idempotencyKey, BuildErrorResult(400, "NO_ITEMS_SELECTED", "Chưa chọn món nào!"));
                }

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
                }

                // [FIX] Có món trong cart => bàn phải được xem là có khách
                var table = await _context.Tables.FindAsync(request.TableID);
                if (table != null && table.TableStatus == "Empty")
                {
                    table.TableStatus = "Occupied";
                }

                foreach (var itemDto in request.Items)
                {
                    var dish = await _context.Dishes.FindAsync(itemDto.DishID);
                    if (dish != null)
                    {
                        var unitPrice = PriceService.GetCurrentPrice(dish.DishID, _context);
                        currentOrder.OrderDetails.Add(new OrderDetail
                        {
                            DishID = dish.DishID,
                            Quantity = itemDto.Quantity,
                            UnitPrice = unitPrice,
                            Note = itemDto.Note,
                            ItemStatus = "New",      // ⭐ Quan trọng: Mới chỉ là New
                            PrintedQuantity = 0,     // ⭐ Chưa in
                            DiscountRate = 0,
                            TotalAmount = itemDto.Quantity * unitPrice
                        });
                    }
                }

                currentOrder.SubTotal = currentOrder.OrderDetails.Sum(d => d.TotalAmount);
                currentOrder.FinalAmount = currentOrder.SubTotal;

                await _context.SaveChangesAsync();

                // Bắn SignalR: WPF sẽ thấy bàn chuyển màu đỏ và hiện món màu vàng
                await _hubContext.Clients.All.SendAsync("TableUpdated", request.TableID);

                return FinishIdempotent(idempotencyKey, Ok(new { Message = "Đã mở bàn (chưa gửi bếp)", OrderID = currentOrder.OrderID }));
            }
            catch (Exception ex)
            {
                AbandonIdempotent(idempotencyKey);
                return BuildErrorResult(500, "ORDER_CREATE_FAILED", "Lỗi tạo đơn");
            }
        }

        // 2. THÊM MÓN VÀO ĐƠN (Lưu vào giỏ chung, CHƯA IN)
        [HttpPost("{tableId}/add")]
        public async Task<IActionResult> AddOrderItems(int tableId, [FromBody] AddOrderItemsRequest request)
        {
            var idempotencyKey = TryBeginIdempotent(out var idempotentResult);
            if (idempotentResult != null) return idempotentResult;

            if (request?.Details == null || request.Details.Count == 0)
            {
                return FinishIdempotent(idempotencyKey, BuildErrorResult(400, "NO_ITEMS_SELECTED", "Chưa chọn món!"));
            }

            using (var transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var currentOrder = await _context.Orders
                        .Include(o => o.OrderDetails)
                        .FirstOrDefaultAsync(o => o.TableID == tableId && o.OrderStatus == "Pending");

                    if (currentOrder == null)
                    {
                        return FinishIdempotent(idempotencyKey, BuildErrorResult(400, "TABLE_NOT_OPEN", "Bàn chưa mở, vui lòng mở bàn trước!"));
                    }

                    // [NEW] Cập nhật nhân viên phục vụ nếu có gửi AccID lên
                    if (request.AccID > 0)
                    {
                        currentOrder.AccID = request.AccID;
                    }

                    // [PERF] Batch load dishes to avoid N+1 queries
                    var dishIds = request.Details.Select(d => d.DishID).Distinct().ToList();
                    var dishMap = await _context.Dishes
                        .Where(d => dishIds.Contains(d.DishID))
                        .ToDictionaryAsync(d => d.DishID);

                    foreach (var itemDto in request.Details)
                    {
                        if (!dishMap.TryGetValue(itemDto.DishID, out var dish)) continue;
                        var unitPrice = PriceService.GetCurrentPrice(dish.DishID, _context);

                        // Gộp vào dòng "New" nếu trùng món và note
                        var existingItem = currentOrder.OrderDetails
                            .FirstOrDefault(d => d.DishID == dish.DishID && d.ItemStatus == "New" && (d.Note ?? "") == (itemDto.Note ?? ""));

                        if (existingItem != null)
                        {
                            existingItem.Quantity += itemDto.Quantity;
                            existingItem.UnitPrice = unitPrice;
                            existingItem.TotalAmount = existingItem.Quantity * existingItem.UnitPrice;
                        }
                        else
                        {
                            currentOrder.OrderDetails.Add(new OrderDetail
                            {
                                DishID = dish.DishID,
                                Quantity = itemDto.Quantity,
                                UnitPrice = unitPrice,
                                Note = itemDto.Note ?? "",
                                ItemStatus = "New",
                                PrintedQuantity = 0,
                                TotalAmount = itemDto.Quantity * unitPrice
                            });
                        }
                    }

                    // [FIX] Có món trong cart => bàn phải đỏ/có khách
                    var table = await _context.Tables.FindAsync(tableId);
                    if (table != null && table.TableStatus == "Empty")
                    {
                        table.TableStatus = "Occupied";
                    }

                    currentOrder.SubTotal = currentOrder.OrderDetails.Sum(d => d.TotalAmount);
                    currentOrder.FinalAmount = currentOrder.SubTotal;

                    await _context.SaveChangesAsync();
                    transaction.Commit();

                    // SignalR after commit
                    await _hubContext.Clients.All.SendAsync("TableUpdated", tableId);

                    return FinishIdempotent(idempotencyKey, Ok(new { Message = "Đã thêm vào giỏ hàng chung" }));
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    AbandonIdempotent(idempotencyKey);
                    return BuildErrorResult(500, "ORDER_ADD_ITEMS_FAILED", "Lỗi khi thêm món");
                }
            }
        }

        // 3. API GỬI BẾP (Tìm món New -> In -> Chuyển thành Sent)
        // Mobile nút "Gửi thực đơn" sẽ gọi cái này
        // 3. API GỬI BẾP (Tìm món New -> In -> Chuyển thành Sent)
        // Mobile nút "Gửi thực đơn" sẽ gọi cái này
        [HttpPost("{tableId}/send")]
        public async Task<IActionResult> SendToKitchen(int tableId, [FromQuery] int accID = 0)
        {
            var idempotencyKey = TryBeginIdempotent(out var idempotentResult);
            if (idempotentResult != null) return idempotentResult;

            using (var transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var order = await _context.Orders
                        .Include(o => o.OrderDetails).ThenInclude(d => d.Dish).ThenInclude(c => c.Category)
                        .Include(o => o.Table)
                        .Include(o => o.Account)
                        .FirstOrDefaultAsync(o => o.TableID == tableId && o.OrderStatus == "Pending");

                    if (order == null)
                    {
                        return FinishIdempotent(idempotencyKey, BuildErrorResult(400, "ORDER_NOT_FOUND", "Bàn này không có đơn hàng!"));
                    }

                    // [NEW] Lấy tên người gửi (Sender)
                    string senderName = "Admin";
                    if (accID > 0)
                    {
                        var senderAcc = await _context.Accounts.FindAsync(accID);
                        if (senderAcc != null) senderName = senderAcc.AccName;
                    }
                    else
                    {
                        // Fallback
                        senderName = order.Account?.AccName ?? "Admin";
                    }

                    // Lấy các món chưa in (New) hoặc số lượng tăng thêm
                    var itemsToPrint = order.OrderDetails
                        .Where(d => d.Quantity > d.PrintedQuantity)
                        .ToList();

                    if (!itemsToPrint.Any()) return FinishIdempotent(idempotencyKey, Ok(new { Message = "Không có món mới cần gửi bếp" }));

                    // Tăng Batch Number
                    var batchNumber = order.OrderDetails.Max(d => (int?)d.KitchenBatch) ?? 0;
                    batchNumber++;

                    if (!order.FirstSentTime.HasValue) order.FirstSentTime = DateTime.Now;

                    // Danh sách tạm để gửi lệnh in
                    var printQueue = new List<OrderDetail>();

                    foreach (var item in itemsToPrint)
                    {
                        int quantityToSend = item.Quantity - item.PrintedQuantity;

                        var printItem = new OrderDetail
                        {
                            Dish = item.Dish,
                            DishID = item.DishID,
                            Quantity = quantityToSend,
                            Note = item.Note,
                            KitchenBatch = batchNumber
                        };
                        printQueue.Add(printItem);
                    }

                    // [NEW] Cập nhật trạng thái bàn thành Occupied nếu chưa
                    if (order.Table != null && order.Table.TableStatus == "Empty")
                    {
                        order.Table.TableStatus = "Occupied";
                    }

                    var printOk = Services.PrintService.PrintKitchen(order, printQueue, batchNumber, senderName);
                    if (!printOk)
                    {
                        transaction.Rollback();
                        AbandonIdempotent(idempotencyKey);
                        return BuildErrorResult(500, "PRINT_KITCHEN_FAILED", "Lỗi in bếp, vui lòng thử lại");
                    }

                    foreach (var item in itemsToPrint)
                    {
                        item.PrintedQuantity = item.Quantity;
                        item.ItemStatus = "Sent";
                        item.KitchenBatch = batchNumber;
                    }

                    await _context.SaveChangesAsync();
                    transaction.Commit();

                    // --- NON DUPLICATE ACTIONS (Post Commit) ---

                    // Bắn SignalR
                    await _hubContext.Clients.All.SendAsync("TableUpdated", tableId);

                    // [NEW] Bắn thông báo cho Desktop
                    try
                    {
                        string tableName = order.Table?.TableName ?? $"Bàn {tableId}";
                        var items = printQueue.Select(i => (i.Dish?.DishName ?? "Unknown", i.Quantity, i.Note ?? string.Empty));
                        var totalQty = printQueue.Sum(i => i.Quantity);
                        var notiMsg = $"{senderName} gửi bếp ({tableName}): {totalQty} phần:\n{SummarizeItems(items)}";
                        await _hubContext.Clients.All.SendAsync("ReceiveOrderNotification", notiMsg);
                    }
                    catch { }

                    return FinishIdempotent(idempotencyKey, Ok(new { Message = $"Đã gửi {printQueue.Count} món xuống bếp!" }));
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    AbandonIdempotent(idempotencyKey);
                    return BuildErrorResult(500, "ORDER_SEND_KITCHEN_FAILED", "Lỗi gửi bếp");
                }
            }
        }
        // POST: api/Order/{tableId}/update-item
        [HttpPost("{tableId}/update-item")]
        public async Task<IActionResult> UpdateItem(int tableId, [FromBody] UpdateItemRequest req)
        {
            var idempotencyKey = TryBeginIdempotent(out var idempotentResult);
            if (idempotentResult != null) return idempotentResult;

            try
            {
                var orderDetail = await _context.OrderDetails
                    .Include(od => od.Dish)
                    .Include(od => od.Order)
                    .FirstOrDefaultAsync(od => od.OrderDetailID == req.OrderDetailID && od.Order.TableID == tableId);

                if (orderDetail == null)
                {
                    return FinishIdempotent(idempotencyKey, BuildErrorResult(404, "ORDER_DETAIL_NOT_FOUND", "Món không tồn tại"));
                }

                // Chỉ cho phép sửa món trạng thái "New" (chưa gửi bếp)
                if (orderDetail.ItemStatus != "New")
                {
                    return FinishIdempotent(idempotencyKey, BuildErrorResult(400, "ITEM_NOT_EDITABLE", "Chỉ sửa được món chưa gửi bếp"));
                }

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
                var remainingItems = await _context.OrderDetails.Where(d => d.OrderID == order.OrderID).ToListAsync();

                if (remainingItems.Count == 0)
                {
                    // Nếu không còn món nào -> Xóa đơn & Trả bàn
                    _context.Orders.Remove(order);
                    var table = await _context.Tables.FindAsync(order.TableID);
                    if (table != null) table.TableStatus = "Empty";
                }
                else
                {
                    // Còn món -> Tính lại tiền
                    order.SubTotal = remainingItems.Sum(d => d.TotalAmount);
                    order.FinalAmount = order.SubTotal;

                    // [FIX] Còn món trong cart => bàn phải Occupied
                    var table = await _context.Tables.FindAsync(order.TableID);
                    if (table != null && table.TableStatus == "Empty")
                    {
                        table.TableStatus = "Occupied";
                    }
                }

                await _context.SaveChangesAsync();

                // Báo cho mọi người biết để cập nhật giao diện
                await _hubContext.Clients.All.SendAsync("TableUpdated", tableId);

                return FinishIdempotent(idempotencyKey, Ok(new { Message = "Cập nhật thành công" }));
            }
            catch (Exception ex)
            {
                AbandonIdempotent(idempotencyKey);
                return BuildErrorResult(500, "ORDER_UPDATE_ITEM_FAILED", "Lỗi cập nhật món");
            }
        }

        // GET: api/order/{tableId} (API lấy dữ liệu cho Mobile)
        [HttpGet("{tableId}")]
        public async Task<IActionResult> GetOrderDetails(int tableId)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                .FirstOrDefaultAsync(o => o.TableID == tableId && o.OrderStatus == "Pending");

            // Mobile polls this endpoint frequently.
            // Returning 404 for empty tables causes noisy "GET ... 404" messages in browser consoles.
            // Treat "no pending order" as a valid state and return an empty payload instead.
            if (order == null)
            {
                return Ok(new
                {
                    OrderID = 0L,
                    OrderTime = (DateTime?)null,
                    SubTotal = 0m,
                    FinalAmount = 0m,
                    Details = Array.Empty<object>()
                });
            }

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
                    d.DiscountRate,
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
            var idempotencyKey = TryBeginIdempotent(out var idempotentResult);
            if (idempotentResult != null) return idempotentResult;

            using (var transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    // 1. Check quyền
                    var acc = await _context.Accounts.FindAsync(request.AccID);
                    if (acc == null || !acc.CanPayment)
                    {
                        return FinishIdempotent(idempotencyKey, BuildErrorResult(403, "PAYMENT_NOT_ALLOWED", "Bạn không có quyền thanh toán!"));
                    }

                    // 2. Logic thanh toán
                    var order = await _context.Orders.Include(o => o.Table).Include(o => o.OrderDetails).ThenInclude(d => d.Dish)
                        .FirstOrDefaultAsync(o => o.OrderID == request.OrderID);

                    if (order == null)
                    {
                        return FinishIdempotent(idempotencyKey, BuildErrorResult(404, "ORDER_NOT_FOUND", "Đơn không tồn tại"));
                    }
                    if (order.OrderStatus == "Paid")
                    {
                        return FinishIdempotent(idempotencyKey, BuildErrorResult(409, "ORDER_ALREADY_PAID", "Đơn đã thanh toán"));
                    }

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
                    transaction.Commit();

                    // --- POST COMMIT ---

                    // 3. GỌI IN HÓA ĐƠN TRÊN SERVER
                    try
                    {
                        Services.PrintService.PrintBill(order.OrderID);
                    }
                    catch { }

                    await _hubContext.Clients.All.SendAsync("TableUpdated", order.TableID);

                    // Activity log
                    try
                    {
                        var tableName = order.Table?.TableName ?? (order.TableID.HasValue ? $"Bàn {order.TableID.Value}" : "Mang về");
                        var accName = acc?.AccName ?? "Admin";
                        var total = order.FinalAmount;
                        var method = string.IsNullOrWhiteSpace(order.PaymentMethod) ? "Cash" : order.PaymentMethod;
                        var msg = $"{accName} thanh toán ({tableName}): {total:n0}đ ({method})";
                        await _hubContext.Clients.All.SendAsync("ReceiveOrderNotification", msg);
                        await _hubContext.Clients.All.SendAsync("PaymentCompleted", tableName, total);
                    }
                    catch { }

                    return FinishIdempotent(idempotencyKey, Ok(new { Message = "Đã thanh toán & In hóa đơn!" }));
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    AbandonIdempotent(idempotencyKey);
                    return BuildErrorResult(500, "PAYMENT_FAILED", "Lỗi thanh toán");
                }
            }
        }

        public class PrintProvisionalRequest
        {
            public int AccID { get; set; }
        }

        [HttpPost("{tableId}/print-provisional")]
        public async Task<IActionResult> PrintProvisional(int tableId, [FromBody] PrintProvisionalRequest req)
        {
            var idempotencyKey = TryBeginIdempotent(out var idempotentResult);
            if (idempotentResult != null) return idempotentResult;

            try
            {
                // 1. Check quyền (Dùng quyền riêng)
                var acc = await _context.Accounts.FindAsync(req.AccID);
                if (acc == null || !acc.CanPrintProvisional)
                {
                    return FinishIdempotent(idempotencyKey, BuildErrorResult(403, "PROVISIONAL_NOT_ALLOWED", "Bạn không có quyền in tạm tính!"));
                }

                var order = await _context.Orders.FirstOrDefaultAsync(o => o.TableID == tableId && o.OrderStatus == "Pending");
                if (order == null)
                {
                    return FinishIdempotent(idempotencyKey, BuildErrorResult(404, "ORDER_NOT_FOUND", "Bàn không có đơn!"));
                }

                // 2. Cập nhật cờ
                order.IsPreCalculated = true;
                await _context.SaveChangesAsync();

                // 3. Gọi in (IsProvisional = true)
                try
                {
                    Services.PrintService.PrintBill(order.OrderID, true);
                }
                catch { }

                // 4. Bắn SignalR
                await _hubContext.Clients.All.SendAsync("TableUpdated", tableId);

                // Activity log
                try
                {
                    var tableName = await GetTableNameAsync(tableId);
                    var msg = $"{acc.AccName} in tạm tính ({tableName})";
                    await _hubContext.Clients.All.SendAsync("ReceiveOrderNotification", msg);
                }
                catch { }

                return FinishIdempotent(idempotencyKey, Ok(new { Message = "Đã in tạm tính!" }));
            }
            catch (Exception ex)
            {
                AbandonIdempotent(idempotencyKey);
                return BuildErrorResult(500, "PRINT_PROVISIONAL_FAILED", "Lỗi in tạm tính");
            }
        }

        // [POST] api/Order/{tableId}/request-payment
        [HttpPost("{tableId}/request-payment")]
        public async Task<IActionResult> RequestPayment(int tableId, [FromQuery] int accID = 0)
        {
            var idempotencyKey = TryBeginIdempotent(out var idempotentResult);
            if (idempotentResult != null) return idempotentResult;

            try
            {
                // [FIX] Cập nhật vào DB
                var order = await _context.Orders.FirstOrDefaultAsync(o => o.TableID == tableId && o.OrderStatus == "Pending");
                if (order != null)
                {
                    order.IsRequestingPayment = true;
                    await _context.SaveChangesAsync();
                }

                // Gửi tín hiệu SignalR tên là "TableRequestPayment"
                // Desktop sẽ lắng nghe sự kiện này để đổi màu bàn
                await _hubContext.Clients.All.SendAsync("TableRequestPayment", tableId);
                // [FIX] Gửi thêm TableUpdated để Mobile reload lại list và hiện icon Chuông
                await _hubContext.Clients.All.SendAsync("TableUpdated", tableId);

                // Activity log
                try
                {
                    var accName = await GetAccountNameAsync(accID, "Admin");
                    var tableName = await GetTableNameAsync(tableId);
                    var msg = $"{accName} yêu cầu thanh toán ({tableName})";
                    await _hubContext.Clients.All.SendAsync("ReceiveOrderNotification", msg);
                }
                catch { }

                return FinishIdempotent(idempotencyKey, Ok(new { Message = "Đã gửi yêu cầu thanh toán!" }));
            }
            catch (Exception ex)
            {
                AbandonIdempotent(idempotencyKey);
                return BuildErrorResult(500, "REQUEST_PAYMENT_FAILED", "Lỗi yêu cầu thanh toán");
            }
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
            var idempotencyKey = TryBeginIdempotent(out var idempotentResult);
            if (idempotentResult != null) return idempotentResult;

            using (var transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    // 1. Check quyền
                    var acc = await _context.Accounts.FindAsync(req.AccID);
                    if (acc == null || !acc.CanMoveTable)
                    {
                        return FinishIdempotent(idempotencyKey, BuildErrorResult(403, "MOVE_NOT_ALLOWED", "Bạn không có quyền chuyển bàn!"));
                    }

                    // 2. Lấy đơn gốc
                    var sourceOrder = await _context.Orders
                        .Include(o => o.OrderDetails).ThenInclude(d => d.Dish).ThenInclude(c => c.Category)
                        .Include(o => o.Table)
                        .FirstOrDefaultAsync(o => o.TableID == sourceTableId && o.OrderStatus == "Pending");

                    if (sourceOrder == null)
                    {
                        return FinishIdempotent(idempotencyKey, BuildErrorResult(404, "ORDER_NOT_FOUND", "Bàn gốc không có đơn!"));
                    }
                    if (sourceTableId == req.TargetTableID)
                    {
                        return FinishIdempotent(idempotencyKey, BuildErrorResult(409, "TABLE_SAME", "Trùng bàn đích!"));
                    }

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
                        // TRƯỜNG HỢP GỘP BÀN
                        foreach (var detail in sourceOrder.OrderDetails.ToList())
                        {
                            detail.OrderID = targetOrder.OrderID;
                            detail.Order = targetOrder;
                            sourceOrder.OrderDetails.Remove(detail);
                        }
                        await _context.SaveChangesAsync();
                        targetOrder.SubTotal += sourceOrder.SubTotal;
                        targetOrder.FinalAmount += sourceOrder.FinalAmount;

                        _context.Orders.Remove(sourceOrder);
                    }
                    else
                    {
                        // TRƯỜNG HỢP CHUYỂN BÀN
                        sourceOrder.TableID = req.TargetTableID;
                        if (targetTable != null) targetTable.TableStatus = "Occupied";
                    }

                    // Trả bàn gốc về trống
                    var sourceTable = await _context.Tables.FindAsync(sourceTableId);
                    if (sourceTable != null) sourceTable.TableStatus = "Empty";

                    await _context.SaveChangesAsync();
                    transaction.Commit();

                    // --- POST COMMIT ---

                    // 4. Cập nhật UI trước để phản hồi nhanh
                    await _hubContext.Clients.All.SendAsync("TableUpdated", sourceTableId);
                    await _hubContext.Clients.All.SendAsync("TableUpdated", req.TargetTableID);

                    // 5. IN PHIẾU BÁO BẾP (chạy nền để không chặn response)
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            Services.PrintService.PrintMoveTableNotification(targetOrder ?? sourceOrder, oldTableName, newTableName);
                        }
                        catch { }
                    });

                    // Activity log
                    try
                    {
                        var action = targetOrder != null ? "gộp bàn" : "chuyển bàn";
                        var msg = $"{acc.AccName} {action}: {oldTableName} → {newTableName}";
                        await _hubContext.Clients.All.SendAsync("ReceiveOrderNotification", msg);
                    }
                    catch { }

                    return FinishIdempotent(idempotencyKey, Ok(new { Message = "Chuyển bàn thành công!" }));
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    AbandonIdempotent(idempotencyKey);
                    return BuildErrorResult(500, "MOVE_TABLE_FAILED", "Lỗi chuyển bàn");
                }
            }
        }
        public class CancelItemRequest
        {
            public int AccID { get; set; }
            public long OrderDetailID { get; set; }
            public int Quantity { get; set; } // Số lượng muốn hủy
                                              //blic string Reason { get; set; }
        }

        [HttpPost("cancel-item")]
        public async Task<IActionResult> CancelItem([FromBody] CancelItemRequest req)
        {
            var idempotencyKey = TryBeginIdempotent(out var idempotentResult);
            if (idempotentResult != null) return idempotentResult;

            using (var transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    // 1. Check quyền
                    var acc = await _context.Accounts.FindAsync(req.AccID);
                    if (acc == null || !acc.CanCancelItem)
                    {
                        return FinishIdempotent(idempotencyKey, BuildErrorResult(403, "CANCEL_NOT_ALLOWED", "Bạn không có quyền hủy món!"));
                    }

                    var detail = await _context.OrderDetails
                        .Include(d => d.Dish).ThenInclude(c => c.Category)
                        .Include(d => d.Order).ThenInclude(o => o.Table)
                        .FirstOrDefaultAsync(d => d.OrderDetailID == req.OrderDetailID);

                    if (detail == null)
                    {
                        return FinishIdempotent(idempotencyKey, BuildErrorResult(404, "ORDER_DETAIL_NOT_FOUND", "Món không tồn tại"));
                    }

                    if (req.Quantity > detail.Quantity)
                    {
                        return FinishIdempotent(idempotencyKey, BuildErrorResult(400, "CANCEL_QTY_EXCEEDED", "Không thể hủy quá số lượng hiện có"));
                    }

                    // 2. Giảm số lượng
                    detail.Quantity -= req.Quantity;
                    detail.PrintedQuantity = Math.Max(0, detail.PrintedQuantity - req.Quantity);
                    detail.TotalAmount = detail.Quantity * detail.UnitPrice;

                    // Nếu giảm về 0 thì xóa luôn dòng
                    bool isRemoved = false;
                    if (detail.Quantity <= 0)
                    {
                        _context.OrderDetails.Remove(detail);
                        isRemoved = true;
                    }

                    // Cập nhật tổng tiền đơn hàng
                    var order = detail.Order;

                    // Cần lấy list về và filter bằng logic vì chưa save
                    var remainingItems = await _context.OrderDetails
                        .Where(d => d.OrderID == order.OrderID)
                        .ToListAsync();

                    if (isRemoved)
                    {
                        remainingItems = remainingItems.Where(d => d.OrderDetailID != detail.OrderDetailID).ToList();
                    }

                    if (remainingItems.Count == 0)
                    {
                        // Nếu hủy hết món -> Xóa đơn & Trả bàn
                        _context.Orders.Remove(order);
                        if (order.Table != null) order.Table.TableStatus = "Empty";
                    }
                    else
                    {
                        order.SubTotal = remainingItems.Sum(d => d.TotalAmount);
                        order.FinalAmount = order.SubTotal;
                    }

                    // [NEW] Lưu log hủy món
                    var log = new CancelledLog
                    {
                        TableID = order.TableID,
                        OrderID = order.OrderID,
                        DishName = detail.Dish?.DishName ?? "Unknown",
                        Quantity = req.Quantity,
                        Amount = req.Quantity * detail.UnitPrice,
                        DeletedBy = acc.AccName,
                        CancelTime = DateTime.Now
                    };
                    _context.CancelledLogs.Add(log);

                    await _context.SaveChangesAsync();
                    transaction.Commit();

                    // --- POST COMMIT ---

                    // 3. IN PHIẾU HỦY XUỐNG BẾP
                    var cancelItem = new OrderDetail
                    {
                        Dish = detail.Dish,
                        DishID = detail.DishID,
                        Quantity = -req.Quantity, // Số âm để template bếp hiểu là trả món
                        Note = detail.Note,
                        KitchenBatch = 0
                    };

                    Services.PrintService.PrintKitchen(order, new List<OrderDetail> { cancelItem }, 0);

                    await _hubContext.Clients.All.SendAsync("TableUpdated", order.TableID);

                    // Activity log
                    try
                    {
                        var tableName = order.Table?.TableName ?? $"Bàn {order.TableID}";
                        var dishName = detail.Dish?.DishName ?? "Unknown";
                        var msg = $"{acc.AccName} hủy món ({tableName}): {FormatItem(dishName, req.Quantity, detail.Note)}";
                        await _hubContext.Clients.All.SendAsync("ReceiveOrderNotification", msg);
                    }
                    catch { }

                    return FinishIdempotent(idempotencyKey, Ok(new { Message = "Đã hủy món & Báo bếp" }));
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    AbandonIdempotent(idempotencyKey);
                    return BuildErrorResult(500, "CANCEL_ITEM_FAILED", "Lỗi hủy món");
                }
            }
        }
        [HttpPost("cancel-multiple")]
        public async Task<IActionResult> CancelMultipleItems([FromBody] List<CancelItemRequest> requests)
        {
            var idempotencyKey = TryBeginIdempotent(out var idempotentResult);
            if (idempotentResult != null) return idempotentResult;

            if (requests == null || requests.Count == 0)
            {
                return FinishIdempotent(idempotencyKey, BuildErrorResult(400, "REQUEST_EMPTY", "Danh sách trống"));
            }

            using (var transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    // 1. Check quyền (Lấy request đầu tiên để check acc)
                    int accId = requests[0].AccID;
                    var acc = await _context.Accounts.FindAsync(accId);
                    if (acc == null || !acc.CanCancelItem)
                    {
                        return FinishIdempotent(idempotencyKey, BuildErrorResult(403, "CANCEL_NOT_ALLOWED", "Bạn không có quyền hủy món!"));
                    }

                    // Group prints
                    var aggregatedPrintItems = new List<OrderDetail>();
                    int tableId = 0;
                    long orderId = 0;

                    // [FIX] Iterate and process DB updates
                    foreach (var req in requests)
                    {
                        var detail = await _context.OrderDetails
                            .Include(d => d.Dish).ThenInclude(c => c.Category)
                            .Include(d => d.Order).ThenInclude(o => o.Table)
                            .FirstOrDefaultAsync(d => d.OrderDetailID == req.OrderDetailID);

                        if (detail == null) continue;

                        // Capture Order Context
                        if (tableId == 0) tableId = detail.Order.TableID ?? 0;
                        if (orderId == 0) orderId = detail.Order.OrderID;

                        if (req.Quantity > detail.Quantity) continue;

                        // Update DB
                        detail.Quantity -= req.Quantity;
                        detail.PrintedQuantity = Math.Max(0, detail.PrintedQuantity - req.Quantity);
                        detail.TotalAmount = detail.Quantity * detail.UnitPrice;

                        // Log
                        var log = new CancelledLog
                        {
                            TableID = detail.Order.TableID,
                            OrderID = detail.Order.OrderID,
                            DishName = detail.Dish?.DishName ?? "Unknown",
                            Quantity = req.Quantity,
                            Amount = req.Quantity * detail.UnitPrice,
                            DeletedBy = acc.AccName,
                            CancelTime = DateTime.Now
                        };
                        _context.CancelledLogs.Add(log);

                        // Add to Print List (Negative Quantity)
                        var existingPrint = aggregatedPrintItems.FirstOrDefault(p => p.DishID == detail.DishID && (p.Note ?? "") == (detail.Note ?? ""));
                        if (existingPrint != null)
                        {
                            existingPrint.Quantity -= req.Quantity;
                        }
                        else
                        {
                            aggregatedPrintItems.Add(new OrderDetail
                            {
                                Dish = detail.Dish,
                                DishID = detail.DishID,
                                Quantity = -req.Quantity,
                                Note = detail.Note,
                                KitchenBatch = 0
                            });
                        }

                        // Remove if 0
                        if (detail.Quantity <= 0) _context.OrderDetails.Remove(detail);
                    }

                    // Save Changes (DB Updates)
                    await _context.SaveChangesAsync();

                    // Check Order Context for Empty
                    var order = await _context.Orders.Include(o => o.Table).FirstOrDefaultAsync(o => o.OrderID == orderId);
                    if (order != null)
                    {
                        var remaining = await _context.OrderDetails.Where(d => d.OrderID == orderId).CountAsync();
                        if (remaining == 0)
                        {
                            _context.Orders.Remove(order);
                            if (order.Table != null) order.Table.TableStatus = "Empty";
                            await _context.SaveChangesAsync();
                        }
                        else
                        {
                            // Recalculate Totals
                            var details = await _context.OrderDetails.Where(d => d.OrderID == orderId).ToListAsync();
                            order.SubTotal = details.Sum(d => d.TotalAmount);
                            order.FinalAmount = order.SubTotal;
                            await _context.SaveChangesAsync();
                        }
                    }

                    transaction.Commit();

                    // --- POST COMMIT ---

                    if (order != null)
                    {
                        // PRINT
                        if (aggregatedPrintItems.Count > 0)
                        {
                            Services.PrintService.PrintKitchen(order, aggregatedPrintItems, 0);
                        }
                        // SignalR
                        await _hubContext.Clients.All.SendAsync("TableUpdated", tableId);

                        // Activity log
                        try
                        {
                            var tableName = order.Table?.TableName ?? $"Bàn {tableId}";
                            var items = aggregatedPrintItems.Select(i => (i.Dish?.DishName ?? "Unknown", -i.Quantity, i.Note ?? string.Empty));
                            var totalQty = aggregatedPrintItems.Sum(i => -i.Quantity);
                            var msg = $"{acc.AccName} hủy món ({tableName}): {totalQty} phần:\n{SummarizeItems(items)}";
                            await _hubContext.Clients.All.SendAsync("ReceiveOrderNotification", msg);
                        }
                        catch { }
                    }

                    return FinishIdempotent(idempotencyKey, Ok(new { Message = $"Đã hủy {requests.Count} yêu cầu thành công" }));
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    AbandonIdempotent(idempotencyKey);
                    return BuildErrorResult(500, "CANCEL_ITEMS_FAILED", "Lỗi hủy món");
                }
            }
        }
    }
}
