using System.Linq;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;

namespace PosSystem.Main
{
    public partial class OrderDetailWindow : Window
    {
        public OrderDetailWindow(long orderId)
        {
            InitializeComponent();
            LoadData(orderId);
        }

        private void LoadData(long orderId)
        {
            using (var db = new AppDbContext())
            {
                var order = db.Orders.Include(o => o.Table).Include(o => o.OrderDetails).ThenInclude(d => d.Dish)
                              .FirstOrDefault(o => o.OrderID == orderId);

                if (order == null) return;

                lblTitle.Text = $"CHI TIẾT ĐƠN HÀNG #{order.OrderID}";
                lblInfo.Text = $"Bàn: {order.Table?.TableName ?? "Mang về"}  |  Ngày: {order.OrderTime:dd/MM/yyyy HH:mm}";
                decimal subTotal = order.SubTotal;
                decimal discountValue = order.DiscountPercent > 0
                    ? subTotal * (order.DiscountPercent / 100m)
                    : order.DiscountAmount;

                lblSubTotal.Text = subTotal.ToString("N0") + "đ";
                lblDiscount.Text = discountValue.ToString("N0") + "đ";
                lblTotal.Text = order.FinalAmount.ToString("N0") + "đ";

                // Map dữ liệu (Bao gồm Note)
                lstDetails.ItemsSource = order.OrderDetails.Select(d => new
                {
                    DishName = d.Dish?.DishName ?? "Unknown",
                    d.Quantity,
                    d.UnitPrice,
                    d.TotalAmount,
                    DiscountDisplay = d.DiscountRate != 0
                        ? (d.DiscountRate > 0
                            ? $"Giảm món: {d.DiscountRate:N0}% (-{(d.UnitPrice * d.Quantity * d.DiscountRate / 100m):N0}đ)"
                            : $"Tăng món: {System.Math.Abs(d.DiscountRate):N0}% (+{System.Math.Abs(d.UnitPrice * d.Quantity * d.DiscountRate / 100m):N0}đ)")
                        : string.Empty,
                    HasDiscount = d.DiscountRate != 0,

                    // Lấy ghi chú từ DB
                    NoteDisplay = string.IsNullOrEmpty(d.Note) ? "" : $"📝 {d.Note}",
                    HasNote = !string.IsNullOrEmpty(d.Note)
                }).ToList();
            }
        }
    }
}