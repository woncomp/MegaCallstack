using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Services;

namespace FuzzyBookmark.Test
{
    [TestClass]
    public class FuzzyBookmarkFileTests
    {
        private static readonly Regex MarkerRegex = new Regex(
            @"^\s*//\s*(?<key>Bookmark|Expect):\s*(?<line>\d+)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static string TestCasesDirectory
        {
            get
            {
                string assemblyDir = Path.GetDirectoryName(typeof(FuzzyBookmarkFileTests).Assembly.Location);
                return Path.Combine(assemblyDir, "TestCases");
            }
        }

        private static string DiagnosticsDirectory
        {
            get
            {
                string assemblyDir = Path.GetDirectoryName(typeof(FuzzyBookmarkFileTests).Assembly.Location);
                return Path.Combine(assemblyDir, "TestResult");
            }
        }

        [TestInitialize]
        public void Initialize()
        {
            if (Directory.Exists(DiagnosticsDirectory))
            {
                Directory.Delete(DiagnosticsDirectory, true);
            }
        }

        [TestCleanup]
        public void Cleanup()
        {
        }

        [TestMethod]
        [DynamicData(nameof(GetTestCases), DynamicDataSourceType.Method)]
        public void ResolveFuzzyBookmarkFromFiles(string testName, string originalPath, string modifiedPath, int bookmarkLine, int expectedLine)
        {
            var diagnostics = new FuzzyBookmarkFileDiagnostics(DiagnosticsDirectory);
            var engine = new FuzzyBookmarkEngine(diagnostics);

            var bookmarks = engine.CreateAll(new[] { bookmarkLine }, originalPath);
            Assert.AreEqual(1, bookmarks.Count, "Exactly one bookmark should be created.");
            Assert.IsNotNull(bookmarks[0], $"Bookmark at line {bookmarkLine} in {testName} was null.");

            var results = engine.ResolveAll(bookmarks, modifiedPath);
            Assert.AreEqual(1, results.Count, "Exactly one resolve result should be produced.");

            var result = results[0];
            Assert.AreEqual(
                expectedLine,
                result.Line,
                $"Test '{testName}' expected line {expectedLine} but resolved to line {result.Line} (match level: {result.MatchLevel}, confidence: {result.Confidence:0.000}).");

            Assert.IsTrue(
                result.Confidence > 0.0,
                $"Test '{testName}' should resolve with positive confidence.");
        }

        public static IEnumerable<object[]> GetTestCases()
        {
            if (!Directory.Exists(TestCasesDirectory))
            {
                Assert.Fail($"Test cases directory not found: {TestCasesDirectory}");
            }

            string[] originalFiles = Directory.GetFiles(TestCasesDirectory, "test_*.cpp", SearchOption.TopDirectoryOnly);

            foreach (string originalPath in originalFiles)
            {
                string fileName = Path.GetFileName(originalPath);
                if (fileName.EndsWith(".mod.cpp", StringComparison.OrdinalIgnoreCase))
                    continue;

                string modifiedPath = Path.Combine(
                    Path.GetDirectoryName(originalPath),
                    Path.GetFileNameWithoutExtension(originalPath) + ".mod.cpp");

                if (!File.Exists(modifiedPath))
                    continue;

                int bookmarkLine = ReadMarker(originalPath, "Bookmark");
                int expectedLine = ReadMarker(modifiedPath, "Expect");

                yield return new object[]
                {
                    Path.GetFileNameWithoutExtension(originalPath),
                    originalPath,
                    modifiedPath,
                    bookmarkLine,
                    expectedLine
                };
            }
        }

        private static int ReadMarker(string filePath, string key)
        {
            Assert.IsTrue(File.Exists(filePath), $"File not found: {filePath}");

            string[] lines = File.ReadAllLines(filePath);
            Assert.IsTrue(lines.Length > 0, $"File is empty: {filePath}");

            string firstLine = lines[0];
            Match match = MarkerRegex.Match(firstLine);
            Assert.IsTrue(
                match.Success,
                $"First line of {filePath} must contain a '{key}: <line>' marker. Actual first line: '{firstLine}'");
            Assert.AreEqual(
                key,
                match.Groups["key"].Value,
                StringComparer.OrdinalIgnoreCase,
                $"First line of {filePath} must be a '{key}' marker.");

            int lineNumber = int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture);
            Assert.IsTrue(lineNumber > 0, $"{key} marker in {filePath} must be a positive line number.");

            return lineNumber;
        }
    }
}
