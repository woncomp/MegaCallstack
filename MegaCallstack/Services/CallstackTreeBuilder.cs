using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using MegaCallstack.Models;

namespace MegaCallstack.Services
{
    public class CallstackTreeBuilder : ICallstackTreeBuilder
    {
        public List<TreeViewNode> BuildTreeNodes(CallstackSession session)
        {
            var nodes = new List<TreeViewNode>();
            if (session == null)
                return nodes;

            int mergeId = 1;
            int rootOrder = 0;
            var rootOrderByHash = new Dictionary<int, int>();
            foreach (var callstack in session.Callstacks)
            {
                var rootNode = BuildTreeFromCallstack(callstack, session, ref mergeId);
                if (rootNode == null)
                    continue;

                if (!rootOrderByHash.ContainsKey(rootNode.Frame.HashCode))
                {
                    rootNode.TreeRootOrder = rootOrder;
                    rootOrderByHash[rootNode.Frame.HashCode] = rootOrder;
                    rootOrder++;
                }
                else
                {
                    rootNode.TreeRootOrder = rootOrderByHash[rootNode.Frame.HashCode];
                }

                MergeTree(nodes, rootNode);
            }

            SortTree(nodes);
            ApplyNodeColors(session, nodes);
            return nodes;
        }

        private void ApplyNodeColors(CallstackSession session, IList<TreeViewNode> nodes)
        {
            if (session == null)
                return;

            foreach (var node in nodes)
            {
                var key = node.NodeKey;
                if (session.NodeColors.TryGetValue(key, out var hexColor))
                {
                    try
                    {
                        var color = (Color)ColorConverter.ConvertFromString(hexColor);
                        node.SetColorAndPropagate(new SolidColorBrush(color));
                    }
                    catch
                    {
                    }
                }

                ApplyNodeColors(session, node.Children);
            }
        }

        public List<TreeViewNode> BuildDisplayTreeNodes(CallstackSession session, List<TreeViewNode> fullTree)
        {
            var displayRoots = new List<TreeViewNode>();
            if (session == null || fullTree == null)
                return displayRoots;

            foreach (var root in fullTree)
            {
                displayRoots.AddRange(GetVisibleRoots(session, root));
            }

            foreach (var node in displayRoots)
            {
                node.IsDisplayRoot = IsDisplayRoot(node, session);
            }

            return displayRoots;
        }

        private TreeViewNode BuildTreeFromCallstack(CallstackData callstack, CallstackSession session, ref int mergeId)
        {
            if (callstack.Frames.Count == 0)
                return null;

            TreeViewNode rootNode = null;
            TreeViewNode currentNode = null;

            for (int i = 0; i < callstack.Frames.Count; i++)
            {
                var frame = callstack.Frames[i];
                var node = new TreeViewNode
                {
                    Frame = frame,
                    DisplayText = frame.FunctionName,
                    IsExpanded = GetExpansionState(session, frame.HashCode, true)
                };

                if (session.NodeNotes.TryGetValue(frame.HashCode, out var notes))
                {
                    foreach (var note in notes)
                        node.Notes.Add(note);
                }

                if (i == 0)
                    rootNode = node;
                else
                    currentNode.Children.Add(node);

                currentNode = node;
            }

            var lastFrame = callstack.Frames[callstack.Frames.Count - 1];
            var leafNode = CreateLeafNode(lastFrame, ref mergeId);
            currentNode.Children.Add(leafNode);

            return rootNode;
        }

        private TreeViewNode CreateLeafNode(CallstackFrame frame, ref int mergeId)
        {
            string displayText;
            if (frame.LineContent != null)
            {
                displayText = frame.LineContent.Length > Constants.LeafNodeDisplayMaxLength
                    ? frame.LineContent.Substring(0, Constants.LeafNodeDisplayMaxLength - 3) + "..."
                    : frame.LineContent;
            }
            else if (!string.IsNullOrEmpty(frame.FileName))
            {
                displayText = $"<{frame.FileName}:{frame.LineNumber}>";
            }
            else
            {
                displayText = "<current line>";
            }

            return new TreeViewNode
            {
                Frame = frame,
                DisplayText = displayText,
                IsLeaf = true,
                MergeId = mergeId++
            };
        }

