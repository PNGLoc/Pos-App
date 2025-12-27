using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PosSystem.Main
{
    public partial class DiscountWindow : Window
    {
        // ResultValue sẽ trả về: % (nếu mode %), Tiền (nếu mode tiền), hoặc Số Lượng (nếu mode Quantity)
        public bool IsPercentage { get; private set; } = true;
        public decimal ResultValue { get; private set; } = 0;

        // Mode: 0 = Discount (như cũ), 1 = Edit Quantity
        private bool _isQuantityMode = false;

        // Constructor cũ (giữ nguyên để không lỗi code cũ)
        public DiscountWindow(decimal currentVal, bool isPercentMode, bool isEditItem = false)
        {
            InitializeComponent();
            // Code cũ xử lý giảm giá/giá món...
            if (isEditItem) // Đây là sửa giá món
            {
                lblCashTitle.Text = "Nhập giá bán mới (đ):";
            }

            if (isPercentMode)
            {
                tabMain.SelectedIndex = 0;
                txtPercent.Text = currentVal.ToString("0");
            }
            else
            {
                tabMain.SelectedIndex = 1;
                txtAmount.Text = currentVal.ToString("0");
            }
        }

        // Constructor mới chuyên dùng để nhập Số lượng
        public DiscountWindow(int currentQuantity)
        {
            InitializeComponent();
            _isQuantityMode = true;

            // Ẩn tab control đi, chỉ hiện 1 ô nhập đơn giản
            // (Hack giao diện nhanh: Ẩn tab 0, force tab 1 và đổi title)
            ((TabItem)tabMain.Items[0]).Visibility = Visibility.Collapsed; // Ẩn tab %
            tabMain.SelectedIndex = 1;

            var tabItem = (TabItem)tabMain.Items[1];
            tabItem.Header = "Số lượng";
            lblCashTitle.Text = "Nhập số lượng mới:";
            txtAmount.Text = currentQuantity.ToString();
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (_isQuantityMode)
            {
                // Logic trả về số lượng
                decimal.TryParse(txtAmount.Text, out decimal val);
                ResultValue = val;
                this.DialogResult = true;
                return;
            }

            // Logic cũ (Discount)
            if (tabMain.SelectedIndex == 0) // Tab %
            {
                IsPercentage = true;
                decimal.TryParse(txtPercent.Text, out decimal val);
                if (val > 100) val = 100;
                ResultValue = val;
            }
            else // Tab Tiền
            {
                IsPercentage = false;
                decimal.TryParse(txtAmount.Text, out decimal val);
                ResultValue = val;
            }

            this.DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => this.DialogResult = false;

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9-]+"); // Cho phép dấu âm nếu cần (giảm món)
            e.Handled = regex.IsMatch(e.Text);
        }

        private void Txt_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb) tb.SelectAll();
        }
    }
}