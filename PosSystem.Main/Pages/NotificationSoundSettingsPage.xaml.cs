using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main.Pages
{
    public partial class NotificationSoundSettingsPage : UserControl
    {
        private const string KeyEnabled = "notificationSoundEnabled";
        private const string KeyVolume = "notificationSoundVolume";

        public NotificationSoundSettingsPage()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var enabledSetting = db.GlobalSettings.FirstOrDefault(s => s.Key == KeyEnabled);
                    var volumeSetting = db.GlobalSettings.FirstOrDefault(s => s.Key == KeyVolume);

                    bool enabled = enabledSetting == null ? true : enabledSetting.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    int volume = 25;
                    if (volumeSetting != null && int.TryParse(volumeSetting.Value, out var v))
                    {
                        volume = Math.Clamp(v, 0, 100);
                    }

                    chkEnableSound.IsChecked = enabled;
                    sldVolume.Value = volume;
                    lblVolume.Text = $"{volume}%";
                    sldVolume.IsEnabled = enabled;
                }
            }
            catch
            {
                chkEnableSound.IsChecked = true;
                sldVolume.Value = 25;
                lblVolume.Text = "25%";
                sldVolume.IsEnabled = true;
            }
        }

        private void ChkEnableSound_Checked(object sender, RoutedEventArgs e)
        {
            if (sldVolume != null) sldVolume.IsEnabled = true;
        }

        private void ChkEnableSound_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sldVolume != null) sldVolume.IsEnabled = false;
        }

        private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (lblVolume != null)
            {
                lblVolume.Text = $"{(int)sldVolume.Value}%";
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    UpsertSetting(db, KeyEnabled, (chkEnableSound.IsChecked == true).ToString().ToLower());
                    UpsertSetting(db, KeyVolume, ((int)sldVolume.Value).ToString());
                    db.SaveChanges();
                }

                MainWindow.ReloadNotificationSoundSettings();
                MessageBox.Show("Đã lưu cài đặt.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                MessageBox.Show("Không thể lưu cài đặt âm thanh.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MainWindow.TestNotificationSound((int)sldVolume.Value, chkEnableSound.IsChecked == true);
            }
            catch { }
        }

        private static void UpsertSetting(AppDbContext db, string key, string value)
        {
            var setting = db.GlobalSettings.FirstOrDefault(s => s.Key == key);
            if (setting == null)
            {
                setting = new GlobalSetting
                {
                    Key = key,
                    Value = value,
                    Description = "Notification sound setting",
                    ModifiedDate = DateTime.Now
                };
                db.GlobalSettings.Add(setting);
            }
            else
            {
                setting.Value = value;
                setting.ModifiedDate = DateTime.Now;
            }
        }
    }
}
