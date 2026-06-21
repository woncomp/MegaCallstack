using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
