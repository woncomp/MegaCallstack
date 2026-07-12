using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MegaCallstack.Controls
{
    internal static class HorizontalScrollWheelHelper
    {
        internal const int WM_MOUSEHWHEEL = 0x020E;
        private const int WHEEL_DELTA = 120;

        internal static bool TryHandleMouseHWheel(CallstackTreeView treeView, IntPtr wParam, IntPtr lParam)
        {
            Point screenPoint = GetScreenPointFromLParam(lParam);
            Point point = treeView.PointFromScreen(screenPoint);
            HitTestResult hit = VisualTreeHelper.HitTest(treeView, point);
            if (hit == null)
            {
                return false;
            }

            ScrollViewer scrollHost = FindDescendant<ScrollViewer>(treeView);
            if (scrollHost == null || scrollHost.ScrollableWidth <= 0)
            {
                return false;
            }

            short delta = (short)((long)wParam >> 16 & 0xFFFF);
            double lineOffset = Math.Max(48.0, scrollHost.ViewportWidth * 0.1);
            double offset = (delta / (double)WHEEL_DELTA) * lineOffset;

            // Positive delta means the wheel was tilted to the right,
            // which maps to scrolling the content to the right.
            scrollHost.ScrollToHorizontalOffset(scrollHost.HorizontalOffset + offset);
            return true;
        }

        private static Point GetScreenPointFromLParam(IntPtr lParam)
        {
            long value = lParam.ToInt64();
            int x = (short)(value & 0xFFFF);
            int y = (short)((value >> 16) & 0xFFFF);
            return new Point(x, y);
        }

        private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                T descendant = FindDescendant<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }
    }
}
