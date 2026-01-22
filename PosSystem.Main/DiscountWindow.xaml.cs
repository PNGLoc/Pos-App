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
        private decimal _maxLimit = -1; // [NEW] Max limit for validation

        // Constructor cũ (giữ nguyên để không lỗi code cũ)
        public DiscountWindow(decimal currentVal, bool isPercentMode, bool isEditItem = false, decimal maxLimit = -1)
        {
            InitializeComponent();
            _maxLimit = maxLimit; // [NEW]
            // Code cũ xử lý giảm giá/giá món...
            if (isEditItem) // Đây là sửa giá món
            {
                lblCashTitle.Text = "Nhập giá bán mới (đ):";
            }
            else
            {
                // [NEW] Bill Discount -> Disable Increase Option (Hide Toggle)
                pnlToggleMode.Visibility = Visibility.Collapsed;
            }

            if (isPercentMode)
            {
                tabMain.SelectedIndex = 0;
                // [MODIFIED] Check sign to set Toggle
                if (currentVal >= 0)
                {
                    optDecrease.IsChecked = true;
                    txtPercent.Text = currentVal.ToString("0");
                }
                else
                {
                    optIncrease.IsChecked = true;
                    txtPercent.Text = (-currentVal).ToString("0"); // Show positive number
                }
            }
            else
            {
                tabMain.SelectedIndex = 1;
                // Does logic support negative cash discount? usually yes.
                if (currentVal >= 0)
                {
                    optDecrease.IsChecked = true;
                    txtAmount.Text = currentVal.ToString("0");
                }
                else
                {
                    optIncrease.IsChecked = true;
                    txtAmount.Text = (-currentVal).ToString("0");
                }
            }
            
            // Set initial label text
            OptMode_Click(null, null);
        }

        // Event for toggle click
        private void OptMode_Click(object sender, RoutedEventArgs e)
        {
            // [NEW] Update Title for Item Price Mode
            bool isItemPriceMode = (tabMain.SelectedIndex == 1 && _maxLimit >= 0); // Hacky check but works for now (passed UnitPrice as limit)
            if (isItemPriceMode)
            {
                if (optDecrease.IsChecked == true)
                    lblCashTitle.Text = "Nhập số tiền giảm (đ):";
                else
                    lblCashTitle.Text = "Nhập giá bán mới (đ):";
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
                // Logic trả về số lượng
                string rawText = txtAmount.Text.Replace(".", ""); // Remove separators
                decimal.TryParse(rawText, out decimal val);
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
                string rawText = txtAmount.Text.Replace(".", ""); // Remove separators
                decimal.TryParse(rawText, out decimal val);

                bool isItemPriceMode = (_maxLimit >= 0); // Assuming Limit is passed only for Item Edit (and Bill which uses Decrease only)

                if (isItemPriceMode && optDecrease.IsChecked == true)
                {
                    // MODE DECREASE: Input is DISCOUNT AMOUNT
                    // Cap at Max Limit (Price)
                    if (val > _maxLimit) val = _maxLimit;

                    // Result = New Price = Price - Discount
                    ResultValue = _maxLimit - val;
                }
                else if (isItemPriceMode && optIncrease.IsChecked == true)
                {
                    // MODE INCREASE: Input is NEW PRICE
                    // [NEW] Validate: New Price MUST be >= Original Price (_maxLimit)
                    if (val < _maxLimit) val = _maxLimit;
                    ResultValue = val;
                }
                else
                {
                    // Fallback / Bill Discount Mode (Decrease only)
                    if (_maxLimit >= 0 && val > _maxLimit) val = _maxLimit;
                    ResultValue = val;
                    if (optIncrease.IsChecked == true) ResultValue = -ResultValue;
                }
            }

            this.DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => this.DialogResult = false;

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+"); // Chỉ cho nhập số dương
            e.Handled = regex.IsMatch(e.Text);
        }

        private void Txt_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb) tb.SelectAll();
        }

        private void TxtAmount_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox txt)
            {
                // Remove existing separators
                string rawText = txt.Text.Replace(".", "").Trim();
                
                if (long.TryParse(rawText, out long value))
                {
                    // [NEW] Immediate Clamp ONLY if Decrease Mode
                    bool isDecrease = (optDecrease.IsChecked == true);
                    if (_maxLimit >= 0 && isDecrease && value > _maxLimit)
                    {
                        value = (long)_maxLimit;
                    }

                    txt.TextChanged -= TxtAmount_TextChanged;
                    // Format with dots (Vietnamese style)
                    txt.Text = value.ToString("#,##0", new System.Globalization.CultureInfo("vi-VN"));
                    txt.CaretIndex = txt.Text.Length;
                    txt.TextChanged += TxtAmount_TextChanged;
                }
                else if (string.IsNullOrEmpty(rawText))
                {
                     // Handle empty case
                }
            }
        }
    }
}