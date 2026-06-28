using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Models;
using MegaCallstack.Services;
using Newtonsoft.Json;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class CallstackSessionTests
    {
        private string _tempDirectory;

        [TestInitialize]
        public void Setup()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "MegaCallstackTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
        public void AddOrUpdateCallstack_NewCallstack_AddsToSession()
        {
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("A", "B", "C");

            var manager = CreateManager();
            manager.AddOrUpdateCallstack(session, callstack);

            Assert.AreEqual(1, session.Callstacks.Count);
            Assert.AreEqual(callstack.LeafHashCode, session.Callstacks[0].LeafHashCode);
        }

        [TestMethod]
        public void AddOrUpdateCallstack_SameLeafHash_ReplacesExisting()
        {
            var session = new CallstackSession("Test");
            var callstack1 = CreateTestCallstack("A", "B", "C");
            var callstack2 = CreateTestCallstack("A", "B", "C");

            var manager = CreateManager();
            manager.AddOrUpdateCallstack(session, callstack1);
            manager.AddOrUpdateCallstack(session, callstack2);

            Assert.AreEqual(1, session.Callstacks.Count);
            Assert.AreEqual(callstack2.CapturedTime, session.Callstacks[0].CapturedTime);
        }

        [TestMethod]
        public void AddOrUpdateCallstack_DifferentLeafHash_AddsNew()
        {
            var session = new CallstackSession("Test");
            var callstack1 = CreateTestCallstack("A", "B", "C");
            var callstack2 = CreateTestCallstack("A", "B", "D");

            var manager = CreateManager();
            manager.AddOrUpdateCallstack(session, callstack1);
            manager.AddOrUpdateCallstack(session, callstack2);

            Assert.AreEqual(2, session.Callstacks.Count);
        }

        [TestMethod]
        public void CreateSession_GeneratesUniqueId()
        {
            var manager = CreateManager();
            var session1 = manager.CreateSession("Session1");
            var session2 = manager.CreateSession("Session2");

            Assert.AreNotEqual(session1.Id, session2.Id);
            Assert.AreEqual(2, manager.SessionData.Sessions.Count);
        }

        [TestMethod]
        public void CreateSession_GeneratesFolderName()
        {
            var manager = CreateManager();
            var session = manager.CreateSession("Test");

            Assert.IsFalse(string.IsNullOrEmpty(session.FolderName));
            Assert.IsTrue(session.FolderName.Length > 0);
        }

        [TestMethod]
        public void SetActiveSession_SetsCorrectly()
        {
            var manager = CreateManager();
            var session1 = manager.CreateSession("Session1");
            var session2 = manager.CreateSession("Session2");

            manager.SetActiveSession(session2.Id);
            var active = manager.GetActiveSession();

            Assert.AreEqual(session2.Id, active.Id);
            Assert.AreEqual("Session2", active.Name);
        }

        [TestMethod]
        public void GetActiveSession_ReturnsNullWhenNoneSet()
        {
            var manager = CreateManager();

            var active = manager.GetActiveSession();
            Assert.IsNull(active);
        }

        [TestMethod]
        public async Task LoadDataAsync_OnlyLoadsMetadata()
        {
            var manager = CreateManager();
            InjectDataDirectory(manager);

            var session = manager.CreateSession("TestSession");
            await manager.SaveSessionMetadataAsync(session);
            await manager.SaveCallstacksAsync(session);
            await manager.SaveStateAsync(session);

            var manager2 = CreateManager();
            InjectDataDirectory(manager2);
            await manager2.LoadDataAsync();

            Assert.AreEqual(1, manager2.SessionData.Sessions.Count);
            var loaded = manager2.SessionData.Sessions[0];
            Assert.IsFalse(loaded.IsLoaded);
            Assert.AreEqual(0, loaded.Callstacks.Count);
            Assert.AreEqual(0, loaded.NodeColors.Count);
            Assert.AreEqual(0, loaded.CollapsedNodes.Count);
            Assert.AreEqual(0, loaded.HiddenAncestorNodes.Count);
            Assert.AreEqual(0, loaded.NodeNotes.Count);
        }

        [TestMethod]
        public async Task LoadSessionDetailsAsync_LoadsCallstacksAndState()
        {
            var manager = CreateManager();
            InjectDataDirectory(manager);

            var session = manager.CreateSession("TestSession");
            var callstack = CreateTestCallstack("main", "Run", "DoWork");
            manager.AddOrUpdateCallstack(session, callstack);
            session.NodeColors[100] = "#FF0000";
            session.CollapsedNodes[200] = true;

            await manager.SaveSessionMetadataAsync(session);
            await manager.SaveCallstacksAsync(session);
            await manager.SaveStateAsync(session);

            var manager2 = CreateManager();
            InjectDataDirectory(manager2);
            await manager2.LoadDataAsync();

            var loaded = manager2.SessionData.Sessions[0];
            Assert.IsFalse(loaded.IsLoaded);

            await manager2.LoadSessionDetailsAsync(loaded);

            Assert.IsTrue(loaded.IsLoaded);
            Assert.AreEqual(1, loaded.Callstacks.Count);
            Assert.AreEqual(3, loaded.Callstacks[0].Frames.Count);
            Assert.AreEqual("#FF0000", loaded.NodeColors[100]);
            Assert.IsTrue(loaded.CollapsedNodes[200]);
            Assert.AreEqual(0, loaded.NodeNotes.Count);
        }

        [TestMethod]
        public async Task LoadSessionDetailsAsync_LoadsHiddenAncestorNodes()
        {
            var manager = CreateManager();
            InjectDataDirectory(manager);

            var session = manager.CreateSession("TestSession");
            var callstack = CreateTestCallstack("main", "Run", "DoWork");
            manager.AddOrUpdateCallstack(session, callstack);
            session.HiddenAncestorNodes[100] = true;
            session.HiddenAncestorNodes[200] = true;

            await manager.SaveSessionMetadataAsync(session);
            await manager.SaveCallstacksAsync(session);
            await manager.SaveStateAsync(session);

            var manager2 = CreateManager();
            InjectDataDirectory(manager2);
            await manager2.LoadDataAsync();

            var loaded = manager2.SessionData.Sessions[0];
            await manager2.LoadSessionDetailsAsync(loaded);

            Assert.IsTrue(loaded.HiddenAncestorNodes.ContainsKey(100));
            Assert.IsTrue(loaded.HiddenAncestorNodes.ContainsKey(200));
        }

        [TestMethod]
        public async Task SaveSessionMetadataAsync_WritesOnlySessionFile()
        {
            var manager = CreateManager();
            InjectDataDirectory(manager);

            var session = manager.CreateSession("TestSession");
            await manager.SaveSessionMetadataAsync(session);

            var folder = Path.Combine(_tempDirectory, session.FolderName);
            Assert.IsTrue(Directory.Exists(folder));
            Assert.IsTrue(File.Exists(Path.Combine(folder, Constants.SessionFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.CallstacksFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.StateFileName)));
        }

        [TestMethod]
        public async Task SaveNotesAsync_WritesOnlyNotesFile()
        {
            var manager = CreateManager();
            InjectDataDirectory(manager);

            var session = manager.CreateSession("TestSession");
            session.NodeNotes[100] = new System.Collections.Generic.List<NodeNote>
            {
                new NodeNote { Emoji = "📝", Text = "Test note" }
            };

            await manager.SaveNotesAsync(session);

            var folder = Path.Combine(_tempDirectory, session.FolderName);
            Assert.IsTrue(File.Exists(Path.Combine(folder, Constants.NotesFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.SessionFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.CallstacksFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.StateFileName)));
        }

        [TestMethod]
        public async Task SaveCallstacksAsync_WritesOnlyCallstacksFile()
        {
            var manager = CreateManager();
            InjectDataDirectory(manager);

            var session = manager.CreateSession("TestSession");
            var callstack = CreateTestCallstack("main", "Run");
            manager.AddOrUpdateCallstack(session, callstack);

            await manager.SaveCallstacksAsync(session);

            var folder = Path.Combine(_tempDirectory, session.FolderName);
            Assert.IsTrue(File.Exists(Path.Combine(folder, Constants.CallstacksFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.SessionFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.StateFileName)));
        }

        [TestMethod]
        public async Task SaveStateAsync_WritesHiddenAncestorNodes()
        {
            var manager = CreateManager();
            InjectDataDirectory(manager);

            var session = manager.CreateSession("TestSession");
            session.HiddenAncestorNodes[100] = true;

            await manager.SaveStateAsync(session);

            var folder = Path.Combine(_tempDirectory, session.FolderName);
            var json = File.ReadAllText(Path.Combine(folder, Constants.StateFileName));
            var state = JsonConvert.DeserializeObject<SessionState>(json);

            Assert.IsTrue(state.HiddenAncestorNodes.ContainsKey(100));
        }

        [TestMethod]
        public async Task SaveStateAsync_WritesOnlyStateFile()
        {
            var manager = CreateManager();
            InjectDataDirectory(manager);

            var session = manager.CreateSession("TestSession");
            session.NodeColors[100] = "#FF0000";
            session.CollapsedNodes[200] = true;

            await manager.SaveStateAsync(session);

            var folder = Path.Combine(_tempDirectory, session.FolderName);
            Assert.IsTrue(File.Exists(Path.Combine(folder, Constants.StateFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.SessionFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.CallstacksFileName)));
        }

        [TestMethod]
        public async Task DeleteSession_RemovesSessionAndFolder()
        {
            var manager = CreateManager();
            InjectDataDirectory(manager);

            var session = manager.CreateSession("TestSession");
            await manager.SaveSessionMetadataAsync(session);

            var folder = Path.Combine(_tempDirectory, session.FolderName);
            Assert.IsTrue(Directory.Exists(folder));

            manager.DeleteSession(session);

            Assert.AreEqual(0, manager.SessionData.Sessions.Count);
            Assert.IsFalse(Directory.Exists(folder));
        }

        [TestMethod]
        public void BuildTreeNodes_CollapseSemantics_DefaultExpanded()
        {
            var manager = CreateManager();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main", "Run", "DoWork");
            manager.AddOrUpdateCallstack(session, callstack);

            var nodes = manager.BuildTreeNodes(session);

            Assert.IsTrue(nodes[0].IsExpanded);
            Assert.IsTrue(nodes[0].Children[0].IsExpanded);
        }

        [TestMethod]
        public void BuildTreeNodes_CollapseSemantics_CollapsedWhenStored()
        {
            var manager = CreateManager();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main", "Run", "DoWork");
            manager.AddOrUpdateCallstack(session, callstack);

            var frame = callstack.Frames[0];
            session.CollapsedNodes[frame.HashCode] = true;

            var nodes = manager.BuildTreeNodes(session);

            Assert.IsFalse(nodes[0].IsExpanded);
        }

        [TestMethod]
        public void BuildTreeNodes_CollapseSemantics_ExpandedWhenNotInCollapsedNodes()
        {
            var manager = CreateManager();
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main", "Run", "DoWork");
            manager.AddOrUpdateCallstack(session, callstack);

            var nodes = manager.BuildTreeNodes(session);

            Assert.IsTrue(nodes[0].IsExpanded);
            Assert.IsTrue(nodes[0].Children[0].IsExpanded);
            Assert.IsTrue(nodes[0].Children[0].Children[0].IsExpanded);
        }

        [TestMethod]
        public void SaveExpansionState_InvertedLogic()
        {
            var manager = CreateManager();
            var session = new CallstackSession("Test");

            manager.SaveExpansionState(session, 100, false);
            Assert.IsTrue(session.CollapsedNodes[100]);

            manager.SaveExpansionState(session, 200, true);
            Assert.IsFalse(session.CollapsedNodes[200]);
        }

        [TestMethod]
        public async Task LoadDataAsync_SetsIsLoadedFalse()
        {
            var manager = CreateManager();
            InjectDataDirectory(manager);

            var session = manager.CreateSession("TestSession");
            await manager.SaveSessionMetadataAsync(session);

            var manager2 = CreateManager();
            InjectDataDirectory(manager2);
            await manager2.LoadDataAsync();

            var loaded = manager2.SessionData.Sessions[0];
            Assert.IsFalse(loaded.IsLoaded);
            Assert.IsNotNull(loaded.Callstacks);
            Assert.IsNotNull(loaded.NodeColors);
            Assert.IsNotNull(loaded.CollapsedNodes);
            Assert.IsNotNull(loaded.HiddenAncestorNodes);
            Assert.IsNotNull(loaded.NodeNotes);
            Assert.AreEqual(0, loaded.Callstacks.Count);
        }

        [TestMethod]
        public async Task LoadSessionDetailsAsync_DoesNotReloadIfAlreadyLoaded()
        {
            var manager = CreateManager();
            InjectDataDirectory(manager);

            var session = manager.CreateSession("TestSession");
            var callstack = CreateTestCallstack("main", "Run");
            manager.AddOrUpdateCallstack(session, callstack);
            await manager.SaveSessionMetadataAsync(session);
            await manager.SaveCallstacksAsync(session);

            session.IsLoaded = true;
            session.Callstacks.Clear();

            await manager.LoadSessionDetailsAsync(session);

            Assert.AreEqual(0, session.Callstacks.Count);
        }

        [TestMethod]
        public async Task ResolveFrameLineNumberAsync_WithBookmark_UpdatesLineNumber()
        {
            var path = Path.Combine(_tempDirectory, "source.cs");
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
            File.WriteAllLines(path, original);

            var engine = new FuzzyBookmarkEngine();
            var bookmark = engine.Create(path, 5);

            var edited = new[]
            {
                "// added header",
                "class Program",
                "{",
                "    static void Main()",
                "    {",
                "        Console.WriteLine(\"hi\");",
                "    }",
                "}"
            };
            File.WriteAllLines(path, edited);

            var frame = new CallstackFrame("Main", path, 5)
            {
                Bookmark = bookmark
            };

            var manager = CreateManager();
            int line = await manager.ResolveFrameLineNumberAsync(frame);

            Assert.AreEqual(6, line);
            Assert.AreEqual(6, frame.LineNumber);
        }

        [TestMethod]
        public async Task ResolveFrameLineNumberAsync_WithoutBookmark_ReturnsStoredLineNumber()
        {
            var frame = new CallstackFrame("Main", "source.cs", 42);

            var manager = CreateManager();
            int line = await manager.ResolveFrameLineNumberAsync(frame);

            Assert.AreEqual(42, line);
        }

        [TestMethod]
        public async Task ResolveFrameLineNumberAsync_MissingFile_ReturnsStoredLineNumber()
        {
            var engine = new FuzzyBookmarkEngine();
            var bookmark = new FuzzyBookmark
            {
                LineContent = "x",
                LineHash = 1,
                ScopePath = new uint[0],
                Ratio = 0.5
            };

            var frame = new CallstackFrame("Main", "missing_file_that_does_not_exist.cs", 42)
            {
                Bookmark = bookmark
            };

            var manager = CreateManager();
            int line = await manager.ResolveFrameLineNumberAsync(frame);

            Assert.AreEqual(42, line);
        }

        private CallstackManager CreateManager()
        {
            return new CallstackManager(null);
        }

        private void InjectDataDirectory(CallstackManager manager)
        {
            var field = typeof(CallstackManager).GetField("_dataDirectory",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(manager, _tempDirectory);
        }

        private CallstackData CreateTestCallstack(params string[] functionNames)
        {
            var frames = new List<CallstackFrame>();
            int hash = 0;
            for (int i = 0; i < functionNames.Length; i++)
            {
                hash = CallstackFrame.ComputeFNV1aHash(hash, functionNames[i]);
                frames.Add(new CallstackFrame(functionNames[i], "test.cs", (i + 1) * 10)
                {
                    HashCode = hash
                });
            }
            return new CallstackData(frames);
        }
    }
}
