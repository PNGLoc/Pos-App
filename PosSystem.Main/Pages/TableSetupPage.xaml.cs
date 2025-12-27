using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PosSystem.Main.Database;
using PosSystem.Main.Models;

namespace PosSystem.Main.Pages
{
    public partial class TableSetupPage : UserControl
    {
        private Table? _selected = null;
        public TableSetupPage() { InitializeComponent(); LoadData(); }

        void LoadData() { using (var db = new AppDbContext()) dgTables.ItemsSource = db.Tables.ToList(); }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            using (var db = new AppDbContext())
            {
                db.Tables.Add(new Table { TableName = txtName.Text, TableType = cboType.Text, TableStatus = "Empty" });
                db.SaveChanges(); LoadData();
            }
        }

        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            using (var db = new AppDbContext())
            {
                var t = db.Tables.Find(_selected.TableID);
                if (t != null) { t.TableName = txtName.Text; t.TableType = cboType.Text; db.SaveChanges(); LoadData(); }
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            if (MessageBox.Show("Xóa bàn này?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                using (var db = new AppDbContext()) { var t = db.Tables.Find(_selected.TableID); if (t != null) db.Tables.Remove(t); db.SaveChanges(); LoadData(); }
            }
        }

        private void dgTables_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgTables.SelectedItem is Table t) { _selected = t; txtName.Text = t.TableName; cboType.Text = t.TableType; }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e) { _selected = null; txtName.Text = ""; }
    }
}