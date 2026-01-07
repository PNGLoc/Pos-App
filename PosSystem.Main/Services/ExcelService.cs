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
        public static (int importedCount, List<string> errors) ImportDishesFromExcel(string filePath)
        {
            var errors = new List<string>();
            int importedCount = 0;

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                if (package.Workbook.Worksheets.Count == 0)
                {
                    errors.Add("File không chứa dữ liệu");
                    return (0, errors);
                }

                var worksheet = package.Workbook.Worksheets[0];
                int rowCount = worksheet.Dimension?.Rows ?? 0;

                if (rowCount < 2)
                {
                    errors.Add("File không chứa dữ liệu hàng");
                    return (0, errors);
                }

                using (var db = new AppDbContext())
                {
                    var categories = db.Categories.ToList();
                    var existingDishes = db.Dishes.ToList();

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

                            // Find category
                            var category = categories.FirstOrDefault(c =>
                                c.CategoryName.Equals(categoryName, StringComparison.OrdinalIgnoreCase));

                            if (category == null && !string.IsNullOrEmpty(categoryName))
                            {
                                errors.Add($"Dòng {row}: Danh mục '{categoryName}' không tồn tại");
                                continue;
                            }

                            // Check if dish already exists
                            if (existingDishes.Any(d => d.DishName.Equals(dishName, StringComparison.OrdinalIgnoreCase)))
                            {
                                errors.Add($"Dòng {row}: Món '{dishName}' đã tồn tại");
                                continue;
                            }

                            // Create new dish
                            var newDish = new Dish
                            {
                                DishName = dishName,
                                CategoryID = category?.CategoryID ?? 0,
                                Price = price,
                                Unit = unit,
                                DishStatus = status,
                                ImagePath = "default.png"
                            };

                            db.Dishes.Add(newDish);
                            importedCount++;
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
                        return (0, errors);
                    }
                }
            }

            return (importedCount, errors);
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
                var ws = package.Workbook.Worksheets.Add("BaoCaoTongHop");

                // 1. Tiêu đề lớn
                ws.Cells["A1:E1"].Merge = true;
                ws.Cells["A1"].Value = $"BÁO CÁO CHẤM CÔNG NHÂN VIÊN ({from:dd/MM/yyyy} - {to:dd/MM/yyyy})";
                ws.Cells["A1"].Style.Font.Size = 16;
                ws.Cells["A1"].Style.Font.Bold = true;
                ws.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                int row = 3; // Bắt đầu từ dòng 3

                foreach (var emp in reports)
                {
                    // 2. Header Từng Nhân Viên (Nền Vàng)
                    ws.Cells[row, 1, row, 5].Merge = true;
                    ws.Cells[row, 1].Value = $"👤 {emp.EmpName} ({emp.Position})  |  Tổng ca: {emp.TotalShifts}  |  Tổng giờ: {emp.TotalHoursDisplay:F2} ({emp.TotalHours:F2} giờ)";
                    ws.Cells[row, 1].Style.Font.Bold = true;
                    ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(Color.LightGoldenrodYellow);
                    ws.Cells[row, 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                    row++;

                    // 3. Header Bảng Chi Tiết
                    ws.Cells[row, 2].Value = "Ngày";
                    ws.Cells[row, 3].Value = "Giờ Vào";
                    ws.Cells[row, 4].Value = "Giờ Ra";
                    ws.Cells[row, 5].Value = "Thời lượng";

                    using (var r = ws.Cells[row, 2, row, 5])
                    {
                        r.Style.Font.Bold = true;
                        r.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }
                    row++;

                    // 4. Danh sách log chi tiết
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
                    else
                    {
                        ws.Cells[row, 2].Value = "(Không có dữ liệu)";
                        row++;
                    }

                    row++; // Dòng trống ngăn cách giữa các nhân viên
                }

                ws.Cells.AutoFitColumns();
                File.WriteAllBytes(filePath, package.GetAsByteArray());
            }
        }

    }
}
