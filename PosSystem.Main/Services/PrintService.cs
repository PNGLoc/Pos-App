using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
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
        // PHẦN 1: CÁC HÀM GỬI DỮ LIỆU CƠ BẢN (CORE)
        // ============================================================

        private static bool SendBytesToPrinter(Printer printer, List<byte> byteList)
        {
            try
            {
                byte[] data = byteList.ToArray();

                if (printer.ConnectionType == "LAN")
                {
                    return PrintLan(printer.ConnectionString, data);
                }
                else
                {
                    return PrintUsb(printer.ConnectionString, data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }
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
        // PHẦN 2: CÁC HÀM NGHIỆP VỤ
        // ============================================================

        public static void PrintTest(Printer printer)
        {
            string pName = printer.PrinterName ?? "Unknown Printer";
            string content =
                "\n" +
                "********************************\n" +
                "      KET NOI THANH CONG!       \n" +
                "********************************\n" +
                $"May: {VietnameseHelper.RemoveSign4VietnameseString(pName)}\n" +
                $"Loai: {printer.ConnectionType}\n" +
                "--------------------------------\n\n\n";

            List<byte> buffer = new List<byte>();
            buffer.AddRange(Encoding.ASCII.GetBytes(content));
            buffer.AddRange(EscPos.CutPaper);

            SendBytesToPrinter(printer, buffer);
        }

        // --- SỬA LỖI: Đổi int orderId -> long orderId ---
        public static void PrintBill(long orderId)
        {
            using (var db = new AppDbContext())
            {
                var order = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                    .Include(o => o.Account)
                    .FirstOrDefault(o => o.OrderID == orderId);

                if (order == null) return;

                var printer = db.Printers.FirstOrDefault(p => p.IsBillPrinter && p.IsActive);
                if (printer == null) return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var template = new Templates.BillTemplate();
                        template.SetData(order);

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
                    catch (Exception ex)
                    {
                        MessageBox.Show("Lỗi in bill: " + ex.Message);
                    }
                });
            }
        }

        // --- SỬA LỖI: Đổi int orderId -> long orderId ---
        public static void PrintKitchen(long orderId, int batchNumber = 0)
        {
            using (var db = new AppDbContext())
            {
                var order = db.Orders
                    .Include(o => o.Table)
                    .Include(o => o.OrderDetails).ThenInclude(d => d.Dish).ThenInclude(cat => cat!.Category)
                    .FirstOrDefault(o => o.OrderID == orderId);

                if (order == null) return;

                // Lọc món theo đợt (batchNumber)
                var itemsToPrint = order.OrderDetails.Where(d => d.KitchenBatch == batchNumber).ToList();
                if (!itemsToPrint.Any()) return;

                // Gom nhóm theo PrinterID
                var printerGroups = itemsToPrint
                    .Where(d => d.Dish?.Category?.PrinterID != null)
                    .GroupBy(d => d.Dish!.Category!.PrinterID)
                    .ToList();

                foreach (var group in printerGroups)
                {
                    if (group.Key == null) continue; // Bỏ qua nếu null
                    int printerId = group.Key.Value;

                    var printer = db.Printers.Find(printerId);
                    if (printer == null || !printer.IsActive) continue;

                    List<byte> buffer = new List<byte>();
                    buffer.AddRange(EscPos.Init);
                    buffer.AddRange(EscPos.AlignCenter);
                    buffer.AddRange(EscPos.SizeDoubleHeight);
                    buffer.AddRange(EscPos.BoldOn);

                    string title = batchNumber > 0 ? $"PHIEU BEP - DOT {batchNumber}\n" : "PHIEU CHE BIEN\n";
                    buffer.AddRange(Encoding.ASCII.GetBytes(title));

                    buffer.AddRange(EscPos.SizeNormal);
                    buffer.AddRange(EscPos.AlignLeft);

                    string tableName = order.Table?.TableName ?? "Mang ve";
                    buffer.AddRange(Encoding.ASCII.GetBytes($"Ban: {VietnameseHelper.RemoveSign4VietnameseString(tableName)} (#{order.OrderID})\n"));
                    buffer.AddRange(Encoding.ASCII.GetBytes($"Gio: {DateTime.Now:HH:mm}\n"));
                    buffer.AddRange(Encoding.ASCII.GetBytes("--------------------------------\n"));

                    buffer.AddRange(EscPos.SizeDoubleHeight);
                    foreach (var item in group)
                    {
                        // Kiểm tra null an toàn cho Dish
                        string dishName = item.Dish?.DishName ?? "Mon khong ten";
                        string dName = VietnameseHelper.RemoveSign4VietnameseString(dishName);

                        buffer.AddRange(Encoding.ASCII.GetBytes($"{item.Quantity} x {dName}\n"));

                        if (!string.IsNullOrEmpty(item.Note))
                        {
                            buffer.AddRange(EscPos.SizeNormal);
                            buffer.AddRange(Encoding.ASCII.GetBytes($"   (Note: {VietnameseHelper.RemoveSign4VietnameseString(item.Note)})\n"));
                            buffer.AddRange(EscPos.SizeDoubleHeight);
                        }
                    }

                    buffer.AddRange(EscPos.SizeNormal);
                    buffer.AddRange(Encoding.ASCII.GetBytes("\n\n\n"));
                    buffer.AddRange(EscPos.CutPaper);

                    SendBytesToPrinter(printer, buffer);
                }
            }
        }

        // In thông báo cập nhật (Thêm/Hủy)
        public static void PrintKitchenNotification(Printer printer, string tableName, string dishName, int quantityChange, string note = "")
        {
            try
            {
                List<byte> buffer = new List<byte>();
                buffer.AddRange(EscPos.Init);
                buffer.AddRange(EscPos.AlignCenter);
                buffer.AddRange(EscPos.SizeDoubleHeight);
                buffer.AddRange(EscPos.BoldOn);

                string title = quantityChange > 0 ? "PHIEU THEM MON" : "PHIEU HUY MON";
                buffer.AddRange(Encoding.ASCII.GetBytes(title + "\n"));

                buffer.AddRange(EscPos.SizeNormal);
                buffer.AddRange(Encoding.ASCII.GetBytes("--------------------------------\n"));
                buffer.AddRange(EscPos.AlignLeft);

                string tName = VietnameseHelper.RemoveSign4VietnameseString(tableName ?? "");
                buffer.AddRange(Encoding.ASCII.GetBytes($"Ban: {tName}\n"));
                buffer.AddRange(Encoding.ASCII.GetBytes($"Gio: {DateTime.Now:HH:mm}\n"));

                buffer.AddRange(EscPos.SizeDoubleHeight);
                string action = quantityChange > 0 ? "THEM: " : "HUY: ";
                string dName = VietnameseHelper.RemoveSign4VietnameseString(dishName ?? "");

                buffer.AddRange(Encoding.ASCII.GetBytes($"{action}{Math.Abs(quantityChange)} x {dName}\n"));

                if (!string.IsNullOrEmpty(note))
                {
                    buffer.AddRange(EscPos.SizeNormal);
                    buffer.AddRange(Encoding.ASCII.GetBytes($"   (Ly do: {VietnameseHelper.RemoveSign4VietnameseString(note)})\n"));
                }

                buffer.AddRange(EscPos.SizeNormal);
                buffer.AddRange(Encoding.ASCII.GetBytes("\n\n\n"));
                buffer.AddRange(EscPos.CutPaper);

                SendBytesToPrinter(printer, buffer);
            }
            catch { }
        }

        // In danh sách các thay đổi (Batch Updates)
        // In danh sách thay đổi kèm Số Đợt
        // In danh sách thay đổi kèm Số Đợt
        public static void PrintKitchenUpdates(Printer printer, string tableName, int batchNumber, dynamic changesList)
        {
            try
            {
                List<byte> buffer = new List<byte>();
                buffer.AddRange(EscPos.Init);
                buffer.AddRange(EscPos.AlignCenter);
                buffer.AddRange(EscPos.SizeDoubleHeight);
                buffer.AddRange(EscPos.BoldOn);

                // TIÊU ĐỀ
                string title = batchNumber > 0 ? $"PHIEU BEP - DOT {batchNumber}\n" : "PHIEU BEP\n";
                buffer.AddRange(Encoding.ASCII.GetBytes(title));

                buffer.AddRange(EscPos.SizeNormal);
                buffer.AddRange(EscPos.AlignLeft);
                string tName = VietnameseHelper.RemoveSign4VietnameseString(tableName ?? "Ban ???");
                buffer.AddRange(Encoding.ASCII.GetBytes($"Ban: {tName}\n"));
                buffer.AddRange(Encoding.ASCII.GetBytes($"Gio: {DateTime.Now:HH:mm}\n"));
                buffer.AddRange(Encoding.ASCII.GetBytes("--------------------------------\n"));

                buffer.AddRange(EscPos.SizeDoubleHeight);

                foreach (var item in changesList)
                {
                    string dName = VietnameseHelper.RemoveSign4VietnameseString(item.DishName);
                    int diff = (int)item.Diff; // Ép kiểu dynamic về int
                    string note = item.Note;

                    if (diff > 0)
                    {
                        buffer.AddRange(Encoding.ASCII.GetBytes($"THEM: {diff} x {dName}\n"));
                    }
                    else if (diff < 0)
                    {
                        buffer.AddRange(Encoding.ASCII.GetBytes($"HUY : {Math.Abs(diff)} x {dName}\n"));
                    }

                    if (!string.IsNullOrEmpty(note))
                    {
                        buffer.AddRange(EscPos.SizeNormal);
                        buffer.AddRange(Encoding.ASCII.GetBytes($"  (Note: {VietnameseHelper.RemoveSign4VietnameseString(note)})\n"));
                        buffer.AddRange(EscPos.SizeDoubleHeight);
                    }
                }

                buffer.AddRange(EscPos.SizeNormal);
                buffer.AddRange(Encoding.ASCII.GetBytes("\n\n\n"));
                buffer.AddRange(EscPos.CutPaper);

                SendBytesToPrinter(printer, buffer);
            }
            catch (Exception ex)
            {
                // Ghi log hoặc bỏ qua nếu in lỗi
                System.Diagnostics.Debug.WriteLine("Loi in bep: " + ex.Message);
            }
        }
    }
}