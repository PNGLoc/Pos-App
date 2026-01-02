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

        // POST: api/order/create (Mở bàn mới)
        [HttpPost("create")]
        public async Task<IActionResult> CreateOrder([FromBody] OrderRequest request)
        {
            if (request.Items.Count == 0) return BadRequest("Chưa chọn món nào!");

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
                    var newDetail = new OrderDetail
                    {
                        DishID = dish.DishID,
                        Quantity = itemDto.Quantity,
                        UnitPrice = dish.Price,
                        Note = itemDto.Note,
                        ItemStatus = "New",
                        DiscountRate = 0,
                        TotalAmount = itemDto.Quantity * dish.Price
                    };

                    if (currentOrder.OrderID > 0) currentOrder.OrderDetails.Add(newDetail);
                    else _context.Add(newDetail);
                }
            }

            currentOrder.SubTotal = currentOrder.OrderDetails.Sum(d => d.TotalAmount);
            currentOrder.FinalAmount = currentOrder.SubTotal;

            await _context.SaveChangesAsync();

            // Gửi tín hiệu real-time
            await _hubContext.Clients.All.SendAsync("TableUpdated", request.TableID);

            return Ok(new { Message = "Đã gửi đơn xuống bếp thành công!", OrderID = currentOrder.OrderID });
        }

        // GET: api/order/{tableId}
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
                    d.Dish!.DishName,
                    d.Quantity,
                    d.UnitPrice,
                    d.TotalAmount,
                    d.Note,
                    d.ItemStatus
                })
            });
        }

        // POST: api/order/{tableId} (Thêm món)
        [HttpPost("{tableId}")]
        public async Task<IActionResult> AddOrderItems(int tableId, [FromBody] AddOrderItemsRequest request)
        {
            if (request?.Details == null || request.Details.Count == 0)
                return BadRequest("Chưa chọn món nào!");

            var currentOrder = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(od => od.Dish).ThenInclude(d => d.Category)
                .FirstOrDefaultAsync(o => o.TableID == tableId && o.OrderStatus == "Pending");

            if (currentOrder == null)
            {
                currentOrder = new Order
                {
                    TableID = tableId,
                    AccID = 1,
                    OrderTime = DateTime.Now,
                    OrderStatus = "Pending",
                    PaymentMethod = "Cash"
                };
                _context.Orders.Add(currentOrder);

                var table = await _context.Tables.FindAsync(tableId);
                if (table != null) table.TableStatus = "Occupied";
            }

            var itemsToPrint = new List<OrderDetail>();
            foreach (var itemDto in request.Details)
            {
                var dish = await _context.Dishes.Include(d => d.Category).FirstOrDefaultAsync(d => d.DishID == itemDto.DishID);
                if (dish != null)
                {
                    var newDetail = new OrderDetail
                    {
                        DishID = dish.DishID,
                        Quantity = itemDto.Quantity,
                        UnitPrice = dish.Price,
                        Note = itemDto.Note ?? "",
                        ItemStatus = "Sent",
                        PrintedQuantity = itemDto.Quantity,
                        DiscountRate = 0,
                        TotalAmount = itemDto.Quantity * dish.Price
                    };

                    if (currentOrder.OrderID > 0) currentOrder.OrderDetails.Add(newDetail);
                    else _context.Add(newDetail); // Link với order mới

                    itemsToPrint.Add(newDetail);
                }
            }

            currentOrder.SubTotal = currentOrder.OrderDetails.Sum(d => d.TotalAmount);
            currentOrder.FinalAmount = currentOrder.SubTotal;

            if (!currentOrder.FirstSentTime.HasValue) currentOrder.FirstSentTime = DateTime.Now;

            await _context.SaveChangesAsync();

            // IN BẾP
            if (itemsToPrint.Any())
            {
                var batchNumber = currentOrder.OrderDetails
                    .Where(d => d.KitchenBatch > 0)
                    .Max(d => (int?)d.KitchenBatch) ?? 0;
                batchNumber++;

                foreach (var item in itemsToPrint) item.KitchenBatch = batchNumber;
                await _context.SaveChangesAsync();

                Services.PrintService.PrintKitchen(currentOrder, itemsToPrint, batchNumber);
            }

            // Gửi tín hiệu real-time
            await _hubContext.Clients.All.SendAsync("TableUpdated", tableId);

            return Ok(new { Message = "Đã gửi đơn xuống bếp thành công!", OrderID = currentOrder.OrderID });
        }

        // POST: api/order/checkout (Thanh toán)
        [HttpPost("checkout")]
        public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.OrderID == request.OrderID);

            if (order == null) return NotFound("Không tìm thấy đơn hàng!");
            if (order.OrderStatus == "Paid") return BadRequest("Đơn này đã thanh toán rồi!");

            order.PaymentMethod = request.PaymentMethod;
            order.DiscountPercent = request.DiscountPercent;
            order.DiscountAmount = request.DiscountAmount;
            order.CheckoutTime = DateTime.Now;

            // Tính tiền
            order.SubTotal = order.OrderDetails.Sum(d => d.TotalAmount);
            decimal discountValue = (order.DiscountPercent > 0)
                ? order.SubTotal * (order.DiscountPercent / 100)
                : order.DiscountAmount;

            order.FinalAmount = order.SubTotal - discountValue;
            if (order.FinalAmount < 0) order.FinalAmount = 0;

            // Chốt đơn
            order.OrderStatus = "Paid";
            if (order.Table != null)
            {
                order.Table.TableStatus = "Empty"; // Trả bàn về trống
            }

            await _context.SaveChangesAsync();

            // --- QUAN TRỌNG: Gửi SignalR báo cho Mobile biết bàn đã trống ---
            if (order.TableID.HasValue)
            {
                await _hubContext.Clients.All.SendAsync("TableUpdated", order.TableID.Value);
            }
            // -------------------------------------------------------------

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