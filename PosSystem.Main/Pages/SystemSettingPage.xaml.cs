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
            SaveSingleSetting(KeyEnabled, "true");
        }

        private void ChkEnableSound_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sldVolume != null) sldVolume.IsEnabled = false;
            SaveSingleSetting(KeyEnabled, "false");
        }

        private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (lblVolume != null)
            {
                lblVolume.Text = $"{(int)sldVolume.Value}%";
            }
        }

        // Save volume only when drag completes to avoid spamming DB
        private void SldVolume_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
             SaveSingleSetting(KeyVolume, ((int)sldVolume.Value).ToString());
        }

        // New universal handler for CheckBoxes
        private void AutoSave_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return; // Prevent triggering during LoadSettings()

            if (sender == chkAutoReturnPay)
                SaveSingleSetting(KeyAutoReturnPay, (chkAutoReturnPay.IsChecked == true).ToString().ToLower());
            
            else if (sender == chkAutoReturnProvisional)
                SaveSingleSetting(KeyAutoReturnProvisional, (chkAutoReturnProvisional.IsChecked == true).ToString().ToLower());
            
            else if (sender == chkAutoReturnKitchen)
                SaveSingleSetting(KeyAutoReturnKitchen, (chkAutoReturnKitchen.IsChecked == true).ToString().ToLower());
        }

        private void SaveSingleSetting(string key, string value)
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    UpsertSetting(db, key, value);
                    db.SaveChanges();
                }

                if (key == KeyEnabled || key == KeyVolume)
                    MainWindow.ReloadNotificationSoundSettings();
                else 
                    MainWindow.ReloadSystemSettings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AutoSave Error [{key}]: {ex.Message}");
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Now redundant, kept just in case user unhides button or legacy call
            MessageBox.Show("Các cài đặt đã được lưu tự động!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
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
