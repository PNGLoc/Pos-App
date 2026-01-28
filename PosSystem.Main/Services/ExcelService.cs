using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main.Services
{
    // Class chứa dữ liệu thống kê để truyền vào hàm Export
    public class EmployeeReportDto
    {
        public string EmpName { get; set; } = "";
        public string Position { get; set; } = "";
        public double TotalHours { get; set; }
        public double HourlyWage { get; set; }

        public double TotalSalary => TotalHours * HourlyWage; // Tính lương

        // Dùng để hiển thị lên bảng (Format tiền Việt)
        public string TotalSalaryDisplay => TotalSalary > 0 ? TotalSalary.ToString("N0") + " đ" : "";
        public int TotalShifts { get; set; }
        public List<TimeLog> Logs { get; set; } = new List<TimeLog>();
        public string TotalHoursDisplay
        {
            get
            {
                // 1. Tính toán Giờ và Phút
                int hours = (int)TotalHours;
                int minutes = (int)Math.Round((TotalHours - hours) * 60);

                // Xử lý làm tròn (ví dụ 59.9 phút -> 60 phút -> tăng 1 giờ)
                if (minutes == 60)
                {
                    hours++;
                    minutes = 0;
                }

                // 2. Tạo chuỗi "X giờ Y phút"
                string textPart = "";
                if (hours > 0) textPart += $"{hours} giờ ";
                if (minutes > 0) textPart += $"{minutes} phút";
                if (hours == 0 && minutes == 0) textPart = "0 phút";
                // 4. Ghép lại: "6 giờ 30 phút (6.5 giờ)"
                return $"{textPart.Trim()}";
            }
        }
    }

    public class ExcelService
    {
        static ExcelService()
        {
            // Set the EPPlus license context
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        /// <summary>
        /// Export dishes to Excel file
        /// </summary>
        public static void ExportDishesToExcel(string filePath)
        {
            using (var db = new AppDbContext())
            {
                var dishes = db.Dishes.ToList();
                var categories = db.Categories.ToList();

                using (var package = new ExcelPackage())
                {
                    // Create worksheet
                    var worksheet = package.Workbook.Worksheets.Add("Danh sách món");

                    // Add headers
                    worksheet.Cells[1, 1].Value = "Mã món";
                    worksheet.Cells[1, 2].Value = "Tên món";
                    worksheet.Cells[1, 3].Value = "Danh mục";
                    worksheet.Cells[1, 4].Value = "Giá";
                    worksheet.Cells[1, 5].Value = "Đơn vị";
                    worksheet.Cells[1, 6].Value = "Trạng thái";

                    // Style header row
                    using (var headerRange = worksheet.Cells[1, 1, 1, 6])
                    {
                        headerRange.Style.Font.Bold = true;
                        headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
                    }

                    // Add data
                    int row = 2;
                    foreach (var dish in dishes)
                    {
                        var category = categories.FirstOrDefault(c => c.CategoryID == dish.CategoryID);

                        worksheet.Cells[row, 1].Value = dish.DishID;
                        worksheet.Cells[row, 2].Value = dish.DishName;
                        worksheet.Cells[row, 3].Value = category?.CategoryName ?? "";
                        worksheet.Cells[row, 4].Value = dish.Price;
                        worksheet.Cells[row, 5].Value = dish.Unit;
                        worksheet.Cells[row, 6].Value = dish.DishStatus;

                        row++;
                    }

                    // Auto-fit columns
                    worksheet.Cells.AutoFitColumns();

                    // Save file
                    FileInfo fileInfo = new FileInfo(filePath);
                    package.SaveAs(fileInfo);
                }
            }
        }

        /// <summary>
        /// Import dishes from Excel file
        /// </summary>
        public static (int addedCount, int unchangedCount, List<string> errors) ImportDishesFromExcel(string filePath)
        {
            var errors = new List<string>();
            int addedCount = 0;
            int unchangedCount = 0;

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                if (package.Workbook.Worksheets.Count == 0)
                {
                    errors.Add("File không chứa dữ liệu");
                    return (0, 0, errors);
                }

                var worksheet = package.Workbook.Worksheets[0];
                int rowCount = worksheet.Dimension?.Rows ?? 0;

                if (rowCount < 2)
                {
                    errors.Add("File không chứa dữ liệu hàng");
                    return (0, 0, errors);
                }

                using (var db = new AppDbContext())
                {
                    var categories = db.Categories.ToList();
                    var existingDishes = db.Dishes.ToList();
                    int nextCategoryOrderIndex = categories.Any() ? categories.Max(c => c.OrderIndex) + 1 : 1;

                    for (int row = 2; row <= rowCount; row++)
                    {
                        try
                        {
                            var dishName = worksheet.Cells[row, 2].Value?.ToString()?.Trim();
                            var categoryName = worksheet.Cells[row, 3].Value?.ToString()?.Trim();
                            var priceStr = worksheet.Cells[row, 4].Value?.ToString()?.Trim();
                            var unit = worksheet.Cells[row, 5].Value?.ToString()?.Trim() ?? "Cốc";
                            var status = worksheet.Cells[row, 6].Value?.ToString()?.Trim() ?? "Active";

                            // Validate required fields
                            if (string.IsNullOrEmpty(dishName))
                            {
                                errors.Add($"Dòng {row}: Tên món không được để trống");
                                continue;
                            }

                            if (!decimal.TryParse(priceStr, out decimal price))
                            {
                                errors.Add($"Dòng {row}: Giá không hợp lệ");
                                continue;
                            }

                            // Find or create category
                            if (string.IsNullOrWhiteSpace(categoryName))
                            {
                                errors.Add($"Dòng {row}: Danh mục không được để trống");
                                continue;
                            }

                            var category = categories.FirstOrDefault(c =>
                                c.CategoryName.Equals(categoryName, StringComparison.OrdinalIgnoreCase));

                            if (category == null)
                            {
                                category = new Category
                                {
                                    CategoryName = categoryName,
                                    OrderIndex = nextCategoryOrderIndex++
                                };
                                db.Categories.Add(category);
                                categories.Add(category);
                            }

                             // Check if dish already exists
                            var existingDish = existingDishes.FirstOrDefault(d => d.DishName.Equals(dishName, StringComparison.OrdinalIgnoreCase));
                            
                            if (existingDish != null)
                            {
                                // SKIP existing dish (Unchanged)
                                unchangedCount++;
                            }
                            else
                            {
                                // Create new dish
                                var newDish = new Dish
                                {
                                    DishName = dishName,
                                    Category = category,
                                    Price = price,
                                    Unit = unit,
                                    DishStatus = status,
                                    ImagePath = "default.png"
                                };

                                db.Dishes.Add(newDish);
                                addedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Dòng {row}: Lỗi - {ex.Message}");
                        }
                    }

                    try
                    {
                        db.SaveChanges();
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Lỗi khi lưu dữ liệu: {ex.Message}");
                        return (0, 0, errors);
                    }
                }
            }

            return (addedCount, unchangedCount, errors);
        }
        public static void ExportTimeLogs(List<TimeLog> logs, string filePath)
        {
            // Thiết lập License cho EPPlus (Bắt buộc với bản mới)
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("ChamCong");

                // 1. Tạo Header
                ws.Cells[1, 1].Value = "ID";
                ws.Cells[1, 2].Value = "Tên Nhân Viên"; // Đổi tiêu đề
                ws.Cells[1, 3].Value = "Ngày";
                ws.Cells[1, 4].Value = "Giờ Vào";
                ws.Cells[1, 5].Value = "Giờ Ra";
                ws.Cells[1, 6].Value = "Tổng giờ";

                // Format Header cho đẹp (In đậm, nền xám)
                using (var range = ws.Cells[1, 1, 1, 6])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }

                // 2. Đổ dữ liệu
                int row = 2;
                foreach (var log in logs)
                {
                    ws.Cells[row, 1].Value = log.LogID;

                    // --- SỬA ĐỔI QUAN TRỌNG TẠI ĐÂY ---
                    // Lấy tên từ Employee thay vì Account
                    ws.Cells[row, 2].Value = log.Employee?.FullName ?? "Không xác định";
                    ws.Cells[row, 3].Value = log.CheckInTime.ToString("dd/MM/yyyy");
                    ws.Cells[row, 4].Value = log.CheckInTime.ToString("HH:mm");
                    ws.Cells[row, 5].Value = log.CheckOutTime?.ToString("HH:mm") ?? "--:--";
                    ws.Cells[row, 6].Value = log.DurationDisplay;
                    row++;
                }

                // Tự động chỉnh độ rộng cột
                ws.Cells.AutoFitColumns();

                // Lưu file
                File.WriteAllBytes(filePath, package.GetAsByteArray());
            }
        }
        public static void ExportComprehensiveReport(List<EmployeeReportDto> reports, DateTime from, DateTime to, string filePath)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("BaoCaoLuong");

                // 1. Tiêu đề lớn
                ws.Cells["A1:G1"].Merge = true; // Mở rộng merge ra 7 cột
                ws.Cells["A1"].Value = $"BẢNG TÍNH CÔNG VÀ LƯƠNG ({from:dd/MM/yyyy} - {to:dd/MM/yyyy})";
                ws.Cells["A1"].Style.Font.Size = 16;
                ws.Cells["A1"].Style.Font.Bold = true;
                ws.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                ws.Cells["A1"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                int row = 3;

                foreach (var emp in reports)
                {
                    // 2. DÒNG TỔNG HỢP NHÂN VIÊN (MÀU VÀNG)
                    // Thiết kế: Tên | Chức vụ | Tổng Ca | Tổng Giờ | Lương/Giờ | TỔNG LƯƠNG

                    // Cột 1: Tên nhân viên
                    ws.Cells[row, 1].Value = emp.EmpName;
                    ws.Cells[row, 1].Style.Font.Bold = true;

                    // Cột 2: Chức vụ
                    ws.Cells[row, 2].Value = emp.Position;

                    // Cột 3: Tổng số ca
                    ws.Cells[row, 3].Value = $"Ca: {emp.TotalShifts}";

                    // Cột 4: Tổng giờ (Hiển thị chữ)
                    ws.Cells[row, 4].Value = emp.TotalHoursDisplay;

                    // Cột 5: Tổng giờ (Số thực - Để ẩn hoặc để kế toán check)
                    ws.Cells[row, 5].Value = emp.TotalHours;
                    ws.Cells[row, 5].Style.Numberformat.Format = "0.00";

                    // Cột 6: Mức Lương/Giờ
                    ws.Cells[row, 6].Value = emp.HourlyWage;
                    ws.Cells[row, 6].Style.Numberformat.Format = "#,##0"; // Format tiền: 20,000

                    // Cột 7: TỔNG LƯƠNG (Quan trọng nhất)
                    ws.Cells[row, 7].Value = emp.TotalSalary;
                    ws.Cells[row, 7].Style.Numberformat.Format = "#,##0";
                    ws.Cells[row, 7].Style.Font.Bold = true;
                    ws.Cells[row, 7].Style.Font.Color.SetColor(Color.DarkRed); // Chữ đỏ

                    // Format dòng tổng hợp
                    using (var range = ws.Cells[row, 1, row, 7])
                    {
                        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(Color.LightGoldenrodYellow);
                        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    }

                    row++;

                    // 3. HEADER BẢNG CHI TIẾT
                    ws.Cells[row, 2].Value = "Ngày";
                    ws.Cells[row, 3].Value = "Giờ Vào";
                    ws.Cells[row, 4].Value = "Giờ Ra";
                    ws.Cells[row, 5].Value = "Thời lượng";

                    // Thêm tiêu đề cho các cột dữ liệu tổng hợp ở trên cho dễ hiểu
                    ws.Cells[row - 1, 5].AddComment("Số giờ thực tế để tính toán", "System");
                    ws.Cells[row - 1, 6].AddComment("Mức lương cài đặt", "System");

                    using (var header = ws.Cells[row, 2, row, 5])
                    {
                        header.Style.Font.Italic = true;
                        header.Style.Font.Size = 10;
                        header.Style.Font.Color.SetColor(Color.Gray);
                        header.Style.Border.Bottom.Style = ExcelBorderStyle.Dotted;
                    }
                    row++;

                    // 4. DANH SÁCH CHI TIẾT
                    if (emp.Logs.Count > 0)
                    {
                        foreach (var log in emp.Logs)
                        {
                            ws.Cells[row, 2].Value = log.CheckInTime.ToString("dd/MM/yyyy");
                            ws.Cells[row, 3].Value = log.CheckInTime.ToString("HH:mm");
                            ws.Cells[row, 4].Value = log.CheckOutTime?.ToString("HH:mm") ?? "--:--";
                            ws.Cells[row, 5].Value = log.DurationDisplay;
                            row++;
                        }
                    }

                    row++; // Dòng trống ngăn cách
                }

                // Header cột (tự tạo header giả ở dòng 2 cho đẹp nếu muốn, hoặc chỉ cần AutoFit)
                ws.Cells[2, 1].Value = "HỌ TÊN";
                ws.Cells[2, 2].Value = "CHỨC VỤ";
                ws.Cells[2, 3].Value = "TỔNG CA";
                ws.Cells[2, 4].Value = "THỜI GIAN";
                ws.Cells[2, 5].Value = "GIỜ (SỐ)";
                ws.Cells[2, 6].Value = "LƯƠNG/H";
                ws.Cells[2, 7].Value = "THÀNH TIỀN";
                using (var r = ws.Cells[2, 1, 2, 7])
                {
                    r.Style.Font.Bold = true;
                    r.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    r.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    r.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                }

                ws.Cells.AutoFitColumns();

                // Lưu file
                File.WriteAllBytes(filePath, package.GetAsByteArray());
            }
        }

    }
}