        private void MergeTree(IList<TreeViewNode> roots, TreeViewNode newNode)
        {
            var existing = roots.FirstOrDefault(r => r.Frame?.HashCode == newNode.Frame?.HashCode);
            if (existing == null)
            {
                roots.Add(newNode);
                return;
            }

            var children = newNode.Children.ToList();
            foreach (var child in children)
            {
                MergeTree(existing.Children, child);
            }
        }

        private void SortTree(IList<TreeViewNode> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return;

            var sorted = nodes[0].Parent == null
                ? nodes.OrderBy(n => n.TreeRootOrder).ToList()
                : nodes.OrderBy(n => n.IsLeaf ? 1 : 0)
                       .ThenBy(n => n.Frame?.LineNumber ?? int.MaxValue)
                       .ThenBy(n => n.DisplayText)
                       .ToList();

            nodes.Clear();
            foreach (var node in sorted)
            {
                nodes.Add(node);
                SortTree(node.Children);
            }
        }

        private bool GetExpansionState(CallstackSession session, int hashCode, bool defaultValue)
        {
            if (session.CollapsedNodes.TryGetValue(hashCode, out var state))
                return !state;
            return defaultValue;
        }

        public void SaveExpansionState(CallstackSession session, int hashCode, bool isExpanded)
        {
            session.CollapsedNodes[hashCode] = !isExpanded;
        }

        public bool CanHideAncestors(TreeViewNode node)
        {
            if (node == null)
                return false;

            foreach (var ancestor in node.GetAncestors())
            {
                if (ancestor.Children.Count > 1)
                    return false;
            }

            return node.Parent != null;
        }

        public bool IsDisplayRoot(TreeViewNode node, CallstackSession session)
        {
            if (node == null || session == null)
                return false;

            var parent = node.Parent;
            if (parent == null)
                return false;

            var parentHashCode = parent.Frame?.HashCode ?? 0;
            if (parentHashCode == 0 || !session.HiddenAncestorNodes.ContainsKey(parentHashCode))
                return false;

            return parent.Children.Count <= 1;
        }

        public void SetHiddenAncestors(CallstackSession session, TreeViewNode node)
        {
            if (session == null || node == null)
                return;

            ClearHiddenAncestorsForPath(session, node);

            foreach (var ancestor in node.GetAncestors())
            {
                var hashCode = ancestor.Frame?.HashCode ?? 0;
                if (hashCode != 0)
                    session.HiddenAncestorNodes[hashCode] = true;
            }
        }

        public void ClearHiddenAncestorsForPath(CallstackSession session, TreeViewNode node)
        {
            if (session == null || node == null)
                return;

            foreach (var ancestor in node.GetAncestors())
            {
                var hashCode = ancestor.Frame?.HashCode ?? 0;
                if (hashCode != 0)
                    session.HiddenAncestorNodes.Remove(hashCode);
            }
        }

        private IEnumerable<TreeViewNode> GetVisibleRoots(CallstackSession session, TreeViewNode root)
        {
            var current = root;
            while (current != null)
            {
                var hashCode = current.Frame?.HashCode ?? 0;
                bool wantsHidden = hashCode != 0 && session.HiddenAncestorNodes.ContainsKey(hashCode);
                bool isBranched = current.Children.Count > 1;

                if (wantsHidden && !isBranched)
                {
                    var next = current.Children.FirstOrDefault(c => !c.IsLeaf);
                    if (next == null)
                    {
                        yield return current;
                        yield break;
                    }

                    foreach (var child in current.Children)
                    {
                        foreach (var visibleRoot in GetVisibleRoots(session, child))
                            yield return visibleRoot;
                    }
                    yield break;
                }

                yield return current;
                yield break;
            }
        }
    }
}
