using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Models;
using MegaCallstack.Services;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class CallstackCaptureServiceTests
    {
        private string _tempDirectory;

        [TestInitialize]
        public void Setup()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "MegaCallstackCaptureTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try { Directory.Delete(_tempDirectory, true); } catch { }
            }
        }

        [TestMethod]
        public void TrimToUserCode_NoRoots_KeepsAllFrames()
        {
            var service = new CallstackCaptureService(null);
            var frames = CreateFrames(
                ("ExternalA", @"C:\External\a.cpp", 1),
                ("ExternalB", @"C:\External\b.cpp", 2),
                ("UserCode", @"C:\Project\main.cpp", 10));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("ExternalA", result[0].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_TrimsTrailingExternalFrames()
        {
            var root = CreateFile(@"Project\main.cpp");
            var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) });
            var frames = CreateFrames(
                ("UserCodeA", root, 10),
                ("ExternalA", @"C:\External\a.cpp", 1),
                ("ExternalB", @"C:\External\b.cpp", 2));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("UserCodeA", result[0].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_EmptyFileName_IsTreatedAsExternal()
        {
            var root = CreateFile(@"Project\main.cpp");
            var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) });
            var frames = CreateFrames(
                ("UserCodeA", root, 10),
                ("ExternalA", "", 0),
                ("ExternalB", "", 0));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("UserCodeA", result[0].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_EmptyFileNameInMiddle_AfterUserCode_IsPreserved()
        {
            var root = CreateFile(@"Project\main.cpp");
            var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) });
            var frames = CreateFrames(
                ("UserCodeA", root, 10),
                ("MysteryFrame", "", 0),
                ("UserCodeB", root, 20));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("UserCodeA", result[0].FunctionName);
            Assert.AreEqual("MysteryFrame", result[1].FunctionName);
            Assert.AreEqual("UserCodeB", result[2].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_ExternalBelowUserCode_Trimmed()
        {
            var root = CreateFile(@"Project\main.cpp");
            var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) });
            var frames = CreateFrames(
                ("UserCodeA", root, 10),
                ("ExternalB", @"C:\External\b.cpp", 2),
                ("MysteryFrame", "", 0));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("UserCodeA", result[0].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_EmptyFileNameInMiddle_AfterExternal_Trimmed()
        {
            var root = CreateFile(@"Project\main.cpp");
            var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) });
            var frames = CreateFrames(
                ("UserCodeA", root, 10),
                ("ExternalB", @"C:\External\b.cpp", 2),
                ("MysteryFrame", "", 0));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("UserCodeA", result[0].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_EmptyFileNameTrailing_IsTrimmed()
        {
            var root = CreateFile(@"Project\main.cpp");
            var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) });
            var frames = CreateFrames(
                ("UserCodeA", root, 10),
                ("ExternalA", "", 0),
                ("ExternalB", "", 0));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("UserCodeA", result[0].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_EmptyFileNameInMiddle_AllExternal_IsPreserved()
        {
            var service = new CallstackCaptureService(null, new[] { _tempDirectory });
            var frames = CreateFrames(
                ("ExternalA", "", 0),
                ("ExternalB", @"C:\External\b.cpp", 2));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("ExternalA", result[0].FunctionName);
            Assert.AreEqual("ExternalB", result[1].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_MissingFileInExternalRoot_KeepsFrameIfNotTrailing()
        {
            var root = CreateFile(@"Project\main.cpp");
            var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) });
            var frames = CreateFrames(
                ("UserCodeA", root, 10),
                ("ExternalMissing", @"C:\External\missing.cpp", 99),
                ("ExternalKnown", @"C:\External\known.cpp", 1));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("UserCodeA", result[0].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_AllExternal_KeepsAllFrames()
        {
            var service = new CallstackCaptureService(null, new[] { _tempDirectory });
            var frames = CreateFrames(
                ("ExternalA", @"C:\External\a.cpp", 1),
                ("ExternalB", @"C:\External\b.cpp", 2));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void TrimToUserCode_RootIsCaseInsensitive()
        {
            var root = CreateFile(@"Project\main.cpp");
            var upperRoot = Path.GetDirectoryName(root).ToUpperInvariant();
            var service = new CallstackCaptureService(null, new[] { upperRoot });
            var frames = CreateFrames(
                ("UserCodeA", root, 10),
                ("ExternalA", @"C:\External\a.cpp", 1));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("UserCodeA", result[0].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_SkipRootFunctionFound_ChildBecomesRoot()
        {
            var root = CreateFile(@"Project\main.cpp");
            var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) }, new[] { "ThreadMainFunc" });
            var frames = CreateFrames(
                ("RenderThread_Main", root, 10),
                ("ThreadMainFunc", @"C:\External\thread.cpp", 1),
                ("ExternalA", @"C:\External\a.cpp", 2));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("RenderThread_Main", result[0].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_SkipRootFunctionNotFound_FallsBackToUserCodeRoots()
        {
            var root = CreateFile(@"Project\main.cpp");
            var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) }, new[] { "SomeOtherFunc" });
            var frames = CreateFrames(
                ("UserCodeA", root, 10),
                ("ExternalA", @"C:\External\a.cpp", 1),
                ("ExternalB", @"C:\External\b.cpp", 2));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("UserCodeA", result[0].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_SkipRootFunctionDeepestWins()
        {
            var root = CreateFile(@"Project\main.cpp");
            var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) }, new[] { "ThreadMainFunc", "RenderThread_Main" });
            var frames = CreateFrames(
                ("Worker", root, 10),
                ("RenderThread_Main", @"C:\External\render.cpp", 1),
                ("ThreadMainFunc", @"C:\External\thread.cpp", 2),
                ("ExternalA", @"C:\External\a.cpp", 3));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Worker", result[0].FunctionName);
            Assert.AreEqual("RenderThread_Main", result[1].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_SkipRootFunctionCaseSensitive_DoesNotMatchWrongCase()
        {
            var root = CreateFile(@"Project\main.cpp");
            var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) }, new[] { "threadmainfunc" });
            var frames = CreateFrames(
                ("UserCodeA", root, 10),
                ("ThreadMainFunc", @"C:\External\thread.cpp", 1),
                ("ExternalA", @"C:\External\a.cpp", 2));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("UserCodeA", result[0].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_SkipRootFunctionEmptyList_FallsBackToUserCodeRoots()
        {
            var root = CreateFile(@"Project\main.cpp");
            var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) }, new string[0]);
            var frames = CreateFrames(
                ("UserCodeA", root, 10),
                ("ExternalA", @"C:\External\a.cpp", 1));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("UserCodeA", result[0].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_SkipRootFunctionAtTop_KeepsAllFramesAbove()
        {
            var root = CreateFile(@"Project\main.cpp");
            var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) }, new[] { "TopFunc" });
            var frames = CreateFrames(
                ("TopFunc", root, 10),
                ("ExternalA", @"C:\External\a.cpp", 1));

            var result = InvokeTrimToUserCode(service, frames);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("TopFunc", result[0].FunctionName);
        }

        [TestMethod]
        public void TrimToUserCode_SkipRootFunction_ReadsFromCurrentSettings()
        {
            var root = CreateFile(@"Project\main.cpp");
            var originalSettings = SettingsService.CurrentSettings;
            try
            {
                SettingsService.CurrentSettings = new MegaCallstackSettings
                {
                    SkipRootFunctions = new List<string> { "ThreadMainFunc" }
                };

                var service = new CallstackCaptureService(null, new[] { Path.GetDirectoryName(root) }, new string[0]);
                var frames = CreateFrames(
                    ("RenderThread_Main", root, 10),
                    ("ThreadMainFunc", @"C:\External\thread.cpp", 1),
                    ("ExternalA", @"C:\External\a.cpp", 2));

                var result = InvokeTrimToUserCode(service, frames);

                Assert.AreEqual(1, result.Count);
                Assert.AreEqual("RenderThread_Main", result[0].FunctionName);
            }
            finally
            {
                SettingsService.CurrentSettings = originalSettings;
            }
        }

        [TestMethod]
        public void TransformToPersistentCallstack_EmptyFrames_ReturnsNull()
        {
            Assert.IsNull(CallstackCaptureService.TransformToPersistentCallstack(null));
            Assert.IsNull(CallstackCaptureService.TransformToPersistentCallstack(new List<CallstackFrame>()));
        }

        [TestMethod]
        public void TransformToPersistentCallstack_RootHasNoLocation()
        {
            var frames = new List<CallstackFrame>
            {
                new CallstackFrame("main", "main.cs", 10)
                {
                    HashCode = 1,
                    Bookmark = CreateBookmark("main")
                }
            };

            var result = CallstackCaptureService.TransformToPersistentCallstack(frames);

            Assert.AreEqual(2, result.Frames.Count);
            var root = result.Frames[0];
            Assert.AreEqual("main", root.FunctionName);
            Assert.IsNull(root.FileName);
            Assert.AreEqual(0, root.LineNumber);
            Assert.IsNull(root.Bookmark);
            Assert.AreEqual(1, root.HashCode);
        }

        [TestMethod]
        public void TransformToPersistentCallstack_NonRootUsesParentLocation()
        {
            var parentBookmark = CreateBookmark("main");
            var childBookmark = CreateBookmark("Run");
            var frames = new List<CallstackFrame>
            {
                new CallstackFrame("main", "main.cs", 10)
                {
                    HashCode = 1,
                    Bookmark = parentBookmark
                },
                new CallstackFrame("Run", "main.cs", 20)
                {
                    HashCode = 2,
                    Bookmark = childBookmark
                }
            };

            var result = CallstackCaptureService.TransformToPersistentCallstack(frames);

            Assert.AreEqual(3, result.Frames.Count);
            var run = result.Frames[1];
            Assert.AreEqual("Run", run.FunctionName);
            Assert.AreEqual("main.cs", run.FileName);
            Assert.AreEqual(10, run.LineNumber);
            Assert.AreEqual(parentBookmark, run.Bookmark);
            Assert.AreEqual(2, run.HashCode);
        }

        [TestMethod]
        public void TransformToPersistentCallstack_LeafUsesDeepestFrameLocation()
        {
            var childBookmark = CreateBookmark("Run");
            var frames = new List<CallstackFrame>
            {
                new CallstackFrame("main", "main.cs", 10) { HashCode = 1 },
                new CallstackFrame("Run", "main.cs", 20)
                {
                    HashCode = 2,
                    Bookmark = childBookmark
                }
            };

            var result = CallstackCaptureService.TransformToPersistentCallstack(frames);

            Assert.AreEqual(3, result.Frames.Count);
            var leaf = result.Frames[2];
            Assert.IsNull(leaf.FunctionName);
            Assert.AreEqual("main.cs", leaf.FileName);
            Assert.AreEqual(20, leaf.LineNumber);
            Assert.AreEqual(childBookmark, leaf.Bookmark);
            Assert.AreEqual(2, leaf.HashCode);
            Assert.AreEqual(2, result.LeafHashCode);
        }

        private static FuzzyBookmarkOpaque CreateBookmark(string content)
        {
            return new FuzzyBookmark
            {
                LineContent = content,
                LineHash = FuzzyBookmarkEngine.FNV1a(0, content),
                ScopePath = new uint[0],
                Ratio = 0.5
            }.ToOpaque();
        }

        private string CreateFile(string relativePath)
        {
            var fullPath = Path.Combine(_tempDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, "// placeholder");
            return fullPath;
        }

        private static List<CallstackFrame> CreateFrames(params (string Function, string File, int Line)[] frames)
        {
            return frames.Select(f => new CallstackFrame(f.Function, f.File, f.Line)).ToList();
        }

        private static List<CallstackFrame> InvokeTrimToUserCode(CallstackCaptureService service, List<CallstackFrame> frames)
        {
            var method = typeof(CallstackCaptureService).GetMethod("TrimToUserCode", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            return (List<CallstackFrame>)method.Invoke(service, new object[] { frames });
        }
    }
}
