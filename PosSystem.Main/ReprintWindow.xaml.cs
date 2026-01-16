using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PosSystem.Main.Models;

namespace PosSystem.Main
{
    public partial class ReprintWindow : Window
    {
        public List<ReprintItemModel> SelectedItems { get; private set; } = new List<ReprintItemModel>();
        public bool IsConfirmed { get; private set; } = false;

        public ReprintWindow(List<OrderDetail> orderDetails)
        {
            InitializeComponent();
            LoadItems(orderDetails);
        }

        private void LoadItems(List<OrderDetail> orderDetails)
        {
            // Chỉ hiện món đã gửi (PrintedQuantity > 0 hoặc ItemStatus == Sent/Modified)
            // Hoặc đơn giản là hiện tất cả món có Quantity > 0
            var items = orderDetails
                .Where(d => d.Quantity > 0)
                .Select(d => new ReprintItemModel
                {
                    OrderDetail = d,
                    DisplayText = $"{d.Dish?.DishName} (SL: {d.Quantity})",
                    IsSelected = false
                })
                .ToList();

            lstItems.ItemsSource = items;
        }

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            var items = lstItems.ItemsSource as List<ReprintItemModel>;
            SelectedItems = items.Where(i => i.IsSelected).ToList();

            if (SelectedItems.Count == 0)
            {
                MessageBox.Show("Vui lòng chọn ít nhất 1 món!");
                return;
            }

            IsConfirmed = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public class ReprintItemModel
    {
        public OrderDetail OrderDetail { get; set; }
        public string DisplayText { get; set; }
        public bool IsSelected { get; set; }
    }
}
