using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using System.Collections.Generic;
using System.Collections.Generic; // Để dùng List
using PosSystem.Main.Services;
namespace PosSystem.Main.Pages
{
    public partial class EmployeeSetupPage : UserControl
    {
        private bool _isLoaded = false;
        private Employee? _editingEmp = null; // null = Thêm mới, có object = Sửa

        public EmployeeSetupPage()
        {
            InitializeComponent();

            var now = DateTime.Now;

            // Setup ngày mặc định cho Tab Lịch sử (Tab 2)
            dpFrom.SelectedDate = new DateTime(now.Year, now.Month, 1);
            dpTo.SelectedDate = now;

            // --- THÊM: Setup ngày mặc định cho Tab Thống kê (Tab 3) ---
            // (Việc set giá trị này sẽ kích hoạt sự kiện OnStatFilterChanged để load dữ liệu luôn)
            dpStatFrom.SelectedDate = new DateTime(now.Year, now.Month, 1);
            dpStatTo.SelectedDate = now;

            _isLoaded = true;

            LoadEmployees();
            LoadFilterData();
            LoadTimeLogs();
            CalculateStats();
        }

        // --- QUẢN LÝ NHÂN VIÊN ---

        private void LoadEmployees()
        {
            using (var db = new AppDbContext())
            {
                dgEmp.ItemsSource = db.Employees.OrderBy(e => e.FullName).ToList();
            }
        }

        // 1. MỞ MODAL THÊM MỚI
        private void BtnAddNew_Click(object sender, RoutedEventArgs e)
        {
            _editingEmp = null; // Chế độ Thêm
            lblModalTitle.Text = "THÊM NHÂN VIÊN MỚI";
            lblModalTitle.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69)); // Màu xanh lá

            // Xóa trắng form
            txtName.Clear();
            txtPos.Clear();
            txtCard.Clear();
            chkActive.IsChecked = true;

