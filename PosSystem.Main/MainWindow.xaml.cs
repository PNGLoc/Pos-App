using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main
{
    // Giữ nguyên các class ViewModel
    public class TableViewModel
    {
        public int TableID { get; set; }
        public required string TableName { get; set; }
        public required string TableStatus { get; set; }
        public string StatusDisplay => TableStatus == "Occupied" ? "Có khách" : "Trống";
        public SolidColorBrush ColorBrush => TableStatus == "Occupied" ? new SolidColorBrush(Color.FromRgb(220, 53, 69)) : new SolidColorBrush(Color.FromRgb(40, 167, 69));
    }
    public class CategoryViewModel { public int CategoryID { get; set; } public string CategoryName { get; set; } = ""; }

    public partial class MainWindow : Window
    {
        private HubConnection _connection = default!;
        private int _selectedTableId = 0;
        private List<Dish> _allDishes = new List<Dish>();

        public MainWindow()
        {
            InitializeComponent();
            LoadTables();
            LoadMenu();
            SetupRealtime();
        }

        // --- 1. LOGIC CHUYỂN VIEW ---
        private void SwitchToOrderView()
        {
            viewOrderDetails.Visibility = Visibility.Visible;
            viewMenu.Visibility = Visibility.Collapsed;
        }

        private void SwitchToMenuView()
        {
            if (_selectedTableId == 0)
            {
                MessageBox.Show("Vui lòng chọn bàn trước!");
                return;
            }
            viewOrderDetails.Visibility = Visibility.Collapsed;
            viewMenu.Visibility = Visibility.Visible;
        }

        private void BtnShowMenu_Click(object sender, RoutedEventArgs e) => SwitchToMenuView();
        private void BtnBackToOrder_Click(object sender, RoutedEventArgs e) => SwitchToOrderView();

        // --- 2. QUẢN LÝ BÀN ---
        private void LoadTables()
        {
            using (var db = new AppDbContext())
            {
                var tables = db.Tables.ToList();
                lstTables.ItemsSource = tables.Select(t => new TableViewModel
                {
                    TableID = t.TableID,
                    TableName = t.TableName,
                    TableStatus = t.TableStatus
                }).ToList();
            }
        }

        private void lstTables_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstTables.SelectedItem is TableViewModel selected)
            {
                _selectedTableId = selected.TableID;
                lblSelectedTable.Text = selected.TableName;

                // Mặc định khi chọn bàn thì xem đơn hàng
                SwitchToOrderView();
                LoadOrderDetails(selected.TableID);
            }
        }

        // --- 3. QUẢN LÝ ĐƠN HÀNG ---
        private void LoadOrderDetails(int tableId)
        {
            using (var db = new AppDbContext())
            {
                var order = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                    .FirstOrDefault(o => o.TableID == tableId && o.OrderStatus == "Pending");

                if (order != null)
                {
                    lstOrderDetails.ItemsSource = order.OrderDetails.Select(d => new
                    {
                        d.Dish!.DishName,
                        d.Quantity,
                        TotalAmount = d.TotalAmount
                    }).ToList();
                    lblTotal.Text = order.FinalAmount.ToString("N0") + "đ";
                    btnCheckout.IsEnabled = true;
                }
                else
                {
                    lstOrderDetails.ItemsSource = null;
                    lblTotal.Text = "0đ";
                    btnCheckout.IsEnabled = false;
                }
            }
        }

        // --- 4. MENU & THÊM MÓN ---
        private void LoadMenu()
        {
            using (var db = new AppDbContext())
            {
                var cats = db.Categories.OrderBy(c => c.OrderIndex).ToList();
                var catViewModels = new List<CategoryViewModel> { new CategoryViewModel { CategoryID = 0, CategoryName = "TẤT CẢ" } };
                catViewModels.AddRange(cats.Select(c => new CategoryViewModel { CategoryID = c.CategoryID, CategoryName = c.CategoryName }));

                lstCategories.ItemsSource = catViewModels;
                _allDishes = db.Dishes.Where(d => d.DishStatus == "Active").ToList();
                lstDishes.ItemsSource = _allDishes;
                lstCategories.SelectedIndex = 0;
            }
        }

        private void lstCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstCategories.SelectedItem is CategoryViewModel selected)
            {
                lstDishes.ItemsSource = selected.CategoryID == 0
                    ? _allDishes
                    : _allDishes.Where(d => d.CategoryID == selected.CategoryID).ToList();
            }
        }

        private void BtnDish_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int dishId)
            {
                AddDishToOrder(_selectedTableId, dishId);
                // Sau khi thêm, reload lại data nền để cập nhật tổng tiền
                LoadOrderDetails(_selectedTableId);
                // Vẫn giữ ở màn hình Menu để người dùng chọn tiếp
            }
        }

        private void AddDishToOrder(int tableId, int dishId)
        {
            using (var db = new AppDbContext())
            {
                var order = db.Orders.Include(o => o.OrderDetails)
                    .FirstOrDefault(o => o.TableID == tableId && o.OrderStatus == "Pending");

                if (order == null)
                {
                    order = new Order { TableID = tableId, AccID = 1, OrderTime = DateTime.Now, OrderStatus = "Pending" };
                    db.Orders.Add(order);
                    var table = db.Tables.Find(tableId);
                    if (table != null) table.TableStatus = "Occupied";
                }

                var existing = order.OrderDetails.FirstOrDefault(d => d.DishID == dishId && d.ItemStatus == "New");
                var dish = _allDishes.FirstOrDefault(d => d.DishID == dishId);
                if (dish == null) return;

                if (existing != null)
                {
                    existing.Quantity++;
                    existing.TotalAmount = existing.Quantity * existing.UnitPrice;
                }
                else
                {
                    var newDetail = new OrderDetail { DishID = dishId, Quantity = 1, UnitPrice = dish.Price, ItemStatus = "New", TotalAmount = dish.Price };
                    if (order.OrderID == 0) order.OrderDetails.Add(newDetail);
                    else db.OrderDetails.Add(newDetail);
                }

                db.SaveChanges(); // Lưu chi tiết

                // Tính tổng
                var details = db.OrderDetails.Where(d => d.OrderID == order.OrderID).ToList();
                order.SubTotal = details.Sum(d => d.TotalAmount);
                order.FinalAmount = order.SubTotal;
                db.SaveChanges(); // Lưu tổng

                Dispatcher.Invoke(() => LoadTables()); // Update màu bàn bên trái
            }
        }

        // --- 5. SIGNALR & CHECKOUT ---
        private async void SetupRealtime()
        {
            _connection = new HubConnectionBuilder().WithUrl("http://localhost:5000/posHub").WithAutomaticReconnect().Build();
            _connection.On<int>("TableUpdated", (id) => Dispatcher.Invoke(() => { LoadTables(); if (_selectedTableId == id) LoadOrderDetails(id); }));
            try { await _connection.StartAsync(); } catch { }
        }

        private void btnCheckout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Chuẩn bị làm chức năng thanh toán!");
        }
    }
}