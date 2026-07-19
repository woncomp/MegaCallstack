using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Models;
using MegaCallstack.Services;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class TreeBuildingTests
    {
        [TestMethod]
        public void BuildTreeNodes_EmptySession_ReturnsEmptyList()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");

            var nodes = builder.BuildTreeNodes(session);

            Assert.AreEqual(0, nodes.Count);
        }

        [TestMethod]
        public void BuildTreeNodes_NullSession_ReturnsEmptyList()
        {
            var builder = CreateBuilder();

            var nodes = builder.BuildTreeNodes(null);

            Assert.AreEqual(0, nodes.Count);
        }

        [TestMethod]
        public void BuildTreeNodes_SingleCallstack_CreatesTreeWithLeaf()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack);

            var nodes = builder.BuildTreeNodes(session);

            Assert.AreEqual(1, nodes.Count);
            Assert.AreEqual("main", nodes[0].DisplayText);
            Assert.AreEqual(1, nodes[0].Children.Count);
            Assert.AreEqual("Run", nodes[0].Children[0].DisplayText);
            Assert.AreEqual(1, nodes[0].Children[0].Children.Count);
            Assert.AreEqual("DoWork", nodes[0].Children[0].Children[0].DisplayText);
            Assert.AreEqual(1, nodes[0].Children[0].Children[0].Children.Count);
            Assert.IsTrue(nodes[0].Children[0].Children[0].Children[0].IsLeaf);
        }

        [TestMethod]
        public void BuildTreeNodes_MergesSameCallSiteAcrossCallstacks()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack1 = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            var callstack2 = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack1);
            AddOrUpdateCallstack(session, callstack2);

            var nodes = builder.BuildTreeNodes(session);

            Assert.AreEqual(1, nodes.Count);
            Assert.AreEqual(1, nodes[0].Children.Count);
            Assert.AreEqual(1, nodes[0].Children[0].Children.Count);
        }

        [TestMethod]
        public void BuildTreeNodes_SeparatesDifferentCallSites()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack1 = CreateTestCallstack("main.cs", "main", "Run");
            var callstack2 = CreateTestCallstack("main.cs", "main", "Execute");
            AddOrUpdateCallstack(session, callstack1);
            AddOrUpdateCallstack(session, callstack2);

            var nodes = builder.BuildTreeNodes(session);

            Assert.AreEqual(1, nodes.Count);
            Assert.AreEqual(2, nodes[0].Children.Count);
            var childNames = nodes[0].Children.Select(c => c.DisplayText).ToList();
            CollectionAssert.Contains(childNames, "Run");
            CollectionAssert.Contains(childNames, "Execute");
        }

        [TestMethod]
        public void BuildTreeNodes_CreatesLeafNodeForLastFrame()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run");
            callstack.Frames.Last().LineContent = "Console.WriteLine();";
            AddOrUpdateCallstack(session, callstack);

            var nodes = builder.BuildTreeNodes(session);

            var runNode = nodes[0].Children[0];
            Assert.AreEqual(1, runNode.Children.Count);
            var leafNode = runNode.Children[0];
            Assert.IsTrue(leafNode.IsLeaf);
            Assert.AreEqual("Console.WriteLine();", leafNode.DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_SeparatesLeavesWithSameContentDifferentLine()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");

            var callstack1 = CreateTestCallstack("main.cs", "main", "FuncA");
            callstack1.Frames[1] = new CallstackFrame("FuncA", "main.cs", 10)
            {
                HashCode = callstack1.Frames[1].HashCode,
                LineContent = "DoWork();"
            };

            var callstack2 = CreateTestCallstack("main.cs", "main", "FuncB");
            callstack2.Frames[1] = new CallstackFrame("FuncB", "main.cs", 20)
            {
                HashCode = callstack2.Frames[1].HashCode,
                LineContent = "DoWork();"
            };

            AddOrUpdateCallstack(session, callstack1);
            AddOrUpdateCallstack(session, callstack2);

            var nodes = builder.BuildTreeNodes(session);

            var mainChildren = nodes[0].Children;
            Assert.AreEqual(2, mainChildren.Count);
        }

        [TestMethod]
        public void BuildTreeNodes_LeafDisplayText_TruncatesLongContent()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main");
            callstack.Frames[0].LineContent = new string('x', Constants.LeafNodeDisplayMaxLength + 10);
            AddOrUpdateCallstack(session, callstack);

            var nodes = builder.BuildTreeNodes(session);

            var leafNode = nodes[0].Children[0];
            Assert.IsTrue(leafNode.IsLeaf);
            Assert.AreEqual(Constants.LeafNodeDisplayMaxLength, leafNode.DisplayText.Length);
            Assert.IsTrue(leafNode.DisplayText.EndsWith("..."));
        }

        [TestMethod]
        public void BuildTreeNodes_LeafDisplayText_UsesAsIsAtMaxLength()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main");
            callstack.Frames[0].LineContent = new string('x', Constants.LeafNodeDisplayMaxLength);
            AddOrUpdateCallstack(session, callstack);

            var nodes = builder.BuildTreeNodes(session);

            var leafNode = nodes[0].Children[0];
            Assert.IsTrue(leafNode.IsLeaf);
            Assert.AreEqual(new string('x', Constants.LeafNodeDisplayMaxLength), leafNode.DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_LeafDisplayText_FallsBackToFileLine()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main");
            callstack.Frames[0].LineContent = null;
            AddOrUpdateCallstack(session, callstack);

            var nodes = builder.BuildTreeNodes(session);

            var leafNode = nodes[0].Children[0];
            Assert.IsTrue(leafNode.IsLeaf);
            Assert.AreEqual("<main.cs:10>", leafNode.DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_LeafDisplayText_FallsBackToCurrentLine()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = new CallstackData(new List<CallstackFrame>
            {
                new CallstackFrame("main", "", 0) { HashCode = 1 }
            });
            AddOrUpdateCallstack(session, callstack);

            var nodes = builder.BuildTreeNodes(session);

            var leafNode = nodes[0].Children[0];
            Assert.IsTrue(leafNode.IsLeaf);
            Assert.AreEqual("<current line>", leafNode.DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_OrdersChildrenByLineNumber()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");

            var callstack1 = CreateTestCallstack("main.cs", "main", "FuncB");
            callstack1.Frames[1] = new CallstackFrame("FuncB", "main.cs", 30)
            {
                HashCode = callstack1.Frames[1].HashCode
            };

            var callstack2 = CreateTestCallstack("main.cs", "main", "FuncA");
            callstack2.Frames[1] = new CallstackFrame("FuncA", "main.cs", 10)
            {
                HashCode = callstack2.Frames[1].HashCode
            };

            AddOrUpdateCallstack(session, callstack1);
            AddOrUpdateCallstack(session, callstack2);

            var nodes = builder.BuildTreeNodes(session);

            var mainChildren = nodes[0].Children;
            Assert.AreEqual(2, mainChildren.Count);
            Assert.AreEqual("FuncA", mainChildren[0].DisplayText);
            Assert.AreEqual("FuncB", mainChildren[1].DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_MultipleRoots_PreservesDiscoveryOrder()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");

            var callstack1 = CreateTestCallstack("file2.cs", "RootB", "Child2");
            var callstack2 = CreateTestCallstack("file1.cs", "RootA", "Child1");
            AddOrUpdateCallstack(session, callstack1);
            AddOrUpdateCallstack(session, callstack2);

            var nodes = builder.BuildTreeNodes(session);

            Assert.AreEqual(2, nodes.Count);
            Assert.AreEqual("RootB", nodes[0].DisplayText);
            Assert.AreEqual("RootA", nodes[1].DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_RootOrder_NotSortedByLineNumber()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");

            var callstack1 = CreateTestCallstack("file1.cs", "RootB");
            callstack1.Frames[0].LineNumber = 100;
            var callstack2 = CreateTestCallstack("file2.cs", "RootA");
            callstack2.Frames[0].LineNumber = 10;
            AddOrUpdateCallstack(session, callstack1);
            AddOrUpdateCallstack(session, callstack2);

            var nodes = builder.BuildTreeNodes(session);

            Assert.AreEqual(2, nodes.Count);
            Assert.AreEqual("RootB", nodes[0].DisplayText);
            Assert.AreEqual("RootA", nodes[1].DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_RootReplacement_PreservesOriginalRootOrder()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");

            var callstack1 = CreateTestCallstack("file1.cs", "RootA", "Child1");
            var callstack2 = CreateTestCallstack("file2.cs", "RootB", "Child2");
            AddOrUpdateCallstack(session, callstack1);
            AddOrUpdateCallstack(session, callstack2);

            var replacement = CreateTestCallstack("file1.cs", "RootA", "Child1");
            replacement.Frames[1].LineContent = "changed";
            AddOrUpdateCallstack(session, replacement);

            var nodes = builder.BuildTreeNodes(session);

            Assert.AreEqual(2, nodes.Count);
            Assert.AreEqual("RootA", nodes[0].DisplayText);
            Assert.AreEqual("RootB", nodes[1].DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_ChildrenStillSortedByLineNumber()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");

            var callstack1 = CreateTestCallstack("file1.cs", "RootA", "FuncB");
            callstack1.Frames[1].LineNumber = 30;

            var callstack2 = CreateTestCallstack("file2.cs", "RootA", "FuncA");
            callstack2.Frames[1].LineNumber = 10;

            AddOrUpdateCallstack(session, callstack1);
            AddOrUpdateCallstack(session, callstack2);

            var nodes = builder.BuildTreeNodes(session);

            Assert.AreEqual(1, nodes.Count);
            var rootChildren = nodes[0].Children;
            Assert.AreEqual(2, rootChildren.Count);
            Assert.AreEqual("FuncA", rootChildren[0].DisplayText);
            Assert.AreEqual("FuncB", rootChildren[1].DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_LeafNode_HasCorrectMergeId()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main");
            callstack.Frames[0].LineContent = "test content";
            AddOrUpdateCallstack(session, callstack);

            var nodes = builder.BuildTreeNodes(session);

            var leafNode = nodes[0].Children[0];
            Assert.IsTrue(leafNode.IsLeaf);
            Assert.AreNotEqual(0, leafNode.MergeId);
        }

        [TestMethod]
        public void CanHideAncestors_NodeWithAllUnbranchedAncestors_ReturnsTrue()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack);

            var nodes = builder.BuildTreeNodes(session);
            var doWorkNode = nodes[0].Children[0].Children[0];

            Assert.IsTrue(builder.CanHideAncestors(doWorkNode));
        }

        [TestMethod]
        public void CanHideAncestors_RootNode_ReturnsFalse()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run");
            AddOrUpdateCallstack(session, callstack);

            var nodes = builder.BuildTreeNodes(session);

            Assert.IsFalse(builder.CanHideAncestors(nodes[0]));
        }

        [TestMethod]
        public void CanHideAncestors_NodeWithBranchedAncestor_ReturnsFalse()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack1 = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            var callstack2 = CreateTestCallstack("main.cs", "main", "Execute", "DoWork");
            AddOrUpdateCallstack(session, callstack1);
            AddOrUpdateCallstack(session, callstack2);

            var nodes = builder.BuildTreeNodes(session);
            var doWorkNode = nodes[0].Children[0].Children[0];

            Assert.IsFalse(builder.CanHideAncestors(doWorkNode));
        }

        [TestMethod]
        public void SetHiddenAncestors_MarksAncestorsAsHidden()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack);

            var nodes = builder.BuildTreeNodes(session);
            var doWorkNode = nodes[0].Children[0].Children[0];

            builder.SetHiddenAncestors(session, doWorkNode);

            Assert.IsTrue(session.HiddenAncestorNodes.ContainsKey(nodes[0].Frame.HashCode));
            Assert.IsTrue(session.HiddenAncestorNodes.ContainsKey(nodes[0].Children[0].Frame.HashCode));
            Assert.IsFalse(session.HiddenAncestorNodes.ContainsKey(doWorkNode.Frame.HashCode));
        }

        [TestMethod]
        public void BuildDisplayTreeNodes_HidesUnbranchedHiddenAncestors()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack);

            var fullTree = builder.BuildTreeNodes(session);
            var doWorkNode = fullTree[0].Children[0].Children[0];
            builder.SetHiddenAncestors(session, doWorkNode);

            var displayTree = builder.BuildDisplayTreeNodes(session, fullTree);

            Assert.AreEqual(1, displayTree.Count);
            Assert.AreEqual("DoWork", displayTree[0].DisplayText);
        }

        [TestMethod]
        public void BuildDisplayTreeNodes_DoesNotHideBranchedNodesEvenWhenMarked()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack1 = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            var callstack2 = CreateTestCallstack("main.cs", "main", "Execute", "DoWork");
            AddOrUpdateCallstack(session, callstack1);
            AddOrUpdateCallstack(session, callstack2);

            var fullTree = builder.BuildTreeNodes(session);
            session.HiddenAncestorNodes[fullTree[0].Frame.HashCode] = true;

            var displayTree = builder.BuildDisplayTreeNodes(session, fullTree);

            Assert.AreEqual(1, displayTree.Count);
            Assert.AreEqual("main", displayTree[0].DisplayText);
        }

        [TestMethod]
        public void BuildDisplayTreeNodes_ShowAncestorsRestoresRoot()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack);

            var fullTree = builder.BuildTreeNodes(session);
            var doWorkNode = fullTree[0].Children[0].Children[0];
            builder.SetHiddenAncestors(session, doWorkNode);

            var displayTree = builder.BuildDisplayTreeNodes(session, fullTree);
            Assert.AreEqual("DoWork", displayTree[0].DisplayText);
            Assert.IsTrue(builder.IsDisplayRoot(displayTree[0], session));

            builder.ClearHiddenAncestorsForPath(session, displayTree[0]);
            var restoredTree = builder.BuildDisplayTreeNodes(session, fullTree);

            Assert.AreEqual(1, restoredTree.Count);
            Assert.AreEqual("main", restoredTree[0].DisplayText);
        }

        [TestMethod]
        public void BuildDisplayTreeNodes_HidingInOneRootDoesNotAffectOtherRoot()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack1 = CreateTestCallstack("file1.cs", "RootA", "Child1", "Leaf1");
            var callstack2 = CreateTestCallstack("file2.cs", "RootB", "Child2", "Leaf2");
            AddOrUpdateCallstack(session, callstack1);
            AddOrUpdateCallstack(session, callstack2);

            var fullTree = builder.BuildTreeNodes(session);
            var leaf1Node = fullTree[0].Children[0].Children[0];
            builder.SetHiddenAncestors(session, leaf1Node);

            var displayTree = builder.BuildDisplayTreeNodes(session, fullTree);

            Assert.AreEqual(2, displayTree.Count);
            Assert.AreEqual("Leaf1", displayTree[0].DisplayText);
            Assert.AreEqual("RootB", displayTree[1].DisplayText);
        }

        [TestMethod]
        public void SetHiddenAncestors_ClearsPreviousHiddenAncestorsForSamePath()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run", "DoWork", "Inner");
            AddOrUpdateCallstack(session, callstack);

            var fullTree = builder.BuildTreeNodes(session);
            var innerNode = fullTree[0].Children[0].Children[0].Children[0];
            builder.SetHiddenAncestors(session, innerNode);

            var doWorkNode = fullTree[0].Children[0].Children[0];
            builder.SetHiddenAncestors(session, doWorkNode);

            Assert.IsFalse(session.HiddenAncestorNodes.ContainsKey(innerNode.Frame.HashCode));
            Assert.IsTrue(session.HiddenAncestorNodes.ContainsKey(doWorkNode.Frame.HashCode));
            Assert.IsTrue(session.HiddenAncestorNodes.ContainsKey(fullTree[0].Frame.HashCode));
        }

        [TestMethod]
        public void BuildDisplayTreeNodes_LeafStopsAtVisibleAncestor()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run");
            AddOrUpdateCallstack(session, callstack);

            var fullTree = builder.BuildTreeNodes(session);
            var runNode = fullTree[0].Children[0];
            builder.SetHiddenAncestors(session, runNode);

            var displayTree = builder.BuildDisplayTreeNodes(session, fullTree);

            Assert.AreEqual(1, displayTree.Count);
            Assert.AreEqual("Run", displayTree[0].DisplayText);
            Assert.AreEqual(1, displayTree[0].Children.Count);
            Assert.IsTrue(displayTree[0].Children[0].IsLeaf);
        }

        private ICallstackTreeBuilder CreateBuilder()
        {
            return new CallstackTreeBuilder();
        }

        private void AddOrUpdateCallstack(CallstackSession session, CallstackData callstack)
        {
            var existing = session.Callstacks.FirstOrDefault(c => c.LeafHashCode == callstack.LeafHashCode);
            if (existing != null)
            {
                var index = session.Callstacks.IndexOf(existing);
                session.Callstacks[index] = callstack;
            }
            else
            {
                session.Callstacks.Add(callstack);
            }
        }

        private CallstackData CreateTestCallstack(string fileName, params string[] functionNames)
        {
            var frames = new List<CallstackFrame>();
            int hash = 0;
            for (int i = 0; i < functionNames.Length; i++)
            {
                hash = CallstackFrame.ComputeFNV1aHash(hash, functionNames[i]);
                frames.Add(new CallstackFrame(functionNames[i], fileName, (i + 1) * 10)
                {
                    HashCode = hash
                });
            }
            return new CallstackData(frames);
        }

        [TestMethod]
        public void BuildTreeNodes_RestoresSavedColorOnFrame()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack);
            session.NodeColors[callstack.Frames[1].HashCode] = "#FF0000";

            var nodes = builder.BuildTreeNodes(session);

            var runNode = nodes[0].Children[0];
            Assert.IsTrue(runNode.IsColorExplicitlySet);
            Assert.IsNotNull(runNode.DisplayForeground);
            Assert.AreEqual(Colors.Red, ((SolidColorBrush)runNode.DisplayForeground).Color);
        }

        [TestMethod]
        public void BuildTreeNodes_RestoredColorSurvivesChildPropagation()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack);
            session.NodeColors[callstack.Frames[2].HashCode] = "#0000FF";

            var nodes = builder.BuildTreeNodes(session);

            var doWorkNode = nodes[0].Children[0].Children[0];
            Assert.AreEqual(Colors.Blue, ((SolidColorBrush)doWorkNode.DisplayForeground).Color);
            var runNode = nodes[0].Children[0];
            Assert.AreEqual(Colors.Blue, ((SolidColorBrush)runNode.DisplayForeground).Color);
            Assert.IsFalse(runNode.IsColorExplicitlySet);
        }

        [TestMethod]
        public void BuildTreeNodes_SavedColorOnParentOverridesChildColor()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack);
            session.NodeColors[callstack.Frames[1].HashCode] = "#00FF00";
            session.NodeColors[callstack.Frames[2].HashCode] = "#0000FF";

            var nodes = builder.BuildTreeNodes(session);

            var runNode = nodes[0].Children[0];
            Assert.IsTrue(runNode.IsColorExplicitlySet);
            Assert.AreEqual(Colors.Lime, ((SolidColorBrush)runNode.DisplayForeground).Color);
        }

        [TestMethod]
        public void BuildTreeNodes_InvalidColorString_IsIgnored()
        {
            var builder = CreateBuilder();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run");
            AddOrUpdateCallstack(session, callstack);
            session.NodeColors[callstack.Frames[1].HashCode] = "not-a-color";

            var nodes = builder.BuildTreeNodes(session);

            Assert.IsFalse(nodes[0].Children[0].IsColorExplicitlySet);
            Assert.IsNull(nodes[0].Children[0].DisplayForeground);
        }
    }
}
