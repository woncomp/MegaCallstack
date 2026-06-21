using System.Collections.Generic;
using System.Linq;
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
            var manager = CreateManager();
            var session = new CallstackSession("Test");

            var nodes = manager.BuildTreeNodes(session);

            Assert.AreEqual(0, nodes.Count);
        }

        [TestMethod]
        public void BuildTreeNodes_NullSession_ReturnsEmptyList()
        {
            var manager = CreateManager();

            var nodes = manager.BuildTreeNodes(null);

            Assert.AreEqual(0, nodes.Count);
        }

        [TestMethod]
        public void BuildTreeNodes_SingleCallstack_CreatesTreeWithLeaf()
        {
            var manager = CreateManager();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            manager.AddOrUpdateCallstack(session, callstack);

            var nodes = manager.BuildTreeNodes(session);

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
            var manager = CreateManager();
            var session = new CallstackSession("Test");
            var callstack1 = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            var callstack2 = CreateTestCallstack("main.cs", "main", "Run", "DoWork");
            manager.AddOrUpdateCallstack(session, callstack1);
            manager.AddOrUpdateCallstack(session, callstack2);

            var nodes = manager.BuildTreeNodes(session);

            Assert.AreEqual(1, nodes.Count);
            Assert.AreEqual(1, nodes[0].Children.Count);
            Assert.AreEqual(1, nodes[0].Children[0].Children.Count);
        }

        [TestMethod]
        public void BuildTreeNodes_SeparatesDifferentCallSites()
        {
            var manager = CreateManager();
            var session = new CallstackSession("Test");
            var callstack1 = CreateTestCallstack("main.cs", "main", "Run");
            var callstack2 = CreateTestCallstack("main.cs", "main", "Execute");
            manager.AddOrUpdateCallstack(session, callstack1);
            manager.AddOrUpdateCallstack(session, callstack2);

            var nodes = manager.BuildTreeNodes(session);

            Assert.AreEqual(1, nodes.Count);
            Assert.AreEqual(2, nodes[0].Children.Count);
            var childNames = nodes[0].Children.Select(c => c.DisplayText).ToList();
            CollectionAssert.Contains(childNames, "Run");
            CollectionAssert.Contains(childNames, "Execute");
        }

        [TestMethod]
        public void BuildTreeNodes_CreatesLeafNodeForLastFrame()
        {
            var manager = CreateManager();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main", "Run");
            callstack.Frames.Last().LineContent = "Console.WriteLine();";
            manager.AddOrUpdateCallstack(session, callstack);

            var nodes = manager.BuildTreeNodes(session);

            var runNode = nodes[0].Children[0];
            Assert.AreEqual(1, runNode.Children.Count);
            var leafNode = runNode.Children[0];
            Assert.IsTrue(leafNode.IsLeaf);
            Assert.AreEqual("Console.WriteLine();", leafNode.DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_SeparatesLeavesWithSameContentDifferentLine()
        {
            var manager = CreateManager();
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

            manager.AddOrUpdateCallstack(session, callstack1);
            manager.AddOrUpdateCallstack(session, callstack2);

            var nodes = manager.BuildTreeNodes(session);

            var mainChildren = nodes[0].Children;
            Assert.AreEqual(2, mainChildren.Count);
        }

        [TestMethod]
        public void BuildTreeNodes_LeafDisplayText_TruncatesLongContent()
        {
            var manager = CreateManager();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main");
            callstack.Frames[0].LineContent = "This is a very long line that should be truncated";
            manager.AddOrUpdateCallstack(session, callstack);

            var nodes = manager.BuildTreeNodes(session);

            var leafNode = nodes[0].Children[0];
            Assert.IsTrue(leafNode.IsLeaf);
            Assert.AreEqual(Constants.LeafNodeDisplayMaxLength, leafNode.DisplayText.Length);
            Assert.IsTrue(leafNode.DisplayText.EndsWith("..."));
        }

        [TestMethod]
        public void BuildTreeNodes_LeafDisplayText_UsesAsIsAtMaxLength()
        {
            var manager = CreateManager();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main");
            callstack.Frames[0].LineContent = new string('x', Constants.LeafNodeDisplayMaxLength);
            manager.AddOrUpdateCallstack(session, callstack);

            var nodes = manager.BuildTreeNodes(session);

            var leafNode = nodes[0].Children[0];
            Assert.IsTrue(leafNode.IsLeaf);
            Assert.AreEqual(new string('x', Constants.LeafNodeDisplayMaxLength), leafNode.DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_LeafDisplayText_FallsBackToFileLine()
        {
            var manager = CreateManager();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main");
            callstack.Frames[0].LineContent = null;
            manager.AddOrUpdateCallstack(session, callstack);

            var nodes = manager.BuildTreeNodes(session);

            var leafNode = nodes[0].Children[0];
            Assert.IsTrue(leafNode.IsLeaf);
            Assert.AreEqual("<main.cs:10>", leafNode.DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_LeafDisplayText_FallsBackToCurrentLine()
        {
            var manager = CreateManager();
            var session = new CallstackSession("Test");
            var callstack = new CallstackData(new List<CallstackFrame>
            {
                new CallstackFrame("main", "", 0) { HashCode = 1 }
            });
            manager.AddOrUpdateCallstack(session, callstack);

            var nodes = manager.BuildTreeNodes(session);

            var leafNode = nodes[0].Children[0];
            Assert.IsTrue(leafNode.IsLeaf);
            Assert.AreEqual("<current line>", leafNode.DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_OrdersChildrenByLineNumber()
        {
            var manager = CreateManager();
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

            manager.AddOrUpdateCallstack(session, callstack1);
            manager.AddOrUpdateCallstack(session, callstack2);

            var nodes = manager.BuildTreeNodes(session);

            var mainChildren = nodes[0].Children;
            Assert.AreEqual(2, mainChildren.Count);
            Assert.AreEqual("FuncA", mainChildren[0].DisplayText);
            Assert.AreEqual("FuncB", mainChildren[1].DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_MultipleRoots_PreservesDiscoveryOrder()
        {
            var manager = CreateManager();
            var session = new CallstackSession("Test");

            var callstack1 = CreateTestCallstack("file1.cs", "RootA", "Child1");
            var callstack2 = CreateTestCallstack("file2.cs", "RootB", "Child2");
            manager.AddOrUpdateCallstack(session, callstack1);
            manager.AddOrUpdateCallstack(session, callstack2);

            var nodes = manager.BuildTreeNodes(session);

            Assert.AreEqual(2, nodes.Count);
            Assert.AreEqual("RootA", nodes[0].DisplayText);
            Assert.AreEqual("RootB", nodes[1].DisplayText);
        }

        [TestMethod]
        public void BuildTreeNodes_LeafNode_HasCorrectMergeId()
        {
            var manager = CreateManager();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main.cs", "main");
            callstack.Frames[0].LineContent = "test content";
            manager.AddOrUpdateCallstack(session, callstack);

            var nodes = manager.BuildTreeNodes(session);

            var leafNode = nodes[0].Children[0];
            Assert.IsTrue(leafNode.IsLeaf);
            Assert.AreNotEqual(0, leafNode.MergeId);
        }

        private CallstackManager CreateManager()
        {
            return new CallstackManager(null);
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
    }
}
