using System;
using System.Windows;

namespace PosSystem.Main
{
    public partial class ExportTimeLogWindow : Window
    {
        public DateTime FromDate { get; private set; }
        public DateTime ToDate { get; private set; }

        public ExportTimeLogWindow()
        {
            InitializeComponent();

            // Mặc định Custom Date là hôm nay
            dpFrom.SelectedDate = DateTime.Now;
            dpTo.SelectedDate = DateTime.Now;
        }

        private void Radio_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            // Enable/Disable DatePicker
            bool isCustom = radCustom.IsChecked == true;
            dpFrom.IsEnabled = isCustom;
            dpTo.IsEnabled = isCustom;
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.Now;

            if (radWeek.IsChecked == true)
            {
                // Lấy đầu tuần (Thứ 2) đến hiện tại
                int diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
                FromDate = now.AddDays(-1 * diff).Date;
                ToDate = now.Date.AddDays(1).AddTicks(-1); // Cuối ngày hôm nay
            }
            else if (radMonth.IsChecked == true)
            {
                FromDate = new DateTime(now.Year, now.Month, 1);
                ToDate = FromDate.AddMonths(1).AddTicks(-1);
            }
            else if (radYear.IsChecked == true)
            {
                FromDate = new DateTime(now.Year, 1, 1);
                ToDate = new DateTime(now.Year, 12, 31, 23, 59, 59);
            }
            else
            {
                // Custom
                FromDate = dpFrom.SelectedDate ?? now.Date;
                ToDate = dpTo.SelectedDate ?? now.Date;
                // Chỉnh ToDate về cuối ngày (23:59:59)
                ToDate = new DateTime(ToDate.Year, ToDate.Month, ToDate.Day, 23, 59, 59);
            }

            DialogResult = true;
            Close();
        }
    }
}