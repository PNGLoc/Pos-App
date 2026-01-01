using System.Collections.Generic;

namespace PosSystem.Main.Server.Dtos
{
    public class OrderRequest
    {
        public int TableID { get; set; }
        public int AccID { get; set; } // Nhân viên nào order
        public List<OrderItemDto> Items { get; set; } = new();
    }

    public class OrderItemDto
    {
        public int DishID { get; set; }
        public int Quantity { get; set; }
        public string Note { get; set; } = "";
    }

    // DTO cho Mobile API POST /api/order/{tableId}
    public class AddOrderItemsRequest
    {
        public List<OrderItemDto> Details { get; set; } = new();
    }
}