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
        // Hàm in bếp mới: Nhận trực tiếp danh sách itemsToPrint (bao gồm cả món Hủy có SL âm)
        public static void PrintKitchen(Order orderInfo, List<OrderDetail> itemsToPrint, int batchNumber)
        {
            if (itemsToPrint == null || !itemsToPrint.Any()) return;

            using (var db = new AppDbContext())
            {
                // 1. Gom nhóm theo Máy in (Dựa vào PrinterID của Category)
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

                    // 2. Tạo Order ảo chứa danh sách món của máy in này
                    var filteredOrder = new Order
                    {
                        OrderID = orderInfo.OrderID,
                        Table = orderInfo.Table, // Lấy thông tin bàn
                        OrderTime = DateTime.Now,
                        OrderDetails = group.ToList() // Danh sách món (có thể có số âm)
                    };

                    // 3. Render và In
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var template = new Templates.KitchenTemplate();
                            template.SetData(filteredOrder, batchNumber); // Template đã sửa ở Bước 1

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

                                SendBytesToPrinter(printer, cmd); // Hàm gửi cơ bản
                            }
                        }
                        catch (Exception ex) { MessageBox.Show("Lỗi in bếp: " + ex.Message); }
                    });
                }
            }
        }

    }
}