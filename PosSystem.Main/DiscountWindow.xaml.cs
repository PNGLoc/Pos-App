using System.Text.RegularExpressions;
using System.Globalization;
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

        // Mode: 0 = Discount, 1 = Edit Quantity
        private bool _isQuantityMode = false;
        private decimal _maxLimit = -1; // Max limit for validation (used for bill discount amount)

        private readonly CultureInfo _vi = new CultureInfo("vi-VN");

        // Constructor cũ (giữ nguyên để không lỗi code cũ)
        public DiscountWindow(decimal currentVal, bool isPercentMode, bool isEditItem = false, decimal maxLimit = -1)
        {
            InitializeComponent();
            _maxLimit = maxLimit; // [NEW]
            // Bill discount: always decrease (no increase mode)
            lblCashTitle.Text = "Nhập số tiền giảm (đ):";

            // Show preview only if we know the bill total
            if (pnlBillPreview != null)
                pnlBillPreview.Visibility = (_maxLimit > 0) ? Visibility.Visible : Visibility.Collapsed;

            if (pnlAfterPercent != null)
                pnlAfterPercent.Visibility = (_maxLimit > 0) ? Visibility.Visible : Visibility.Collapsed;
            if (pnlAfterAmount != null)
                pnlAfterAmount.Visibility = (_maxLimit > 0) ? Visibility.Visible : Visibility.Collapsed;

            if (isPercentMode)
            {
                tabMain.SelectedIndex = 0;
                if (currentVal < 0) currentVal = 0;
                txtPercent.Text = currentVal.ToString("0");
            }
            else
            {
                tabMain.SelectedIndex = 1;
                if (currentVal < 0) currentVal = 0;
                if (_maxLimit >= 0 && currentVal > _maxLimit) currentVal = _maxLimit;
                txtAmount.Text = currentVal.ToString("0");
            }

            UpdateBillPreview();
        }

        // Constructor mới chuyên dùng để nhập Số lượng
        public DiscountWindow(int currentQuantity)
        {
            InitializeComponent();
            _isQuantityMode = true;

            // Quantity mode doesn't need Undo (avoid accidental 0 quantity)
            if (btnUndo != null) btnUndo.Visibility = Visibility.Collapsed;

            // Quantity mode doesn't need bill preview
            if (pnlBillPreview != null) pnlBillPreview.Visibility = Visibility.Collapsed;
            if (pnlAfterPercent != null) pnlAfterPercent.Visibility = Visibility.Collapsed;
            if (pnlAfterAmount != null) pnlAfterAmount.Visibility = Visibility.Collapsed;

            // Ẩn tab control đi, chỉ hiện 1 ô nhập đơn giản
            // (Hack giao diện nhanh: Ẩn tab 0, force tab 1 và đổi title)
            ((TabItem)tabMain.Items[0]).Visibility = Visibility.Collapsed; // Ẩn tab %
            tabMain.SelectedIndex = 1;

            var tabItem = (TabItem)tabMain.Items[1];
            tabItem.Header = "Số lượng";
            lblCashTitle.Text = "Nhập số lượng mới:";
            txtAmount.Text = currentQuantity.ToString();
        }

        private void TabMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isQuantityMode) return;
            UpdateBillPreview();
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_isQuantityMode) return;

            // Undo bill discount => apply 0 immediately and close
            if (tabMain.SelectedIndex == 0)
            {
                IsPercentage = true;
                ResultValue = 0;
            }
            else
            {
                IsPercentage = false;
                ResultValue = 0;
            }

            DialogResult = true;
        }

        private void TxtPercent_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isQuantityMode) return;

            if (decimal.TryParse(txtPercent.Text, out decimal val))
            {
                if (val > 100)
                {
                    txtPercent.Text = "100";
                    txtPercent.CaretIndex = 3; // Move cursor to end
                }
            }

            UpdateBillPreview();
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
                if (val < 0) val = 0;
                if (val > 100) val = 100;
                ResultValue = val;
            }
            else // Tab Tiền
            {
                IsPercentage = false;
                string rawText = txtAmount.Text.Replace(".", ""); // Remove separators
                decimal.TryParse(rawText, out decimal val);

                if (val < 0) val = 0;
                if (_maxLimit >= 0 && val > _maxLimit) val = _maxLimit;
                ResultValue = val;
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
                    // Immediate clamp for bill discount amount
                    if (_maxLimit >= 0 && value > _maxLimit)
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

            if (!_isQuantityMode)
                UpdateBillPreview();
        }

        private void UpdateBillPreview()
        {
            if (_isQuantityMode) return;
            if (_maxLimit <= 0) return;
            if (txtBillTotal == null || txtBillDiscount == null) return;

            decimal billTotal = _maxLimit;
            txtBillTotal.Text = FormatMoney(billTotal);

            decimal discountValue = 0;
            if (tabMain.SelectedIndex == 0)
            {
                // % discount
                decimal.TryParse(txtPercent.Text, out decimal percent);
                if (percent < 0) percent = 0;
                if (percent > 100) percent = 100;
                discountValue = billTotal * (percent / 100m);
            }
            else
            {
                // amount discount
                string raw = (txtAmount.Text ?? string.Empty).Replace(".", "").Trim();
                decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount);
                if (amount < 0) amount = 0;
                if (amount > billTotal) amount = billTotal;
                discountValue = amount;
            }

            var after = billTotal - discountValue;
            if (after < 0) after = 0;

            txtBillDiscount.Text = FormatMoney(discountValue);

            if (txtAfterPercent != null) txtAfterPercent.Text = FormatMoney(after);
            if (txtAfterAmount != null) txtAfterAmount.Text = FormatMoney(after);
        }

        private string FormatMoney(decimal value)
        {
            var v = (long)decimal.Round(value, 0, MidpointRounding.AwayFromZero);
            return v.ToString("#,##0", _vi);
        }
    }
}