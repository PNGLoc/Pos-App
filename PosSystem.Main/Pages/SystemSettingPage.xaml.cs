using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main.Pages
{
    public partial class SystemSettingPage : UserControl
    {
        private const string KeyEnabled = "notificationSoundEnabled";
        private const string KeyVolume = "notificationSoundVolume";
        // [NEW] 3 separate keys
        private const string KeyAutoReturnPay = "autoReturnPay";
        private const string KeyAutoReturnProvisional = "autoReturnProvisional";
        private const string KeyAutoReturnKitchen = "autoReturnKitchen";

        public SystemSettingPage()
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
                    
                    var autoReturnPay = db.GlobalSettings.FirstOrDefault(s => s.Key == KeyAutoReturnPay);
                    var autoReturnProvisional = db.GlobalSettings.FirstOrDefault(s => s.Key == KeyAutoReturnProvisional);
                    var autoReturnKitchen = db.GlobalSettings.FirstOrDefault(s => s.Key == KeyAutoReturnKitchen);

                    bool enabled = enabledSetting == null ? true : enabledSetting.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    int volume = 25;
                    if (volumeSetting != null && int.TryParse(volumeSetting.Value, out var v))
                    {
                        volume = Math.Clamp(v, 0, 100);
                    }
                    
                    bool bPay = autoReturnPay != null && autoReturnPay.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    bool bProv = autoReturnProvisional != null && autoReturnProvisional.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    bool bKitchen = autoReturnKitchen != null && autoReturnKitchen.Value.Equals("true", StringComparison.OrdinalIgnoreCase);

                    chkEnableSound.IsChecked = enabled;
                    sldVolume.Value = volume;
                    lblVolume.Text = $"{volume}%";
                    sldVolume.IsEnabled = enabled;
                    
                    chkAutoReturnPay.IsChecked = bPay;
                    chkAutoReturnProvisional.IsChecked = bProv;
                    chkAutoReturnKitchen.IsChecked = bKitchen;
                }
            }
            catch
            {
                chkEnableSound.IsChecked = true;
                sldVolume.Value = 25;
                lblVolume.Text = "25%";
                sldVolume.IsEnabled = true;
                
                chkAutoReturnPay.IsChecked = false;
                chkAutoReturnProvisional.IsChecked = false;
                chkAutoReturnKitchen.IsChecked = false;
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
                    
                    UpsertSetting(db, KeyAutoReturnPay, (chkAutoReturnPay.IsChecked == true).ToString().ToLower());
                    UpsertSetting(db, KeyAutoReturnProvisional, (chkAutoReturnProvisional.IsChecked == true).ToString().ToLower());
                    UpsertSetting(db, KeyAutoReturnKitchen, (chkAutoReturnKitchen.IsChecked == true).ToString().ToLower());
                    
                    db.SaveChanges();
                }

                MainWindow.ReloadNotificationSoundSettings();
                MainWindow.ReloadSystemSettings(); 
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

        private void BtnUpload_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio Files|*.wav;*.mp3",
                Title = "Chọn file âm thanh thông báo"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string sourcePath = openFileDialog.FileName;
                    string ext = System.IO.Path.GetExtension(sourcePath).ToLower();
                    
                    if (ext != ".wav" && ext != ".mp3")
                    {
                        MessageBox.Show("Vui lòng chọn file .wav hoặc .mp3", "Lỗi định dạng", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string targetDir = PosSystem.Main.Helpers.AppPaths.AudioDir;
                    System.IO.Directory.CreateDirectory(targetDir); // Ensure exists

                    string destWav = System.IO.Path.Combine(targetDir, "notification.wav");
                    string destMp3 = System.IO.Path.Combine(targetDir, "notification.mp3");

                    if (ext == ".wav")
                    {
                        System.IO.File.Copy(sourcePath, destWav, true);
                    }
                    else
                    {
                        if (System.IO.File.Exists(destWav))
                        {
                             var result = MessageBox.Show("Hệ thống đang sử dụng file .wav (ưu tiên cao hơn). Bạn có muốn xóa file .wav cũ để sử dụng file .mp3 này không?", 
                                 "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);
                             if (result == MessageBoxResult.Yes)
                             {
                                 System.IO.File.Delete(destWav);
                             }
                             else
                             {
                                 MessageBox.Show("File .mp3 đã được tải lên nhưng sẽ không được sử dụng do có file .wav ưu tiên.", "Lưu ý", MessageBoxButton.OK, MessageBoxImage.Information);
                             }
                        }
                        System.IO.File.Copy(sourcePath, destMp3, true);
                    }

                    MessageBox.Show("Đã tải lên file âm thanh thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi tải file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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
                    Description = "System setting",
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
