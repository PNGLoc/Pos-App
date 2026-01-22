using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PosSystem.Main.Database;

namespace PosSystem.Main.Pages
{
    public partial class ExpenseHistoryPage : Page
    {
        public ExpenseHistoryPage()
        {
            InitializeComponent();
            dpFrom.SelectedDate = DateTime.Today;
            dpTo.SelectedDate = DateTime.Today;
            LoadData(); // Load immediately on init
        }

        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadData();
        }

        private void LoadData()
        {
            DateTime start = dpFrom.SelectedDate ?? DateTime.Today;
            DateTime end = (dpTo.SelectedDate ?? DateTime.Today).AddDays(1).AddTicks(-1);

            using (var db = new AppDbContext())
            {
                var list = db.Expenses
                    .Where(x => x.ExpenseDate >= start && x.ExpenseDate <= end)
                    .OrderByDescending(x => x.ExpenseDate)
                    .ToList();
                dgExpenses.ItemsSource = list;
            }
        }
    }
}
