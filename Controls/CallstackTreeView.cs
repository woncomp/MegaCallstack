using System.Windows;
using System.Windows.Controls;

namespace MegaCallstack.Controls
{
    public class CallstackTreeView : TreeView
    {
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
