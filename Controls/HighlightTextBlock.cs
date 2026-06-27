using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MegaCallstack.Controls
{
    public class HighlightTextBlock : Control
    {
        private StackPanel _panel;

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(HighlightTextBlock),
                new PropertyMetadata(null, OnTextOrHighlightChanged));

        public static readonly DependencyProperty HighlightTextProperty =
            DependencyProperty.Register(nameof(HighlightText), typeof(string), typeof(HighlightTextBlock),
                new PropertyMetadata(null, OnTextOrHighlightChanged));

        public static readonly DependencyProperty HighlightBackgroundProperty =
            DependencyProperty.Register(nameof(HighlightBackground), typeof(Brush), typeof(HighlightTextBlock),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(255, 255, 0))));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public string HighlightText
        {
            get => (string)GetValue(HighlightTextProperty);
            set => SetValue(HighlightTextProperty, value);
        }

        public Brush HighlightBackground
        {
            get => (Brush)GetValue(HighlightBackgroundProperty);
            set => SetValue(HighlightBackgroundProperty, value);
        }

        public HighlightTextBlock()
        {
            _panel = new StackPanel { Orientation = Orientation.Horizontal };
            AddVisualChild(_panel);
            AddLogicalChild(_panel);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _panel;

        protected override Size MeasureOverride(Size constraint)
        {
            _panel.Measure(constraint);
            return _panel.DesiredSize;
        }

        protected override Size ArrangeOverride(Size arrangeBounds)
        {
            _panel.Arrange(new Rect(arrangeBounds));
            return arrangeBounds;
        }

        // Refresh the rendered segments when the inherited Foreground changes
        // (e.g. when the VS color theme switches). Without this the inner
        // TextBlocks keep the stale foreground until Text/HighlightText changes.
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == ForegroundProperty ||
                e.Property == TextProperty ||
                e.Property == HighlightTextProperty ||
                e.Property == HighlightBackgroundProperty)
            {
                Rebuild();
            }
        }

        private static void OnTextOrHighlightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((HighlightTextBlock)d).Rebuild();
        }

        private void Rebuild()
        {
            _panel.Children.Clear();

            var foreground = Foreground;

            var text = Text;
            if (string.IsNullOrEmpty(text))
            {
                _panel.Children.Add(new TextBlock());
                return;
            }

            var highlight = HighlightText;
            if (string.IsNullOrEmpty(highlight))
            {
                _panel.Children.Add(new TextBlock { Text = text, Foreground = foreground });
                return;
            }

            var segments = SplitText(text, highlight);
            foreach (var (segmentText, isHighlighted) in segments)
            {
                var tb = new TextBlock { Text = segmentText };
                if (isHighlighted)
                {
                    tb.Background = HighlightBackground;
                    // The highlight background is yellow (#FFFF00); use black
                    // text so the highlighted span stays readable in both
                    // light and dark themes regardless of the inherited color.
                    tb.Foreground = Brushes.Black;
                }
                else
                {
                    tb.Foreground = foreground;
                }
                _panel.Children.Add(tb);
            }
        }

        private static List<(string Text, bool IsHighlighted)> SplitText(string text, string highlight)
        {
            var result = new List<(string, bool)>();
            var lowerText = text.ToLower();
            var lowerHighlight = highlight.ToLower();
            int lastIndex = 0;

            int index = lowerText.IndexOf(lowerHighlight, lastIndex);
            while (index >= 0)
            {
                if (index > lastIndex)
                {
                    result.Add((text.Substring(lastIndex, index - lastIndex), false));
                }

                result.Add((text.Substring(index, highlight.Length), true));
                lastIndex = index + highlight.Length;
                index = lowerText.IndexOf(lowerHighlight, lastIndex);
            }

            if (lastIndex < text.Length)
            {
                result.Add((text.Substring(lastIndex), false));
            }

            return result;
        }
    }
}
