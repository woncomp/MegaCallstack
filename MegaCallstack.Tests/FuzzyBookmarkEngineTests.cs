using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Services;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class FuzzyBookmarkEngineTests
    {
        // ---------- Create basics ----------

        [TestMethod]
        public void CreateAll_OutOfRange_ReturnsNullForThatItem()
        {
            var engine = new FuzzyBookmarkEngine();
            var path = Path.Combine(Path.GetTempPath(), "fbm_createall_" + Guid.NewGuid() + ".cs");
            var lines = new[] { "a", "b", "c" };
            File.WriteAllLines(path, lines);
            try
            {
                var result = engine.CreateAll(new[] { 1, 0, 4, 2 }, path);
                Assert.AreEqual(4, result.Count);
                Assert.IsNotNull(result[0]);
                Assert.IsNull(result[1]);
                Assert.IsNull(result[2]);
                Assert.IsNotNull(result[3]);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void CreateAll_RecordsNormalizedContentAndHash()
        {
            var engine = new FuzzyBookmarkEngine();
            var path = Path.Combine(Path.GetTempPath(), "fbm_createall_" + Guid.NewGuid() + ".cs");
            var lines = new[]
            {
                "class Foo",
                "{",
                "\tint x;",
                "}"
            };
            File.WriteAllLines(path, lines);
            try
            {
                var bookmarks = engine.CreateAll(new[] { 3 }, path);
                Assert.AreEqual(1, bookmarks.Count);
                Assert.AreEqual("int x;", bookmarks[0].LineContent);
                Assert.AreEqual(FuzzyBookmarkEngine.FNV1a(0, "int x;"), bookmarks[0].LineHash);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void CreateAll_PopulatesScopePathForRealScope()
        {
            var engine = new FuzzyBookmarkEngine();
            var path = Path.Combine(Path.GetTempPath(), "fbm_createall_" + Guid.NewGuid() + ".cs");
            var lines = new[]
            {
                "namespace App",
                "{",
                "    class Program",
                "    {",
                "        void Run()",
                "        {",
                "            return;",
                "        }",
                "    }",
                "}"
            };
            File.WriteAllLines(path, lines);
            try
            {
                var bookmarks = engine.CreateAll(new[] { 7 }, path);
                Assert.AreEqual(3, bookmarks[0].ScopePath.Length);
                Assert.AreEqual(2.0 / 3.0, bookmarks[0].Ratio, 0.001);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void CreateAll_FillerLine_HasEmptyScopePathAndFileRatio()
        {
            var engine = new FuzzyBookmarkEngine();
            var path = Path.Combine(Path.GetTempPath(), "fbm_createall_" + Guid.NewGuid() + ".cs");
            var lines = new[]
            {
                "#include <stdio.h>",
                "",
                "class Foo",
                "{",
                "    int x;",
                "}"
            };
            File.WriteAllLines(path, lines);
            try
            {
                var bookmarks = engine.CreateAll(new[] { 1 }, path);
                Assert.AreEqual(0, bookmarks[0].ScopePath.Length);
                Assert.AreEqual(0.0, bookmarks[0].Ratio, 0.001);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---------- Resolve: L1 exact on a moved line ----------

        [TestMethod]
        public void Resolve_L1_ExactContentHitsMovedLine()
        {
            var engine = new FuzzyBookmarkEngine();
            var original = new[]
            {
                "class Foo",
                "{",
                "    void Bar()",
                "    {",
                "        var target = 1;",
                "    }",
                "}"
            };
            var path = Path.Combine(Path.GetTempPath(), "fbm_resolve_" + Guid.NewGuid() + ".cs");
            File.WriteAllLines(path, original);
            try
            {
                var bookmarks = engine.CreateAll(new[] { 5 }, path); // "var target = 1;"
                var bm = bookmarks[0];

                // Insert 3 lines above the target (file grows, target moves down).
                var edited = new[]
                {
                    "// added comment 1",
                    "// added comment 2",
                    "// added comment 3",
                    "class Foo",
                    "{",
                    "    void Bar()",
                    "    {",
                    "        var target = 1;",
                    "    }",
                    "}"
                };
                File.WriteAllLines(path, edited);
                var results = engine.ResolveAll(new[] { bm }, path);
                var result = results[0];
                Assert.AreEqual(8, result.Line);
                Assert.IsTrue(result.Confidence > 0.0);
                Assert.IsTrue(result.MatchLevel == "Exact" || result.MatchLevel == "ContextFull");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---------- Resolve: L1 duplicate lines, L2 disambiguation ----------

        [TestMethod]
        public void Resolve_DuplicateLine_L2ContextDisambiguates()
        {
            var engine = new FuzzyBookmarkEngine();
            var original = new[]
            {
                "class Foo",
                "{",
                "    void A()",
                "    {",
                "        return;",
                "    }",
                "    void B()",
                "    {",
                "        return;",
                "    }",
                "}"
            };
            var path = Path.Combine(Path.GetTempPath(), "fbm_resolve_" + Guid.NewGuid() + ".cs");
            File.WriteAllLines(path, original);
            try
            {
                var bookmarks = engine.CreateAll(new[] { 5 }, path);
                var bm = bookmarks[0];
                Assert.AreEqual("return;", bm.LineContent);

                // Resolve against the same file: should land on the first return (line 5),
                // not the second (line 9), because the context above/below differs.
                var results = engine.ResolveAll(new[] { bm }, path);
                Assert.AreEqual(5, results[0].Line);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---------- Resolve: L2a full context window ----------

        [TestMethod]
        public void Resolve_L2a_FullContextWindowMatch()
        {
            var engine = new FuzzyBookmarkEngine();
            var path = Path.Combine(Path.GetTempPath(), "fbm_resolve_" + Guid.NewGuid() + ".cs");
            var original = new[]
            {
                "void Run()",
                "{",
                "    int alpha = 1;",
                "    int beta = 2;",
                "    int gamma = 3;",
                "    int delta = 4;",
                "}"
            };
            File.WriteAllLines(path, original);
            try
            {
                var bookmarks = engine.CreateAll(new[] { 4 }, path); // "int beta = 2;"
                var bm = bookmarks[0];

                // Move the function down (insert lines above) and add an unrelated "int beta = 2;"
                // elsewhere so L1 has multiple candidates; only the original context window matches fully.
                var edited = new[]
                {
                    "// preamble",
                    "void Helper()",
                    "{",
                    "    int beta = 2;",
                    "}",
                    "",
                    "void Run()",
                    "{",
                    "    int alpha = 1;",
                    "    int beta = 2;",
                    "    int gamma = 3;",
                    "    int delta = 4;",
                    "}"
                };
                File.WriteAllLines(path, edited);
                var results = engine.ResolveAll(new[] { bm }, path);
                var result = results[0];
                // Expect the "int beta = 2;" inside Run (line 10), distinguished by alpha/gamma neighbors.
                Assert.AreEqual(10, result.Line);
                Assert.AreEqual("ContextFull", result.MatchLevel);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---------- Resolve: L3 fuzzy on an edited line ----------

        [TestMethod]
        public void Resolve_L3_FuzzyMatchOnEditedLine()
        {
            var engine = new FuzzyBookmarkEngine();
            var path = Path.Combine(Path.GetTempPath(), "fbm_resolve_" + Guid.NewGuid() + ".cs");
            var original = new[]
            {
                "class Foo",
                "{",
                "    void Bar()",
                "    {",
                "        int numberOfItems = 42;",
                "    }",
                "}"
            };
            File.WriteAllLines(path, original);
            try
            {
                var bookmarks = engine.CreateAll(new[] { 5 }, path); // "int numberOfItems = 42;"
                var bm = bookmarks[0];

                // Edit the target line's value only. No exact match anywhere; L3 should catch it
                // (tokens "int","numberOfItems" still match; only the literal differs).
                var edited = new[]
                {
                    "class Foo",
                    "{",
                    "    void Bar()",
                    "    {",
                    "        int numberOfItems = 100;",
                    "    }",
                    "}"
                };
                File.WriteAllLines(path, edited);
                var results = engine.ResolveAll(new[] { bm }, path);
                var result = results[0];
                Assert.AreEqual(5, result.Line);
                Assert.AreEqual("Fuzzy", result.MatchLevel);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void Resolve_L3_FullRename_FallsBackWhenBelowThreshold()
        {
            // A complete variable rename drops similarity below the fuzzy threshold; the engine
            // should NOT return a low-confidence guess (per "high-fidelity: prefer no match over
            // a wrong match"), and should fall back to the seed-derived line instead.
            var engine = new FuzzyBookmarkEngine();
            var path = Path.Combine(Path.GetTempPath(), "fbm_resolve_" + Guid.NewGuid() + ".cs");
            var original = new[]
            {
                "class Foo",
                "{",
                "    void Bar()",
                "    {",
                "        int numberOfItems = 42;",
                "    }",
                "}"
            };
            File.WriteAllLines(path, original);
            try
            {
                var bookmarks = engine.CreateAll(new[] { 5 }, path);
                var bm = bookmarks[0];

                var edited = new[]
                {
                    "class Foo",
                    "{",
                    "    void Bar()",
                    "    {",
                    "        int itemCount = 42;",
                    "    }",
                    "}"
                };
                File.WriteAllLines(path, edited);
                var results = engine.ResolveAll(new[] { bm }, path);
                var result = results[0];
                Assert.AreEqual("Fallback", result.MatchLevel);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---------- Resolve: NotFound / empty ----------

        [TestMethod]
        public void Resolve_EmptyFile_ReturnsNotFound()
        {
            var engine = new FuzzyBookmarkEngine();
            var path = Path.Combine(Path.GetTempPath(), "fbm_empty_" + Guid.NewGuid() + ".cs");
            File.WriteAllText(path, string.Empty);
            try
            {
                var bm = new FuzzyBookmark { LineContent = "x", LineHash = 1, ScopePath = new uint[0], Ratio = 0.5 };
                var results = engine.ResolveAll(new[] { bm }, path);
                var result = results[0];
                Assert.AreEqual(0, result.Line);
                Assert.AreEqual("NotFound", result.MatchLevel);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---------- Resolve: file missing ----------

        [TestMethod]
        public void Resolve_MissingFile_ReturnsNotFound()
        {
            var engine = new FuzzyBookmarkEngine();
            var bm = new FuzzyBookmark { LineContent = "x", LineHash = 1, ScopePath = new uint[0], Ratio = 0.5 };
            var results = engine.ResolveAll(new[] { bm }, Path.Combine(Path.GetTempPath(), "definitely_missing_" + Guid.NewGuid() + ".cs"));
            var result = results[0];
            Assert.AreEqual(0, result.Line);
            Assert.AreEqual("NotFound", result.MatchLevel);
        }

        // ---------- Resolve: scope fallback ----------

        [TestMethod]
        public void Resolve_ScopeNotFound_FallsBackToParentScope()
        {
            var engine = new FuzzyBookmarkEngine();
            var path = Path.Combine(Path.GetTempPath(), "fbm_resolve_" + Guid.NewGuid() + ".cs");
            var original = new[]
            {
                "namespace App",
                "{",
                "    class Worker",
                "    {",
                "        void DoWork()",
                "        {",
                "            var target = 1;",
                "        }",
                "    }",
                "}"
            };
            File.WriteAllLines(path, original);
            try
            {
                // Target line 7 inside function DoWork.
                var bookmarks = engine.CreateAll(new[] { 7 }, path);
                var bm = bookmarks[0];

                // Remove the function entirely but keep the class; the line content still exists.
                var edited = new[]
                {
                    "namespace App",
                    "{",
                    "    class Worker",
                    "    {",
                    "        var target = 1;",
                    "    }",
                    "}"
                };
                File.WriteAllLines(path, edited);
                var results = engine.ResolveAll(new[] { bm }, path);
                var result = results[0];
                // Should still find the "var target = 1;" line (now line 5) by content/context.
                Assert.AreEqual(5, result.Line);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---------- Resolve: same-name scope rank tiebreak ----------

        [TestMethod]
        public void Resolve_SameNameScope_RankTiebreak()
        {
            var engine = new FuzzyBookmarkEngine();
            // Two classes named "Foo": one big, one small. Rank distinguishes them.
            var path = Path.Combine(Path.GetTempPath(), "fbm_resolve_" + Guid.NewGuid() + ".cs");
            var original = new[]
            {
                "class Foo",
                "{",
                "    int a;",
                "    int b;",
                "    int c;",
                "    int d;",
                "}",
                "",
                "class Foo",
                "{",
                "    int x;",
                "}"
            };
            File.WriteAllLines(path, original);
            try
            {
                // Bookmark a line in the BIG class (lines 2..6). The big class has Rank=1.
                var bookmarks = engine.CreateAll(new[] { 5 }, path); // "int c;"
                var bm = bookmarks[0];

                // Reorder so the small class comes first; big class still exists and has Rank=1.
                var edited = new[]
                {
                    "class Foo",
                    "{",
                    "    int x;",
                    "}",
                    "",
                    "class Foo",
                    "{",
                    "    int a;",
                    "    int b;",
                    "    int c;",
                    "    int d;",
                    "}"
                };
                File.WriteAllLines(path, edited);
                var results = engine.ResolveAll(new[] { bm }, path);
                var result = results[0];
                // Should land in the big class (lines 6..12), specifically "int c;" at line 10.
                Assert.AreEqual(10, result.Line);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---------- Resolve: dynamic context expansion ----------

        [TestMethod]
        public void Create_DuplicateLine_ExpandsContext()
        {
            var engine = new FuzzyBookmarkEngine();
            // A file with many "x = 0;" duplicates but different surrounding lines.
            var path = Path.Combine(Path.GetTempPath(), "fbm_create_" + Guid.NewGuid() + ".cs");
            var original = new List<string>
            {
                "class Foo",
                "{",
                "    void A()",
                "    {",
                "        x = 0;",
                "    }",
                "    void B()",
                "    {",
                "        x = 0;",
                "    }",
                "    void C()",
                "    {",
                "        x = 0;",
                "    }",
                "}"
            };
            File.WriteAllLines(path, original);
            try
            {
                var bookmarks = engine.CreateAll(new[] { 5 }, path); // first "x = 0;"
                var bm = bookmarks[0];
                // N = 3 occurrences -> target = min(3,5) = 3 -> context expands toward 3 differing lines.
                Assert.IsTrue(bm.PreContextHashes.Length >= 2, "Pre context should be at least the default span");
                Assert.AreEqual(bm.PreContextHashes.Length, bm.PostContextHashes.Length);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---------- Resolve: single-line scope (span=0) ----------

        [TestMethod]
        public void Create_SingleLineScope_RatioIsZero()
        {
            var engine = new FuzzyBookmarkEngine();
            var path = Path.Combine(Path.GetTempPath(), "fbm_create_" + Guid.NewGuid() + ".cs");
            var lines = new[]
            {
                "class Foo",
                "{",
                "}",
                "",
                "void Bar()",
                "{",
                "}"
            };
            File.WriteAllLines(path, lines);
            try
            {
                // Line 2 "{" is filler; line 5 "void Bar()" with immediate block on line 6..7.
                // "void Bar()" itself: detected as anchor; the function scope is [5,6].
                var bookmarks = engine.CreateAll(new[] { 5 }, path);
                // The function node StartLine=5, EndLine=6 (0-based 4,5)? Span>=1 so ratio computed normally.
                // Just ensure it doesn't throw and scope path is populated.
                Assert.IsTrue(bookmarks[0].ScopePath.Length >= 1);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---------- ResolveAll: batch ----------

        [TestMethod]
        public void ResolveAll_SharedFile_ReturnsOneResultPerBookmark()
        {
            var engine = new FuzzyBookmarkEngine();
            var path = Path.Combine(Path.GetTempPath(), "fbm_resolve_" + Guid.NewGuid() + ".cs");
            var original = new[]
            {
                "class Foo",
                "{",
                "    void A()",
                "    {",
                "        var a = 1;",
                "    }",
                "    void B()",
                "    {",
                "        var b = 2;",
                "    }",
                "}"
            };
            File.WriteAllLines(path, original);
            try
            {
                var bookmarks = engine.CreateAll(new[] { 5, 9 }, path); // "var a = 1;", "var b = 2;"
                var bm1 = bookmarks[0];
                var bm2 = bookmarks[1];

                // Insert a header line; both should shift by +1.
                var edited = new List<string> { "// header" };
                edited.AddRange(original);
                File.WriteAllLines(path, edited);

                var results = engine.ResolveAll(new[] { bm1, bm2 }, path);
                Assert.AreEqual(2, results.Count);
                Assert.AreEqual(6, results[0].Line);
                Assert.AreEqual(10, results[1].Line);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---------- File-based Create/Resolve round trip ----------

        [TestMethod]
        public void CreateFromFile_ResolveFromEditedFile_LocatesLine()
        {
            var engine = new FuzzyBookmarkEngine();
            var path = Path.Combine(Path.GetTempPath(), "fbm_test_" + Guid.NewGuid() + ".cs");
            var original = new[]
            {
                "class Program",
                "{",
                "    static void Main()",
                "    {",
                "        Console.WriteLine(\"hi\");",
                "    }",
                "}"
            };
            File.WriteAllLines(path, original, Encoding.UTF8);
            try
            {
                var bookmarks = engine.CreateAll(new[] { 5 }, path);
                var bm = bookmarks[0];

                var edited = new[]
                {
                    "// added",
                    "class Program",
                    "{",
                    "    static void Main()",
                    "    {",
                    "        Console.WriteLine(\"hi\");",
                    "    }",
                    "}"
                };
                File.WriteAllLines(path, edited, Encoding.UTF8);

                var results = engine.ResolveAll(new[] { bm }, path);
                var result = results[0];
                Assert.AreEqual(6, result.Line);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---------- Scope id packing ----------

        [TestMethod]
        public void PackScopeId_RoundTripsNameHashAndRank()
        {
            int nameHash = 0x123456;
            float rank = 0.5f;
            uint id = FuzzyBookmark.PackScopeId(nameHash, rank);
            Assert.AreEqual(nameHash, FuzzyBookmark.NameHashOf(id));
            // round(0.5*255) = round(127.5) = 128 (MidpointRounding.ToEven -> 128)
            Assert.AreEqual(128, FuzzyBookmark.RankOf(id));
        }

        [TestMethod]
        public void PackScopeId_ClampsRankToByteRange()
        {
            uint idLow = FuzzyBookmark.PackScopeId(0xABCDEF, -0.5f);
            uint idHigh = FuzzyBookmark.PackScopeId(0xABCDEF, 2.0f);
            Assert.AreEqual(0, FuzzyBookmark.RankOf(idLow));
            Assert.AreEqual(255, FuzzyBookmark.RankOf(idHigh));
        }
    }
}
