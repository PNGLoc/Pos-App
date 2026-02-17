using System;
using System.Collections.Concurrent;
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
using PosSystem.Main.Helpers;

namespace PosSystem.Main.Services
{
    public static class PrintService
    {
        public static event Action<string>? PrintFailed;
        private static readonly ConcurrentDictionary<string, object> PrinterLocks = new ConcurrentDictionary<string, object>();
        // ============================================================
        // PHẦN 1: CÁC HÀM GỬI DỮ LIỆU CƠ BẢN (CORE) - GIỮ NGUYÊN
        // ============================================================

        private static bool SendBytesToPrinter(Printer printer, List<byte> byteList, string context)
        {
            try
            {
                List<byte> finalBytes;
                int beepCount = printer.BeepCount;
                if (beepCount < 0) beepCount = 0;
                if (beepCount > 3) beepCount = 3;

                if (beepCount > 0)
                {
                    var buzzerCmd = EscPos.BuzzerTimes(beepCount);

                    // Nếu job đã có Init (ESC @) ở đầu, chèn buzzer ngay sau Init
                    if (byteList.Count >= 2 && byteList[0] == 0x1B && byteList[1] == 0x40)
                    {
                        finalBytes = new List<byte>(byteList.Count + buzzerCmd.Length);
                        finalBytes.Add(byteList[0]);
                        finalBytes.Add(byteList[1]);
                        finalBytes.AddRange(buzzerCmd);
                        finalBytes.AddRange(byteList.Skip(2));
                    }
                    else
                    {
                        finalBytes = new List<byte>(byteList.Count + buzzerCmd.Length);
                        finalBytes.AddRange(buzzerCmd);
                        finalBytes.AddRange(byteList);
                    }
                }
                else
                {
                    finalBytes = byteList;
                }

                byte[] data = finalBytes.ToArray();
                bool success;
                var lockKey = GetPrinterLockKey(printer);
                var lockObj = PrinterLocks.GetOrAdd(lockKey, _ => new object());
                lock (lockObj)
                {
                    success = TrySend(printer, data);
                }
                if (!success)
                {
                    LogPrintFailure(context, printer, "SendBytesToPrinter failed");
                }
                return success;
            }
            catch (Exception ex)
            {
                LogPrintFailure(context, printer, ex.Message);
                return false;
            }
        }

        private static bool TrySend(Printer printer, byte[] data)
        {
            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                bool ok = printer.ConnectionType == "LAN"
                    ? PrintLan(printer.ConnectionString, data)
                    : PrintUsb(printer.ConnectionString, data);

                if (ok) return true;
                System.Threading.Thread.Sleep(200 * attempt);
            }
            return false;
        }

        private static void LogPrintFailure(string context, Printer printer, string details)
        {
            try
            {
                var root = AppPaths.DataRoot;
                var path = System.IO.Path.Combine(root, "print_failures.log");
                var name = printer.PrinterName ?? "Unknown";
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {context} | {name} | {printer.ConnectionType} | {details}";
                System.IO.File.AppendAllText(path, line + Environment.NewLine);
                PrintFailed?.Invoke($"{context} | {name}");
            }
            catch { }
        }

        private static string GetPrinterLockKey(Printer printer)
        {
            var type = (printer.ConnectionType ?? string.Empty).Trim();
            var conn = (printer.ConnectionString ?? string.Empty).Trim();
            return $"{type}|{conn}".ToLowerInvariant();
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
            SendBytesToPrinter(printer, buffer, "PrintTest");
        }

        // 1. HÀM IN BILL (HÓA ĐƠN)
        // 1. HÀM IN BILL (HÓA ĐƠN) - ĐÃ SỬA GỘP MÓN
        public static void PrintBill(long orderId, bool isProvisional = false)
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

                // [NEW] Inject QR Code if Transfer
                if (order.PaymentMethod == "Transfer" && elements != null)
                {
                    // Check if QR already exists to avoid duplicate
                    if (!elements.Any(e => e.ElementType == "QRCode"))
                    {
                        elements.Add(new PrintElement
                        {
                            ElementType = "QRCode",
                            Content = "qr_code.png", // File name in Images folder
                            Align = "Center",
                            ImageHeight = 250,
                            IsVisible = true
                        });
                    }
                }

                System.Drawing.Bitmap? rendered = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var template = new Templates.BillTemplate();
                        template.SetData(order, elements, order.PaymentMethod, isProvisional);

