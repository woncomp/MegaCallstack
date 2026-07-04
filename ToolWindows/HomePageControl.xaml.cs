using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace MegaCallstack.ToolWindows
{
    public partial class HomePageControl : UserControl
    {
        public HomePageControl()
        {
            InitializeComponent();
        }
    }

    public class ArrowTailGeometryConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 ||
                !(values[0] is double width) ||
                !(values[1] is double height) ||
                width <= 0 || height <= 0)
            {
                return Geometry.Empty;
            }

            const double arrowBaseCenterX = 17.0;
            const double arrowBaseCenterY = 15.0;
            const double tailBottomInset = 3.0;

            double startY = height - tailBottomInset;
            var startPoint = new Point(width, startY);
            var endPoint = new Point(arrowBaseCenterX, arrowBaseCenterY);
            var controlPoint1 = new Point(width * 0.5, startY);
            var controlPoint2 = new Point(arrowBaseCenterX, height * 0.5);

            var figure = new PathFigure
            {
                StartPoint = startPoint,
                Segments =
                {
                    new BezierSegment(controlPoint1, controlPoint2, endPoint, true)
                }
            };

            return new PathGeometry { Figures = { figure } };
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class DoubleToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && d >= 0)
            {
                return new GridLength(d);
            }

            return GridLength.Auto;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
