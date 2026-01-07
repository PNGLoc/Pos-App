using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main
{
    public partial class TimeKeepingWindow : Window
    {
        private const int MIN_SECONDS_WAIT = 10;

        public TimeKeepingWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => txtScanInput.Focus(); // Tự focus khi mở
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); // Đóng Modal
        }

        private void TxtScanInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string code = txtScanInput.Text.Trim();
                ProcessCard(code);
                txtScanInput.Clear();
                txtScanInput.Focus();
            }
        }

        private void ProcessCard(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode)) return;

            using (var db = new AppDbContext())
            {
                var emp = db.Employees.FirstOrDefault(e => e.CardNumber == cardCode && e.IsActive);
                if (emp == null)
                {
                    ShowError($"Thẻ lạ: {cardCode}");
                    return;
                }

                var lastLog = db.TimeLogs
                    .Where(t => t.EmpID == emp.EmpID && t.CheckOutTime == null)
                    .OrderByDescending(t => t.CheckInTime)
                    .FirstOrDefault();

                DateTime now = DateTime.Now;

                if (lastLog == null)
                {
                    // Check In
                    db.TimeLogs.Add(new TimeLog { EmpID = emp.EmpID, CheckInTime = now });
                    ShowSuccess("XIN CHÀO (CHECK-IN)", emp.FullName, now, true);
                }
                else
                {
                    // Check Out
                    TimeSpan elapsed = now - lastLog.CheckInTime;
                    if (elapsed.TotalSeconds < MIN_SECONDS_WAIT)
                    {
                        int remaining = MIN_SECONDS_WAIT - (int)elapsed.TotalSeconds;
                        ShowError($"Vừa checkin, thử lại sau {remaining}s!");
                        return;
                    }
                    lastLog.CheckOutTime = now;
                    ShowSuccess("TẠM BIỆT (CHECK-OUT)", emp.FullName, now, false);
                }
                db.SaveChanges();
            }
        }

        private void ShowSuccess(string title, string name, DateTime time, bool isCheckIn)
        {
            lblStatus.Text = title;
            lblStatus.Foreground = isCheckIn ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Orange;
            lblName.Text = name;
            lblTime.Text = time.ToString("dd/MM/yyyy HH:mm:ss");
            pnlResult.Visibility = Visibility.Visible;
        }

        private void ShowError(string msg)
        {
            lblStatus.Text = "CẢNH BÁO";
            lblStatus.Foreground = System.Windows.Media.Brushes.Red;
            lblName.Text = msg;
            lblTime.Text = "";
            pnlResult.Visibility = Visibility.Visible;
        }
        // Đảm bảo đoạn này vẫn có trong TimeKeepingWindow.xaml.cs
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            txtScanInput.Focus(); // Tự động focus để quét hoặc gõ luôn
        }
    }
}