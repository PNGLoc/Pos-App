using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.ObjectModel; // [NEW]
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using PosSystem.Main.Services;
using System.Threading.Tasks;
using System.Globalization;
using System.Text;
using System.IO;
using System.Media;
// [THÊM] Namespace quan trọng
using Microsoft.AspNetCore.SignalR;        // Cho Server (Gửi đi)
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Main.Server.Hubs;
using System.Threading;
using PosSystem.Main.Helpers;
namespace PosSystem.Main
{
    // ViewModels
    public class TableViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public int TableID { get; set; }
        public required string TableName { get; set; }
        public required string TableStatus { get; set; }

        public DateTime? PendingOrderTime { get; set; }

        private string _timeDisplay = "";
        public string TimeDisplay
        {
            get => _timeDisplay;
            set
            {
                if (_timeDisplay != value)
                {
                    _timeDisplay = value;
                    OnPropertyChanged(nameof(TimeDisplay));
                }
            }
        }

        public string StatusDisplay => TableStatus == "Occupied" ? "Có khách" : "Trống";

        private bool _isGrayedOut;
        public bool IsGrayedOut
        {
            get => _isGrayedOut;
            set
            {
                if (_isGrayedOut != value)
                {
                    _isGrayedOut = value;
                    OnPropertyChanged(nameof(IsGrayedOut));
                    OnPropertyChanged(nameof(ColorBrush));
                    OnPropertyChanged(nameof(TextBrush));
                }
            }
        }

        public SolidColorBrush ColorBrush => IsGrayedOut
            ? new SolidColorBrush(Colors.Gray)
            : (TableStatus == "Occupied"
                ? new SolidColorBrush(Color.FromRgb(212, 237, 218))
                : new SolidColorBrush(Color.FromRgb(245, 246, 248)));

        public SolidColorBrush TextBrush => IsGrayedOut
            ? new SolidColorBrush(Colors.White)
            : (TableStatus == "Occupied"
                ? new SolidColorBrush(Colors.Black)
                : new SolidColorBrush(Color.FromRgb(55, 65, 81)));

        public SolidColorBrush CategoryBorderBrush { get; set; } = new SolidColorBrush(Color.FromRgb(208, 208, 208));
        public string CategoryIconGlyph { get; set; } = "\uf6c0";
        private bool _isCategoryIconVisible = true;
        public bool IsCategoryIconVisible
        {
            get => _isCategoryIconVisible;
            set
            {
                if (_isCategoryIconVisible != value)
                {
                    _isCategoryIconVisible = value;
                    OnPropertyChanged(nameof(IsCategoryIconVisible));
                }
            }
        }
        public bool IsRequestingPayment { get; set; } = false;
        public bool HasProvisionalBill { get; set; } = false; // [NEW]
        public bool HasUnsentItems { get; set; } = false;

        private decimal _totalAmount;
        public decimal TotalAmount
        {
            get => _totalAmount;
            set
            {
                if (_totalAmount != value)
                {
                    _totalAmount = value;
                    OnPropertyChanged(nameof(TotalAmount));
                    OnPropertyChanged(nameof(TotalAmountDisplay));
                    OnPropertyChanged(nameof(HasTotalAmount));
                }
            }
        }

        public string TotalAmountDisplay => (TableStatus == "Occupied" && TotalAmount > 0)
            ? $"{TotalAmount:N0}đ"
            : string.Empty;

