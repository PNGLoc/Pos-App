using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json; // Cần thêm dòng này để đọc cấu hình in
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main.Services
{
    public static class PrintService
    {
        // ============================================================
        // PHẦN 1: CÁC HÀM GỬI DỮ LIỆU CƠ BẢN (CORE) - GIỮ NGUYÊN
        // ============================================================

        private static bool SendBytesToPrinter(Printer printer, List<byte> byteList)
        {
            try
            {
                byte[] data = byteList.ToArray();
                if (printer.ConnectionType == "LAN") return PrintLan(printer.ConnectionString, data);
                else return PrintUsb(printer.ConnectionString, data);
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); return false; }
        }

        public static bool PrintLan(string ipAddress, byte[] data)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    var result = client.BeginConnect(ipAddress, 9100, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3));
                    if (!success) return false;
                    client.EndConnect(result);
                    using (NetworkStream stream = client.GetStream())
                    {
                        stream.Write(data, 0, data.Length);
                        stream.Flush();
                    }
                }
                return true;
            }
            catch { return false; }
        }

        public static bool PrintUsb(string printerName, byte[] data)
        {
            try
            {
                int length = data.Length;
                IntPtr pUnmanagedBytes = Marshal.AllocCoTaskMem(length);
                Marshal.Copy(data, 0, pUnmanagedBytes, length);
                bool success = RawPrinterHelper.SendBytesToPrinter(printerName, pUnmanagedBytes, length);
                Marshal.FreeCoTaskMem(pUnmanagedBytes);
                return success;
            }
            catch { return false; }
        }

        // ============================================================
        // PHẦN 2: CÁC HÀM NGHIỆP VỤ - ĐÃ SỬA LỖI LOGIC
        // ============================================================

        public static void PrintTest(Printer printer)
        {
            string pName = printer.PrinterName ?? "Unknown Printer";
            string content = $"\n*** KET NOI THANH CONG! ***\nMay: {pName}\nLoai: {printer.ConnectionType}\n----------------\n\n\n";
            List<byte> buffer = new List<byte>();
            buffer.AddRange(Encoding.ASCII.GetBytes(content));
            buffer.AddRange(EscPos.CutPaper);
            SendBytesToPrinter(printer, buffer);
        }

        // 1. HÀM IN BILL (HÓA ĐƠN)
        // 1. HÀM IN BILL (HÓA ĐƠN) - ĐÃ SỬA GỘP MÓN
        public static void PrintBill(long orderId)
        {
            using (var db = new AppDbContext())
            {
                var order = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                    .Include(o => o.Account)
                    .Include(o => o.Table)
                    .FirstOrDefault(o => o.OrderID == orderId);

                if (order == null) return;

                // --- [ĐOẠN CODE MỚI: GỘP MÓN TRƯỚC KHI IN] ---
                // Logic giống hệt MainWindow: Gộp theo Món + Trạng thái + Ghi chú
                var groupedDetails = order.OrderDetails
                    .GroupBy(d => new
                    {
                        d.DishID,
                        d.ItemStatus,
                        Note = (d.Note ?? "").Trim()
                    })
                    .Select(g => new OrderDetail
                    {
                        // Lấy thông tin từ món đầu tiên trong nhóm
                        DishID = g.Key.DishID,
                        Dish = g.First().Dish, // Quan trọng: Phải giữ object Dish để lấy tên món
                        UnitPrice = g.First().UnitPrice,
                        Note = g.Key.Note,
                        ItemStatus = g.Key.ItemStatus,

                        // Cộng dồn số lượng và thành tiền
                        Quantity = g.Sum(x => x.Quantity),
                        TotalAmount = g.Sum(x => x.TotalAmount),

                        // Các thuộc tính phụ (nếu cần)
                        PrintedQuantity = g.Sum(x => x.PrintedQuantity)
                    })
                    .OrderBy(d => d.ItemStatus == "New" ? 0 : 1) // Sắp xếp: Mới trước, Cũ sau
                    .ThenBy(d => d.Dish?.DishName)
                    .ToList();

                // Gán danh sách đã gộp vào Order (Chỉ gán trong bộ nhớ để in, không lưu DB)
                order.OrderDetails = groupedDetails;
                // ----------------------------------------------

                var printer = db.Printers.FirstOrDefault(p => p.IsBillPrinter && p.IsActive);
                if (printer == null) return;

                var layoutConfig = db.PrintTemplates.FirstOrDefault(t => t.TemplateType == "Bill" && t.IsActive);
                List<PrintElement> elements = null;
                if (layoutConfig != null && !string.IsNullOrEmpty(layoutConfig.TemplateContentJson))
                {
                    try { elements = JsonSerializer.Deserialize<List<PrintElement>>(layoutConfig.TemplateContentJson); } catch { }
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var template = new Templates.BillTemplate();
                        template.SetData(order, elements, order.PaymentMethod);

                        int width = printer.PaperSize == 58 ? 380 : 550;
                        using (var bmp = EscPosImageHelper.RenderVisualToBitmap(template, width))
                        {
                            byte[] imgBytes = EscPosImageHelper.ConvertBitmapToEscPosBytes(bmp);
                            List<byte> cmd = new List<byte>();
                            cmd.AddRange(EscPos.Init);
                            cmd.AddRange(EscPos.AlignCenter);
                            cmd.AddRange(imgBytes);
                            cmd.AddRange(Encoding.ASCII.GetBytes("\n\n\n"));
                            cmd.AddRange(EscPos.CutPaper);
                            SendBytesToPrinter(printer, cmd);
                        }
                    }
                    catch (Exception ex) { MessageBox.Show("Lỗi in bill: " + ex.Message); }
                });
            }
        }

        // 2. HÀM IN BẾP - ĐÃ SỬA GỘP MÓN
        public static void PrintKitchen(Order orderInfo, List<OrderDetail> itemsToPrint, int batchNumber)
        {
            if (itemsToPrint == null || !itemsToPrint.Any()) return;

            using (var db = new AppDbContext())
            {
                var layoutConfig = db.PrintTemplates.FirstOrDefault(t => t.TemplateType == "Kitchen" && t.IsActive);
                List<PrintElement> elements = null;
                if (layoutConfig != null && !string.IsNullOrEmpty(layoutConfig.TemplateContentJson))
                {
                    try { elements = JsonSerializer.Deserialize<List<PrintElement>>(layoutConfig.TemplateContentJson); } catch { }
                }

                // --- [ĐOẠN CODE MỚI: GỘP MÓN CHO BẾP] ---
                // Trước khi chia theo máy in, ta gộp các món giống nhau lại
                // (Phòng trường hợp bấm + 2 lần tạo ra 2 object riêng lẻ)
                var groupedItemsToPrint = itemsToPrint
                    .GroupBy(d => new
                    {
                        d.DishID,
                        Note = (d.Note ?? "").Trim()
                        // Bếp in theo đợt nên không cần group theo ItemStatus, tất cả đều là New/Modified
                    })
                    .Select(g => new OrderDetail
                    {
                        DishID = g.Key.DishID,
                        Dish = g.First().Dish, // Object Dish chứa Category -> PrinterID
                        Note = g.Key.Note,
                        Quantity = g.Sum(x => x.Quantity), // Cộng dồn số lượng cần in
                        // Các field khác không quan trọng với Bếp
                    })
                    .ToList();
                // ----------------------------------------

                // Gom nhóm theo Máy in (Dựa vào PrinterID của Danh mục món)
                var printerGroups = groupedItemsToPrint
                    .Where(d => d.Dish?.Category?.PrinterID != null)
                    .GroupBy(d => d.Dish!.Category!.PrinterID)
                    .ToList();

                foreach (var group in printerGroups)
                {
                    if (group.Key == null) continue;
                    int printerId = group.Key.Value;

                    var printer = db.Printers.Find(printerId);
                    if (printer == null || !printer.IsActive) continue;

                    // Tạo Order ảo chỉ chứa các món của máy in này
                    var filteredOrder = new Order
                    {
                        OrderID = orderInfo.OrderID,
                        Table = orderInfo.Table, // Lấy tên bàn
                        OrderTime = DateTime.Now,
                        OrderDetails = group.ToList() // Danh sách món CẦN IN (Đã gộp)
                    };

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var template = new Templates.KitchenTemplate();
                            template.SetData(filteredOrder, batchNumber, elements);

                            int width = printer.PaperSize == 58 ? 380 : 550;
                            using (var bmp = EscPosImageHelper.RenderVisualToBitmap(template, width))
                            {
                                byte[] imgBytes = EscPosImageHelper.ConvertBitmapToEscPosBytes(bmp);
                                List<byte> cmd = new List<byte>();
                                cmd.AddRange(EscPos.Init);
                                cmd.AddRange(EscPos.AlignCenter);
                                cmd.AddRange(imgBytes);
                                cmd.AddRange(Encoding.ASCII.GetBytes("\n\n\n"));
                                cmd.AddRange(EscPos.CutPaper);
                                SendBytesToPrinter(printer, cmd);
                            }
                        }
                        catch (Exception ex) { MessageBox.Show("Lỗi in bếp: " + ex.Message); }
                    });
                }
            }
        }
        // 3. HÀM IN THÔNG BÁO CHUYỂN BÀN
        public static void PrintMoveTableNotification(Order orderInfo, string oldTableName, string newTableName)
        {
            if (orderInfo == null) return;

            using (var db = new AppDbContext())
            {
                // Lấy danh sách tất cả các OrderDetail của order
                var orderDetails = db.OrderDetails
                    .Include(od => od.Dish).ThenInclude(d => d.Category)
                    .Where(od => od.OrderID == orderInfo.OrderID)
                    .ToList();

                if (!orderDetails.Any()) return;

                // Gom nhóm theo PrinterID của Category
                var printerGroups = orderDetails
                    .Where(od => od.Dish?.Category?.PrinterID != null)
                    .GroupBy(od => od.Dish!.Category!.PrinterID)
                    .ToList();

                // Nếu không có nhóm nào, in cho tất cả các printer
                if (!printerGroups.Any())
                {
                    var allActivePrinters = db.Printers.Where(p => p.IsActive && !p.IsBillPrinter).ToList();
                    foreach (var printer in allActivePrinters)
                    {
                        PrintMoveNotificationToPrinter(printer, oldTableName, newTableName);
                    }
                    return;
                }

                // In thông báo cho từng máy in
                foreach (var group in printerGroups)
                {
                    if (group.Key == null) continue;
                    int printerId = group.Key.Value;

                    var printer = db.Printers.Find(printerId);
                    if (printer == null || !printer.IsActive) continue;

                    PrintMoveNotificationToPrinter(printer, oldTableName, newTableName);
                }
            }
        }

        private static void PrintMoveNotificationToPrinter(Printer printer, string oldTableName, string newTableName)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var template = new Templates.MoveTableTemplate();
                        template.SetData(oldTableName, newTableName);

                        int width = printer.PaperSize == 58 ? 380 : 550;
                        using (var bmp = EscPosImageHelper.RenderVisualToBitmap(template, width))
                        {
                            byte[] imgBytes = EscPosImageHelper.ConvertBitmapToEscPosBytes(bmp);
                            List<byte> cmd = new List<byte>();
                            cmd.AddRange(EscPos.Init);
                            cmd.AddRange(EscPos.AlignCenter);
                            cmd.AddRange(imgBytes);
                            cmd.AddRange(Encoding.ASCII.GetBytes("\n\n\n"));
                            cmd.AddRange(EscPos.CutPaper);
                            SendBytesToPrinter(printer, cmd);
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"Lỗi in thông báo chuyển bàn: {ex.Message}"); }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi in thông báo chuyển bàn: {ex.Message}");
            }
        }
    }
}