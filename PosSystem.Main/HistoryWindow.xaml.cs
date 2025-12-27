using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;

namespace PosSystem.Main
{
    public partial class HistoryWindow : Window
    {
        public HistoryWindow()
        {
            InitializeComponent();
            LoadTodayHistory();
        }

        private void LoadTodayHistory()
        {
            using (var db = new AppDbContext())
            {
                // Lấy đơn hôm nay
                DateTime startOfDay = DateTime.Today;
                var list = db.Orders.Include(o => o.Table)
                    .Where(o => o.OrderTime >= startOfDay && o.OrderStatus == "Paid")
                    .OrderByDescending(o => o.OrderTime)
                    .Select(o => new
                    {
                        o.OrderID,
                        TableName = o.Table != null ? o.Table.TableName : "Mang về",
                        o.OrderTime,
                        o.FinalAmount
                    })
                    .ToList();
                dgHistory.ItemsSource = list;
            }
        }

        private void BtnView_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long id)
            {
                new OrderDetailWindow(id).ShowDialog();
            }
        }

        private void BtnReprint_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long id)
            {
                if (MessageBox.Show("In lại hóa đơn này?", "Xác nhận", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    Services.PrintService.PrintBill(id);
                }
            }
        }
    }
}