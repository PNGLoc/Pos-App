using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PosSystem.Main
{
    public partial class EditItemPriceWindow : Window
    {
        private readonly CultureInfo _vi = new CultureInfo("vi-VN");
        private readonly decimal _originalPrice;

        private bool _isFormatting;

        public decimal NewPrice { get; private set; }

        public EditItemPriceWindow(decimal originalPrice, decimal currentEffectivePrice)
        {
            InitializeComponent();

            _originalPrice = originalPrice < 0 ? 0 : originalPrice;

            txtOriginalPrice.Text = FormatMoney(_originalPrice);

            // Pre-fill: current effective price (after previous adjustment), fallback to original
            var init = currentEffectivePrice > 0 ? currentEffectivePrice : _originalPrice;
            txtNewPrice.Text = FormatMoney(init);

            UpdateHintFromText();
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            // Undo => apply original price immediately
            if (_originalPrice <= 0)
            {
                DialogResult = false;
                return;
            }

            NewPrice = _originalPrice;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            var parsed = ParseMoney(txtNewPrice.Text);
            if (parsed <= 0)
            {
                MessageBox.Show("Giá mới không hợp lệ.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            NewPrice = parsed;
            DialogResult = true;
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void Txt_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb) tb.SelectAll();
        }

        private void TxtNewPrice_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isFormatting) return;

            try
            {
                _isFormatting = true;

                if (sender is TextBox txt)
                {
                    string rawText = (txt.Text ?? string.Empty).Replace(".", "").Replace(",", "").Trim();
                    if (long.TryParse(rawText, out long value))
                    {
                        txt.Text = value.ToString("#,##0", _vi);
                        txt.CaretIndex = txt.Text.Length;
                    }
                }
            }
            finally
            {
                _isFormatting = false;
            }

            UpdateHintFromText();
        }

        private void UpdateHintFromText()
        {
            var newPrice = ParseMoney(txtNewPrice.Text);
            if (newPrice <= 0 || _originalPrice <= 0)
            {
                txtDeltaHint.Text = "";
                txtDeltaHint.Foreground = System.Windows.Media.Brushes.Gray;
                return;
            }

            var delta = newPrice - _originalPrice;
            if (delta == 0)
            {
                txtDeltaHint.Text = "Đang ở giá gốc";
                txtDeltaHint.Foreground = System.Windows.Media.Brushes.Gray;
            }
            else if (delta > 0)
            {
                txtDeltaHint.Text = $"Tăng {FormatMoney(delta)} đ";
                txtDeltaHint.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }
            else
            {
                txtDeltaHint.Text = $"Giảm {FormatMoney(Math.Abs(delta))} đ";
                txtDeltaHint.Foreground = System.Windows.Media.Brushes.SeaGreen;
            }
        }

        private string FormatMoney(decimal value)
        {
            var v = (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);
            return v.ToString("#,##0", _vi);
        }

        private decimal ParseMoney(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            string raw = text.Replace(".", "").Replace(",", "").Trim();
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return 0;
            return v;
        }
    }
}
