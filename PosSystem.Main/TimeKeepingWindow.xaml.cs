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
                    PlaySound("fail.wav");
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
                    PlaySound("insert.wav");
                }
                else
                {
                    // Check Out
                    TimeSpan elapsed = now - lastLog.CheckInTime;
                    if (elapsed.TotalSeconds < MIN_SECONDS_WAIT)
                    {
                        int remaining = MIN_SECONDS_WAIT - (int)elapsed.TotalSeconds;
                        ShowError($"Vừa checkin, thử lại sau {remaining}s!");
                        PlaySound("fail.wav");
                        return;
                    }
                    lastLog.CheckOutTime = now;
                    
                    // Format duration string
                    string durationStr = "";
                    if (elapsed.TotalHours >= 1)
                        durationStr = $"{(int)elapsed.TotalHours} giờ {elapsed.Minutes} phút";
                    else
                        durationStr = $"{elapsed.Minutes} phút";

                    ShowSuccess("TẠM BIỆT (CHECK-OUT)", emp.FullName, now, false, durationStr);
                    PlaySound("remove.wav");
                }
                db.SaveChanges();
            }
        }

        private void ShowSuccess(string title, string name, DateTime time, bool isCheckIn, string duration = "")
        {
            lblStatus.Text = title;
            lblStatus.Foreground = isCheckIn ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Orange;
            lblName.Text = name;
            
            if (!string.IsNullOrEmpty(duration))
            {
                lblTime.Text = $"{time:dd/MM/yyyy HH:mm:ss}\nThời gian làm: {duration}";
            }
            else
            {
                lblTime.Text = time.ToString("dd/MM/yyyy HH:mm:ss");
            }
            
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

        private void PlaySound(string fileName)
        {
            try
            {
                // 1. Try local Assets folder first
                string localPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
                if (System.IO.File.Exists(localPath))
                {
                    using (var player = new System.Media.SoundPlayer(localPath))
                    {
                        player.Play();
                    }
                    return;
                }

                // 2. Fallback to Windows System Sounds
                string winMediaPath = @"C:\Windows\Media";
                string systemSoundFile = "";

                switch (fileName.ToLower())
                {
                    case "insert.wav":
                        systemSoundFile = "Windows Hardware Insert.wav";
                        break;
                    case "remove.wav":
                        systemSoundFile = "Windows Hardware Remove.wav";
                        break;
                    case "fail.wav":
                        systemSoundFile = "Windows Critical Stop.wav"; // or "Windows Foreground.wav"
                        break;
                }

                if (!string.IsNullOrEmpty(systemSoundFile))
                {
                    string sysPath = System.IO.Path.Combine(winMediaPath, systemSoundFile);
                    if (System.IO.File.Exists(sysPath))
                    {
                        using (var player = new System.Media.SoundPlayer(sysPath))
                        {
                            player.Play();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing sound {fileName}: {ex.Message}");
            }
        }
        // Đảm bảo đoạn này vẫn có trong TimeKeepingWindow.xaml.cs
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            txtScanInput.Focus(); // Tự động focus để quét hoặc gõ luôn
        }
    }
}