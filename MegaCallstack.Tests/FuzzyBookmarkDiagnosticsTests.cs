using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Services;
using Newtonsoft.Json.Linq;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class FuzzyBookmarkDiagnosticsTests
    {
        private string _tempDirectory;

        [TestInitialize]
        public void Initialize()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "MegaCallstack_DiagnosticsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_tempDirectory))
                    Directory.Delete(_tempDirectory, true);
            }
            catch { }
        }

        [TestMethod]
        public void CreateAll_WithDiagnostics_WritesScopeParserJsonAndCreateLog()
        {
            var diagnostics = new FuzzyBookmarkFileDiagnostics(_tempDirectory);
            var engine = new FuzzyBookmarkEngine(diagnostics);

            string filePath = Path.Combine(_tempDirectory, "source.cs");
            File.WriteAllLines(filePath, new[]
            {
                "class Program",
                "{",
                "    static void Main()",
                "    {",
                "        Console.WriteLine(\"hi\");",
                "    }",
                "}"
            });

            var bookmarks = engine.CreateAll(new[] { 5 }, filePath);

            var files = Directory.GetFiles(_tempDirectory, "*-scope-parser.json");
            Assert.AreEqual(1, files.Length, "Expected one scope-parser JSON file.");
            string operationId = Path.GetFileNameWithoutExtension(files[0]).Replace("-scope-parser", "");

            string json = File.ReadAllText(files[0]);
            var root = JObject.Parse(json);
            Assert.AreEqual(operationId, root["OperationId"]?.Value<string>());
            Assert.AreEqual(7, root["LineCount"]?.Value<int>());
            Assert.IsNotNull(root["Root"]);

            string createLog = Path.Combine(_tempDirectory, $"{operationId}-bookmark-create.txt");
            Assert.IsTrue(File.Exists(createLog), "Expected bookmark create log.");
            string log = File.ReadAllText(createLog);
            StringAssert.Contains(log, "=== Bookmark Created ===");
            StringAssert.Contains(log, "Original line: 5");
            StringAssert.Contains(log, bookmarks[0].LineContent);

            string resolveLog = Path.Combine(_tempDirectory, $"{operationId}-bookmark-resolve.txt");
            Assert.IsFalse(File.Exists(resolveLog), "Create operation should not produce a resolve log.");
        }

        [TestMethod]
        public void ResolveAll_WithDiagnostics_WritesDecisionDetails()
        {
            var diagnostics = new FuzzyBookmarkFileDiagnostics(_tempDirectory);
            var engine = new FuzzyBookmarkEngine(diagnostics);

            string filePath = Path.Combine(_tempDirectory, "source.cs");
            File.WriteAllLines(filePath, new[]
            {
                "class Program",
                "{",
                "    static void Main()",
                "    {",
                "        Console.WriteLine(\"hi\");",
                "    }",
                "}"
            });

            var bookmarks = engine.CreateAll(new[] { 5 }, filePath);

            File.WriteAllLines(filePath, new[]
            {
                "// added header",
                "class Program",
                "{",
                "    static void Main()",
                "    {",
                "        Console.WriteLine(\"hi\");",
                "    }",
                "}"
            });

            var results = engine.ResolveAll(bookmarks, filePath);

            var files = Directory.GetFiles(_tempDirectory, "*-scope-parser.json");
            Assert.IsTrue(files.Length >= 1);
            string operationId = Path.GetFileNameWithoutExtension(files[0]).Replace("-scope-parser", "");
            string resolveLog = Path.Combine(_tempDirectory, $"{operationId}-bookmark-resolve.txt");
            string log = File.ReadAllText(resolveLog);

            StringAssert.Contains(log, "=== Bookmark Resolved ===");
            StringAssert.Contains(log, "L1 exact candidates:");
            StringAssert.Contains(log, "L2a full context:");
            StringAssert.Contains(log, "L2b partial context:");
            StringAssert.Contains(log, "L3 fuzzy search window:");
            StringAssert.Contains(log, $"Result: line={results[0].Line}");
        }

        [TestMethod]
        public void ResolveAll_WithDiagnostics_AppendsAllBookmarksToSameLog()
        {
            var diagnostics = new FuzzyBookmarkFileDiagnostics(_tempDirectory);
            var engine = new FuzzyBookmarkEngine(diagnostics);

            string filePath = Path.Combine(_tempDirectory, "multi.cs");
            File.WriteAllLines(filePath, new[]
            {
                "class Program",
                "{",
                "    static void A() { int x = 1; }",
                "    static void B() { int y = 2; }",
                "}"
            });

            var bookmarks = engine.CreateAll(new[] { 3, 4 }, filePath);

            var results = engine.ResolveAll(bookmarks, filePath);

            Assert.AreEqual(2, results.Count);
            var resolveFiles = Directory.GetFiles(_tempDirectory, "*-bookmark-resolve.txt");
            Assert.IsTrue(resolveFiles.Length >= 1, "Expected at least one bookmark resolve log.");
            var resolveLogFile = resolveFiles
                .Select(f => new { Path = f, Text = File.ReadAllText(f) })
                .OrderByDescending(x => x.Text.Split(new[] { "=== Bookmark Resolved ===" }, StringSplitOptions.None).Length - 1)
                .First();
            int resolvedCount = resolveLogFile.Text.Split(new[] { "=== Bookmark Resolved ===" }, StringSplitOptions.None).Length - 1;
            Assert.AreEqual(2, resolvedCount, "Expected both bookmarks resolved in the same operation log.");
        }

        [TestMethod]
        public void BeginOperation_Collisions_AppendCounter()
        {
            var diagnostics = new FuzzyBookmarkFileDiagnostics(_tempDirectory);
            string filePath = Path.Combine(_tempDirectory, "sample.cs");
            string id1 = diagnostics.BeginOperation(filePath);
            diagnostics.CompleteOperation(id1);

            File.WriteAllText(diagnostics.GetScopeParserFilePath(id1), "{}");

            string id2 = diagnostics.BeginOperation(filePath);
            Assert.IsTrue(id2.StartsWith(id1 + "-", StringComparison.Ordinal), "Expected counter suffix on collision.");
        }
    }
}
