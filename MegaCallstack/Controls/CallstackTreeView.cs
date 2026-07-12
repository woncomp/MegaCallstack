using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace MegaCallstack.Controls
{
    public class CallstackTreeView : TreeView
    {
        private HwndSource _hwndSource;
        private HwndSourceHook _hwndSourceHook;

        public CallstackTreeView()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        protected override DependencyObject GetContainerForItemOverride()
        {
            return new CallstackTreeViewItem();
        }

        protected override bool IsItemItsOwnContainerOverride(object item)
        {
            return item is CallstackTreeViewItem;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            DetachHook();
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (_hwndSource != null)
            {
                _hwndSourceHook = WndProc;
                _hwndSource.AddHook(_hwndSourceHook);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DetachHook();
        }

        private void DetachHook()
        {
            if (_hwndSource != null && _hwndSourceHook != null)
            {
                _hwndSource.RemoveHook(_hwndSourceHook);
            }

            _hwndSource = null;
            _hwndSourceHook = null;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == HorizontalScrollWheelHelper.WM_MOUSEHWHEEL)
            {
                if (HorizontalScrollWheelHelper.TryHandleMouseHWheel(this, wParam, lParam))
                {
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }
    }
}
