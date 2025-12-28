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
        public static void PrintBill(long orderId)
        {
            using (var db = new AppDbContext())
            {
                var order = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                    .Include(o => o.Account)
                    .Include(o => o.Table)
                    .Include(o => o.Account)
                    .FirstOrDefault(o => o.OrderID == orderId);

                if (order == null) return;

                // SỬA LỖI CS1061: Dùng IsBillPrinter thay vì PrinterType
                var printer = db.Printers.FirstOrDefault(p => p.IsBillPrinter && p.IsActive);
                if (printer == null) return;

                // Lấy cấu hình Layout từ DB
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

                        // Truyền thêm elements vào SetData
                        template.SetData(order, elements);

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

        // 2. HÀM IN BẾP
        public static void PrintKitchen(Order orderInfo, List<OrderDetail> itemsToPrint, int batchNumber)
        {
            // Kiểm tra danh sách món
            if (itemsToPrint == null || !itemsToPrint.Any()) return;

            using (var db = new AppDbContext())
            {
                // Lấy cấu hình Layout Bếp từ DB
                var layoutConfig = db.PrintTemplates.FirstOrDefault(t => t.TemplateType == "Kitchen" && t.IsActive);
                List<PrintElement> elements = null;
                if (layoutConfig != null && !string.IsNullOrEmpty(layoutConfig.TemplateContentJson))
                {
                    try { elements = JsonSerializer.Deserialize<List<PrintElement>>(layoutConfig.TemplateContentJson); } catch { }
                }

                // Gom nhóm món theo Máy in (Dựa vào PrinterID của Danh mục món)
                var printerGroups = itemsToPrint
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
                        Table = orderInfo.Table,
                        OrderTime = DateTime.Now,
                        OrderDetails = group.ToList() // Danh sách món cần in
                    };

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var template = new Templates.KitchenTemplate();

                            // SỬA LỖI CS7036: Truyền đủ 3 tham số (Order, Batch, Layout)
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
    }
}