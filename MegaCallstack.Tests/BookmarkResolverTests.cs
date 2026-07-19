using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Models;
using MegaCallstack.Services;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class BookmarkResolverTests
    {
        private string _tempDirectory;

        [TestInitialize]
        public void Initialize()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "MegaCallstack_BookmarkResolverTests_" + Guid.NewGuid().ToString("N"));
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
        public async Task CreateBookmarksForCallstack_GroupsByFileAndAssignsBookmarks()
        {
            var resolver = new BookmarkResolver(new FuzzyBookmarkEngine());
            string fileA = Path.Combine(_tempDirectory, "a.cs");
            string fileB = Path.Combine(_tempDirectory, "b.cs");
            File.WriteAllLines(fileA, new[] { "class A { void M() { int x = 1; } }" });
            File.WriteAllLines(fileB, new[] { "class B { void N() { int y = 2; } }" });

            var callstack = new CallstackData(new List<CallstackFrame>
            {
                new CallstackFrame("M", fileA, 1),
                new CallstackFrame("N", fileB, 1)
            });

            await resolver.CreateBookmarksForCallstackAsync(callstack);

            Assert.IsNotNull(callstack.Frames[0].Bookmark);
            Assert.IsNotNull(callstack.Frames[1].Bookmark);
        }

        [TestMethod]
        public async Task CreateBookmarksForCallstack_InvalidLineReturnsNullBookmark()
        {
            var resolver = new BookmarkResolver(new FuzzyBookmarkEngine());
            string fileA = Path.Combine(_tempDirectory, "a.cs");
            File.WriteAllLines(fileA, new[] { "class A { void M() { int x = 1; } }" });

            var callstack = new CallstackData(new List<CallstackFrame>
            {
                new CallstackFrame("M", fileA, 100)
            });

            await resolver.CreateBookmarksForCallstackAsync(callstack);

            Assert.IsNull(callstack.Frames[0].Bookmark);
        }

        [TestMethod]
        public async Task ResolveSession_GroupsByFileAndUpdatesLineNumbers()
        {
            var engine = new FuzzyBookmarkEngine();
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
            var frame = new CallstackFrame("Main", filePath, 5) { Bookmark = bookmarks[0] };

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

            var session = new CallstackSession("Test");
            session.Callstacks.Add(new CallstackData(new List<CallstackFrame> { frame }));

            var resolver = new BookmarkResolver(engine);
            await resolver.ResolveSessionAsync(session);

            Assert.AreEqual(6, frame.LineNumber);
            Assert.IsTrue(session.ResolvedFileWriteTimes.ContainsKey(filePath));
        }

        [TestMethod]
        public async Task ResolveFiles_OnlyResolvesGivenFiles()
        {
            var engine = new FuzzyBookmarkEngine();
            string fileA = Path.Combine(_tempDirectory, "a.cs");
            string fileB = Path.Combine(_tempDirectory, "b.cs");
            File.WriteAllLines(fileA, new[] { "class A { void M() { int x = 1; } }" });
            File.WriteAllLines(fileB, new[] { "class B { void N() { int y = 2; } }" });

            var bookmarksA = engine.CreateAll(new[] { 1 }, fileA);
            var bookmarksB = engine.CreateAll(new[] { 1 }, fileB);

            var frameA = new CallstackFrame("M", fileA, 1) { Bookmark = bookmarksA[0] };
            var frameB = new CallstackFrame("N", fileB, 1) { Bookmark = bookmarksB[0] };

            var session = new CallstackSession("Test");
            session.Callstacks.Add(new CallstackData(new List<CallstackFrame> { frameA, frameB }));

            var resolver = new BookmarkResolver(engine);
            await resolver.ResolveFilesAsync(new[] { fileB }, session);

            Assert.IsFalse(session.ResolvedFileWriteTimes.ContainsKey(fileA));
            Assert.IsTrue(session.ResolvedFileWriteTimes.ContainsKey(fileB));
        }
    }
}
