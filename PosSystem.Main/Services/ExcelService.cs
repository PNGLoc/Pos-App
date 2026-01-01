using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main.Services
{
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
    }
}