        public bool HasTotalAmount => TableStatus == "Occupied" && TotalAmount > 0;
    }

    public class CategoryViewModel { public int CategoryID { get; set; } public string CategoryName { get; set; } = ""; }

    // View Model cho Món ăn trong menu (Đơn giản hóa vì bỏ checkbox)
    public class DishViewModel
    {
        public int DishID { get; set; }
        public string DishName { get; set; } = "";
        public decimal Price { get; set; }
        public int CategoryID { get; set; }
        public string ImagePath { get; set; } = "";
    }

    public class ReprintItemViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public List<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
        public string DisplayText { get; set; }

        public int TotalQuantity => OrderDetails.Sum(d => d.Quantity);

        private int _selectedQuantity;
        public int SelectedQuantity
        {
            get => _selectedQuantity;
            set
            {
                if (_selectedQuantity != value)
                {
                    _selectedQuantity = value;
                    OnPropertyChanged(nameof(SelectedQuantity));
                    OnPropertyChanged(nameof(QuantityDisplay));
                }
            }
        }

        public string QuantityDisplay => $"{SelectedQuantity}/{TotalQuantity}";
        public string Note => OrderDetails.FirstOrDefault()?.Note ?? "";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    // If selected -> Default to Max, else 0? 
                    // Let's keep SelectedQuantity sticky or reset? 
                    // Logic: If checking, ensure at least 1 is selected (Max).
                    if (value && SelectedQuantity == 0) SelectedQuantity = TotalQuantity;

                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
    }

    public partial class MainWindow : Window
    {
        private const string TableSearchPlaceholder = "Tìm bàn";
        private static SoundPlayer? _notificationPlayer;
        private static MediaPlayer? _mediaPlayer = new MediaPlayer(); // [NEW] MP3 Player
        private static MemoryStream? _notificationStream;
        private static bool _notificationSoundEnabled = true;
        private static int _notificationSoundVolume = 25;
        private static int _notificationSoundVolumeApplied = -1;
        private static DateTime _notificationSettingsLastRead = DateTime.MinValue;
        private static DateTime _lastNotificationSoundAt = DateTime.MinValue;
        private static DateTime _lastActivitySoundAt = DateTime.MinValue;
        private static string _lastActivitySoundMessage = string.Empty;
        private static readonly TimeSpan _activitySoundCooldown = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan _activitySoundSameMessageWindow = TimeSpan.FromSeconds(5);
        private static bool _showTableCardIcons = true;
        // [NEW] 3 separate settings (Default: false)
        private static bool _autoReturnPay = false;
        private static bool _autoReturnProvisional = false;
        private static bool _autoReturnKitchen = false;
        private HubConnection _connection = default!;
        private int _selectedTableId = 0;
        private int? _selectedCategoryId = null; // Filter by CategoryID
        private string _tableSearchText = string.Empty;

        // Menu dish ordering: category OrderIndex lookup (used when selecting "TẤT CẢ")
        private Dictionary<int, int> _menuCategoryOrderIndexById = new Dictionary<int, int>();

        // List categories for Filter Bar
        public List<TableCategory> FilterCategories { get; set; } = new List<TableCategory>();

        // Notification List for UI (persisted in DB)
        public ObservableCollection<string> NotificationList { get; } = new ObservableCollection<string>();

        private readonly SemaphoreSlim _activityLogLock = new SemaphoreSlim(1, 1);

        private List<Dish> _allDishes = new List<Dish>();
        private List<DishViewModel> _dishViewModels = new List<DishViewModel>();
        private DispatcherTimer _tableTimeTimer = new DispatcherTimer();
        private DispatcherTimer _tableListUpdateTimer = new DispatcherTimer();
        private DateTime? _currentOrderTime = null;
        private HashSet<int> _tablesRequestingPayment = new HashSet<int>();

        private bool _isWaitingForTargetTable = false;  // True when waiting for user to click target table
        private Dictionary<long, int> _pendingSplitItems = new Dictionary<long, int>();  // Items to split when table selected

        // Move table mode variables
        private bool _isWaitingForMoveTargetTable = false;  // True when waiting for user to click target table for move
        private const int MIN_SECONDS_WAIT = 10; // Thời gian chờ giữa Check-in và Check-out chấm công
        private static readonly SemaphoreSlim _moveTableLock = new SemaphoreSlim(1, 1);

        private static void LogMoveTableDesktop(string message)
        {
            try
            {
                var path = Path.Combine(AppPaths.DataRoot, "move_table.log");
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | WPF | {message}{Environment.NewLine}");
            }
            catch { }
        }

        public MainWindow()
        {
            InitializeComponent();

            PrintService.PrintFailed -= OnPrintFailed;
            PrintService.PrintFailed += OnPrintFailed;

            ReloadTableIconSettings();
            LoadAutoReturnSettings();

            // Defer heavy work until the window is rendered at least once.
            Loaded += MainWindow_Loaded;

            // Load Categories for Filter
            using (var db = new AppDbContext())
            {
                FilterCategories = db.TableCategories
                    .OrderBy(c => c.DisplayOrder)
                    .ThenBy(c => c.CategoryID)
                    .ToList();
            }
            this.DataContext = this; // Bind to self

            LoadActivityLogFromDb();

            if (UserSession.IsLoggedIn) lblStaffName.Text = UserSession.AccName;
            if (UserSession.IsLoggedIn && UserSession.AccRole == "Admin") btnBackToAdmin.Visibility = Visibility.Visible;

            // Setup timer to update table time every minute (reduce lag)
            _tableTimeTimer.Interval = TimeSpan.FromMinutes(1);
            _tableTimeTimer.Tick += TableTimeTimer_Tick;

            // Setup timer to refresh table list every second (for displaying elapsed times)
            // Setup timer to refresh table list every minute (as requested to reduce UI lag)
            _tableListUpdateTimer.Interval = TimeSpan.FromMinutes(1);
            _tableListUpdateTimer.Tick += (s, e) => UpdateTableTimeDisplays();
            _tableListUpdateTimer.Start();

            // Reset buttons on startup
            btnCheckout.IsEnabled = false;
            btnProvisional.IsEnabled = false;
            btnSendKitchen.IsEnabled = false;
            btnSendKitchen.Background = new SolidColorBrush(Color.FromRgb(108, 117, 125));  // Màu xám
            btnSendKitchen.Content = "🔔 GỬI BẾP";
            btnSplitTable.Visibility = Visibility.Collapsed;
            btnMoveTable.Visibility = Visibility.Collapsed;
            lblSubTotal.Text = "0đ";
            lblTotal.Text = "0đ";
            pnlDiscount.Visibility = Visibility.Collapsed;
            btnDiscountBill.Visibility = Visibility.Collapsed; // [FIX] Hide initially

            // LoadTables/LoadMenu/SetupRealtime are started in MainWindow_Loaded
        }

        private void OnPrintFailed(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowToast($"❌ In thất bại: {message}", 2500);
            }));
        }

        public static void ReloadTableIconSettings()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var setting = db.GlobalSettings.FirstOrDefault(s => s.Key == "showTableCardIcons");
                    _showTableCardIcons = setting == null || setting.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                _showTableCardIcons = true;
            }
        }

        public static void LoadAutoReturnSettings()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var sPay = db.GlobalSettings.FirstOrDefault(s => s.Key == "autoReturnPay");
                    var sProv = db.GlobalSettings.FirstOrDefault(s => s.Key == "autoReturnProvisional");
                    var sKitchen = db.GlobalSettings.FirstOrDefault(s => s.Key == "autoReturnKitchen");

                    _autoReturnPay = sPay != null && sPay.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    _autoReturnProvisional = sProv != null && sProv.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    _autoReturnKitchen = sKitchen != null && sKitchen.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Default to false if DB error
                _autoReturnPay = false;
                _autoReturnProvisional = false;
                _autoReturnKitchen = false;
            }
        }



        public static void ApplyTableIconSettingsToOpenWindows()
        {
            ReloadTableIconSettings();
            if (Application.Current == null) return;
            foreach (Window w in Application.Current.Windows)
            {
                if (w is MainWindow mw)
                {
                    mw.Dispatcher.Invoke(() => mw.RefreshTableIconVisibility());
                }
            }
        }

        private void RefreshTableIconVisibility()
        {
            if (lstTables.ItemsSource is not IEnumerable<TableViewModel> items) return;
            foreach (var vm in items)
            {
                vm.IsCategoryIconVisible = _showTableCardIcons;
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;

            // Let layout/render happen first, then populate data.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

            _ = LoadTablesAsync();
            Dispatcher.BeginInvoke(new Action(UpdateFilterButtonStyles), DispatcherPriority.Loaded);

            // LoadMenu is UI-bound; keep it deferred so table grid appears ASAP.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { LoadMenu(); } catch { }
            }), DispatcherPriority.Background);

            try { SetupRealtime(); } catch { }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    txtTableSearch.Focus();
                    Keyboard.Focus(txtTableSearch);
                }
                catch { }
            }), DispatcherPriority.Input);
        }

        private void LoadActivityLogFromDb()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var latest = db.ActivityLogs
                        .OrderByDescending(x => x.Id)
                        .Take(200)
                        .Select(x => new { x.CreatedAt, x.Message })
                        .ToList();

                    NotificationList.Clear();
                    foreach (var row in latest)
                    {
                        var ts = row.CreatedAt == default ? DateTime.Now : row.CreatedAt;
                        NotificationList.Add($"[{ts:HH:mm:ss}] {row.Message}");
                    }
                }
            }
            catch
            {
                // Ignore (DB may not be ready yet); realtime will still function.
            }
        }

        private static void EnsureNotificationPlayer()
        {
            if ((DateTime.Now - _notificationSettingsLastRead).TotalSeconds > 5)
            {
                ReloadNotificationSoundSettings();
            }

            if (!_notificationSoundEnabled || _notificationSoundVolume <= 0)
                return;

            if (_notificationPlayer != null && _notificationSoundVolumeApplied == _notificationSoundVolume)
                return;

            try
            {
                _notificationPlayer?.Stop();
                _notificationPlayer?.Dispose();
                _notificationStream?.Dispose();

                var wav = BuildNotificationWav(_notificationSoundVolume);
                _notificationStream = new MemoryStream(wav);
                _notificationPlayer = new SoundPlayer(_notificationStream);
                _notificationPlayer.Load();
                _notificationSoundVolumeApplied = _notificationSoundVolume;
            }
            catch
            {
                _notificationPlayer = null;
                _notificationStream = null;
            }
        }

        private static void PlayNotificationSound()
        {
            if (!_notificationSoundEnabled || _notificationSoundVolume <= 0)
            {
                try { _notificationPlayer?.Stop(); } catch { }
                return;
            }

            if ((DateTime.Now - _lastNotificationSoundAt).TotalMilliseconds < 800)
            {
                return;
            }
            _lastNotificationSoundAt = DateTime.Now;

            try
            {
                // [MODIFIED] Use AppPaths.AudioDir
                string audioDir = PosSystem.Main.Helpers.AppPaths.AudioDir;
                string wavPath = System.IO.Path.Combine(audioDir, "notification.wav");
                string mp3Path = System.IO.Path.Combine(audioDir, "notification.mp3");
                // [NEW] Asset Path (Bundled with Exe)
                string assetPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "notification.wav");

                // [PRIORITY 1] Check for notification.wav (User Custom)
                if (System.IO.File.Exists(wavPath))
                {
                    // [MODIFIED] Use Boosted Playback
                    PlayCustomWavWithGain(wavPath, _notificationSoundVolume);
                    return;
                }

                // [PRIORITY 2] Check for notification.mp3 (User Custom)
                if (System.IO.File.Exists(mp3Path))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (_mediaPlayer == null) _mediaPlayer = new MediaPlayer();
                            _mediaPlayer.Open(new Uri(mp3Path));
                            _mediaPlayer.Volume = Math.Clamp(_notificationSoundVolume, 0, 100) / 100.0;
                            _mediaPlayer.Play();
                        }
                        catch { }
                    });
                    return;
                }

                // [PRIORITY 3] Check for Bundled Asset (Installer provided)
                if (System.IO.File.Exists(assetPath))
                {
                    PlayCustomWavWithGain(assetPath, _notificationSoundVolume);
                    return;
                }
            }
            catch { }

            // [FALLBACK] Use internal Tink sound
            EnsureNotificationPlayer();
            _notificationPlayer?.Play();
        }

        public static void TestNotificationSound()
        {
            PlayNotificationSound();
        }

        public static void TestNotificationSound(int volumePercent, bool enabled)
        {
            if (!enabled || volumePercent <= 0) return;
            try
            {
                // [MODIFIED] Use AppPaths.AudioDir
                string audioDir = PosSystem.Main.Helpers.AppPaths.AudioDir;
                string wavPath = System.IO.Path.Combine(audioDir, "notification.wav");
                string mp3Path = System.IO.Path.Combine(audioDir, "notification.mp3");
                // [NEW] Asset Path
                string assetPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "notification.wav");

                // [PRIORITY 1] Check for notification.wav
                if (System.IO.File.Exists(wavPath))
                {
                    // [MODIFIED] Use Boosted Playback
                    PlayCustomWavWithGain(wavPath, volumePercent);
                    return;
                }

                // [PRIORITY 2] Check for notification.mp3
                if (System.IO.File.Exists(mp3Path))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                     {
                         try
                         {
                             if (_mediaPlayer == null) _mediaPlayer = new MediaPlayer();
                             _mediaPlayer.Open(new Uri(mp3Path));
                             _mediaPlayer.Volume = Math.Clamp(volumePercent, 0, 100) / 100.0;
                             _mediaPlayer.Play();
                         }
                         catch { }
                     });
                    return;
                }

                // [PRIORITY 3] Check for Bundled Asset
                if (System.IO.File.Exists(assetPath))
                {
                    PlayCustomWavWithGain(assetPath, volumePercent);
                    return;
                }

                // [FALLBACK]
                var wav = BuildNotificationWav(Math.Clamp(volumePercent, 0, 100));
                using var ms = new MemoryStream(wav);
                using var player = new SoundPlayer(ms);
                player.Load();
                player.PlaySync();
            }
            catch { }
        }

        public static void ReloadNotificationSoundSettings()
        {
            ReloadSystemSettings();
        }

        public static void ReloadSystemSettings()
        {
            // [NEW] Call the shared AutoReturn loader
            LoadAutoReturnSettings();

            try
            {
                using (var db = new AppDbContext())
                {
                    var enabledSetting = db.GlobalSettings.FirstOrDefault(s => s.Key == "notificationSoundEnabled");
                    var volumeSetting = db.GlobalSettings.FirstOrDefault(s => s.Key == "notificationSoundVolume");

                    _notificationSoundEnabled = enabledSetting == null || enabledSetting.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (volumeSetting != null && int.TryParse(volumeSetting.Value, out var v))
                    {
                        _notificationSoundVolume = Math.Clamp(v, 0, 100);
                    }
                    else
                    {
                        _notificationSoundVolume = 25;
                    }
                }
            }
            catch
            {
                // Fallback defaults
                _notificationSoundEnabled = true;
                _notificationSoundVolume = 25;
            }

            // Apply settings (e.g. stop player if disabled)
            if (!_notificationSoundEnabled || _notificationSoundVolume <= 0)
            {
                try { _notificationPlayer?.Stop(); } catch { }
            }
            _notificationSettingsLastRead = DateTime.Now;
        }

        private static byte[] BuildNotificationWav(int volumePercent)
        {
            // [MODIFIED] "Hyper" Sound Generation - Square Wave for Max Energy
            const int sampleRate = 44100;
            const double durationSeconds = 1.0; // Long duration
            const double frequency = 2000.0; // High sensitivity range

            double volume = Math.Clamp(volumePercent, 0, 100) / 100.0;
            double maxAmplitude = 32000.0 * volume;

            int sampleCount = (int)(sampleRate * durationSeconds);

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + sampleCount * 2);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(sampleCount * 2);

            for (int i = 0; i < sampleCount; i++)
            {
                double t = (double)i / sampleRate;

                // Delayed Decay: Hold max volume for 0.15s before fading
                double decay = 1.0;
                if (t > 0.15)
                {
                    decay = Math.Exp(-4.0 * (t - 0.15));
                }

                // Square Wave logic: Math.Sign(Sin)
                // This maximizes the area under the curve (Energy)
                double sine = Math.Sin(2 * Math.PI * frequency * t);
                double square = sine >= 0 ? 1.0 : -1.0;

                // Mix: 60% Square (Loudness) + 40% Sine (Tone)
                // Plus a high-frequency alternating "click" for attack
                double wave = (0.6 * square) + (0.4 * sine);

                double sample = wave * maxAmplitude * decay;

                bw.Write((short)Math.Clamp((int)sample, -32768, 32767));
            }

            return ms.ToArray();
        }

        private static void PlayCustomWavWithGain(string filePath, int volumePercent)
        {
            try
            {
                byte[] fileBytes = System.IO.File.ReadAllBytes(filePath);

                // Simple WAV parsing
                // Header is 44 bytes minimum. 
                // We need to find "fmt " to get BitsPerSample
                // We need to find "data" to get AudioData

                // Helper to find chunk
                int FindChunk(byte[] src, string chunkName, int startObj)
                {
                    byte[] chunkTag = Encoding.ASCII.GetBytes(chunkName);
                    for (int i = startObj; i < src.Length - 4; i++)
                    {
                        if (src[i] == chunkTag[0] && src[i + 1] == chunkTag[1] && src[i + 2] == chunkTag[2] && src[i + 3] == chunkTag[3])
                            return i;
                    }
                    return -1;
                }

                // 1. Find fmt chunk
                int fmtIdx = FindChunk(fileBytes, "fmt ", 12);
                if (fmtIdx == -1) throw new Exception("No fmt chunk");

                // BitsPerSample is at fmtIdx + 8 (chunk size) + 2 (format) + 2 (channels) + 4 (sample rate) + 4 (byte rate) + 2 (block align)
                // Offset 22 from Chunk Tag start?
                // fmt tag (4) + size (4) + format tag (2) + channels (2) + samplerate (4) + byterate (4) + blockalign (2) + BitsPerSample (2)
                int bitsPerSampleIdx = fmtIdx + 8 + 2 + 2 + 4 + 4 + 2;
                short bitsPerSample = BitConverter.ToInt16(fileBytes, bitsPerSampleIdx);

                // 2. Find data chunk
                int dataIdx = FindChunk(fileBytes, "data", 12);
                if (dataIdx == -1) throw new Exception("No data chunk");

                int dataSize = BitConverter.ToInt32(fileBytes, dataIdx + 4);
                int dataStart = dataIdx + 8;

                // 3. Apply Gain
                // User wants "Louder" than max. Max slider (100) = 300% Gain.
                double gain = (volumePercent / 100.0) * 3.0f; // Boost up to 3x

                // Process in-memory (Copy first)
                byte[] newBytes = new byte[fileBytes.Length];
                Array.Copy(fileBytes, newBytes, fileBytes.Length);

                if (bitsPerSample == 16)
                {
                    for (int i = dataStart; i < dataStart + dataSize; i += 2)
                    {
                        if (i + 1 >= newBytes.Length) break;
                        short sample = BitConverter.ToInt16(newBytes, i);
                        int newSample = (int)(sample * gain);
                        short clamped = (short)Math.Clamp(newSample, -32768, 32767);
                        newBytes[i] = (byte)(clamped & 0xFF);
                        newBytes[i + 1] = (byte)((clamped >> 8) & 0xFF);
                    }
                }
                else if (bitsPerSample == 8)
                {
                    for (int i = dataStart; i < dataStart + dataSize; i++)
                    {
                        // 8-bit is unsigned 0..255, center 128
                        int sample = newBytes[i] - 128;
                        int newSample = (int)(sample * gain);
                        byte clamped = (byte)Math.Clamp(newSample + 128, 0, 255);
                        newBytes[i] = clamped;
                    }
                }

                // 4. Play
                using var ms = new MemoryStream(newBytes);
                using var player = new SoundPlayer(ms);
                player.Load();
                player.PlaySync();
            }
            catch (Exception)
            {
                // Fallback if parsing fails: Use standard MediaPlayer without boost
                Application.Current.Dispatcher.Invoke(() =>
               {
                   if (_mediaPlayer == null) _mediaPlayer = new MediaPlayer();
                   _mediaPlayer.Open(new Uri(filePath));
                   _mediaPlayer.Volume = Math.Clamp(volumePercent, 0, 100) / 100.0;
                   _mediaPlayer.Play();
               });
            }
        }

        private void AppendActivityLog(string message)
        {
            // Fire-and-forget to avoid blocking UI thread
            _ = Task.Run(async () =>
            {
                try
                {
                    await _activityLogLock.WaitAsync();
                    try
                    {
                        using (var db = new AppDbContext())
                        {
                            db.ActivityLogs.Add(new ActivityLogEntry
                            {
                                CreatedAt = DateTime.Now,
                                Message = message
                            });
                            await db.SaveChangesAsync();

                            // Keep only 200 newest in DB
                            try
                            {
                                await db.Database.ExecuteSqlRawAsync(@"
                                    DELETE FROM ""ActivityLogs""
                                    WHERE ""Id"" NOT IN (
                                        SELECT ""Id"" FROM ""ActivityLogs"" ORDER BY ""Id"" DESC LIMIT 200
                                    );
                                ");
                            }
                            catch { }
                        }
                    }
                    finally
                    {
                        _activityLogLock.Release();
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        NotificationList.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
                        while (NotificationList.Count > 200) NotificationList.RemoveAt(NotificationList.Count - 1);
                        try
                        {
                            if (ShouldPlayActivitySound(message))
                            {
                                PlayNotificationSound();
                            }
                        }
                        catch { }
                    });
                }
                catch { }
            });
        }

        private static bool ShouldPlayActivitySound(string message)
        {
            var now = DateTime.Now;
            var msg = (message ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(msg) && msg.Equals(_lastActivitySoundMessage, StringComparison.OrdinalIgnoreCase))
            {
                if ((now - _lastActivitySoundAt) < _activitySoundSameMessageWindow)
                {
                    return false;
                }
            }

            if ((now - _lastActivitySoundAt) < _activitySoundCooldown)
            {
                return false;
            }

            _lastActivitySoundAt = now;
            _lastActivitySoundMessage = msg;
            return true;
        }


        // --- 1. CHUYỂN ĐỔI VIEW ---
        private void lstTables_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstTables.SelectedItem is TableViewModel selected)
            {
                OpenTableFromSelection(selected);
            }
        }

        private void lstTables_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBox listBox)
                return;

            if (e.OriginalSource is not DependencyObject dep)
                return;

            var container = ItemsControl.ContainerFromElement(listBox, dep) as ListBoxItem;
            if (container?.DataContext is not TableViewModel selected)
                return;

            // With virtualization, SelectedItem can stay the same, so SelectionChanged won't fire.
            // Force open behavior on click.
            if (!Equals(listBox.SelectedItem, selected))
                listBox.SelectedItem = selected;

            OpenTableFromSelection(selected);
            e.Handled = true;
        }

        private void OpenTableFromSelection(TableViewModel selected)
        {
            // [NEW] Prevent selecting grayed-out tables (Source table in split mode)
            if (selected.IsGrayedOut)
            {
                lstTables.SelectedItem = null;
                return;
            }

            // If waiting for target table in split mode, transfer items instead of opening menu
            if (_isWaitingForTargetTable && _pendingSplitItems.Count > 0)
            {
                int targetTableId = selected.TableID;
                lstTables.SelectedItem = null;  // Deselect to reset
                ExecuteSplitTransfer(targetTableId);
                return;
            }

            ResetTableSearch();

            if (_selectedCategoryId.HasValue)
            {
                _selectedCategoryId = null;
                UpdateFilterButtonStyles();
                LoadTables();
            }

            // [FIX] Clear "Request Payment" flag in DB when selecting table
            if (selected.IsRequestingPayment)
            {
                using (var db = new AppDbContext())
                {
                    var order = db.Orders.FirstOrDefault(o => o.TableID == selected.TableID && o.OrderStatus == "Pending");
                    if (order != null && order.IsRequestingPayment)
                    {
                        order.IsRequestingPayment = false;
                        db.SaveChanges();

                        // Send SignalR to update other clients
                        if (_connection.State == HubConnectionState.Connected)
                        {
                            _connection.SendAsync("NotifyTableUpdated", selected.TableID);
                        }
                    }
                }

                // Refresh UI immediately
                LoadTables();
            }

            // If waiting for target table in move mode, move entire order instead of opening menu
            if (_isWaitingForMoveTargetTable)
            {
                int targetTableId = selected.TableID;
                lstTables.SelectedItem = null;  // Deselect to reset
                _ = ExecuteMoveTableAsync(targetTableId);
                return;
            }

            _selectedTableId = selected.TableID;
            lblSelectedTable.Text = selected.TableName;

            pnlTableList.Visibility = Visibility.Collapsed;
            pnlMenu.Visibility = Visibility.Visible;

            // Ensure menu data is ready when entering a table
            if (lstCategories.ItemsSource == null || _dishViewModels.Count == 0)
            {
                LoadMenu();
            }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Default to "TẤT CẢ" every time entering a table
                if (lstCategories.ItemsSource != null)
                {
                    lstCategories.SelectedIndex = 0;
                }

                if (txtDishSearch != null && !string.IsNullOrEmpty(txtDishSearch.Text))
                {
                    txtDishSearch.Clear();
                }

                UpdateDishListDisplay();

                if (txtDishSearch != null)
                {
                    txtDishSearch.Focus();
                    Keyboard.Focus(txtDishSearch);
                }
            }), DispatcherPriority.Input);

            // Show split and move buttons when selecting a table
            btnSplitTable.Visibility = Visibility.Visible;
            btnMoveTable.Visibility = Visibility.Visible;
            btnSplitTable.Visibility = Visibility.Visible;
            btnMoveTable.Visibility = Visibility.Visible;
            btnReprintKitchen.Visibility = Visibility.Visible;
            btnDiscountBill.Visibility = Visibility.Visible; // [FIX] Show when table selected
            lblItemCount.Visibility = Visibility.Visible;
            lblItemCount.Text = "Số lượng món: 0";

            // Stop timer when entering a table (will start only when sending kitchen)
            _tableTimeTimer.Stop();
            _currentOrderTime = null;
            lblTableTime.Text = "";

            // Get order time (but don't start timer - wait for first kitchen send)
            using (var db = new AppDbContext())
            {
                var order = db.Orders.FirstOrDefault(o => o.TableID == selected.TableID && o.OrderStatus == "Pending");
                if (order != null)
                {
                    // [MODIFIED] Use OrderTime (Creation Time) immediately
                    _currentOrderTime = order.OrderTime;
                    _tableTimeTimer.Start();
                    TableTimeTimer_Tick(null, null); // [FIX] Update UI immediately
                }
            }

            LoadOrderDetails(selected.TableID);
        }

        private void BtnBackToTables_Click(object sender, RoutedEventArgs e)
        {
            _selectedTableId = 0;
            _currentOrderTime = null;
            lblSelectedTable.Text = "Chưa chọn bàn";
            lblTableTime.Text = "";
            lstOrderDetails.ItemsSource = null;

            _tableTimeTimer.Stop();
            pnlMenu.Visibility = Visibility.Collapsed;
            pnlTableList.Visibility = Visibility.Visible;

            // Hide split and move buttons when returning to table list
            btnSplitTable.Visibility = Visibility.Collapsed;
            btnMoveTable.Visibility = Visibility.Collapsed;
            btnReprintKitchen.Visibility = Visibility.Collapsed;
            lblItemCount.Visibility = Visibility.Collapsed;
            lblItemCount.Text = "Số lượng món: 0";

            // Reset split mode when returning to table list
            _isWaitingForTargetTable = false;
            _pendingSplitItems.Clear();
            _pendingSplitItems.Clear();
            // btnDiscountBill.Visibility = Visibility.Collapsed; // [REMOVED] Don't hide it

            // Reset move mode when returning to table list
            _isWaitingForMoveTargetTable = false;

            // Reset buttons và labels
            btnCheckout.IsEnabled = false;
            btnProvisional.IsEnabled = false;
            btnDiscountBill.IsEnabled = false;
            btnDiscountBill.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#E9ECEF"); // Gray
            btnDiscountBill.Visibility = Visibility.Visible; // [FIX] Ensure it stays visible

            btnSendKitchen.IsEnabled = false;
            btnSendKitchen.Background = new SolidColorBrush(Color.FromRgb(108, 117, 125));  // Màu xám
            lblSubTotal.Text = "0đ";
            lblTotal.Text = "0đ";
            pnlDiscount.Visibility = Visibility.Collapsed;
            pnlAdjustment.Visibility = Visibility.Collapsed; // [FIX] Hide adjustment panel

            LoadTables();
            lstTables.SelectedItem = null;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    txtTableSearch.Focus();
                    Keyboard.Focus(txtTableSearch);
                }
                catch { }
            }), DispatcherPriority.Input);
        }

        // Helper method to switch to a specific table
        private void SelectAndLoadTable(int tableId)
        {
            _selectedTableId = tableId;

            using (var db = new AppDbContext())
            {
                var table = db.Tables.FirstOrDefault(t => t.TableID == tableId);
                if (table != null)
                {
                    lblSelectedTable.Text = table.TableName;
                }
            }

            pnlTableList.Visibility = Visibility.Collapsed;
            pnlMenu.Visibility = Visibility.Visible;
            btnSplitTable.Visibility = Visibility.Visible;
            btnMoveTable.Visibility = Visibility.Visible;
            btnMoveTable.Visibility = Visibility.Visible;
            btnReprintKitchen.Visibility = Visibility.Visible;
            btnDiscountBill.Visibility = Visibility.Visible; // [FIX] Show when table loaded
            lblItemCount.Visibility = Visibility.Visible;
            lblItemCount.Text = "Số lượng món: 0";

            // Stop timer when entering a table
            _tableTimeTimer.Stop();
            _currentOrderTime = null;
            lblTableTime.Text = "";

            // Check if order has been sent to kitchen
            using (var db = new AppDbContext())
            {
                var order = db.Orders.FirstOrDefault(o => o.TableID == tableId && o.OrderStatus == "Pending");
                if (order != null)
                {
                    _currentOrderTime = order.OrderTime;
                    _tableTimeTimer.Start();
                    // Manually trigger timer tick to show time immediately
                    TableTimeTimer_Tick(null, null);
                }
            }

            LoadOrderDetails(tableId);

            // Force UI refresh with proper rebinding
            Dispatcher.Invoke(() =>
            {
                // Rebind to force UI update
                var source = lstOrderDetails.ItemsSource;
                lstOrderDetails.ItemsSource = null;
                System.Threading.Thread.Sleep(10);
                lstOrderDetails.ItemsSource = source;
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        // Recalculate order totals based on order details
        // Recalculate order totals based on order details
        private void RecalculateOrderTotals(Order order)
        {
            if (order == null) return;

            // 1. Calculate Base Total (Original Price * Quantity)
            decimal baseTotal = order.OrderDetails.Where(d => d.Quantity > 0).Sum(d => d.Quantity * d.UnitPrice);

            // 2. Calculate Actual Total (After Item Discount/Surcharge)
            decimal actualTotal = order.OrderDetails.Where(d => d.Quantity > 0).Sum(d => d.TotalAmount);

            order.SubTotal = actualTotal;

            // [UPDATE] Show base total (original price) in UI
            lblSubTotal.Text = baseTotal.ToString("N0") + "đ";

            // 3. Display Adjustment (Difference)
            decimal adjustment = actualTotal - baseTotal;
            if (adjustment != 0)
            {
                pnlAdjustment.Visibility = Visibility.Visible;
                if (adjustment > 0)
                {
                    lblAdjustmentTitle.Text = "Tăng giá món: ";
                    lblAdjustment.Text = $"+{adjustment:N0}đ";
                    lblAdjustment.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#FD7E14"); // Orange
                }
                else
                {
                    lblAdjustmentTitle.Text = "Giảm giá món: ";
                    lblAdjustment.Text = $"{adjustment:N0}đ";
                    lblAdjustment.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            else
            {
                pnlAdjustment.Visibility = Visibility.Collapsed;
            }

            // 4. Calculate Final Bill Discount
            decimal discountValue = (order.DiscountPercent > 0) ? actualTotal * (order.DiscountPercent / 100) : order.DiscountAmount;
            order.FinalAmount = actualTotal - discountValue;
        }

        // --- 2. LOAD DATA ---
        private static string FormatElapsedTime(DateTime start)
        {
            var elapsed = DateTime.Now - start;
            if (elapsed.TotalDays >= 1)
            {
                return $"{(int)elapsed.TotalDays} ngày {elapsed.Hours} giờ";
            }
            if (elapsed.TotalMinutes < 60)
            {
                return $"{(int)elapsed.TotalMinutes} phút";
            }
            return $"{(int)elapsed.TotalHours} giờ {elapsed.Minutes} phút";
        }

        private static SolidColorBrush ParseHexBrush(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return new SolidColorBrush(fallback);

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex.Trim());
                return new SolidColorBrush(color);
            }
            catch
            {
                return new SolidColorBrush(fallback);
            }
        }

        private void UpdateTableTimeDisplays()
        {
            if (lstTables.ItemsSource is not IEnumerable<TableViewModel> items)
                return;

            foreach (var vm in items)
            {
                if (vm.TableStatus == "Occupied" && vm.PendingOrderTime.HasValue)
                {
                    vm.TimeDisplay = FormatElapsedTime(vm.PendingOrderTime.Value);
                }
                else
                {
                    if (!string.IsNullOrEmpty(vm.TimeDisplay))
                        vm.TimeDisplay = "";
                }
            }
        }

        private static string GetCategoryIconGlyph(string? iconClass)
        {
            var cls = (iconClass ?? string.Empty).Trim().ToLowerInvariant();
            int codepoint = cls switch
            {
                "fas fa-chair" or "fa-solid fa-chair" => 0xf6c0,
                "fas fa-shopping-bag" or "fa-solid fa-bag-shopping" or "fa-solid fa-shopping-bag" => 0xf290,
                "fas fa-walking" or "fa-solid fa-person-walking" or "fa-solid fa-walking" => 0xf554,
                "fas fa-motorcycle" or "fa-solid fa-motorcycle" => 0xf21c,
                "fas fa-crown" or "fa-solid fa-crown" => 0xf521,
                "fas fa-users" or "fa-solid fa-users" => 0xf0c0,
                "fas fa-clock" or "fa-solid fa-clock" => 0xf017,
                _ => 0xf6c0
            };

            return char.ConvertFromUtf32(codepoint);
        }

        private void LoadTables()
        {
            using (var db = new AppDbContext())
            {
                int? catId = _selectedCategoryId;

                var query = db.Tables
                    .AsNoTracking()
                    .Select(t => new
                    {
                        t.TableID,
                        t.TableName,
                        t.TableStatus,
                        t.CategoryID,
                        CategoryDisplayOrder = t.Category != null ? t.Category.DisplayOrder : int.MaxValue,
                        CategoryBorderColorHex = t.Category != null ? t.Category.BorderColorHex : "#D0D0D0",
                        CategoryIconClass = t.Category != null ? t.Category.IconClass : "fas fa-chair",
                        PendingOrder = t.Orders
                            .Where(o => o.OrderStatus == "Pending")
                            .OrderByDescending(o => o.OrderTime)
                            .Select(o => new { o.OrderTime, o.IsPreCalculated, o.IsRequestingPayment })
                            .FirstOrDefault(),
                        PendingOrderTotal = t.Orders
                            .Where(o => o.OrderStatus == "Pending")
                            .OrderByDescending(o => o.OrderTime)
                            .Select(o => o.FinalAmount)
                            .FirstOrDefault(),
                        PendingOrderHasUnsent = t.Orders
                            .Where(o => o.OrderStatus == "Pending")
                            .OrderByDescending(o => o.OrderTime)
                            .Select(o => o.OrderDetails.Any(d => d.ItemStatus == "New" && d.Quantity > 0))
                            .FirstOrDefault()
                    });

                if (catId.HasValue)
                {
                    query = query.Where(t => t.CategoryID == catId.Value);
                }

                var rows = query.ToList();

                rows = catId.HasValue
                    ? rows.OrderBy(t => t.TableID).ToList()
                    : rows.OrderBy(t => t.CategoryDisplayOrder)
                          .ThenBy(t => t.CategoryID ?? int.MaxValue)
                          .ThenBy(t => t.TableID)
                          .ToList();

                var viewModels = rows.Select(t =>
                {
                    var pendingTime = (t.TableStatus == "Occupied") ? t.PendingOrder?.OrderTime : (DateTime?)null;
                    return new TableViewModel
                    {
                        TableID = t.TableID,
                        TableName = t.TableName,
                        TableStatus = t.TableStatus,
                        PendingOrderTime = pendingTime,
                        TimeDisplay = pendingTime.HasValue ? FormatElapsedTime(pendingTime.Value) : "",
                        HasProvisionalBill = t.PendingOrder?.IsPreCalculated ?? false,
                        IsRequestingPayment = t.PendingOrder?.IsRequestingPayment ?? false,
                        IsGrayedOut = ((_isWaitingForTargetTable || _isWaitingForMoveTargetTable) && t.TableID == _selectedTableId),
                        CategoryBorderBrush = ParseHexBrush(t.CategoryBorderColorHex, Color.FromRgb(208, 208, 208)),
                        CategoryIconGlyph = GetCategoryIconGlyph(t.CategoryIconClass),
                        IsCategoryIconVisible = _showTableCardIcons,
                        TotalAmount = t.PendingOrderTotal,
                        HasUnsentItems = t.PendingOrderHasUnsent
                    };
                }).ToList();

                lstTables.ItemsSource = viewModels;
                ApplyTableSearchFilter();
            }
        }

        // --- 1. CẬP NHẬT HIỂN THỊ: Món SL=0 sẽ hiện rõ "CHỜ HỦY" ---
        private void LoadOrderDetails(int tableId)
        {
            using (var db = new AppDbContext())
            {
                var order = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                    .FirstOrDefault(o => o.TableID == tableId && o.OrderStatus == "Pending");

                if (order != null)
                {
                    // [MODIFIED] Start Timer immediately
                    _currentOrderTime = order.OrderTime;
                    if (!_tableTimeTimer.IsEnabled)
                    {
                        _tableTimeTimer.Start();
                        TableTimeTimer_Tick(null, null);
                    }

                    var viewModels = order.OrderDetails
                        .GroupBy(d => new
                        {
                            d.DishID,
                            // [FIX] Group Sent and Modified together. New items stay separate.
                            GroupStatus = (d.ItemStatus == "New") ? "New" : "SentGroup",
                            Note = (d.Note ?? "").Trim(),
                            d.DiscountRate
                        })
                        .Select(g => new OrderDetailViewModel
                        {
                            // [FIX] Use First ID as representative. Handlers will look up siblings.
                            OrderDetailID = g.First().OrderDetailID,
                            DishName = g.First().Dish != null ? g.First().Dish.DishName : "Unknown",
                            UnitPrice = g.First().UnitPrice,
                            DiscountRate = g.First().DiscountRate,
                            SortTime = g.Max(x => x.ItemOrderTime),
                            // Status for logic: If any is New, it's New. Else check if modified.
                            ItemStatus = g.Key.GroupStatus == "New" ? "New" : (g.Sum(x => x.Quantity) < g.Sum(x => x.PrintedQuantity) ? "Modified" : "Sent"),
                            Note = g.Key.Note,
                            Quantity = g.Sum(x => x.Quantity),
                            TotalAmount = g.Sum(x => x.TotalAmount),

                            KitchenBatch = g.Max(x => x.KitchenBatch),

                            BatchDisplay = g.Sum(x => x.PrintedQuantity) == 0 ? "⏳" : (g.Max(x => x.KitchenBatch) > 0 ? $"Đợt {g.Max(x => x.KitchenBatch)}" : "---"),

                            // [FIX] Status Display Logic
                            StatusDisplay = g.Sum(x => x.Quantity) == 0 ? "❌ CHỜ HỦY" :
                                            (g.Key.GroupStatus == "New" ? "Mới" :
                                            (g.Sum(x => x.Quantity) < g.Sum(x => x.PrintedQuantity) ? "⚠️ Sửa đổi" : "✓ Đã gửi")),

                            // [FIX] Row Color Logic
                            RowColor = g.Sum(x => x.Quantity) == 0 ? "#FFCCCC" : // Red for Cancel All
                                       (g.Key.GroupStatus == "New" ? "#FFF3CD" : // Yellow for New
                                       (g.Sum(x => x.Quantity) < g.Sum(x => x.PrintedQuantity) ? "#FFF3CD" : "#D4EDDA")) // Yellow for Modified, Green for Sent
                        })
                        // --- [SỬA ĐỔI QUAN TRỌNG: LOGIC SẮP XẾP] ---
                        .OrderByDescending(vm => vm.ItemStatus == "New")
                        .ThenByDescending(vm => vm.SortTime)
                        .ThenByDescending(vm => vm.KitchenBatch)
                        .ThenBy(vm => vm.DishName)
                        .ToList();

                    lstOrderDetails.ItemsSource = viewModels;

                    var totalQty = order.OrderDetails.Sum(d => d.Quantity);
                    lblItemCount.Text = $"Số lượng món: {totalQty}";

                    // --- Tính tổng tiền (Code cũ giữ nguyên) ---
                    RecalculateOrderTotals(order);
                    decimal discountValue = (order.DiscountPercent > 0) ? order.SubTotal * (order.DiscountPercent / 100) : order.DiscountAmount;

                    if (discountValue > 0)
                    {
                        lblDiscount.Text = $"-{discountValue:N0}đ";
                        pnlDiscount.Visibility = Visibility.Visible;
                    }
                    else pnlDiscount.Visibility = Visibility.Collapsed;

                    lblTotal.Text = order.FinalAmount.ToString("N0") + "đ";

                    // Logic nút bấm
                    bool hasChanges = order.OrderDetails.Any(d => d.Quantity != d.PrintedQuantity);
                    bool hasValidItems = order.OrderDetails.Any(d => d.Quantity > 0);

                    btnCheckout.IsEnabled = hasValidItems;
                    btnProvisional.IsEnabled = hasValidItems;
                    btnDiscountBill.IsEnabled = hasValidItems;
                    btnDiscountBill.Background = hasValidItems ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#47a67f") // Green
                                                               : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#E9ECEF"); // Gray

                    btnSendKitchen.IsEnabled = hasChanges;
                    btnSendKitchen.Content = "🔔 GỬI BẾP";
                    btnSendKitchen.Background = hasChanges ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#fff3cd")
                                                           : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#6C757D");
                }
                else
                {
                    // Reset giao diện khi bàn trống
                    lstOrderDetails.ItemsSource = null;
                    lblItemCount.Text = "Số lượng món: 0";
                    lblTotal.Text = "0đ";
                    lblSubTotal.Text = "0đ";
                    pnlDiscount.Visibility = Visibility.Collapsed;
                    btnDiscountBill.IsEnabled = false;
                    btnDiscountBill.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#E9ECEF");
                    btnCheckout.IsEnabled = false;
                    btnProvisional.IsEnabled = false;
                    btnSendKitchen.IsEnabled = false;
                    _currentOrderTime = null;
                    lblTableTime.Text = "";
                    _tableTimeTimer.Stop();
                }
            }
        }

        private void LoadMenu()
        {
            using (var db = new AppDbContext())
            {
                var cats = db.Categories
                    .OrderBy(c => c.OrderIndex)
                    .ThenBy(c => c.CategoryID)
                    .ToList();

                _menuCategoryOrderIndexById = cats.ToDictionary(c => c.CategoryID, c => c.OrderIndex);
                var catViewModels = new List<CategoryViewModel> { new CategoryViewModel { CategoryID = 0, CategoryName = "TẤT CẢ" } };
                catViewModels.AddRange(cats.Select(c => new CategoryViewModel { CategoryID = c.CategoryID, CategoryName = c.CategoryName }));

                lstCategories.ItemsSource = catViewModels;
                _allDishes = db.Dishes.Where(d => d.DishStatus == "Active").ToList();

                _dishViewModels = _allDishes.Select(d => new DishViewModel
                {
                    DishID = d.DishID,
                    DishName = d.DishName,
                    Price = Services.PriceService.GetCurrentPrice(d.DishID),
                    CategoryID = d.CategoryID,
                    ImagePath = d.ImagePath
                }).ToList();

                lstCategories.SelectedIndex = 0;
                UpdateDishListDisplay();
            }
        }

        private void UpdateDishListDisplay()
        {
            var filtered = _dishViewModels;

            // Filter by category
            if (lstCategories.SelectedItem is CategoryViewModel selected && selected.CategoryID != 0)
            {
                filtered = filtered.Where(d => d.CategoryID == selected.CategoryID).ToList();
            }

            // Filter by search
            string searchText = txtDishSearch?.Text?.Trim().ToLower() ?? "";
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered.Where(d => MatchDishSearch(d.DishName, searchText)).ToList();
            }

            // Ordering:
            // - When "TẤT CẢ": order by category display order (OrderIndex), then dish name
            // - When a category is chosen: order by dish name
            if (lstCategories.SelectedItem is CategoryViewModel selectedCategory && selectedCategory.CategoryID != 0)
            {
                filtered = filtered
                    .OrderBy(d => d.DishName)
                    .ToList();
            }
            else
            {
                filtered = filtered
                    .OrderBy(d =>
                        _menuCategoryOrderIndexById.TryGetValue(d.CategoryID, out var idx)
                            ? idx
                            : int.MaxValue)
                    .ThenBy(d => d.DishName)
                    .ToList();
            }

            lstDishes.ItemsSource = filtered;
        }

        private string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalized)
            {
                var category = Char.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb
                .ToString()
                .Normalize(NormalizationForm.FormC)
                .Replace('đ', 'd')
                .Replace('Đ', 'D');
        }


        private bool MatchDishSearch(string dishName, string searchText)
        {
            string normalized = RemoveDiacritics(dishName).ToLower();
            string normalizedSearch = RemoveDiacritics(searchText).ToLower();

            // Full name match (handles diacritical marks)
            if (normalized.Contains(normalizedSearch))
                return true;

            // First letter abbreviation match (e.g., "mctc" for "mỳ cay thập cẩm")
            var words = dishName.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            string firstLetters = string.Concat(words.Select(w => RemoveDiacritics(w.Substring(0, 1)).ToLower()));
            if (firstLetters.Contains(normalizedSearch))
                return true;

            // Partial first letter match (e.g., "tc" for "thập cẩm" in "mỳ cay thập cẩm")
            foreach (var word in words)
            {
                if (RemoveDiacritics(word).ToLower().StartsWith(normalizedSearch))
                    return true;
            }

            return false;
        }

        // --- ASYNC VERSIONS FOR HIGH PERFORMANCE ---
        private async Task LoadTablesAsync()
        {
            try
            {
                // Use local copies of filters to avoid threading issues
                int? catId = _selectedCategoryId;

                using var db = new AppDbContext();

                var query = db.Tables
                    .AsNoTracking()
                    .Select(t => new
                    {
                        t.TableID,
                        t.TableName,
                        t.TableStatus,
                        t.CategoryID,
                        CategoryDisplayOrder = t.Category != null ? t.Category.DisplayOrder : int.MaxValue,
                        CategoryBorderColorHex = t.Category != null ? t.Category.BorderColorHex : "#D0D0D0",
                        CategoryIconClass = t.Category != null ? t.Category.IconClass : "fas fa-chair",
                        PendingOrder = t.Orders
                            .Where(o => o.OrderStatus == "Pending")
                            .OrderByDescending(o => o.OrderTime)
                            .Select(o => new { o.OrderTime, o.IsPreCalculated, o.IsRequestingPayment })
                            .FirstOrDefault(),
                        PendingOrderTotal = t.Orders
                            .Where(o => o.OrderStatus == "Pending")
                            .OrderByDescending(o => o.OrderTime)
                            .Select(o => o.FinalAmount)
                            .FirstOrDefault(),
                        PendingOrderHasUnsent = t.Orders
                            .Where(o => o.OrderStatus == "Pending")
                            .OrderByDescending(o => o.OrderTime)
                            .Select(o => o.OrderDetails.Any(d => d.ItemStatus == "New" && d.Quantity > 0))
                            .FirstOrDefault()
                    });

                if (catId.HasValue)
                {
                    query = query.Where(t => t.CategoryID == catId.Value);
                }

                var rows = await query.ToListAsync();

                rows = catId.HasValue
                    ? rows.OrderBy(t => t.TableID).ToList()
                    : rows.OrderBy(t => t.CategoryDisplayOrder)
                          .ThenBy(t => t.CategoryID ?? int.MaxValue)
                          .ThenBy(t => t.TableID)
                          .ToList();

                var viewModels = rows.Select(t =>
                {
                    var pendingTime = (t.TableStatus == "Occupied") ? t.PendingOrder?.OrderTime : (DateTime?)null;
                    return new TableViewModel
                    {
                        TableID = t.TableID,
                        TableName = t.TableName,
                        TableStatus = t.TableStatus,
                        PendingOrderTime = pendingTime,
                        TimeDisplay = pendingTime.HasValue ? FormatElapsedTime(pendingTime.Value) : "",
                        HasProvisionalBill = t.PendingOrder?.IsPreCalculated ?? false,
                        IsRequestingPayment = t.PendingOrder?.IsRequestingPayment ?? false,
                        IsGrayedOut = (_isWaitingForTargetTable || _isWaitingForMoveTargetTable),
                        CategoryBorderBrush = ParseHexBrush(t.CategoryBorderColorHex, Color.FromRgb(208, 208, 208)),
                        CategoryIconGlyph = GetCategoryIconGlyph(t.CategoryIconClass),
                        IsCategoryIconVisible = _showTableCardIcons,
                        TotalAmount = t.PendingOrderTotal,
                        HasUnsentItems = t.PendingOrderHasUnsent
                    };
                }).ToList();

                lstTables.ItemsSource = viewModels;
                ApplyTableSearchFilter();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Async LoadTables Error: " + ex.Message);
            }
        }

        private void TxtTableSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || lstTables == null) return;
            var text = txtTableSearch.Text?.Trim() ?? string.Empty;
            _tableSearchText = text.Equals(TableSearchPlaceholder, StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
            ApplyTableSearchFilter();
        }

        private void TxtTableSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtTableSearch.Text == TableSearchPlaceholder)
            {
                txtTableSearch.Text = string.Empty;
                txtTableSearch.Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51));
            }
        }

        private void TxtTableSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtTableSearch.Text))
            {
                txtTableSearch.Text = TableSearchPlaceholder;
                txtTableSearch.Foreground = new SolidColorBrush(Color.FromRgb(154, 160, 166));
            }
        }

        private void TxtTableSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                ResetTableSearch();
                e.Handled = true;
            }
        }

        private void ResetTableSearch()
        {
            _tableSearchText = string.Empty;
            if (txtTableSearch != null)
            {
                txtTableSearch.Text = TableSearchPlaceholder;
                txtTableSearch.Foreground = new SolidColorBrush(Color.FromRgb(154, 160, 166));
            }
            ApplyTableSearchFilter();
        }

        private void ApplyTableSearchFilter()
        {
            if (lstTables == null) return;
            if (lstTables.ItemsSource == null) return;

            var view = CollectionViewSource.GetDefaultView(lstTables.ItemsSource);
            if (view == null) return;

            if (string.IsNullOrWhiteSpace(_tableSearchText))
            {
                view.Filter = null;
            }
            else
            {
                var keyword = RemoveDiacritics(_tableSearchText).ToLowerInvariant();
                view.Filter = obj =>
                {
                    if (obj is not TableViewModel vm) return false;
                    var name = RemoveDiacritics(vm.TableName ?? string.Empty).ToLowerInvariant();
                    var compact = new string(name.Where(c => !char.IsWhiteSpace(c)).ToArray());
                    var words = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var acronym = string.Concat(words.Select(w => w.Length > 0 ? w[0].ToString() : ""));
                    return name.Contains(keyword)
                           || compact.Contains(keyword)
                           || acronym.Contains(keyword);
                };
            }

            view.Refresh();
        }

        private async Task LoadOrderDetailsAsync(int tableId)
        {
            try
            {
                // 1. Fetch Logic in Background
                var result = await Task.Run(() =>
                {
                    using (var db = new AppDbContext())
                    {
                        var order = db.Orders
                            .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                            .FirstOrDefault(o => o.TableID == tableId && o.OrderStatus == "Pending");

                        if (order == null) return null;

                        // Calculate totals
                        decimal baseTotal = order.OrderDetails.Where(d => d.Quantity > 0).Sum(d => d.Quantity * d.UnitPrice);
                        decimal actualTotal = order.OrderDetails.Where(d => d.Quantity > 0).Sum(d => d.TotalAmount);
                        decimal discountVal = (order.DiscountPercent > 0) ? actualTotal * (order.DiscountPercent / 100) : order.DiscountAmount;
                        decimal final = actualTotal - discountVal;

                        // Create View Models
                        var vms = order.OrderDetails
                            .GroupBy(d => new { d.DishID, GroupStatus = (d.ItemStatus == "New" ? "New" : "SentGroup"), Note = (d.Note ?? "").Trim(), d.DiscountRate })
                            .Select(g => new OrderDetailViewModel
                            {
                                OrderDetailID = g.First().OrderDetailID,
                                DishName = g.First().Dish != null ? g.First().Dish.DishName : "Unknown",
                                UnitPrice = g.First().UnitPrice,
                                DiscountRate = g.First().DiscountRate,
                                SortTime = g.Max(x => x.ItemOrderTime),
                                ItemStatus = g.Key.GroupStatus == "New" ? "New" : (g.Sum(x => x.Quantity) < g.Sum(x => x.PrintedQuantity) ? "Modified" : "Sent"),
                                Note = g.Key.Note,
                                Quantity = g.Sum(x => x.Quantity),
                                TotalAmount = g.Sum(x => x.TotalAmount),
                                KitchenBatch = g.Max(x => x.KitchenBatch),
                                BatchDisplay = g.Sum(x => x.PrintedQuantity) == 0 ? "⏳" : (g.Max(x => x.KitchenBatch) > 0 ? $"Đợt {g.Max(x => x.KitchenBatch)}" : "---"),
                                StatusDisplay = g.Sum(x => x.Quantity) == 0 ? "❌ CHỜ HỦY" : (g.Key.GroupStatus == "New" ? "Mới" : (g.Sum(x => x.Quantity) < g.Sum(x => x.PrintedQuantity) ? "⚠️ Sửa đổi" : "✓ Đã gửi")),
                                RowColor = g.Sum(x => x.Quantity) == 0 ? "#FFCCCC" : (g.Key.GroupStatus == "New" ? "#FFF3CD" : (g.Sum(x => x.Quantity) < g.Sum(x => x.PrintedQuantity) ? "#FFF3CD" : "#D4EDDA"))
                            })
                            .OrderByDescending(vm => vm.ItemStatus == "New")
                            .ThenByDescending(vm => vm.SortTime)
                            .ThenByDescending(vm => vm.KitchenBatch)
                            .ThenBy(vm => vm.DishName)
                            .ToList();

                        return new
                        {
                            ViewModels = vms,
                            HasChanges = order.OrderDetails.Any(d => d.Quantity != d.PrintedQuantity),
                            HasValidItems = order.OrderDetails.Any(d => d.Quantity > 0),
                            BaseTotal = baseTotal,
                            ActualTotal = actualTotal,
                            Order = new { order.SubTotal, order.FinalAmount, order.DiscountPercent, order.DiscountAmount, order.OrderTime, order.FirstSentTime }
                        };
                    }
                });

                // 2. Update UI
                // [FIX] Race Condition: Check if user is still on this table
                if (tableId != _selectedTableId) return;

                if (result != null)
                {
                    lstOrderDetails.ItemsSource = result.ViewModels;

                    var totalQty = result.ViewModels.Sum(vm => vm.Quantity);
                    lblItemCount.Text = $"Số lượng món: {totalQty}";

                    // Update Labels
                    lblSubTotal.Text = result.BaseTotal.ToString("N0") + "đ";
                    lblTotal.Text = result.Order.FinalAmount.ToString("N0") + "đ";

                    // Update Adjustment (Actual - Base)
                    var adjustment = result.ActualTotal - result.BaseTotal;
                    if (adjustment != 0)
                    {
                        pnlAdjustment.Visibility = Visibility.Visible;
                        if (adjustment > 0)
                        {
                            lblAdjustmentTitle.Text = "Tăng giá món: ";
                            lblAdjustment.Text = $"+{adjustment:N0}đ";
                            lblAdjustment.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#FD7E14");
                        }
                        else
                        {
                            lblAdjustmentTitle.Text = "Giảm giá món: ";
                            lblAdjustment.Text = $"{adjustment:N0}đ";
                            lblAdjustment.Foreground = System.Windows.Media.Brushes.Red;
                        }
                    }
                    else
                    {
                        pnlAdjustment.Visibility = Visibility.Collapsed;
                    }

                    decimal dVal = (result.Order.DiscountPercent > 0) ? result.Order.SubTotal * (result.Order.DiscountPercent / 100) : result.Order.DiscountAmount;
                    if (dVal > 0)
                    {
                        lblDiscount.Text = $"-{dVal:N0}đ";
                        pnlDiscount.Visibility = Visibility.Visible;
                    }
                    else pnlDiscount.Visibility = Visibility.Collapsed;

                    // Update Buttons
                    btnCheckout.IsEnabled = result.HasValidItems;
                    btnProvisional.IsEnabled = result.HasValidItems;
                    btnSendKitchen.IsEnabled = result.HasChanges;
                    btnSendKitchen.Content = "🔔 GỬI BẾP";
                    btnSendKitchen.Background = result.HasChanges ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#fff3cd") : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#6C757D");

                    // Timer logic if needed (Assuming timer handles itself or only stopped when leaving table)
                    // [MODIFIED] Start Timer immediately using OrderTime
                    if (result.Order.OrderTime != DateTime.MinValue)
                    {
                        _currentOrderTime = result.Order.OrderTime;
                        if (!_tableTimeTimer.IsEnabled) _tableTimeTimer.Start();
                        // Async context, invoke tick on dispatcher if needed, but Start() is enough for next tick
                    }
                }
                else
                {
                    // Empty table logic
                    lstOrderDetails.ItemsSource = null;
                    lblItemCount.Text = "Số lượng món: 0";
                    lblTotal.Text = "0đ";
                    lblSubTotal.Text = "0đ";
                    pnlDiscount.Visibility = Visibility.Collapsed;
                    btnCheckout.IsEnabled = false;
                    btnProvisional.IsEnabled = false;
                    btnSendKitchen.IsEnabled = false;
                }
            }
            catch { }
        }

        private void lstCategories_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDishListDisplay();

        private void TxtDishSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateDishListDisplay();

        // --- 3. THAO TÁC NHANH TRÊN MÓN ĂN ---

        // A. Nhấn vào món -> Thêm ngay
        private void Dish_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is int dishId)
            {
                AddDishToOrder(_selectedTableId, dishId);
                // Reset search after selecting dish
                txtDishSearch.Clear();
            }
        }

        private void AddDishToOrder(int tableId, int dishId)
        {
            using (var db = new AppDbContext())
            {
                // 1. Lấy đơn hàng
                var order = db.Orders.Include(o => o.OrderDetails)
                                     .FirstOrDefault(o => o.TableID == tableId && o.OrderStatus == "Pending");

                if (order == null)
                {
                    // ... (Giữ nguyên logic tạo order mới của bạn) ...
                    int? currentAccId = UserSession.AccID > 0 ? UserSession.AccID : (db.Accounts.FirstOrDefault()?.AccID);
                    order = new Order
                    {
                        TableID = tableId,
                        AccID = currentAccId,
                        OrderTime = DateTime.Now,
                        OrderStatus = "Pending",
                        PaymentMethod = "Cash",
                        OrderDetails = new List<OrderDetail>()
                    };
                    db.Orders.Add(order);
                    var table = db.Tables.Find(tableId);
                    if (table != null) table.TableStatus = "Occupied";
                }

                // 2. Tìm dòng để gộp: PHẢI LÀ MÓN MỚI (New) VÀ KHÔNG GHI CHÚ
                var existingDetail = order.OrderDetails
                    .FirstOrDefault(d => d.DishID == dishId
                                      && d.ItemStatus == "New" // <--- QUAN TRỌNG: Chỉ gộp vào món chưa gửi
                                      && (string.IsNullOrEmpty(d.Note)));

                var dishInfo = db.Dishes.Find(dishId);
                if (dishInfo == null) return;

                // [FIX] Lấy giá hiện tại (áp dụng rule giá nếu có)
                decimal currentPrice = Services.PriceService.GetCurrentPrice(dishId);

                if (existingDetail != null)
                {
                    // TÌM THẤY món New -> Cộng dồn
                    // Cập nhật lại giá mới nhất cho dòng đang chờ (nếu giá có thay đổi)
                    existingDetail.UnitPrice = currentPrice;
                    existingDetail.Quantity++;
                    existingDetail.TotalAmount = existingDetail.Quantity * existingDetail.UnitPrice;
                    // Update order time so newest selection stays on top
                    existingDetail.ItemOrderTime = DateTime.Now;
                }
                else
                {
                    // KHÔNG TÌM THẤY (hoặc chỉ có món Sent) -> TẠO DÒNG MỚI
                    order.OrderDetails.Add(new OrderDetail
                    {
                        DishID = dishId,
                        Quantity = 1,
                        UnitPrice = currentPrice,
                        ItemStatus = "New", // Luôn là New
                        PrintedQuantity = 0,
                        TotalAmount = currentPrice,
                        Note = "",
                        ItemOrderTime = DateTime.Now
                    });
                }

                // Tính tổng tiền
                order.SubTotal = order.OrderDetails.Sum(d => d.TotalAmount);
                order.FinalAmount = order.SubTotal;

                db.SaveChanges();

                if (_selectedTableId == tableId) LoadOrderDetails(tableId);
                ShowToast($"Đã chọn: {dishInfo.DishName}");
                NotifyTableUpdated(tableId);
            }
        }
        // --- 2. NÚT CỘNG (+) (CHỈ CỘNG SỐ) ---
        private void BtnIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long detailId)
            {
                using (var db = new AppDbContext())
                {
                    var detail = db.OrderDetails.Find(detailId);
                    if (detail == null) return;

                    // [FIX] Group Logic: Nếu click vào nhóm Sent/Modified
                    if (detail.ItemStatus == "Sent" || detail.ItemStatus == "Modified")
                    {
                        // Tìm xem trong nhóm (Cùng Dish + Note + Đã gửi) có món nào đang bị giảm (Qty < Printed) không?
                        // Nếu có -> Tăng nó lên (Undo Cancel)
                        // Nếu không -> Gọi AddDishToOrder để thêm mới

                        var note = (detail.Note ?? "").Trim();
                        var candidate = db.OrderDetails
                            .Where(d => d.OrderID == detail.OrderID
                                     && d.DishID == detail.DishID
                                     && (d.Note ?? "").Trim() == note
                                     && (d.ItemStatus == "Sent" || d.ItemStatus == "Modified")
                                     && d.Quantity < d.PrintedQuantity) // ĐK: Đang bị giảm
                            .OrderByDescending(d => d.KitchenBatch) // Ưu tiên đợt mới nhất
                            .FirstOrDefault();

                        if (candidate != null)
                        {
                            // Restore Quantity
                            candidate.Quantity++;
                            candidate.TotalAmount = candidate.Quantity * candidate.UnitPrice * (1 - candidate.DiscountRate / 100);

                            // Restore Status if full
                            if (candidate.ItemStatus == "Modified" && candidate.Quantity == candidate.PrintedQuantity)
                            {
                                candidate.ItemStatus = "Sent";
                            }
                            db.SaveChanges();
                            RecalculateOrder(db, candidate.OrderID);
                            LoadOrderDetails(_selectedTableId);
                            NotifyTableUpdated(_selectedTableId);
                        }
                        else
                        {
                            // Full hết rồi -> Thêm món mới
                            AddDishToOrder(_selectedTableId, detail.DishID);
                        }
                    }
                    else
                    {
                        // Món đang chờ (New) -> Tăng số lượng bình thường
                        detail.Quantity++;
                        detail.TotalAmount = detail.Quantity * detail.UnitPrice * (1 - detail.DiscountRate / 100);
                        detail.ItemOrderTime = DateTime.Now;
                        db.SaveChanges();
                        RecalculateOrder(db, detail.OrderID);
                        LoadOrderDetails(_selectedTableId);
                        NotifyTableUpdated(_selectedTableId);
                    }
                }
            }
        }

        // --- 3. NÚT TRỪ (-) (GIẢM SỐ, VỀ 0 CŨNG KHÔNG XÓA NGAY) ---
        private void BtnDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long detailId)
            {
                using (var db = new AppDbContext())
                {
                    // Lấy detail gốc để biết Context (Order, Dish, Note)
                    var originalDetail = db.OrderDetails.Include(d => d.Dish).FirstOrDefault(d => d.OrderDetailID == detailId);
                    if (originalDetail == null) return;

                    OrderDetail targetDetail = originalDetail;

                    // [FIX] Group Logic: Nếu Click vào nhóm Sent/Modified
                    // Cần tìm món nào trong nhóm CÒN SỐ LƯỢNG để trừ
                    if (originalDetail.ItemStatus == "Sent" || originalDetail.ItemStatus == "Modified")
                    {
                        var note = (originalDetail.Note ?? "").Trim();
                        // Ưu tiên trừ món Batch cao nhất trước (Mới gọi nhất)
                        var candidate = db.OrderDetails
                            .Where(d => d.OrderID == originalDetail.OrderID
                                     && d.DishID == originalDetail.DishID
                                     && (d.Note ?? "").Trim() == note
                                     && (d.ItemStatus == "Sent" || d.ItemStatus == "Modified")
                                     && d.Quantity > 0)
                            .OrderByDescending(d => d.KitchenBatch)
                            .FirstOrDefault();

                        // Nếu tìm thấy ứng viên tốt hơn, đổi target
                        if (candidate != null) targetDetail = candidate;
                    }

                    long currentOrderId = targetDetail.OrderID;

                    // 1. GIẢM SỐ LƯỢNG (Nếu đang > 0)
                    if (targetDetail.Quantity > 0)
                    {
                        // [NEW] Log if Item was Sent
                        if (targetDetail.ItemStatus == "Sent")
                        {
                            var log = new CancelledLog
                            {
                                TableID = db.Orders.Where(o => o.OrderID == currentOrderId).Select(o => o.TableID).FirstOrDefault(),
                                OrderID = targetDetail.OrderID,
                                DishName = targetDetail.Dish?.DishName ?? "Unknown",
                                Quantity = 1, // Decrease 1
                                Amount = targetDetail.UnitPrice,
                                DeletedBy = UserSession.AccName ?? "Admin",
                                CancelTime = DateTime.Now
                            };
                            db.CancelledLogs.Add(log);

                            targetDetail.ItemStatus = "Modified";
                        }

                        targetDetail.Quantity--;
                        targetDetail.TotalAmount = targetDetail.Quantity * targetDetail.UnitPrice * (1 - targetDetail.DiscountRate / 100);
                    }

                    // 2. LOGIC XÓA
                    bool isRemoved = false;

                    // Nếu món Mới (chưa in) về 0 -> XÓA
                    if (targetDetail.Quantity == 0 && targetDetail.PrintedQuantity == 0)
                    {
                        db.OrderDetails.Remove(targetDetail);
                        isRemoved = true;
                    }
                    // Nếu món Cũ (đã in) -> Giữ lại số 0 để báo hủy (Không xóa dòng này ngay)

                    db.SaveChanges();

                    // 3. QUAN TRỌNG: KIỂM TRA XEM ĐƠN HÀNG CÒN MÓN NÀO KHÔNG?
                    if (isRemoved)
                    {
                        bool hasAnyItem = db.OrderDetails.Any(d => d.OrderID == currentOrderId);
                        if (!hasAnyItem)
                        {
                            var order = db.Orders.Find(currentOrderId);
                            if (order != null)
                            {
                                var table = db.Tables.Find(order.TableID);
                                if (table != null) table.TableStatus = "Empty";
                                db.Orders.Remove(order);
                                db.SaveChanges();
                                LoadTables();
                                LoadOrderDetails(_selectedTableId);
                                NotifyTableUpdated(order.TableID ?? _selectedTableId);
                                return;
                            }
                        }
                    }

                    RecalculateOrder(db, targetDetail.OrderID);
                    LoadOrderDetails(_selectedTableId);
                    NotifyTableUpdated(_selectedTableId);
                }
            }
        }

        // --- C. NÚT SỬA (✎) -> MỞ DISCOUNT WINDOW ---
        private void BtnEditItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long detailId)
            {
                using (var db = new AppDbContext())
                {
                    var detail = db.OrderDetails.Find(detailId);
                    if (detail == null) return;

                    // New flow: show original price + allow entering a new price directly.
                    // Increase/decrease is inferred by comparing New Price vs Original Price.
                    decimal originalPrice = detail.UnitPrice;
                    decimal currentEffectivePrice = originalPrice * (1 - detail.DiscountRate / 100m);

                    var dialog = new EditItemPriceWindow(originalPrice, currentEffectivePrice)
                    {
                        Owner = this
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        decimal newPrice = dialog.NewPrice;

                        // Apply discount to the whole visible group (same Dish + Note + GroupStatus + DiscountRate)
                        var groupNote = (detail.Note ?? "").Trim();
                        bool isNewGroup = detail.ItemStatus == "New";
                        var currentRate = detail.DiscountRate;

                        var groupDetails = db.OrderDetails
                            .Where(d => d.OrderID == detail.OrderID
                                        && d.DishID == detail.DishID
                                        && ((d.Note ?? "").Trim() == groupNote)
                                        && d.DiscountRate == currentRate
                                        && (isNewGroup ? d.ItemStatus == "New" : d.ItemStatus != "New"))
                            .ToList();

                        // Derive DiscountRate from new price (positive = decrease, negative = increase)
                        decimal newRate = 0;
                        if (originalPrice > 0)
                        {
                            newRate = ((originalPrice - newPrice) / originalPrice) * 100m;
                            if (Math.Abs(newRate) < 0.0001m) newRate = 0;
                        }

                        foreach (var d in groupDetails)
                        {
                            if (originalPrice <= 0)
                            {
                                d.UnitPrice = newPrice;
                                d.DiscountRate = 0;
                                d.TotalAmount = d.Quantity * d.UnitPrice;
                            }
                            else
                            {
                                d.DiscountRate = newRate;
                                d.TotalAmount = d.Quantity * d.UnitPrice * (1 - d.DiscountRate / 100m);
                            }
                        }

                        db.SaveChanges();
                        RecalculateOrder(db, detail.OrderID);
                        LoadOrderDetails(_selectedTableId);

                        NotifyTableUpdated(_selectedTableId);

                        ShowToast("Đã cập nhật giá món!");
                    }
                }
            }
        }

        // --- 2. CẬP NHẬT NÚT GỬI BẾP: Tự động dọn dẹp đơn rỗng ---
        private void BtnSendKitchen_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTableId == 0) return;

            Task.Run(() =>
            {
                using (var db = new AppDbContext())
                {
                    var order = db.Orders
                        .Include(o => o.Table)
                        .Include(o => o.Account) // [FIX] Include Account to get Staff Name
                        .Include(o => o.OrderDetails).ThenInclude(d => d.Dish).ThenInclude(c => c.Category)
                        .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                    if (order == null) return;

                    // Lấy các món có thay đổi
                    var changedItems = order.OrderDetails.Where(d => d.Quantity != d.PrintedQuantity).ToList();
                    if (!changedItems.Any()) return;

                    int currentMaxBatch = order.OrderDetails.Max(d => (int?)d.KitchenBatch) ?? 0;
                    bool isFirstSend = (currentMaxBatch == 0); // Lần gửi đầu tiên
                    int nextBatch = currentMaxBatch + 1;

                    // --- 1. TẠO DANH SÁCH IN (ẢO) TRƯỚC KHI SỬA DB ---
                    var itemsToPrint = new List<OrderDetail>();

                    foreach (var item in changedItems)
                    {
                        int diff = item.Quantity - item.PrintedQuantity;
                        if (diff == 0) continue;

                        // Tạo món ảo để in
                        var printItem = new OrderDetail
                        {
                            Dish = item.Dish,          // Giữ thông tin món (để lấy Tên, PrinterID)
                            DishID = item.DishID,      // [FIX] Gán DishID để không bị gộp sai
                            Quantity = diff,           // Số lượng thay đổi (Dương = Thêm, Âm = Hủy)
                            Note = item.Note,
                            KitchenBatch = nextBatch   // Gán đợt mới
                        };
                        itemsToPrint.Add(printItem);
                    }

                    if (itemsToPrint.Any())
                    {
                        string senderName = UserSession.AccName ?? "Admin";
                        var printOk = Services.PrintService.PrintKitchen(order, itemsToPrint, nextBatch, senderName);
                        if (!printOk)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                ShowToast("❌ In bếp thất bại, vui lòng thử lại", 2500);
                            }));
                            return;
                        }
                    }

                    // --- 2. CẬP NHẬT DATABASE ---
                    // Set FirstSentTime on first send
                    if (isFirstSend)
                    {
                        order.FirstSentTime = DateTime.Now;
                    }

                    foreach (var item in changedItems)
                    {
                        // Cập nhật số lượng đã in
                        if (item.Quantity > item.PrintedQuantity) item.KitchenBatch = nextBatch;
                        item.PrintedQuantity = item.Quantity;

                        if (item.Quantity == 0)
                        {
                            db.OrderDetails.Remove(item); // Xóa món SL=0
                        }
                        else
                        {
                            item.ItemStatus = "Sent";
                        }
                    }
                    db.SaveChanges(); // Lưu thay đổi (lúc này món hủy sẽ mất khỏi DB)

                    // Start timer on first send
                    if (isFirstSend)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            _currentOrderTime = order.OrderTime;
                            if (!_tableTimeTimer.IsEnabled)
                            {
                                _tableTimeTimer.Start();
                            }
                        }));
                    }
                    // In đã xử lý trước khi cập nhật DB

                    // ⭐ Notify mobile via SignalR
                    NotifyTableUpdated(_selectedTableId);

                    // --- 4. KIỂM TRA ĐƠN RỖNG ---
                    bool isOrderEmpty = !db.OrderDetails.Any(d => d.OrderID == order.OrderID);
                    if (isOrderEmpty)
                    {
                        db.Orders.Remove(order);
                        var table = db.Tables.Find(order.TableID);
                        if (table != null) table.TableStatus = "Empty";
                        db.SaveChanges();

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            _ = LoadTablesAsync();
                            _ = LoadOrderDetailsAsync(_selectedTableId);
                            ShowToast("✅ Đã hủy món & Trả bàn trống");
                            if (_autoReturnKitchen) BtnBackToTables_Click(null, null);
                        }));
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            _ = LoadOrderDetailsAsync(_selectedTableId);
                            ShowToast($"✅ Đã gửi Đợt {nextBatch}!");
                            if (_autoReturnKitchen) BtnBackToTables_Click(null, null);
                        }));
                    }
                }
            });
        }
        // --- CÁC HÀM HỖ TRỢ KHÁC (GIỮ NGUYÊN) ---
        private void RecalculateOrder(AppDbContext db, long orderId)
        {
            var order = db.Orders.Include(o => o.OrderDetails).FirstOrDefault(o => o.OrderID == orderId);
            if (order == null) return;
            order.SubTotal = order.OrderDetails.Where(d => d.ItemStatus != "Cancelled").Sum(d => d.TotalAmount);
            decimal discount = (order.DiscountPercent > 0) ? order.SubTotal * order.DiscountPercent / 100 : order.DiscountAmount;
            order.FinalAmount = order.SubTotal - discount;
            if (order.FinalAmount < 0) order.FinalAmount = 0;
            db.SaveChanges();
        }

        public async void ShowToast(string message, int durationMs = 1500)
        {
            lblToastMessage.Text = message;
            bdToast.Visibility = Visibility.Visible;
            await Task.Delay(durationMs);
            bdToast.Visibility = Visibility.Collapsed;
        }

        private void ShowToastPersistent(string message)
        {
            lblToastMessage.Text = message;
            bdToast.Visibility = Visibility.Visible;
        }

        private void HideToast()
        {
            bdToast.Visibility = Visibility.Collapsed;
        }



        private void BtnDiscountBill_Click(object sender, RoutedEventArgs e)
        {
            // (Giữ nguyên logic cũ của bạn)
            if (_selectedTableId == 0) return;
            using (var db = new AppDbContext())
            {
                var order = db.Orders.FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");
                if (order == null) return;

                bool isPercent = order.DiscountPercent > 0;
                decimal currentVal = isPercent ? order.DiscountPercent : order.DiscountAmount;
                // [MODIFIED] Pass MaxLimit = SubTotal
                var dialog = new DiscountWindow(currentVal, isPercentMode: isPercent, isEditItem: false, maxLimit: order.SubTotal)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true)
                {
                    if (dialog.IsPercentage) { order.DiscountPercent = dialog.ResultValue; order.DiscountAmount = 0; }
                    else { order.DiscountAmount = dialog.ResultValue; order.DiscountPercent = 0; }
                    db.SaveChanges();
                    RecalculateOrder(db, order.OrderID);
                    LoadOrderDetails(_selectedTableId);
                    NotifyTableUpdated(_selectedTableId);
                }
            }
        }

        private void BtnBackToAdmin_Click(object sender, RoutedEventArgs e)
        {
            AdminWindow admin = new AdminWindow(); admin.Show(); this.Close();
        }

        // --- REALTIME UPDATE COALESCING (ANTI-LAG) ---
        // Khi mobile spam update (tăng số lượng liên tục), SignalR sẽ bắn rất nhiều "TableUpdated".
        // Nếu mỗi event đều trigger load + update UI ngay, UI thread sẽ bị queue và trông như "đơ".
        // Các biến dưới đây giúp debounce + chạy tuần tự, luôn cập nhật trạng thái mới nhất.
        private int _pendingRealtimeTableId = -1;
        private int _realtimeRefreshRunning = 0;
        private CancellationTokenSource? _realtimeDebounceCts;

        private Task InvokeOnUiAsync(Func<Task> action)
        {
            if (Dispatcher.CheckAccess()) return action();
            return Dispatcher.InvokeAsync(action).Task.Unwrap();
        }

        private void QueueRealtimeTableUpdate(int tableId)
        {
            // Keep only the latest tableId; LoadTables will refresh all statuses anyway.
            Interlocked.Exchange(ref _pendingRealtimeTableId, tableId);

            // Debounce bursts (spam + button hold)
            try { _realtimeDebounceCts?.Cancel(); } catch { }
            var cts = new CancellationTokenSource();
            _realtimeDebounceCts = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(120, cts.Token);
                    await ProcessRealtimeTableUpdatesAsync();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"Realtime debounce error: {ex.Message}");
                }
            });
        }

        private async Task ProcessRealtimeTableUpdatesAsync()
        {
            // Ensure single refresh pipeline at a time
            if (Interlocked.CompareExchange(ref _realtimeRefreshRunning, 1, 0) != 0) return;

            try
            {
                while (true)
                {
                    int tableId = Interlocked.Exchange(ref _pendingRealtimeTableId, -1);
                    if (tableId <= 0) break;

                    // Always refresh table grid; refresh details only if it is the selected table.
                    int selected = _selectedTableId;
                    await InvokeOnUiAsync(async () =>
                    {
                        if (selected == tableId)
                        {
                            await LoadOrderDetailsAsync(tableId);
                        }
                        await LoadTablesAsync();
                    });

                    // If another event arrived during refresh, loop and pick latest.
                }
            }
            finally
            {
                Interlocked.Exchange(ref _realtimeRefreshRunning, 0);
                // If a new update arrived right after we released, kick again.
                if (Volatile.Read(ref _pendingRealtimeTableId) > 0)
                {
                    QueueRealtimeTableUpdate(Volatile.Read(ref _pendingRealtimeTableId));
                }
            }
        }

        // --- 6. SIGNALR & CHECKOUT & IN BẾP (MAIN) ---
        private HubConnection BuildRealtimeConnection(bool forceWebSockets)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl("http://localhost:5000/posHub", options =>
                {
                    if (forceWebSockets)
                    {
                        options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
                        options.SkipNegotiation = true;
                    }
                    else
                    {
                        // Fallback to normal negotiation (allows SSE/LongPolling if WS is blocked)
                        options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets |
                                             Microsoft.AspNetCore.Http.Connections.HttpTransportType.ServerSentEvents |
                                             Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                        options.SkipNegotiation = false;
                    }
                })
                .WithAutomaticReconnect()
                .Build();

            connection.On<int>("TableRequestPayment", (tableId) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _tablesRequestingPayment.Add(tableId);
                    // Thay vì LoadTables sync (dễ lag), coalesce vào pipeline async
                    QueueRealtimeTableUpdate(tableId);
                    ShowToast($"🔔 Bàn {tableId} yêu cầu thanh toán!", 3000);
                });
            });

            connection.On<int>("TableUpdated", (id) =>
            {
                QueueRealtimeTableUpdate(id);
            });

            connection.On<int, int>("TableMoved", (sourceTableId, targetTableId) =>
            {
                Dispatcher.Invoke(() =>
                {
                    QueueRealtimeTableUpdate(sourceTableId);
                    QueueRealtimeTableUpdate(targetTableId);

                    if (_selectedTableId == sourceTableId)
                    {
                        SelectAndLoadTable(targetTableId);
                        ShowToast($"🔄 Đơn đã chuyển sang bàn {targetTableId}", 2000);
                    }
                    else if (_selectedTableId == targetTableId)
                    {
                        _ = LoadOrderDetailsAsync(targetTableId);
                    }
                });
            });

            // [NEW] Listen for Order Notifications
            connection.On<string>("ReceiveOrderNotification", (msg) =>
            {
                AppendActivityLog(msg);
            });

            connection.On<string, decimal>("PaymentCompleted", (tableName, total) =>
            {
                Dispatcher.Invoke(() =>
                {
                    lblLatestPayment.Text = $"{tableName}: {total:N0}đ";
                });
            });

            return connection;
        }

        private async void SetupRealtime()
        {
            _connection = BuildRealtimeConnection(forceWebSockets: true);

            try
            {
                await _connection.StartAsync();
            }
            catch (Exception ex)
            {
                // If WS is blocked on some networks/devices, fall back to negotiated transport.
                Console.WriteLine($"SignalR WS start failed, falling back: {ex.Message}");
                try
                {
                    await _connection.DisposeAsync();
                }
                catch { }

                _connection = BuildRealtimeConnection(forceWebSockets: false);
                try { await _connection.StartAsync(); } catch { }
            }
        }

        // ⭐ Helper: Gửi sự kiện cập nhật bàn cho mobile (via SignalR)
        // [SỬA] Hàm gửi thông báo: Dùng Server Host trực tiếp (Tin cậy hơn)
        private async void NotifyTableUpdated(int tableId)
        {
            try
            {
                // Cách 1 (Tối ưu): Dùng Hub của Server đang chạy trên App
                if (App.WebHost != null)
                {
                    var hubContext = App.WebHost.Services.GetService<IHubContext<PosHub>>();
                    if (hubContext != null)
                    {
                        await hubContext.Clients.All.SendAsync("TableUpdated", tableId);
                        return;
                    }
                }

                // Cách 2 (Fallback): Dùng Client connection nếu không lấy được Server Hub
                if (_connection != null && _connection.State == HubConnectionState.Connected)
                {
                    await _connection.SendAsync("TableUpdated", tableId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SignalR send error: {ex.Message}");
            }
        }

        private async void NotifyTableMoved(int sourceTableId, int targetTableId)
        {
            try
            {
                if (App.WebHost != null)
                {
                    var hubContext = App.WebHost.Services.GetService<IHubContext<PosHub>>();
                    if (hubContext != null)
                    {
                        await hubContext.Clients.All.SendAsync("TableMoved", sourceTableId, targetTableId);
                        return;
                    }
                }

                if (_connection != null && _connection.State == HubConnectionState.Connected)
                {
                    await _connection.SendAsync("TableMoved", sourceTableId, targetTableId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SignalR send move error: {ex.Message}");
            }
        }

        private void BtnPrintProvisional_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTableId == 0) return;

            int orderId;
            using (var db = new AppDbContext())
            {
                var order = db.Orders
                    .Include(o => o.OrderDetails)
                    .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                if (order == null)
                {
                    MessageBox.Show("Không tìm thấy đơn hàng!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!order.OrderDetails.Any(d => d.Quantity > 0))
                {
                    MessageBox.Show("Đơn hàng đang trống!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                order.IsPreCalculated = true;
                db.SaveChanges();
                orderId = (int)order.OrderID;
            }

            PrintService.PrintBill(orderId, isProvisional: true);
            ShowToast("🧾 Đã in tạm tính thành công!");
            NotifyTableUpdated(_selectedTableId);

            NotifyTableUpdated(_selectedTableId);

            // [NEW] Check Auto Return (Provisional)
            if (_autoReturnProvisional)
            {
                BtnBackToTables_Click(null, null);
            }
        }

        private void btnCheckout_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTableId == 0) return;
            int orderId = 0;
            decimal finalAmount = 0;
            string tableName = string.Empty;

            using (var db = new AppDbContext())
            {
                var order = db.Orders.Include(o => o.OrderDetails)
                                     .Include(o => o.Table)
                                     .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                if (order != null)
                {
                    orderId = (int)order.OrderID;
                    finalAmount = order.FinalAmount;
                    tableName = order.Table?.TableName ?? $"Bàn {_selectedTableId}";

                    bool hasValidItems = order.OrderDetails.Any(d => d.Quantity > 0);
                    if (!hasValidItems)
                    {
                        MessageBox.Show("Đơn hàng đang trống!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (finalAmount <= 0)
                    {
                        // [MODIFIED] Removed confirmation dialog for 0đ bill
                        // if (MessageBox.Show("Thanh toán 0đ để đóng bàn?", "Xác nhận", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    }
                }
            }

            if (orderId == 0) return;

            // Mở cửa sổ thanh toán
            var payWindow = new PaymentWindow(orderId);
            payWindow.ShowDialog();

            if (payWindow.IsPaidSuccess)
            {
                UpdateLatestPaymentLabel(orderId, tableName, finalAmount);
                // --- SỬA: Kiểm tra ToggleButton (tglPrintBill) VÀ lựa chọn từ PaymentWindow ---
                if (tglPrintBill.IsChecked == true && payWindow.ShouldPrint)
                {
                    Services.PrintService.PrintBill(orderId);
                    ShowToast("🖨 Đã in hóa đơn & Thanh toán xong!");
                }
                else
                {
                    ShowToast("💰 Thanh toán thành công (Không in)");
                }

                // Reset table time
                _currentOrderTime = null;
                lblTableTime.Text = "";
                _tableTimeTimer.Stop();

                LoadTables();
                LoadTables();
                // [MODIFIED] Return to Table List after Payment (ONLY IF SETTING IS ON)
                if (_autoReturnPay)
                {
                    BtnBackToTables_Click(null, null);
                }
            }
            else if (payWindow.IsProvisionalSuccess)
            {
                LoadTables(); // Update icon
                ShowToast("🧾 Đã in tạm tính thành công!");
            }
        }

        private void UpdateLatestPaymentLabel(int orderId, string fallbackTableName, decimal fallbackAmount)
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var order = db.Orders
                        .Include(o => o.Table)
                        .FirstOrDefault(o => o.OrderID == orderId);

                    var tableName = order?.Table?.TableName ?? fallbackTableName;
                    var amount = order?.FinalAmount ?? fallbackAmount;
                    if (!string.IsNullOrWhiteSpace(tableName))
                    {
                        lblLatestPayment.Text = $"{tableName}: {amount:N0}đ";
                    }
                }
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(fallbackTableName))
                {
                    lblLatestPayment.Text = $"{fallbackTableName}: {fallbackAmount:N0}đ";
                }
            }
        }

        private void BtnHistory_Click(object sender, RoutedEventArgs e)
        {
            var historyWin = new HistoryWindow();
            historyWin.ShowDialog();
        }
        private void TxtNote_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox txt && txt.Tag is long detailId)
            {
                string newNote = txt.Text.Trim(); // Lấy nội dung mới

                using (var db = new AppDbContext())
                {
                    var detail = db.OrderDetails.Find(detailId);
                    if (detail != null)
                    {
                        // BUGFIX: Chỉ cho phép sửa ghi chú cho món CHƯA gửi bếp.
                        // Nếu sửa ghi chú cho món đã gửi bếp/đang cập nhật (Sent/Modified) sẽ bị "dính" ghi chú vào món còn lại sau khi giảm số lượng.
                        if (!string.Equals(detail.ItemStatus, "New", StringComparison.OrdinalIgnoreCase))
                        {
                            // Revert UI text (TwoWay binding safety)
                            txt.Text = (detail.Note ?? "").Trim();
                            return;
                        }

                        // Chỉ lưu nếu nội dung thay đổi
                        string oldNote = detail.Note ?? "";
                        if (oldNote != newNote)
                        {
                            detail.Note = newNote;

                            db.SaveChanges();

                            // Lưu ý: Không cần reload lại toàn bộ bảng để tránh bị mất focus hoặc giật
                            // Chỉ cần cập nhật trạng thái nút Gửi bếp nếu cần
                            btnSendKitchen.IsEnabled = true;
                            btnSendKitchen.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#fff3cd");
                            btnSendKitchen.Content = "🔔 GỬI BẾP";
                        }
                    }
                }
            }
        }

        private void BtnMoveTable_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTableId == 0) return;

            using (var db = new AppDbContext())
            {
                var currentOrder = db.Orders
                    .Include(o => o.Table)
                    .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                if (currentOrder == null)
                {
                    ShowToast("❌ Không có đơn hàng để chuyển!", 2000);
                    return;
                }

                // Check if there are any available tables
                var availableTables = db.Tables.Where(t => t.TableID != _selectedTableId).ToList();
                if (!availableTables.Any())
                {
                    ShowToast("❌ Không có bàn khác để chuyển!", 2000);
                    return;
                }
            }

            // Enter move mode
            _isWaitingForMoveTargetTable = true;

            ShowToastPersistent("📍 Chọn bàn đích để chuyển...");

            // Switch back to table list view
            pnlMenu.Visibility = Visibility.Collapsed;
            pnlTableList.Visibility = Visibility.Visible;
            btnCancelMove.Visibility = Visibility.Visible; // [NEW] Show Cancel button

            LoadTables(); // [NEW] Refresh to gray out source table
            _tableTimeTimer.Stop();
        }

        private void BtnCancelMove_Click(object sender, RoutedEventArgs e)
        {
            _isWaitingForMoveTargetTable = false;
            btnCancelMove.Visibility = Visibility.Collapsed;
            HideToast();

            // Return to Menu / Order Screen for the current table
            SelectAndLoadTable(_selectedTableId);
        }

        private async Task ExecuteMoveTableAsync(int targetTableId)
        {
            var sourceTableId = _selectedTableId;
            if (targetTableId == sourceTableId)
            {
                ShowToast("❌ Vui lòng chọn bàn khác!", 2000);
                _isWaitingForMoveTargetTable = false;
                return;
            }

            ShowToastPersistent("⏳ Đang chuyển bàn...");

            var result = await Task.Run(() =>
            {
                try
                {
                    _moveTableLock.Wait();
                    using (var db = new AppDbContext())
                    {
                        using var transaction = db.Database.BeginTransaction();
                        var sourceOrder = db.Orders
                            .Include(o => o.Table)
                            .FirstOrDefault(o => o.TableID == sourceTableId && o.OrderStatus == "Pending");

                        var targetOrder = db.Orders
                            .FirstOrDefault(o => o.TableID == targetTableId && o.OrderStatus == "Pending");

                        if (sourceOrder == null)
                        {
                            return (Success: false, Error: "❌ Không có đơn hàng để chuyển!", TargetTableId: targetTableId, OrderIdToNotify: 0L, OldName: "", NewName: "");
                        }

                        // Lưu tên bàn cũ trước khi cập nhật
                        string oldTableName = sourceOrder.Table?.TableName ?? $"Bàn {sourceTableId}";

                        // If target table already has an order, merge them
                        if (targetOrder != null)
                        {
                            var sourceOrderId = sourceOrder.OrderID;
                            var targetOrderId = targetOrder.OrderID;
                            var sourceDetailCount = db.OrderDetails.Count(d => d.OrderID == sourceOrderId);
                            var movedRows = db.Database.ExecuteSqlInterpolated(
                                $"UPDATE OrderDetails SET OrderID = {targetOrderId} WHERE OrderID = {sourceOrderId}");

                            LogMoveTableDesktop($"Move merge sourceTable={sourceTableId} targetTable={targetTableId} sourceOrder={sourceOrderId} targetOrder={targetOrderId} sourceDetailCount={sourceDetailCount} movedRows={movedRows}");

                            if (sourceDetailCount != movedRows)
                            {
                                transaction.Rollback();
                                LogMoveTableDesktop($"Move merge rollback mismatch sourceOrder={sourceOrderId} sourceDetailCount={sourceDetailCount} movedRows={movedRows}");
                                return (Success: false, Error: "❌ Không thể chuyển món. Vui lòng thử lại.", TargetTableId: targetTableId, OrderIdToNotify: 0L, OldName: "", NewName: "");
                            }

                            targetOrder.SubTotal += sourceOrder.SubTotal;
                            targetOrder.FinalAmount += sourceOrder.FinalAmount;

                            // [FIX] Delete source order so the table becomes Empty
                            db.Orders.Remove(sourceOrder);
                        }
                        else
                        {
                            // Move entire order to target table
                            sourceOrder.TableID = targetTableId;
                        }

                        // Update source table status to empty
                        var sourceTable = db.Tables.FirstOrDefault(t => t.TableID == sourceTableId);
                        if (sourceTable != null)
                        {
                            sourceTable.TableStatus = "Empty";
                        }

                        // Update target table status to occupied
                        var targetTable = db.Tables.FirstOrDefault(t => t.TableID == targetTableId);
                        if (targetTable != null)
                        {
                            targetTable.TableStatus = "Occupied";
                        }

                        db.SaveChanges();

                        transaction.Commit();
                        LogMoveTableDesktop($"Move success sourceTable={sourceTableId} targetTable={targetTableId} sourceOrder={(sourceOrder?.OrderID ?? 0)} targetOrder={(targetOrder?.OrderID ?? sourceOrder?.OrderID ?? 0)}");

                        // Recalculate totals
                        if (targetOrder != null)
                        {
                            RecalculateOrder(db, targetOrder.OrderID);
                        }
                        else
                        {
                            RecalculateOrder(db, sourceOrder.OrderID);
                        }

                        // Lấy tên bàn mới
                        var newTableInfo = db.Tables.FirstOrDefault(t => t.TableID == targetTableId);
                        string newTableName = newTableInfo?.TableName ?? $"Bàn {targetTableId}";

                        var orderIdToNotify = (targetOrder ?? sourceOrder).OrderID;
                        return (Success: true, Error: (string?)null, TargetTableId: targetTableId, OrderIdToNotify: orderIdToNotify, OldName: oldTableName, NewName: newTableName);
                    }
                }
                catch (Exception ex)
                {
                    LogMoveTableDesktop($"Move exception sourceTable={sourceTableId} targetTable={targetTableId} error={ex.Message}");
                    return (Success: false, Error: "❌ Không thể chuyển bàn. Vui lòng thử lại.", TargetTableId: targetTableId, OrderIdToNotify: 0L, OldName: "", NewName: "");
                }
                finally
                {
                    _moveTableLock.Release();
                }
            });

            Dispatcher.Invoke(() =>
            {
                _isWaitingForMoveTargetTable = false;
                HideToast();
                btnCancelMove.Visibility = Visibility.Collapsed; // [NEW] Hide Cancel button

                if (result.Success)
                {
                    LoadTables();
                    SelectAndLoadTable(result.TargetTableId);
                    NotifyTableMoved(sourceTableId, result.TargetTableId);
                    ShowToast("✅ Chuyển bàn thành công!", 2000);

                    if (result.OrderIdToNotify > 0 && !string.IsNullOrWhiteSpace(result.OldName) && !string.IsNullOrWhiteSpace(result.NewName))
                    {
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                var orderStub = new Order { OrderID = result.OrderIdToNotify };
                                PrintService.PrintMoveTableNotification(orderStub, result.OldName, result.NewName);
                            }
                            catch { }
                        });
                    }
                }
                else
                {
                    ShowToast(result.Error ?? "❌ Không thể chuyển bàn.", 2000);
                }
            });
        }

        private void BtnSplitTable_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTableId == 0) return;

            using (var db = new AppDbContext())
            {
                var order = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(d => d.Dish)
                    .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                if (order == null || !order.OrderDetails.Any())
                {
                    ShowToast("❌ Không có món để tách!");
                    return;
                }

                var items = order.OrderDetails
                    .Where(d => d.Quantity > 0)
                    .GroupBy(d => new { d.DishID, Note = (d.Note ?? "").Trim() })
                    .Select(g => new ReprintItemViewModel
                    {
                        OrderDetails = g.ToList(),
                        DisplayText = g.First().Dish?.DishName ?? "Unknown",
                        IsSelected = false,
                        SelectedQuantity = 0 // Initially 0 for Split
                    })
                    .ToList();

                lstSplitItems.ItemsSource = items;
                btnSelectAllSplit.Content = "Chọn tất cả";
                pnlSplitPopup.Visibility = Visibility.Visible;
            }
        }


        private void BtnReprint_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTableId == 0) return;

            using (var db = new AppDbContext())
            {
                var order = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(d => d.Dish)
                    .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                if (order == null || !order.OrderDetails.Any())
                {
                    ShowToast("❌ Không có món nào để in lại!");
                    return;
                }

                // Load items to popup
                var items = order.OrderDetails
                    .Where(d => d.Quantity > 0)
                    .GroupBy(d => new { d.DishID, Note = (d.Note ?? "").Trim() })
                    .Select(g => new ReprintItemViewModel
                    {
                        OrderDetails = g.ToList(),
                        DisplayText = g.First().Dish?.DishName ?? "Unknown",
                        IsSelected = false,
                        SelectedQuantity = g.Sum(d => d.Quantity) // Default to Max
                    })
                    .ToList();

                lstReprintItems.ItemsSource = items;
                btnSelectAllReprint.Content = "Chọn tất cả";
                pnlReprintPopup.Visibility = Visibility.Visible;
            }
        }

        private void BtnCloseReprintPopup_Click(object sender, RoutedEventArgs e)
        {
            pnlReprintPopup.Visibility = Visibility.Collapsed;
        }

        private void PnlReprintPopup_OutsideClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            pnlReprintPopup.Visibility = Visibility.Collapsed;
        }

        private void BtnSelectAllReprint_Click(object sender, RoutedEventArgs e)
        {
            if (lstReprintItems.ItemsSource is List<ReprintItemViewModel> items)
            {
                // Check if currently all selected
                bool allSelected = items.All(i => i.IsSelected);

                // Toggle: If all selected -> Deselect all. Otherwise -> Select all.
                bool newValue = !allSelected;

                foreach (var item in items) item.IsSelected = newValue;
                lstReprintItems.Items.Refresh();

                // Update button text if needed, but for now purely logic
                if (sender is Button btn)
                {
                    btn.Content = newValue ? "Bỏ chọn tất cả" : "Chọn tất cả";
                }
            }
        }

        private void BtnConfirmReprint_Click(object sender, RoutedEventArgs e)
        {
            if (lstReprintItems.ItemsSource is List<ReprintItemViewModel> items)
            {
                var selected = items.Where(i => i.IsSelected).SelectMany(i => i.OrderDetails).ToList();
                if (!selected.Any())
                {
                    ShowToast("⚠️ Vui lòng chọn ít nhất 1 món!");
                    return;
                }

                using (var db = new AppDbContext())
                {
                    var order = db.Orders
                       .Include(o => o.OrderDetails).ThenInclude(d => d.Dish).ThenInclude(c => c.Category)
                       .Include(o => o.Table)
                       .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                    if (order == null) return;

                    // Filter selected items from THIS order context
                    // Logic: For each selected group, pick TOP N items
                    var itemsToPrint = new List<OrderDetail>();

                    foreach (var vm in items.Where(x => x.IsSelected && x.SelectedQuantity > 0))
                    {
                        // Get IDs of the items we want to print
                        var idsToPrint = vm.OrderDetails.Take(vm.SelectedQuantity).Select(d => d.OrderDetailID).ToList();

                        // Fetch the actual entities from the current 'order' context (which has Category loaded)
                        var details = order.OrderDetails.Where(d => idsToPrint.Contains(d.OrderDetailID)).ToList();
                        itemsToPrint.AddRange(details);
                    }

                    if (itemsToPrint.Any())
                    {
                        int maxBatch = order.OrderDetails.Max(d => (int?)d.KitchenBatch) ?? 1;
                        string senderName = UserSession.AccName ?? "Admin";
                        Services.PrintService.PrintKitchen(order, itemsToPrint, maxBatch, senderName + " (IN LẠI)");

                        ShowToast($"✅ Đã gửi lệnh in lại {itemsToPrint.Count} món!");
                        pnlReprintPopup.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private void BtnIncreaseReprint_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ReprintItemViewModel vm)
            {
                if (vm.SelectedQuantity < vm.TotalQuantity)
                {
                    vm.SelectedQuantity++;
                    if (!vm.IsSelected) vm.IsSelected = true;
                }
            }
        }

        private void BtnDecreaseReprint_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ReprintItemViewModel vm)
            {
                if (vm.SelectedQuantity > 1)
                {
                    vm.SelectedQuantity--;
                }
            }
        }

        private void ReprintItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is ReprintItemViewModel vm)
            {
                // Toggle selection
                vm.IsSelected = !vm.IsSelected;

                // Note: IsSelected setter in ViewModel handles the default quantity logic (defaults to max if 0).
            }
        }

        private void BtnCloseSplitPopup_Click(object sender, RoutedEventArgs e)
        {
            pnlSplitPopup.Visibility = Visibility.Collapsed;
        }

        private void PnlSplitPopup_OutsideClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            pnlSplitPopup.Visibility = Visibility.Collapsed;
        }

        private void BtnSelectAllSplit_Click(object sender, RoutedEventArgs e)
        {
            if (lstSplitItems.ItemsSource is List<ReprintItemViewModel> items)
            {
                bool allSelected = items.All(i => i.IsSelected);
                bool newValue = !allSelected;

                foreach (var item in items) item.IsSelected = newValue;
                lstSplitItems.Items.Refresh();

                if (sender is Button btn)
                {
                    btn.Content = newValue ? "Bỏ chọn tất cả" : "Chọn tất cả";
                }
            }
        }

        private void BtnConfirmSplit_Click(object sender, RoutedEventArgs e)
        {
            if (lstSplitItems.ItemsSource is List<ReprintItemViewModel> items)
            {
                var selectedItems = items.Where(x => x.IsSelected && x.SelectedQuantity > 0).ToList();
                if (!selectedItems.Any())
                {
                    ShowToast("❌ Vui lòng chọn món để tách!", 2000);
                    return;
                }

                // Prepare transfer dictionary
                var itemsToTransfer = new Dictionary<long, int>();

                foreach (var vm in selectedItems)
                {
                    int quantityRemainingToSplit = vm.SelectedQuantity;

                    // Iterate through the underlying OrderDetails in this group
                    foreach (var detail in vm.OrderDetails)
                    {
                        if (quantityRemainingToSplit <= 0) break;

                        int take = Math.Min(detail.Quantity, quantityRemainingToSplit);
                        itemsToTransfer[detail.OrderDetailID] = take;
                        quantityRemainingToSplit -= take;
                    }
                }

                if (itemsToTransfer.Count == 0) return;

                // Set waiting mode and show persistent popup to select destination table
                _isWaitingForTargetTable = true;
                _pendingSplitItems = itemsToTransfer;

                ShowToastPersistent("📍 Chọn bàn đích để tách...");

                // Switch back to table list view
                pnlSplitPopup.Visibility = Visibility.Collapsed;
                pnlMenu.Visibility = Visibility.Collapsed;
                pnlTableList.Visibility = Visibility.Visible;
                btnCancelSplit.Visibility = Visibility.Visible; // [NEW] Show Cancel button

                LoadTables(); // [NEW] Refresh to gray out source table
                _tableTimeTimer.Stop();
            }
        }

        private void BtnCancelSplit_Click(object sender, RoutedEventArgs e)
        {
            _isWaitingForTargetTable = false;
            _pendingSplitItems.Clear();
            btnCancelSplit.Visibility = Visibility.Collapsed;
            HideToast();

            // Return to Menu / Order Screen for the current table
            SelectAndLoadTable(_selectedTableId);
        }

        private void ExecuteSplitTransfer(int targetTableId)
        {
            if (targetTableId == _selectedTableId)
            {
                ShowToast("❌ Vui lòng chọn bàn khác!", 2000);
                _isWaitingForTargetTable = false;
                _pendingSplitItems.Clear();
                return;
            }

            using (var db = new AppDbContext())
            {
                var sourceOrder = db.Orders
                    .Include(o => o.OrderDetails).ThenInclude(od => od.Dish)
                    .FirstOrDefault(o => o.TableID == _selectedTableId && o.OrderStatus == "Pending");

                var targetOrder = db.Orders
                    .Include(o => o.OrderDetails)
                    .FirstOrDefault(o => o.TableID == targetTableId && o.OrderStatus == "Pending");

                if (targetOrder == null)
                {
                    targetOrder = new Order
                    {
                        TableID = targetTableId,
                        OrderTime = DateTime.Now,
                        OrderStatus = "Pending",
                        PaymentMethod = "Cash",
                        FirstSentTime = sourceOrder?.FirstSentTime
                    };
                    db.Orders.Add(targetOrder);
                    db.SaveChanges();
                }

                if (sourceOrder != null)
                {
                    // Transfer selected items
                    foreach (var kvp in _pendingSplitItems)
                    {
                        var detail = sourceOrder.OrderDetails.FirstOrDefault(d => d.OrderDetailID == kvp.Key);
                        if (detail != null)
                        {
                            if (kvp.Value == detail.Quantity)
                            {
                                // Move entire item
                                detail.OrderID = targetOrder.OrderID;
                            }
                            else
                            {
                                // Split item: create new item for target table
                                decimal dishPrice = detail.Dish?.Price ?? 0;

                                // Calculate how many of the split items were already printed
                                int printedToSplit = Math.Min(kvp.Value, detail.PrintedQuantity);

                                var newDetail = new OrderDetail
                                {
                                    OrderID = targetOrder.OrderID,
                                    DishID = detail.DishID,
                                    Quantity = kvp.Value,
                                    PrintedQuantity = printedToSplit,  // Split the printed quantity
                                    KitchenBatch = detail.KitchenBatch,
                                    TotalAmount = dishPrice * kvp.Value,
                                    ItemStatus = detail.ItemStatus,  // Inherit status from original
                                    Note = detail.Note
                                };
                                db.OrderDetails.Add(newDetail);

                                // Reduce original item
                                detail.Quantity -= kvp.Value;
                                detail.PrintedQuantity -= printedToSplit;  // Also reduce printed quantity
                                detail.TotalAmount = dishPrice * detail.Quantity;
                            }
                        }
                    }

                    // Update target table status
                    var targetTable = db.Tables.FirstOrDefault(t => t.TableID == targetTableId);
                    if (targetTable != null)
                    {
                        targetTable.TableStatus = "Occupied";
                    }

                    db.SaveChanges();

                    // Reload source order to get updated OrderDetails
                    db.Entry(sourceOrder).Reload();

                    // Check if source order still has items with quantity > 0
                    bool sourceOrderHasItems = sourceOrder.OrderDetails.Any(d => d.Quantity > 0);

                    // If source order has no items left, delete it and mark table as empty
                    if (!sourceOrderHasItems)
                    {
                        // Delete source order
                        db.Orders.Remove(sourceOrder);

                        var sourceTable = db.Tables.FirstOrDefault(t => t.TableID == _selectedTableId);
                        if (sourceTable != null)
                        {
                            sourceTable.TableStatus = "Empty";
                        }

                        db.SaveChanges();
                    }
                    else
                    {
                        // Recalculate totals for source order if it still has items
                        RecalculateOrder(db, sourceOrder.OrderID);
                    }

                    // Recalculate totals for target order
                    RecalculateOrder(db, targetOrder.OrderID);

                    Dispatcher.Invoke(() =>
                    {
                        _isWaitingForTargetTable = false;
                        _pendingSplitItems.Clear();
                        HideToast();

                        btnDiscountBill.Visibility = Visibility.Visible;
                        btnCancelSplit.Visibility = Visibility.Collapsed; // [NEW] Hide Cancel button

                        LoadTables();
                        SelectAndLoadTable(targetTableId);

                        ShowToast("✅ Tách bàn thành công!", 2000);
                    });
                }
            }
        }

        // Timer handler to update table time display
        private void TableTimeTimer_Tick(object sender, EventArgs e)
        {
            if (_currentOrderTime.HasValue)
            {
                lblTableTime.Text = FormatElapsedTime(_currentOrderTime.Value);
            }
        }
        private void BtnTimeKeeping_Click(object sender, RoutedEventArgs e)
        {
            // Tạo cửa sổ từ file riêng
            var tkWindow = new TimeKeepingWindow();

            // Gán Owner là MainWindow để nó hiện chính giữa cửa sổ chính
            tkWindow.Owner = this;

            // QUAN TRỌNG: Dùng ShowDialog() để biến nó thành MODAL
            // (Người dùng không thể bấm vào MainWindow khi cửa sổ này đang mở)
            tkWindow.ShowDialog();

            // Sau khi đóng modal, focus lại màn hình chính để bán hàng tiếp
            this.Focus();
        }
        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Bạn có chắc chắn muốn đăng xuất?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                // Xóa session
                UserSession.AccID = 0;
                UserSession.AccName = "";
                UserSession.AccRole = "";

                // Mở lại màn hình Login
                LoginWindow login = new LoginWindow();
                login.Show();

                // Đóng màn hình hiện tại
                this.Close();
            }
        }



        private void BtnExpense_Click(object sender, RoutedEventArgs e)
        {
            var expenseWin = new ExpenseWindow();
            expenseWin.Owner = this;
            expenseWin.ShowDialog();
        }

        private void BtnFilterAll_Click(object sender, RoutedEventArgs e)
        {
            _selectedCategoryId = null;
            UpdateFilterButtonStyles();
            LoadTables();
        }

        private void BtnFilterCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int catId)
            {
                _selectedCategoryId = catId;
                UpdateFilterButtonStyles();
                LoadTables();
            }
        }

        private void UpdateFilterButtonStyles()
        {
            if (btnFilterAll != null)
            {
                SetFilterButtonSelected(btnFilterAll, !_selectedCategoryId.HasValue);
            }

            if (icFilterCategories == null)
                return;

            foreach (var item in icFilterCategories.Items)
            {
                var container = icFilterCategories.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
                if (container == null)
                {
                    icFilterCategories.UpdateLayout();
                    container = icFilterCategories.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
                }

                if (container == null)
                    continue;

                var btn = container.FindVisualChild<Button>();
                if (btn == null)
                    continue;

                if (btn.Tag is int catId)
                {
                    SetFilterButtonSelected(btn, _selectedCategoryId == catId);
                }
            }
        }

        private void SetFilterButtonSelected(Button btn, bool isSelected)
        {
            if (isSelected)
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                btn.Foreground = new SolidColorBrush(Colors.White);
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            }
            else
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(241, 243, 245));
                btn.Foreground = new SolidColorBrush(Color.FromRgb(73, 80, 87));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(208, 215, 222));
            }
        }
    }
    // Class dùng để hiển thị lên DataGrid và hỗ trợ sửa Ghi chú
    public class OrderDetailViewModel
    {
        public long OrderDetailID { get; set; }
        public string DishName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DiscountRate { get; set; }
        public string ItemStatus { get; set; } = "";
        public int KitchenBatch { get; set; }
        public DateTime SortTime { get; set; }
        // Ghi chú (Cho phép sửa đổi)
        public string Note { get; set; } = "";

        // Các thuộc tính hiển thị
        public bool HasDiscount => DiscountRate != 0;
        public string DiscountDisplay => DiscountRate > 0 ? $"Giảm {DiscountRate:0.#}%" : $"Tăng {-DiscountRate:0.#}%";
        public string BatchDisplay { get; set; } = "";
        public string StatusDisplay { get; set; } = "";
        public string RowColor { get; set; } = "White";
    }



    // Extension methods để tìm visual children
    public static class VisualTreeHelper_Extensions
    {
        public static T FindVisualChild<T>(this DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T childOfType)
                    return childOfType;
                var result = child.FindVisualChild<T>();
                if (result != null)
                    return result;
            }
            return null;
        }

        public static IEnumerable<T> FindVisualChildren<T>(this DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T childOfType)
                    yield return childOfType;
                foreach (var descendant in child.FindVisualChildren<T>())
                    yield return descendant;
            }
        }
    }



    // Converter để hiển thị placeholder khi text rỗng
    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
            {
                // Nếu text rỗng hoặc null → Hiện placeholder (Visible)
                // Nếu có text → Ẩn placeholder (Collapsed)
                return string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }


    }

    public class NotEmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
            {
                return !string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

} // End of namespace