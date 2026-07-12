using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MegaCallstack.Controls
{
    public class CallstackTreeViewItem : TreeViewItem
    {
        public static readonly RoutedEvent ItemDoubleClickEvent =
            EventManager.RegisterRoutedEvent("ItemDoubleClick", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(CallstackTreeViewItem));

        public event RoutedEventHandler ItemDoubleClick
        {
            add { AddHandler(ItemDoubleClickEvent, value); }
            remove { RemoveHandler(ItemDoubleClickEvent, value); }
        }

        protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                RaiseEvent(new RoutedEventArgs(ItemDoubleClickEvent, this));
                e.Handled = true;
            }
            else
            {
                base.OnMouseDoubleClick(e);
            }
        }

        public CallstackTreeViewItem()
        {
            this.RequestBringIntoView += OnRequestBringIntoView;
        }

        private void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            var bd = Template?.FindName("Bd", this) as FrameworkElement;
            if (bd == null)
                return;

            // The framework raises RequestBringIntoView with TargetObject set to
            // this item's ContentPresenter (PART_Header) when a row is clicked or
            // selected, and to the TreeViewItem itself for programmatic
            // BringIntoView. Only redirect requests that originate from THIS
            // item's header; a child item's event bubbles up through us too and
            // must be left alone.
            if (!IsTargetInOwnHeader(e.TargetObject, bd))
                return;

            e.Handled = true;
            bd.BringIntoView(GetBringIntoViewRect(bd));
        }

        private bool IsTargetInOwnHeader(object target, DependencyObject bd)
        {
            if (target == this)
                return true;

            // Bd is the element we call BringIntoView on, so the event it raises
            // (TargetObject == Bd) must pass through to the ScrollViewer
            // unhandled -- otherwise we'd recurse infinitely.
            if (target == bd || !(target is DependencyObject d))
                return false;

            var current = d;
            while (current != null && current != this)
            {
                if (current == bd)
                    return true;
                current = (current is Visual)
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return false;
        }

        // Bd stretches across the full row (so the whole row stays clickable),
        // but only its left portion -- the collapse/root icon followed by the
        // actual label text and note emojis -- is meaningful. Bringing the full
        // Bd into view does nothing horizontally when the row is already
        // viewport-wide, leaving the icon (and the start of a long label)
        // scrolled out of sight if the user scrolled right.
        //
        // The rect runs from Bd's left edge (icon included) to the natural right
        // edge of the header content, excluding the empty stretched space on the
        // right. When the content is wider than the viewport, WPF aligns the
        // rect's left edge with the viewport, keeping the icon and the left part
        // of the text visible.
        private Rect GetBringIntoViewRect(FrameworkElement bd)
        {
            double height = bd.ActualHeight > 0 ? bd.ActualHeight : 0;

            if (Template?.FindName("PART_Header", this) is FrameworkElement header
                && VisualTreeHelper.GetChildrenCount(header) > 0
                && VisualTreeHelper.GetChild(header, 0) is FrameworkElement contentRoot
                && contentRoot.DesiredSize.Width > 0)
            {
                try
                {
                    // contentRoot is the HierarchicalDataTemplate root (a
                    // horizontal StackPanel of the label + note-emoji buttons).
                    // Its DesiredSize.Width is the un-stretched content width.
                    Point origin = contentRoot.TransformToVisual(bd).Transform(new Point(0, 0));
                    double right = origin.X + contentRoot.DesiredSize.Width;
                    if (right > 0)
                        return new Rect(0, 0, right, height);
                }
                catch (InvalidOperationException)
                {
                    // Visual relationship not established yet (e.g. not yet
                    // rendered); fall back to the full Bd rect below.
                }
            }

            return new Rect(0, 0, bd.ActualWidth, height);
        }

        protected override DependencyObject GetContainerForItemOverride()
        {
            return new CallstackTreeViewItem();
        }

        protected override bool IsItemItsOwnContainerOverride(object item)
        {
            return item is CallstackTreeViewItem;
        }
    }
}
