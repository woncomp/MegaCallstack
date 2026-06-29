using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Models;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class TreeViewNodeTests
    {
        [TestMethod]
        public void ColorPropagation_ParentInheritsChildColor()
        {
            var parent = new TreeViewNode { DisplayText = "Parent" };
            var child = new TreeViewNode { DisplayText = "Child" };
            parent.Children.Add(child);

            child.SetColorAndPropagate(new SolidColorBrush(Colors.Red));

            Assert.IsNotNull(parent.DisplayForeground);
            var parentColor = ((SolidColorBrush)parent.DisplayForeground).Color;
            Assert.AreEqual(Colors.Red, parentColor);
        }

        [TestMethod]
        public void ColorPropagation_GrandparentInheritsChildColor()
        {
            var grandparent = new TreeViewNode { DisplayText = "Grandparent" };
            var parent = new TreeViewNode { DisplayText = "Parent" };
            var child = new TreeViewNode { DisplayText = "Child" };

            grandparent.Children.Add(parent);
            parent.Children.Add(child);

            child.SetColorAndPropagate(new SolidColorBrush(Colors.Blue));

            Assert.IsNotNull(grandparent.DisplayForeground);
            var grandparentColor = ((SolidColorBrush)grandparent.DisplayForeground).Color;
            Assert.AreEqual(Colors.Blue, grandparentColor);
        }

        [TestMethod]
        public void ClearColor_ParentResolvesFromOtherChild()
        {
            var parent = new TreeViewNode { DisplayText = "Parent" };
            var child1 = new TreeViewNode { DisplayText = "Child1" };
            var child2 = new TreeViewNode { DisplayText = "Child2" };

            parent.Children.Add(child1);
            parent.Children.Add(child2);

            child1.SetColorAndPropagate(new SolidColorBrush(Colors.Red));
            child2.SetColorAndPropagate(new SolidColorBrush(Colors.Green));

            child1.ClearColorAndPropagate();

            var parentColor = ((SolidColorBrush)parent.DisplayForeground).Color;
            Assert.AreEqual(Colors.Green, parentColor);
        }

        [TestMethod]
        public void ClearColor_NoChildren_SetsNull()
        {
            var node = new TreeViewNode { DisplayText = "Node" };
            node.SetColorAndPropagate(new SolidColorBrush(Colors.Red));
            node.ClearColorAndPropagate();

            Assert.IsNull(node.DisplayForeground);
        }

        [TestMethod]
        public void FindNodeByHash_FindsCorrectNode()
        {
            var root = new TreeViewNode
            {
                DisplayText = "Root",
                Frame = new CallstackFrame("Root", "root.cs", 1) { HashCode = 100 }
            };
            var child = new TreeViewNode
            {
                DisplayText = "Child",
                Frame = new CallstackFrame("Child", "child.cs", 5) { HashCode = 200 }
            };
            root.Children.Add(child);

            var found = root.FindNodeByHash(200);
            Assert.IsNotNull(found);
            Assert.AreEqual("Child", found.Frame.FunctionName);
        }

        [TestMethod]
        public void FindNodeByHash_ReturnsNullWhenNotFound()
        {
            var root = new TreeViewNode
            {
                DisplayText = "Root",
                Frame = new CallstackFrame("Root", "root.cs", 1) { HashCode = 100 }
            };

            var found = root.FindNodeByHash(999);
            Assert.IsNull(found);
        }

        [TestMethod]
        public void SetPathBold_SetsAllAncestors()
        {
            var root = new TreeViewNode { DisplayText = "Root" };
            var parent = new TreeViewNode { DisplayText = "Parent" };
            var child = new TreeViewNode { DisplayText = "Child" };

            root.Children.Add(parent);
            parent.Children.Add(child);

            child.SetPathBold(true);

            Assert.IsTrue(root.IsBold);
            Assert.IsTrue(parent.IsBold);
            Assert.IsTrue(child.IsBold);
        }

        [TestMethod]
        public void GetEffectiveColor_ReturnsOwnColorIfSet()
        {
            var node = new TreeViewNode { DisplayText = "Node" };
            node.DisplayForeground = new SolidColorBrush(Colors.Red);

            var color = node.GetEffectiveColor();
            Assert.IsNotNull(color);
            Assert.AreEqual(Colors.Red, ((SolidColorBrush)color).Color);
        }

        [TestMethod]
        public void GetEffectiveColor_ReturnsChildColorIfOwnIsNull()
        {
            var parent = new TreeViewNode { DisplayText = "Parent" };
            var child = new TreeViewNode { DisplayText = "Child" };
            parent.Children.Add(child);

            child.DisplayForeground = new SolidColorBrush(Colors.Green);

            var color = parent.GetEffectiveColor();
            Assert.IsNotNull(color);
            Assert.AreEqual(Colors.Green, ((SolidColorBrush)color).Color);
        }

        [TestMethod]
        public void NotesCollection_IsInitiallyEmpty()
        {
            var node = new TreeViewNode { DisplayText = "Node" };
            Assert.IsNotNull(node.Notes);
            Assert.AreEqual(0, node.Notes.Count);
        }

        [TestMethod]
        public void NodeKey_ReturnsFrameHashCode_WhenFrameIsSet()
        {
            var node = new TreeViewNode
            {
                DisplayText = "Node",
                Frame = new CallstackFrame("Test", "test.cs", 1) { HashCode = 123 }
            };
            Assert.AreEqual(123, node.NodeKey);
        }

        [TestMethod]
        public void NodeKey_ReturnsMergeId_WhenFrameIsNull()
        {
            var node = new TreeViewNode { DisplayText = "Node", MergeId = 456 };
            Assert.AreEqual(456, node.NodeKey);
        }
    }
}
