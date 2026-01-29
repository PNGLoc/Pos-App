using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore; // For Include
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Server.Hubs;

namespace PosSystem.Main.Pages
{
    public partial class TableSetupPage : UserControl
    {
        private Table? _selected = null;
        public List<TableCategory> Categories { get; set; } = new List<TableCategory>();

        public TableSetupPage()
        {
            InitializeComponent();
            this.DataContext = this; // Set DataContext for Binding
            LoadData();
        }

        private void NotifyTablesUpdated()
        {
            try
            {
                if (App.WebHost == null) return;
                var hubContext = App.WebHost.Services.GetService<IHubContext<PosHub>>();
                if (hubContext != null)
                {
                    _ = hubContext.Clients.All.SendAsync("TableUpdated", -1);
                }
            }
            catch { }
        }

        void LoadData()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // Load Categories for ComboBox
                    Categories = db.TableCategories
                        .OrderBy(c => c.DisplayOrder)
                        .ThenBy(c => c.CategoryID)
                        .ToList();
                    cboType.ItemsSource = Categories; // Refresh ItemsSource

                    // Load Tables with Category info
                    dgTables.ItemsSource = db.Tables.Include(t => t.Category).OrderBy(t => t.TableID).ToList();
                }
            }
            catch { }
        }

        // --- CÁC HÀM XỬ LÝ MODAL ---

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            // Mở form thêm mới
            _selected = null;
            txtName.Text = "";
            if (Categories.Count > 0) cboType.SelectedIndex = 0;

            lblModalTitle.Text = "THÊM BÀN MỚI";
            modalOverlay.Visibility = Visibility.Visible;
            txtName.Focus();
        }

        private void BtnEditRow_Click(object sender, RoutedEventArgs e)
        {
            // Mở form sửa từ nút trên dòng
            if (sender is Button btn && btn.Tag is Table t)
            {
                _selected = t;
                txtName.Text = t.TableName;

                // Select Category in ComboBox
                if (t.CategoryID.HasValue)
                {
                    cboType.SelectedValue = t.CategoryID.Value;
                }
                else
                {
                    cboType.SelectedIndex = -1;
                }

                lblModalTitle.Text = "SỬA THÔNG TIN BÀN";
                modalOverlay.Visibility = Visibility.Visible;
                txtName.Focus();
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Table t)
            {
                if (MessageBox.Show($"Bạn có chắc muốn xóa bàn '{t.TableName}'?\nTất cả đơn hàng và lịch sử liên quan sẽ bị xóa.", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    using (var db = new AppDbContext())
                    {
                        var item = db.Tables.Find(t.TableID);
                        if (item != null)
                        {
                            try
                            {
                                var orders = db.Orders
                                    .Include(o => o.OrderDetails)
                                    .Where(o => o.TableID == item.TableID)
                                    .ToList();

                                var orderDetails = orders.SelectMany(o => o.OrderDetails).ToList();
                                if (orderDetails.Count > 0)
                                    db.OrderDetails.RemoveRange(orderDetails);

                                if (orders.Count > 0)
                                    db.Orders.RemoveRange(orders);

                                var cancelLogs = db.CancelledLogs
                                    .Where(c => c.TableID == item.TableID)
                                    .ToList();
                                if (cancelLogs.Count > 0)
                                    db.CancelledLogs.RemoveRange(cancelLogs);

                                db.Tables.Remove(item);
                                db.SaveChanges();
                                LoadData();
                                NotifyTablesUpdated();
                            }
                            catch (DbUpdateException)
                            {
                                MessageBox.Show("Không thể xóa bàn do còn dữ liệu liên quan.", "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                    }
                }
            }
        }

        private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
        {
            modalOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnSaveModal_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Vui lòng nhập tên bàn!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int? selectedCatId = (int?)cboType.SelectedValue;

            using (var db = new AppDbContext())
            {
                if (_selected == null)
                {
                    // Thêm mới
                    // Validate
                    if (db.Tables.ToList().Any(tbl => tbl.TableName.ToLower() == txtName.Text.ToLower()))
                    {
                        MessageBox.Show("Tên bàn đã tồn tại!");
                        return;
                    }

                    db.Tables.Add(new Table
                    {
                        TableName = txtName.Text,
                        CategoryID = selectedCatId,
                        TableStatus = "Empty"
                    });
                }
                else
                {
                    // Cập nhật
                    var t = db.Tables.Find(_selected.TableID);
                    if (t != null)
                    {
                        // Validate
                        if (t.TableName.ToLower() != txtName.Text.ToLower())
                        {
                            if (db.Tables.ToList().Any(tbl => tbl.TableName.ToLower() == txtName.Text.ToLower()))
                            {
                                MessageBox.Show("Tên bàn đã tồn tại!");
                                return;
                            }
                        }

                        t.TableName = txtName.Text;
                        t.CategoryID = selectedCatId;
                    }
                }
                db.SaveChanges();
            }

            // Đóng modal và tải lại dữ liệu
            modalOverlay.Visibility = Visibility.Collapsed;
            LoadData();
            NotifyTablesUpdated();
        }
    }
}