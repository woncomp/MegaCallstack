using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Services;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class ScopeParserTests
    {
        [TestMethod]
        public void Parse_NullInput_ReturnsEmptyRoot()
        {
            var parser = new LightScopeParser();
            var root = parser.Parse(null);
            Assert.AreEqual("Root", root.Type);
            Assert.AreEqual(0, root.StartLine);
            Assert.AreEqual(0, root.EndLine);
            Assert.AreEqual(0, root.Children.Count);
        }

        [TestMethod]
        public void Parse_EmptyInput_ReturnsEmptyRoot()
        {
            var parser = new LightScopeParser();
            var root = parser.Parse(new string[0]);
            Assert.AreEqual("Root", root.Type);
            Assert.AreEqual(0, root.StartLine);
            Assert.AreEqual(0, root.EndLine);
        }

        [TestMethod]
        public void Parse_SingleClass_CreatesClassNode()
        {
            var lines = new[]
            {
                "class Foo",
                "{",
                "    int x;",
                "}"
            };
            var parser = new LightScopeParser();
            var root = parser.Parse(lines);

            Assert.AreEqual(1, root.Children.Count);

            var classNode = root.Children[0];
            Assert.AreEqual("Class", classNode.Type);
            Assert.AreEqual("class Foo", classNode.Name);
            Assert.AreEqual(0, classNode.StartLine);
            Assert.AreEqual(3, classNode.EndLine);
        }

        [TestMethod]
        public void Parse_Enum_DetectsEnum()
        {
            var lines = new[]
            {
                "enum Color",
                "{",
                "    Red,",
                "    Green",
                "}"
            };
            var parser = new LightScopeParser();
            var root = parser.Parse(lines);

            var enumNode = root.Children.Single(c => c.Type == "Enum");
            Assert.AreEqual("Enum", enumNode.Type);
            Assert.AreEqual("enum Color", enumNode.Name);
        }

        [TestMethod]
        public void Parse_Struct_DetectsStruct()
        {
            var lines = new[]
            {
                "struct Point",
                "{",
                "    int x;",
                "    int y;",
                "}"
            };
            var parser = new LightScopeParser();
            var root = parser.Parse(lines);

            var structNode = root.Children.Single(c => c.Type == "Struct");
            Assert.AreEqual("struct Point", structNode.Name);
        }

        [TestMethod]
        public void Parse_Interface_DetectsInterface()
        {
            var lines = new[]
            {
                "interface IWorker",
                "{",
                "    void DoWork();",
                "}"
            };
            var parser = new LightScopeParser();
            var root = parser.Parse(lines);

            var ifaceNode = root.Children.Single(c => c.Type == "Interface");
            Assert.AreEqual("interface IWorker", ifaceNode.Name);
        }

        [TestMethod]
        public void Parse_NamespaceAndClass_NestsCorrectly()
        {
            var lines = new[]
            {
                "namespace MyApp",
                "{",
                "    class Program",
                "    {",
                "        void Run()",
                "        {",
                "        }",
                "    }",
                "}"
            };
            var parser = new LightScopeParser();
            var root = parser.Parse(lines);

            Assert.AreEqual(1, root.Children.Count);
            var ns = root.Children[0];
            Assert.AreEqual("Namespace", ns.Type);

            Assert.AreEqual(3, ns.Children.Count);
            var nsClass = ns.Children.Single(c => c.Type == "Class");
            Assert.AreEqual("class Program", nsClass.Name);

            Assert.AreEqual(3, nsClass.Children.Count);
            var func = nsClass.Children.Single(c => c.Type == "Function");
            Assert.AreEqual("function Run()", func.Name);
            Assert.AreEqual(0, func.Children.Count);
        }

        [TestMethod]
        public void Parse_Function_DetectsFunction()
        {
            var lines = new[]
            {
                "void DoWork(int x)",
                "{",
                "    return;",
                "}"
            };
            var parser = new LightScopeParser();
            var root = parser.Parse(lines);

            var funcNode = root.Children.Single(c => c.Type == "Function");
            Assert.AreEqual("function DoWork()", funcNode.Name);
            Assert.AreEqual(0, funcNode.StartLine);
            Assert.AreEqual(3, funcNode.EndLine);
        }

        [TestMethod]
        public void Parse_ControlKeywords_NotDetectedAsFunctions()
        {
            var controlKeywords = new[] { "if", "for", "while", "switch", "catch", "sizeof", "typeof" };
            var parser = new LightScopeParser();

            foreach (var keyword in controlKeywords)
            {
                var lines = new[]
                {
                    $"{keyword} (condition)",
                    "{",
                    "    doSomething();",
                    "}"
                };
                var root = parser.Parse(lines);
                var functions = root.Children.Where(c => c.Type == "Function").ToList();
                Assert.AreEqual(0, functions.Count, $"Keyword '{keyword}' should not be detected as function");
            }
        }

        [TestMethod]
        public void Parse_FunctionWithInnerIf_TracksInternalBraces()
        {
            var lines = new[]
            {
                "void Process()",
                "{",
                "    if (x > 0)",
                "    {",
                "        return;",
                "    }",
                "}"
            };
            var parser = new LightScopeParser();
            var root = parser.Parse(lines);

            var funcNode = root.Children.Single(c => c.Type == "Function");
            Assert.AreEqual(0, funcNode.StartLine);
            Assert.AreEqual(6, funcNode.EndLine);
        }

        [TestMethod]
        public void Parse_FillerGaps_TilePerfectly()
        {
            var lines = new[]
            {
                "#include <stdio.h>",
                "",
                "class Foo",
                "{",
                "};"
            };
            var parser = new LightScopeParser();
            var root = parser.Parse(lines);

            Assert.AreEqual(2, root.Children.Count);
            var headerFiller = root.Children[0];
            Assert.AreEqual("Filler", headerFiller.Type);
            Assert.AreEqual(0, headerFiller.StartLine);
            Assert.AreEqual(1, headerFiller.EndLine);

            var classNode = root.Children[1];
            Assert.AreEqual("Class", classNode.Type);
            Assert.AreEqual(2, classNode.StartLine);

            Assert.AreEqual(0, root.Children[0].StartLine);
            Assert.AreEqual(root.Children[0].EndLine + 1, root.Children[1].StartLine);
            Assert.AreEqual(root.Children[1].EndLine, root.EndLine);
        }

        [TestMethod]
        public void Parse_MultipleSiblings_GapsFilled()
        {
            var lines = new[]
            {
                "class A",
                "{",
                "};",
                "",
                "void FuncB()",
                "{",
                "}"
            };
            var parser = new LightScopeParser();
            var root = parser.Parse(lines);

            Assert.IsTrue(root.Children.Count >= 2);
            var types = root.Children.Select(c => c.Type).ToList();
            Assert.IsTrue(types.Contains("Filler"));
            Assert.IsTrue(types.Contains("Class"));
            Assert.IsTrue(types.Contains("Function"));
        }

        [TestMethod]
        public void Parse_UnclosedScope_ClosesAtEOF()
        {
            var lines = new[]
            {
                "class Foo",
                "{",
                "    int x;"
            };
            var parser = new LightScopeParser();
            var root = parser.Parse(lines);

            var classNode = root.Children.Single(c => c.Type == "Class");
            Assert.AreEqual(2, classNode.EndLine);
        }

        [TestMethod]
        public void Parse_RootRange_CoversAllLines()
        {
            var lines = new[]
            {
                "// Header comment",
                "class Foo",
                "{",
                "    void Bar()",
                "    {",
                "        return;",
                "    }",
                "}"
            };
            var parser = new LightScopeParser();
            var root = parser.Parse(lines);

            Assert.AreEqual(0, root.StartLine);
            Assert.AreEqual(7, root.EndLine);
        }

        [TestMethod]
        public void Parse_CommentOnlyFile_AllFiller()
        {
            var lines = new[]
            {
                "// Just a comment",
                "// Another comment",
                "/* Block comment */"
            };
            var parser = new LightScopeParser();
            var root = parser.Parse(lines);

            Assert.AreEqual(1, root.Children.Count);
            Assert.AreEqual("Filler", root.Children[0].Type);
            Assert.AreEqual(0, root.Children[0].StartLine);
            Assert.AreEqual(2, root.Children[0].EndLine);
        }

        [TestMethod]
        public void ToString_FormatsCorrectly()
        {
            var node = new ScopeNode("class Foo", "Class", 5, 4);
            node.EndLine = 10;
            Assert.AreEqual("[Class] class Foo : [5, 10]", node.ToString());
        }
    }
}
