using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MegaCallstack.Dialogs
{
    public partial class ColorPickerDialog : Window
    {
        private bool _updatingFromSliders;
        private bool _updatingFromText;
        private bool _updatingFromHex;
        private bool _updatingFromHsvControls;
        private bool _isDraggingColorArea;
        private bool _isDraggingHueSlider;
        private Color? _selectedColor;
        private bool _isNoColor;

        private double _hue;
        private double _saturation = 1.0;
        private double _value = 1.0;

        public ColorPickerDialog(Color? initialColor)
        {
            InitializeComponent();
            Loaded += OnLoaded;
            SizeChanged += OnSizeChanged;

            if (initialColor.HasValue)
            {
                SetColor(initialColor.Value);
            }
            else
            {
                SetColor(Color.FromRgb(255, 255, 255));
                _isNoColor = true;
            }
        }

        public Color? SelectedColor => _selectedColor;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
            UpdateHsvControls();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateHsvControls();
        }

        private void SetColor(Color color)
        {
            _selectedColor = color;
            _isNoColor = false;
            RgbToHsv(color.R, color.G, color.B, out _hue, out _saturation, out _value);
            UpdateControls();
        }

        private void UpdateControls()
        {
            var color = _selectedColor ?? Color.FromRgb(255, 255, 255);

            _updatingFromSliders = true;
            RedSlider.Value = color.R;
            GreenSlider.Value = color.G;
            BlueSlider.Value = color.B;
            _updatingFromSliders = false;

            UpdateTextBoxes(color);
        }

        private void UpdateTextBoxes(Color color)
        {
            _updatingFromText = true;
            RedText.Text = color.R.ToString(CultureInfo.InvariantCulture);
            GreenText.Text = color.G.ToString(CultureInfo.InvariantCulture);
            BlueText.Text = color.B.ToString(CultureInfo.InvariantCulture);
            _updatingFromText = false;

            _updatingFromHex = true;
            HexText.Text = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            _updatingFromHex = false;
        }

        private void UpdatePreview()
        {
            if (_isNoColor)
            {
                PreviewBorder.Background = Brushes.Transparent;
            }
            else
            {
                PreviewBorder.Background = new SolidColorBrush(_selectedColor ?? Color.FromRgb(255, 255, 255));
            }
        }

        private void UpdateHsvControls()
        {
            if (ColorArea == null || HueSlider == null || ColorCursor == null || HueCursor == null || ColorAreaBase == null)
                return;

            _updatingFromHsvControls = true;

            var hueColor = HsvToRgbColor(_hue, 1.0, 1.0);
            ColorAreaBase.Fill = new SolidColorBrush(hueColor);

            double width = Math.Max(1, ColorArea.ActualWidth);
            double height = Math.Max(1, ColorArea.ActualHeight);

            double cursorX = _saturation * width;
            double cursorY = (1.0 - _value) * height;

            Canvas.SetLeft(ColorCursor, cursorX - ColorCursor.Width / 2.0);
            Canvas.SetTop(ColorCursor, cursorY - ColorCursor.Height / 2.0);

            double hueY = (1.0 - _hue / 360.0) * height;
            Canvas.SetTop(HueCursor, hueY - HueCursor.Height / 2.0);

            _updatingFromHsvControls = false;
        }

        private void ApplyHsvToColor()
        {
            if (_updatingFromHsvControls)
                return;

            var color = HsvToRgbColor(_hue, _saturation, _value);
            _selectedColor = color;
            _isNoColor = false;

            _updatingFromSliders = true;
            RedSlider.Value = color.R;
            GreenSlider.Value = color.G;
            BlueSlider.Value = color.B;
            _updatingFromSliders = false;

            UpdateTextBoxes(color);
            UpdatePreview();
        }

        private void ColorArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingColorArea = true;
            ColorArea.CaptureMouse();
            UpdateColorAreaFromMouse(e.GetPosition(ColorArea));
        }

        private void ColorArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingColorArea)
            {
                UpdateColorAreaFromMouse(e.GetPosition(ColorArea));
            }
        }

        private void ColorArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingColorArea)
            {
                _isDraggingColorArea = false;
                ColorArea.ReleaseMouseCapture();
            }
        }

        private void UpdateColorAreaFromMouse(Point position)
        {
            double width = Math.Max(1, ColorArea.ActualWidth);
            double height = Math.Max(1, ColorArea.ActualHeight);

            double x = Math.Max(0, Math.Min(width, position.X));
            double y = Math.Max(0, Math.Min(height, position.Y));

            _saturation = x / width;
            _value = 1.0 - (y / height);

            UpdateHsvControls();
            ApplyHsvToColor();
        }

        private void HueSlider_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingHueSlider = true;
            HueSlider.CaptureMouse();
            UpdateHueSliderFromMouse(e.GetPosition(HueSlider));
        }

        private void HueSlider_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingHueSlider)
            {
                UpdateHueSliderFromMouse(e.GetPosition(HueSlider));
            }
        }

        private void HueSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingHueSlider)
            {
                _isDraggingHueSlider = false;
                HueSlider.ReleaseMouseCapture();
            }
        }

        private void UpdateHueSliderFromMouse(Point position)
        {
            double height = Math.Max(1, HueSlider.ActualHeight);
            double y = Math.Max(0, Math.Min(height, position.Y));

            _hue = (1.0 - (y / height)) * 360.0;
            if (_hue >= 360.0)
                _hue = 0.0;

            UpdateHsvControls();
            ApplyHsvToColor();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFromSliders)
                return;

            var color = Color.FromRgb(
                (byte)RedSlider.Value,
                (byte)GreenSlider.Value,
                (byte)BlueSlider.Value);

            _selectedColor = color;
            _isNoColor = false;
            RgbToHsv(color.R, color.G, color.B, out _hue, out _saturation, out _value);

            UpdateTextBoxes(color);
            UpdatePreview();
            UpdateHsvControls();
        }

        private void NumericText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromText)
                return;

            if (TryParseByte(RedText.Text, out byte r) &&
                TryParseByte(GreenText.Text, out byte g) &&
                TryParseByte(BlueText.Text, out byte b))
            {
                var color = Color.FromRgb(r, g, b);
                _selectedColor = color;
                _isNoColor = false;
                RgbToHsv(r, g, b, out _hue, out _saturation, out _value);

                _updatingFromSliders = true;
                RedSlider.Value = r;
                GreenSlider.Value = g;
                BlueSlider.Value = b;
                _updatingFromSliders = false;

                _updatingFromHex = true;
                HexText.Text = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                _updatingFromHex = false;

                UpdatePreview();
                UpdateHsvControls();
            }
        }

        private void HexText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingFromHex)
                return;

            var hex = HexText.Text?.Trim() ?? string.Empty;
            if (TryParseHexColor(hex, out byte r, out byte g, out byte b))
            {
                var color = Color.FromRgb(r, g, b);
                _selectedColor = color;
                _isNoColor = false;
                RgbToHsv(r, g, b, out _hue, out _saturation, out _value);

                _updatingFromSliders = true;
                RedSlider.Value = r;
                GreenSlider.Value = g;
                BlueSlider.Value = b;
                _updatingFromSliders = false;

                _updatingFromText = true;
                RedText.Text = r.ToString(CultureInfo.InvariantCulture);
                GreenText.Text = g.ToString(CultureInfo.InvariantCulture);
                BlueText.Text = b.ToString(CultureInfo.InvariantCulture);
                _updatingFromText = false;

                UpdatePreview();
                UpdateHsvControls();
            }
        }

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string hex)
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                SetColor(color);
                UpdatePreview();
                UpdateHsvControls();
            }
        }

        public bool IsCleared { get; private set; }

        private void ClearColor_Click(object sender, RoutedEventArgs e)
        {
            _isNoColor = true;
            _selectedColor = null;
            IsCleared = true;
            PreviewBorder.Background = Brushes.Transparent;

            DialogResult = true;
            Close();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static bool TryParseByte(string text, out byte value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return byte.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseHexColor(string hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrWhiteSpace(hex))
                return false;

            hex = hex.Trim().Replace("#", string.Empty);
            if (!Regex.IsMatch(hex, "^[0-9A-Fa-f]{6}$"))
                return false;

            r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return true;
        }

        private static Color HsvToRgbColor(double hue, double saturation, double value)
        {
            HsvToRgb(hue, saturation, value, out byte r, out byte g, out byte b);
            return Color.FromRgb(r, g, b);
        }

        private static void HsvToRgb(double hue, double saturation, double value, out byte r, out byte g, out byte b)
        {
            hue = hue % 360.0;
            if (hue < 0)
                hue += 360.0;

            saturation = Math.Max(0, Math.Min(1, saturation));
            value = Math.Max(0, Math.Min(1, value));

            double c = value * saturation;
            double x = c * (1 - Math.Abs((hue / 60.0) % 2 - 1));
            double m = value - c;

            double rp = 0, gp = 0, bp = 0;

            if (hue < 60)
            {
                rp = c; gp = x; bp = 0;
            }
            else if (hue < 120)
            {
                rp = x; gp = c; bp = 0;
            }
            else if (hue < 180)
            {
                rp = 0; gp = c; bp = x;
            }
            else if (hue < 240)
            {
                rp = 0; gp = x; bp = c;
            }
            else if (hue < 300)
            {
                rp = x; gp = 0; bp = c;
            }
            else
            {
                rp = c; gp = 0; bp = x;
            }

            r = (byte)Math.Round((rp + m) * 255);
            g = (byte)Math.Round((gp + m) * 255);
            b = (byte)Math.Round((bp + m) * 255);
        }

        private static void RgbToHsv(byte r, byte g, byte b, out double hue, out double saturation, out double value)
        {
            double rp = r / 255.0;
            double gp = g / 255.0;
            double bp = b / 255.0;

            double max = Math.Max(rp, Math.Max(gp, bp));
            double min = Math.Min(rp, Math.Min(gp, bp));
            double delta = max - min;

            value = max;

            if (max == 0 || delta == 0)
            {
                hue = 0;
                saturation = 0;
                return;
            }

            saturation = delta / max;

            if (max == rp)
            {
                hue = (gp - bp) / delta;
            }
            else if (max == gp)
            {
                hue = 2 + (bp - rp) / delta;
            }
            else
            {
                hue = 4 + (rp - gp) / delta;
            }

            hue *= 60;
            if (hue < 0)
                hue += 360;
        }
    }
}
