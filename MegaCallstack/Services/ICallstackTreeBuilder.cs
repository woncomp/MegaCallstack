using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCallstack.Models;

namespace MegaCallstack.Services
{
    /// <summary>
    /// Builds tree and display-tree nodes from a callstack session.
    /// This service is stateless and does not depend on solution state.
    /// </summary>
    public interface ICallstackTreeBuilder
    {
        List<TreeViewNode> BuildTreeNodes(CallstackSession session);
        List<TreeViewNode> BuildDisplayTreeNodes(CallstackSession session, List<TreeViewNode> fullTree);
        bool CanHideAncestors(TreeViewNode node);
        bool IsDisplayRoot(TreeViewNode node, CallstackSession session);
        void SetHiddenAncestors(CallstackSession session, TreeViewNode node);
        void ClearHiddenAncestorsForPath(CallstackSession session, TreeViewNode node);
        void SaveExpansionState(CallstackSession session, int hashCode, bool isExpanded);
    }
}