            // Hiện Modal
            modalOverlay.Visibility = Visibility.Visible;
            txtName.Focus();
        }

        // 2. MỞ MODAL SỬA
        private void BtnEditRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Employee emp)
            {
                _editingEmp = emp; // Chế độ Sửa
                lblModalTitle.Text = "CHỈNH SỬA THÔNG TIN";
                lblModalTitle.Foreground = new SolidColorBrush(Color.FromRgb(0, 123, 255)); // Màu xanh dương

                // Đổ dữ liệu cũ vào form
                txtName.Text = emp.FullName;
                txtPos.Text = emp.Position;
                txtCard.Text = emp.CardNumber;
                chkActive.IsChecked = emp.IsActive;

                // Hiện Modal
                modalOverlay.Visibility = Visibility.Visible;
                txtName.Focus();
            }
        }

        // 3. ĐÓNG MODAL
        private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
        {
            modalOverlay.Visibility = Visibility.Collapsed;
        }

        // 4. LƯU DỮ LIỆU (NÚT SAVE TRONG MODAL)
        private void BtnSaveModal_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Vui lòng nhập tên nhân viên!");
                txtName.Focus();
                return;
            }

            using (var db = new AppDbContext())
            {
                string card = txtCard.Text.Trim();

                // Check trùng mã thẻ (Trừ chính mình nếu đang sửa)
                int currentId = _editingEmp?.EmpID ?? 0;
                if (!string.IsNullOrEmpty(card) && db.Employees.Any(x => x.CardNumber == card && x.EmpID != currentId))
                {
                    MessageBox.Show("Mã thẻ này đã thuộc về nhân viên khác!");
                    txtCard.SelectAll();
                    txtCard.Focus();
                    return;
                }

                if (_editingEmp == null)
                {
                    // === THÊM MỚI ===
                    var newEmp = new Employee
                    {
                        FullName = txtName.Text,
                        Position = txtPos.Text,
                        CardNumber = card,
                        IsActive = chkActive.IsChecked == true
                    };
                    db.Employees.Add(newEmp);
                }
                else
                {
                    // === CẬP NHẬT ===
                    var dbEmp = db.Employees.Find(_editingEmp.EmpID);
                    if (dbEmp != null)
                    {
                        dbEmp.FullName = txtName.Text;
                        dbEmp.Position = txtPos.Text;
                        dbEmp.CardNumber = card;
                        dbEmp.IsActive = chkActive.IsChecked == true;
                    }
                }

                db.SaveChanges();
            }

            // Đóng modal và reload
            modalOverlay.Visibility = Visibility.Collapsed;
            LoadEmployees();
            LoadFilterData(); // Cập nhật dropdown bên tab kia

            // Thông báo nhỏ (tùy chọn)
            // ShowToast("Đã lưu thành công"); 
        }

        // 5. XÓA CỨNG
        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Employee emp)
            {
                if (MessageBox.Show($"CẢNH BÁO QUAN TRỌNG:\n\n" +
                                    $"Bạn đang chọn XÓA VĨNH VIỄN nhân viên: {emp.FullName}\n" +
                                    $"- Hồ sơ nhân viên sẽ bị mất hoàn toàn.\n" +
                                    $"- Toàn bộ LỊCH SỬ CHẤM CÔNG cũng sẽ bị xóa sạch.\n\n" +
                                    $"Bạn có chắc chắn muốn thực hiện không?",
                                    "Xác nhận xóa dữ liệu",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Stop) == MessageBoxResult.Yes)
                {
                    using (var db = new AppDbContext())
                    {
                        var dbEmp = db.Employees.Find(emp.EmpID);
                        if (dbEmp != null)
                        {
                            // Xóa cascade: TimeLog trước
                            var logs = db.TimeLogs.Where(t => t.EmpID == emp.EmpID);
                            db.TimeLogs.RemoveRange(logs);

                            // Xóa Employee
                            db.Employees.Remove(dbEmp);
                            db.SaveChanges();

                            LoadEmployees();
                            LoadFilterData();
                            LoadTimeLogs();
                        }
                    }
                }
            }
        }

        // --- TAB 2: LỊCH SỬ CHẤM CÔNG (Logic cũ giữ nguyên) ---

        private void LoadFilterData()
        {
            using (var db = new AppDbContext())
            {
                var list = db.Employees.ToList();
                list.Insert(0, new Employee { EmpID = 0, FullName = "-- Tất cả nhân viên --" });
                cboEmpFilter.ItemsSource = list;
                cboEmpFilter.SelectedIndex = 0;
            }
        }

        private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoaded) LoadTimeLogs();
        }

        private void LoadTimeLogs()
        {
            DateTime fromDate = dpFrom.SelectedDate ?? DateTime.MinValue;
            DateTime toDate = dpTo.SelectedDate ?? DateTime.MaxValue;
            toDate = new DateTime(toDate.Year, toDate.Month, toDate.Day, 23, 59, 59);

            int selectedEmpId = (int?)cboEmpFilter.SelectedValue ?? 0;

            using (var db = new AppDbContext())
            {
                var query = db.TimeLogs.Include(t => t.Employee).AsQueryable();
                query = query.Where(t => t.CheckInTime >= fromDate && t.CheckInTime <= toDate);
                if (selectedEmpId > 0) query = query.Where(t => t.EmpID == selectedEmpId);

                dgTimeLogs.ItemsSource = query.OrderByDescending(t => t.CheckInTime).ToList();
            }
        }
        private void BtnViewStats_Click(object sender, RoutedEventArgs e)
        {
            CalculateStats();
        }

        // Hàm tính toán và hiển thị lên bảng
        private List<EmployeeReportDto> CalculateStats()
        {
            // Lấy ngày từ DatePicker
            DateTime from = dpStatFrom.SelectedDate ?? DateTime.MinValue;
            DateTime to = dpStatTo.SelectedDate ?? DateTime.MaxValue;
            // Chỉnh về cuối ngày (23:59:59)
            to = new DateTime(to.Year, to.Month, to.Day, 23, 59, 59);

            using (var db = new AppDbContext())
            {
                var employees = db.Employees.ToList();
                var logs = db.TimeLogs
                    .Where(t => t.CheckInTime >= from && t.CheckInTime <= to)
                    .ToList();

                var reportList = new List<EmployeeReportDto>();

                foreach (var emp in employees)
                {
                    var empLogs = logs.Where(l => l.EmpID == emp.EmpID).ToList();
                    if (empLogs.Count == 0) continue;

                    double totalHours = 0;
                    foreach (var l in empLogs)
                    {
                        if (l.CheckOutTime.HasValue)
                            totalHours += (l.CheckOutTime.Value - l.CheckInTime).TotalHours;
                    }

                    reportList.Add(new EmployeeReportDto
                    {
                        EmpName = emp.FullName,
                        Position = emp.Position ?? "",
                        TotalShifts = empLogs.Count,
                        TotalHours = totalHours,
                        Logs = empLogs
                    });
                }

                // Gán dữ liệu vào bảng NGAY TẠI ĐÂY
                dgStats.ItemsSource = reportList;

                return reportList;
            }
        }

        // NÚT XUẤT BÁO CÁO (Nút Tím)
        private void BtnExportReport_Click(object sender, RoutedEventArgs e)
        {
            var modal = new ExportTimeLogWindow();
            if (modal.ShowDialog() == true)
            {
                // 1. Cập nhật lại DatePicker
                dpStatFrom.SelectedDate = modal.FromDate;
                dpStatTo.SelectedDate = modal.ToDate;

                // 2. Tính toán dữ liệu
                var reportData = CalculateStats();

                if (reportData.Count == 0)
                {
                    MessageBox.Show("Không có dữ liệu chấm công trong khoảng thời gian này!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 3. Chọn nơi lưu file
                SaveFileDialog dlg = new SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    FileName = $"BaoCao_{modal.FromDate:ddMM}_{modal.ToDate:ddMM}.xlsx"
                };

                if (dlg.ShowDialog() == true)
                {
                    // --- BẮT ĐẦU SỬA TỪ ĐÂY (THÊM TRY-CATCH) ---
                    try
                    {
                        // Gọi service để xuất file
                        Services.ExcelService.ExportComprehensiveReport(reportData, modal.FromDate, modal.ToDate, dlg.FileName);

                        // Thông báo thành công
                        MessageBox.Show("Xuất báo cáo thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);

                        // (Tùy chọn) Tự động mở file sau khi xuất xong
                        // System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dlg.FileName, UseShellExecute = true });
                    }
                    catch (System.IO.IOException)
                    {
                        // Bắt lỗi file đang mở
                        MessageBox.Show("File này đang được mở bởi một chương trình khác (Ví dụ: Excel).\n\nVui lòng ĐÓNG FILE trước khi lưu đè!",
                                        "Không thể lưu file", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch (Exception ex)
                    {
                        // Bắt các lỗi khác (ví dụ hết ổ cứng, không có quyền ghi...)
                        MessageBox.Show($"Đã xảy ra lỗi khi xuất file:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    // --- KẾT THÚC SỬA ---
                }
            }
        }
        // Hàm sự kiện mới: Tự động chạy khi chọn ngày
        private void OnStatFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            // Chỉ chạy khi page đã load xong để tránh lỗi null lúc khởi tạo
            if (this.IsLoaded)
            {
                CalculateStats();
            }
        }
    }
}