                        int width = printer.PaperSize == 58 ? 380 : 550;
                        rendered = EscPosImageHelper.RenderVisualToBitmap(template, width);
                    }
                    catch (Exception ex) { Console.WriteLine("Lỗi render bill: " + ex.Message); }
                });

                if (rendered == null) return;

                try
                {
                    using (rendered)
                    {
                        byte[] imgBytes = EscPosImageHelper.ConvertBitmapToEscPosBytes(rendered);
                        List<byte> cmd = new List<byte>();
                        cmd.AddRange(EscPos.Init);
                        cmd.AddRange(EscPos.AlignCenter);
                        cmd.AddRange(imgBytes);
                        cmd.AddRange(Encoding.ASCII.GetBytes("\n\n\n"));
                        cmd.AddRange(EscPos.CutPaper);
                        SendBytesToPrinter(printer, cmd, $"PrintBill:{order.OrderID}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Lỗi in bill: " + ex.Message);
                }
            }
        }

        // 2. HÀM IN BẾP - ĐÃ SỬA GỘP MÓN
        public static bool PrintKitchen(Order orderInfo, List<OrderDetail> itemsToPrint, int batchNumber, string senderName = "")
        {
            if (itemsToPrint == null || !itemsToPrint.Any()) return true;

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

                var assignedItems = groupedItemsToPrint
                    .Where(d => d.Dish?.Category?.PrinterID != null)
                    .ToList();
                var unassignedItems = groupedItemsToPrint
                    .Where(d => d.Dish?.Category?.PrinterID == null)
                    .ToList();

                // Gom nhóm theo Máy in (Dựa vào PrinterID của Danh mục món)
                var printerGroups = assignedItems
                    .GroupBy(d => d.Dish!.Category!.PrinterID)
                    .ToList();

                var fallbackPrinters = db.Printers.Where(p => p.IsActive && !p.IsBillPrinter).ToList();
                bool allOk = true;

                if (!printerGroups.Any())
                {
                    if (!fallbackPrinters.Any())
                    {
                        LogPrintFailure($"PrintKitchen:{orderInfo.OrderID}", new Printer { PrinterName = "(none)", ConnectionType = "N/A" }, "No active kitchen printer");
                        return false;
                    }

                    foreach (var printer in fallbackPrinters)
                    {
                        if (!PrintKitchenToPrinter(orderInfo, groupedItemsToPrint, batchNumber, elements, senderName, printer))
                        {
                            allOk = false;
                        }
                    }
                    return allOk;
                }

                foreach (var group in printerGroups)
                {
                    if (group.Key == null) continue;
                    int printerId = group.Key.Value;

                    var printer = db.Printers.Find(printerId);
                    if (printer == null || !printer.IsActive)
                    {
                        LogPrintFailure($"PrintKitchen:{orderInfo.OrderID}", new Printer { PrinterName = "(missing)", ConnectionType = "N/A" }, $"Inactive printer {printerId}");
                        allOk = false;
                        continue;
                    }

                    if (!PrintKitchenToPrinter(orderInfo, group.ToList(), batchNumber, elements, senderName, printer))
                    {
                        allOk = false;
                    }
                }

                if (unassignedItems.Any())
                {
                    if (!fallbackPrinters.Any())
                    {
                        LogPrintFailure($"PrintKitchen:{orderInfo.OrderID}", new Printer { PrinterName = "(none)", ConnectionType = "N/A" }, "No fallback printer for unassigned items");
                        allOk = false;
                    }
                    else
                    {
                        foreach (var printer in fallbackPrinters)
                        {
                            if (!PrintKitchenToPrinter(orderInfo, unassignedItems, batchNumber, elements, senderName, printer))
                            {
                                allOk = false;
                            }
                        }
                    }
                }

                return allOk;
            }
        }

        private static bool PrintKitchenToPrinter(Order orderInfo, List<OrderDetail> items, int batchNumber, List<PrintElement>? elements, string senderName, Printer printer)
        {
            if (items == null || items.Count == 0) return true;

            var filteredOrder = new Order
            {
                OrderID = orderInfo.OrderID,
                Table = orderInfo.Table,
                OrderTime = DateTime.Now,
                OrderDetails = items
            };

            System.Drawing.Bitmap? rendered = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    var template = new Templates.KitchenTemplate();
                    template.SetData(filteredOrder, batchNumber, elements, senderName);

                    int width = printer.PaperSize == 58 ? 380 : 550;
                    rendered = EscPosImageHelper.RenderVisualToBitmap(template, width);
                }
                catch (Exception ex) { Console.WriteLine("Lỗi render bếp: " + ex.Message); }
            });

            if (rendered == null)
            {
                LogPrintFailure($"PrintKitchen:{orderInfo.OrderID}", printer, "Render failed");
                return false;
            }

            try
            {
                using (rendered)
                {
                    byte[] imgBytes = EscPosImageHelper.ConvertBitmapToEscPosBytes(rendered);
                    List<byte> cmd = new List<byte>();
                    cmd.AddRange(EscPos.Init);
                    cmd.AddRange(EscPos.AlignCenter);
                    cmd.AddRange(imgBytes);
                    cmd.AddRange(Encoding.ASCII.GetBytes("\n\n\n"));
                    cmd.AddRange(EscPos.CutPaper);
                    var ok = SendBytesToPrinter(printer, cmd, $"PrintKitchen:{orderInfo.OrderID}");
                    return ok;
                }
            }
            catch (Exception ex)
            {
                LogPrintFailure($"PrintKitchen:{orderInfo.OrderID}", printer, ex.Message);
                return false;
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
                System.Drawing.Bitmap? rendered = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var template = new Templates.MoveTableTemplate();
                        template.SetData(oldTableName, newTableName);

                        int width = printer.PaperSize == 58 ? 380 : 550;
                        rendered = EscPosImageHelper.RenderVisualToBitmap(template, width);
                    }
                    catch (Exception ex) { Console.WriteLine($"Lỗi render thông báo chuyển bàn: {ex.Message}"); }
                });

                if (rendered == null) return;

                try
                {
                    using (rendered)
                    {
                        byte[] imgBytes = EscPosImageHelper.ConvertBitmapToEscPosBytes(rendered);
                        List<byte> cmd = new List<byte>();
                        cmd.AddRange(EscPos.Init);
                        cmd.AddRange(EscPos.AlignCenter);
                        cmd.AddRange(imgBytes);
                        cmd.AddRange(Encoding.ASCII.GetBytes("\n\n\n"));
                        cmd.AddRange(EscPos.CutPaper);
                        SendBytesToPrinter(printer, cmd, "PrintMoveTableNotification");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lỗi in thông báo chuyển bàn: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi in thông báo chuyển bàn: {ex.Message}");
            }
        }
    }
}