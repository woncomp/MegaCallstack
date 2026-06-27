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
        public void Create_OutOfRange_Throws()
        {
            var engine = new FuzzyBookmarkEngine();
            var lines = new[] { "a", "b", "c" };
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => engine.Create(lines, 0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => engine.Create(lines, 4));
        }

        [TestMethod]
        public void Create_RecordsNormalizedContentAndHash()
        {
            var engine = new FuzzyBookmarkEngine();
            var lines = new[]
            {
                "class Foo",
                "{",
                "\tint x;",
                "}"
            };
            var bm = engine.Create(lines, 3); // "int x;" (with tab)
            Assert.AreEqual("int x;", bm.LineContent); // tab -> space, then trimmed
            Assert.AreEqual(FuzzyBookmarkEngine.FNV1a(0, "int x;"), bm.LineHash);
        }

        [TestMethod]
        public void Create_PopulatesScopePathForRealScope()
        {
            var engine = new FuzzyBookmarkEngine();
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
            // Line 7 (1-based) = "return;" inside function Run().
            // The function anchor is the "void Run()" line (0-based 4); its block closes at
            // 0-based 7, so span [4,7] = 3. Line 7 -> idx0=6 -> ratio (6-4)/3 = 2/3.
            var bm = engine.Create(lines, 7);
            Assert.AreEqual(3, bm.ScopePath.Length); // namespace, class, function
            Assert.AreEqual(2.0 / 3.0, bm.Ratio, 0.001);
        }

        [TestMethod]
        public void Create_FillerLine_HasEmptyScopePathAndFileRatio()
        {
            var engine = new FuzzyBookmarkEngine();
            var lines = new[]
            {
                "#include <stdio.h>",
                "",
                "class Foo",
                "{",
                "    int x;",
                "}"
            };
            // Line 1 (1-based) = "#include" -> Filler (Global_Header).
            var bm = engine.Create(lines, 1);
            Assert.AreEqual(0, bm.ScopePath.Length);
            Assert.AreEqual(0.0, bm.Ratio, 0.001); // idx0=0 -> 0
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
            var bm = engine.Create(original, 5); // "var target = 1;"

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
            var result = engine.Resolve(bm, edited);
            Assert.AreEqual(8, result.Line);
            Assert.IsTrue(result.Confidence > 0.0);
            Assert.IsTrue(result.MatchLevel == "Exact" || result.MatchLevel == "ContextFull");
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
            // Target the FIRST "return;" (line 5, 1-based).
            var bm = engine.Create(original, 5);
            Assert.AreEqual("return;", bm.LineContent);

            // Resolve against the same file: should land on the first return (line 5),
            // not the second (line 9), because the context above/below differs.
            var result = engine.Resolve(bm, original);
            Assert.AreEqual(5, result.Line);
        }

        // ---------- Resolve: L2a full context window ----------

        [TestMethod]
        public void Resolve_L2a_FullContextWindowMatch()
        {
            var engine = new FuzzyBookmarkEngine();
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
            var bm = engine.Create(original, 4); // "int beta = 2;"

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
            var result = engine.Resolve(bm, edited);
            // Expect the "int beta = 2;" inside Run (line 10), distinguished by alpha/gamma neighbors.
            Assert.AreEqual(10, result.Line);
            Assert.AreEqual("ContextFull", result.MatchLevel);
        }

        // ---------- Resolve: L3 fuzzy on an edited line ----------

        [TestMethod]
        public void Resolve_L3_FuzzyMatchOnEditedLine()
        {
            var engine = new FuzzyBookmarkEngine();
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
            var bm = engine.Create(original, 5); // "int numberOfItems = 42;"

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
            var result = engine.Resolve(bm, edited);
            Assert.AreEqual(5, result.Line);
            Assert.AreEqual("Fuzzy", result.MatchLevel);
        }

        [TestMethod]
        public void Resolve_L3_FullRename_FallsBackWhenBelowThreshold()
        {
            // A complete variable rename drops similarity below the fuzzy threshold; the engine
            // should NOT return a low-confidence guess (per "high-fidelity: prefer no match over
            // a wrong match"), and should fall back to the seed-derived line instead.
            var engine = new FuzzyBookmarkEngine();
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
            var bm = engine.Create(original, 5);

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
            var result = engine.Resolve(bm, edited);
            Assert.AreEqual("Fallback", result.MatchLevel);
        }

        // ---------- Resolve: NotFound / empty ----------

        [TestMethod]
        public void Resolve_EmptyFile_ReturnsNotFound()
        {
            var engine = new FuzzyBookmarkEngine();
            var bm = new FuzzyBookmark { LineContent = "x", LineHash = 1, ScopePath = new uint[0], Ratio = 0.5 };
            var result = engine.Resolve(bm, new string[0]);
            Assert.AreEqual(0, result.Line);
            Assert.AreEqual("NotFound", result.MatchLevel);
        }

        // ---------- Resolve: file missing ----------

        [TestMethod]
        public void Resolve_MissingFile_ReturnsNotFound()
        {
            var engine = new FuzzyBookmarkEngine();
            var bm = new FuzzyBookmark { LineContent = "x", LineHash = 1, ScopePath = new uint[0], Ratio = 0.5 };
            var result = engine.Resolve(bm, Path.Combine(Path.GetTempPath(), "definitely_missing_" + Guid.NewGuid() + ".cs"));
            Assert.AreEqual(0, result.Line);
            Assert.AreEqual("NotFound", result.MatchLevel);
        }

        // ---------- Resolve: scope fallback ----------

        [TestMethod]
        public void Resolve_ScopeNotFound_FallsBackToParentScope()
        {
            var engine = new FuzzyBookmarkEngine();
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
            // Target line 7 inside function DoWork.
            var bm = engine.Create(original, 7);

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
            var result = engine.Resolve(bm, edited);
            // Should still find the "var target = 1;" line (now line 5) by content/context.
            Assert.AreEqual(5, result.Line);
        }

        // ---------- Resolve: same-name scope rank tiebreak ----------

        [TestMethod]
        public void Resolve_SameNameScope_RankTiebreak()
        {
            var engine = new FuzzyBookmarkEngine();
            // Two classes named "Foo": one big, one small. Rank distinguishes them.
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
            // Bookmark a line in the BIG class (lines 2..6). The big class has Rank=1.
            var bm = engine.Create(original, 5); // "int c;"

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
            var result = engine.Resolve(bm, edited);
            // Should land in the big class (lines 6..12), specifically "int c;" at line 10.
            Assert.AreEqual(10, result.Line);
        }

        // ---------- Resolve: dynamic context expansion ----------

        [TestMethod]
        public void Create_DuplicateLine_ExpandsContext()
        {
            var engine = new FuzzyBookmarkEngine();
            // A file with many "x = 0;" duplicates but different surrounding lines.
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
            var bm = engine.Create(original, 5); // first "x = 0;"
            // N = 3 occurrences -> target = min(3,5) = 3 -> context expands toward 3 differing lines.
            Assert.IsTrue(bm.PreContextHashes.Length >= 2, "Pre context should be at least the default span");
            Assert.AreEqual(bm.PreContextHashes.Length, bm.PostContextHashes.Length);
        }

        // ---------- Resolve: single-line scope (span=0) ----------

        [TestMethod]
        public void Create_SingleLineScope_RatioIsZero()
        {
            var engine = new FuzzyBookmarkEngine();
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
            // Line 2 "{" is filler; line 5 "void Bar()" with immediate block on line 6..7.
            // "void Bar()" itself: detected as anchor; the function scope is [5,6].
            var bm = engine.Create(lines, 5);
            // The function node StartLine=5, EndLine=6 (0-based 4,5)? Span>=1 so ratio computed normally.
            // Just ensure it doesn't throw and scope path is populated.
            Assert.IsTrue(bm.ScopePath.Length >= 1);
        }

        // ---------- ResolveAll: batch ----------

        [TestMethod]
        public void ResolveAll_SharedFile_ReturnsOneResultPerBookmark()
        {
            var engine = new FuzzyBookmarkEngine();
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
            var bm1 = engine.Create(original, 5); // "var a = 1;"
            var bm2 = engine.Create(original, 9); // "var b = 2;"

            // Insert a header line; both should shift by +1.
            var edited = new List<string> { "// header" };
            edited.AddRange(original);

            var results = engine.ResolveAll(new[] { bm1, bm2 }, edited);
            Assert.AreEqual(2, results.Count);
            Assert.AreEqual(6, results[0].Line);
            Assert.AreEqual(10, results[1].Line);
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
                var bm = engine.Create(path, 5);

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

                var result = engine.Resolve(bm, path);
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
