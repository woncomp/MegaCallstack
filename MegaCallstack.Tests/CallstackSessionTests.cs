using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Models;
using MegaCallstack.Services;
using MegaCallstack.ViewModels;
using Newtonsoft.Json;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class CallstackSessionTests
    {
        private string _tempDirectory;
        private SolutionInfo _solutionInfo;
        private ISessionRepository _repository;
        private SolutionSessionData _sessionData;
        private ICallstackTreeBuilder _treeBuilder;
        private ICallstackCaptureService _captureService;

        [TestInitialize]
        public void Setup()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "MegaCallstackTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tempDirectory);
            _solutionInfo = new SolutionInfo(Path.Combine(_tempDirectory, "Test.sln"));
            _repository = new SessionRepository(_solutionInfo);
            _sessionData = new SolutionSessionData();
            _treeBuilder = new CallstackTreeBuilder();
            _captureService = new CallstackCaptureService(null);
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

            AddOrUpdateCallstack(session, callstack);

            Assert.AreEqual(1, session.Callstacks.Count);
            Assert.AreEqual(callstack.LeafHashCode, session.Callstacks[0].LeafHashCode);
        }

        [TestMethod]
        public void AddOrUpdateCallstack_SameLeafHash_ReplacesExisting()
        {
            var session = new CallstackSession("Test");
            var callstack1 = CreateTestCallstack("A", "B", "C");
            var callstack2 = CreateTestCallstack("A", "B", "C");

            AddOrUpdateCallstack(session, callstack1);
            System.Threading.Thread.Sleep(10);
            AddOrUpdateCallstack(session, callstack2);

            Assert.AreEqual(1, session.Callstacks.Count);
            Assert.AreEqual(callstack2.CapturedTime, session.Callstacks[0].CapturedTime);
        }

        [TestMethod]
        public void AddOrUpdateCallstack_DifferentLeafHash_AddsNew()
        {
            var session = new CallstackSession("Test");
            var callstack1 = CreateTestCallstack("A", "B", "C");
            var callstack2 = CreateTestCallstack("A", "B", "D");

            AddOrUpdateCallstack(session, callstack1);
            AddOrUpdateCallstack(session, callstack2);

            Assert.AreEqual(2, session.Callstacks.Count);
        }

        [TestMethod]
        public void CreateSession_GeneratesUniqueId()
        {
            var session1 = CreateSession("Session1");
            var session2 = CreateSession("Session2");

            Assert.AreNotEqual(session1.Id, session2.Id);
            Assert.AreEqual(2, _sessionData.Sessions.Count);
        }

        [TestMethod]
        public void CreateSession_GeneratesFolderName()
        {
            var session = CreateSession("Test");

            Assert.IsFalse(string.IsNullOrEmpty(session.FolderName));
            Assert.IsTrue(session.FolderName.Length > 0);
        }

        [TestMethod]
        public async Task SetActiveSession_SetsCorrectly()
        {
            var session1 = CreateSession("Session1");
            var session2 = CreateSession("Session2");

            await _repository.SaveSessionMetadataAsync(session1);
            await _repository.SaveSessionMetadataAsync(session2);

            _sessionData.PreviousSessionId = session2.Id;
            await _repository.SavePreviousSessionIdAsync(session2.Id);

            var loaded = await new SessionRepository(_solutionInfo).LoadDataAsync();
            var active = loaded.Sessions.FirstOrDefault(s => s.Id == loaded.PreviousSessionId);

            Assert.IsNotNull(active);
            Assert.AreEqual(session2.Id, active.Id);
            Assert.AreEqual("Session2", active.Name);
        }

        [TestMethod]
        public async Task GetPreviousSession_ReturnsNullWhenNoSessions()
        {
            var loaded = await new SessionRepository(_solutionInfo).LoadDataAsync();

            Assert.IsNull(loaded.PreviousSessionId);
        }

        [TestMethod]
        public async Task LoadPreviousSessionId_FallsBackToLastCreatedSession_WhenFileMissing()
        {
            var session1 = CreateSession("Session1");
            var session2 = CreateSession("Session2");
            session2.CreatedTime = session1.CreatedTime.AddMinutes(1);

            await _repository.SaveSessionMetadataAsync(session1);
            await _repository.SaveSessionMetadataAsync(session2);

            var loaded = await new SessionRepository(_solutionInfo).LoadDataAsync();

            Assert.AreEqual(session2.Id, loaded.PreviousSessionId);
        }

        [TestMethod]
        public async Task LoadPreviousSessionId_FallsBackToLastCreatedSession_WhenFileIsEmpty()
        {
            var session1 = CreateSession("Session1");
            var session2 = CreateSession("Session2");
            session2.CreatedTime = session1.CreatedTime.AddMinutes(1);

            await _repository.SaveSessionMetadataAsync(session1);
            await _repository.SaveSessionMetadataAsync(session2);
            await _repository.SavePreviousSessionIdAsync(null);

            var loaded = await new SessionRepository(_solutionInfo).LoadDataAsync();

            Assert.AreEqual(session2.Id, loaded.PreviousSessionId);
        }

        [TestMethod]
        public async Task LoadDataAsync_OnlyLoadsMetadata()
        {
            var session = CreateSession("TestSession");
            var callstack = CreateTestCallstack("main", "Run");
            AddOrUpdateCallstack(session, callstack);

            await _repository.SaveSessionMetadataAsync(session);
            await _repository.SaveCallstacksAsync(session);
            await _repository.SaveStateAsync(session);

            var loaded = await new SessionRepository(_solutionInfo).LoadDataAsync();

            Assert.AreEqual(1, loaded.Sessions.Count);
            var loadedSession = loaded.Sessions[0];
            Assert.IsFalse(loadedSession.IsLoaded);
            Assert.AreEqual(0, loadedSession.Callstacks.Count);
            Assert.AreEqual(0, loadedSession.NodeColors.Count);
            Assert.AreEqual(0, loadedSession.CollapsedNodes.Count);
            Assert.AreEqual(0, loadedSession.HiddenAncestorNodes.Count);
            Assert.AreEqual(0, loadedSession.NodeNotes.Count);
        }

        [TestMethod]
        public async Task LoadSessionDetailsAsync_LoadsCallstacksAndState()
        {
            var session = CreateSession("TestSession");
            var callstack = CreateTestCallstack("main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack);
            session.NodeColors[100] = "#FF0000";
            session.CollapsedNodes[200] = true;

            await _repository.SaveSessionMetadataAsync(session);
            await _repository.SaveCallstacksAsync(session);
            await _repository.SaveStateAsync(session);

            var loaded = await new SessionRepository(_solutionInfo).LoadDataAsync();
            var loadedSession = loaded.Sessions[0];
            Assert.IsFalse(loadedSession.IsLoaded);

            await new SessionRepository(_solutionInfo).LoadSessionDetailsAsync(loadedSession);

            Assert.IsTrue(loadedSession.IsLoaded);
            Assert.AreEqual(1, loadedSession.Callstacks.Count);
            Assert.AreEqual(3, loadedSession.Callstacks[0].Frames.Count);
            Assert.AreEqual("#FF0000", loadedSession.NodeColors[100]);
            Assert.IsTrue(loadedSession.CollapsedNodes[200]);
            Assert.AreEqual(0, loadedSession.NodeNotes.Count);
        }

        [TestMethod]
        public async Task LoadSessionDetailsAsync_LoadsHiddenAncestorNodes()
        {
            var session = CreateSession("TestSession");
            var callstack = CreateTestCallstack("main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack);
            session.HiddenAncestorNodes[100] = true;
            session.HiddenAncestorNodes[200] = true;

            await _repository.SaveSessionMetadataAsync(session);
            await _repository.SaveCallstacksAsync(session);
            await _repository.SaveStateAsync(session);

            var loaded = await new SessionRepository(_solutionInfo).LoadDataAsync();
            var loadedSession = loaded.Sessions[0];
            await new SessionRepository(_solutionInfo).LoadSessionDetailsAsync(loadedSession);

            Assert.IsTrue(loadedSession.HiddenAncestorNodes.ContainsKey(100));
            Assert.IsTrue(loadedSession.HiddenAncestorNodes.ContainsKey(200));
        }

        [TestMethod]
        public async Task SaveSessionMetadataAsync_WritesOnlySessionFile()
        {
            var session = CreateSession("TestSession");
            await _repository.SaveSessionMetadataAsync(session);

            var folder = _repository.GetSessionFolderPath(session);
            Assert.IsTrue(Directory.Exists(folder));
            Assert.IsTrue(File.Exists(Path.Combine(folder, Constants.SessionFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.CallstacksFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.StateFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.NotesFileName)));
        }

        [TestMethod]
        public async Task SaveNotesAsync_WritesOnlyNotesFile()
        {
            var session = CreateSession("TestSession");
            session.NodeNotes[100] = new List<NodeNote>
            {
                new NodeNote { Emoji = "📝", Text = "Test note" }
            };

            await _repository.SaveNotesAsync(session);

            var folder = _repository.GetSessionFolderPath(session);
            Assert.IsTrue(File.Exists(Path.Combine(folder, Constants.NotesFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.SessionFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.CallstacksFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.StateFileName)));
        }

        [TestMethod]
        public async Task SaveCallstacksAsync_WritesOnlyCallstacksFile()
        {
            var session = CreateSession("TestSession");
            var callstack = CreateTestCallstack("main", "Run");
            AddOrUpdateCallstack(session, callstack);

            await _repository.SaveCallstacksAsync(session);

            var folder = _repository.GetSessionFolderPath(session);
            Assert.IsTrue(File.Exists(Path.Combine(folder, Constants.CallstacksFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.SessionFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.StateFileName)));
        }

        [TestMethod]
        public async Task SaveStateAsync_WritesHiddenAncestorNodes()
        {
            var session = CreateSession("TestSession");
            session.HiddenAncestorNodes[100] = true;

            await _repository.SaveStateAsync(session);

            var folder = _repository.GetSessionFolderPath(session);
            var json = File.ReadAllText(Path.Combine(folder, Constants.StateFileName));
            var state = JsonConvert.DeserializeObject<SessionState>(json);

            Assert.IsTrue(state.HiddenAncestorNodes.ContainsKey(100));
        }

        [TestMethod]
        public async Task SaveStateAsync_WritesOnlyStateFile()
        {
            var session = CreateSession("TestSession");
            session.NodeColors[100] = "#FF0000";
            session.CollapsedNodes[200] = true;

            await _repository.SaveStateAsync(session);

            var folder = _repository.GetSessionFolderPath(session);
            Assert.IsTrue(File.Exists(Path.Combine(folder, Constants.StateFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.SessionFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(folder, Constants.CallstacksFileName)));
        }

        [TestMethod]
        public void Sessions_AreSortedByCreatedTimeDescending()
        {
            var oldest = CreateSession("Oldest");
            oldest.CreatedTime = new DateTime(2020, 1, 1);
            var middle = CreateSession("Middle");
            middle.CreatedTime = new DateTime(2021, 6, 15);
            var newest = CreateSession("Newest");
            newest.CreatedTime = new DateTime(2022, 12, 31);

            var viewModel = CreateViewModel();
            viewModel.TriggerSwitchToSessionView();

            Assert.AreEqual(3, viewModel.FilteredSessions.Count);
            Assert.AreEqual("Newest", viewModel.FilteredSessions[0].Name);
            Assert.AreEqual("Middle", viewModel.FilteredSessions[1].Name);
            Assert.AreEqual("Oldest", viewModel.FilteredSessions[2].Name);
        }

        [TestMethod]
        public void SessionList_MarksActiveSessionAsActive()
        {
            var session1 = CreateSession("One");
            var session2 = CreateSession("Two");

            var viewModel = CreateViewModel();
            viewModel.SetActiveSession(session1);
            viewModel.TriggerSwitchToSessionView();

            Assert.IsTrue(viewModel.FilteredSessions.First(s => s.Id == session1.Id).IsActive);
            Assert.IsFalse(viewModel.FilteredSessions.First(s => s.Id == session2.Id).IsActive);
        }

        [TestMethod]
        public async Task RenameSelectedSession_UpdatesNameAndPersists()
        {
            var session = CreateSession("OldName");
            await _repository.SaveSessionMetadataAsync(session);

            var viewModel = CreateViewModel();
            await viewModel.LoadDataAsync();
            viewModel.TriggerSwitchToSessionView();
            var loadedSession = viewModel.FilteredSessions.First(s => s.Id == session.Id);
            viewModel.SelectedSession = loadedSession;
            viewModel.RenameSelectedSessionText = "Renamed";
            viewModel.TriggerSaveRenameSelectedSession();

            Assert.AreEqual("Renamed", loadedSession.Name);

            var loaded = await new SessionRepository(_solutionInfo).LoadDataAsync();
            var reloadedSession = loaded.Sessions.FirstOrDefault(s => s.Id == session.Id);
            Assert.IsNotNull(reloadedSession);
            Assert.AreEqual("Renamed", reloadedSession.Name);
        }

        [TestMethod]
        public void RenameSelectedSession_CancelRestoresOriginalName()
        {
            var session = CreateSession("Original");
            var viewModel = CreateViewModel();
            viewModel.TriggerSwitchToSessionView();
            viewModel.SelectedSession = viewModel.FilteredSessions.First(s => s.Id == session.Id);
            viewModel.RenameSelectedSessionText = "Changed";

            viewModel.TriggerCancelRenameSelectedSession();

            Assert.AreEqual("Original", session.Name);
            Assert.IsFalse(viewModel.IsRenamingSelectedSession);
        }

        [TestMethod]
        public void DeleteSelectedSession_RemovesSessionAndFolder()
        {
            var session = CreateSession("TestSession");
            _repository.SaveSessionMetadataAsync(session).Wait();

            var folder = _repository.GetSessionFolderPath(session);
            Assert.IsTrue(Directory.Exists(folder));

            var viewModel = CreateViewModel();
            viewModel.LoadDataAsync().Wait();
            viewModel.TriggerSwitchToSessionView();
            viewModel.SelectedSession = viewModel.FilteredSessions.First(s => s.Id == session.Id);
            viewModel.TriggerDeleteSelectedSession();

            Assert.AreEqual(0, _sessionData.Sessions.Count);
            Assert.IsFalse(Directory.Exists(folder));
            Assert.IsNull(viewModel.SelectedSession);
        }

        [TestMethod]
        public void DeleteActiveSession_KeepsActiveNullButResolvesPreviousFallback()
        {
            var older = CreateSession("Older");
            older.CreatedTime = new DateTime(2020, 1, 1);
            var newer = CreateSession("Newer");
            newer.CreatedTime = new DateTime(2021, 6, 15);

            var viewModel = CreateViewModel();
            viewModel.TriggerSwitchToSessionView();
            viewModel.SelectedSession = viewModel.FilteredSessions.First(s => s.Id == older.Id);

            viewModel.SetActiveSession(older);

            viewModel.TriggerDeleteSelectedSession();

            Assert.IsNull(viewModel.ActiveSession);
            Assert.AreEqual(newer.Id, _sessionData.PreviousSessionId);
            Assert.IsTrue(viewModel.CanResumePreviousSession);
            Assert.AreEqual("Newer", viewModel.PreviousSessionName);
            Assert.IsFalse(viewModel.FilteredSessions.Any(s => s.IsActive));

            var switchToTreeMethod = typeof(SessionViewModel).GetMethod("ExecuteSwitchToTreeView", BindingFlags.NonPublic | BindingFlags.Instance);
            switchToTreeMethod.Invoke(viewModel, null);

            Assert.IsTrue(viewModel.IsHomePageVisible);
        }

        [TestMethod]
        public void DeleteNonActivePreviousSession_ResolvesFallback()
        {
            var older = CreateSession("Older");
            older.CreatedTime = new DateTime(2020, 1, 1);
            var newer = CreateSession("Newer");
            newer.CreatedTime = new DateTime(2021, 6, 15);
            _sessionData.PreviousSessionId = older.Id;

            var viewModel = CreateViewModel();
            viewModel.TriggerSwitchToSessionView();
            viewModel.SelectedSession = viewModel.FilteredSessions.First(s => s.Id == older.Id);
            viewModel.TriggerDeleteSelectedSession();

            Assert.AreEqual(newer.Id, _sessionData.PreviousSessionId);
            Assert.IsTrue(viewModel.CanResumePreviousSession);
            Assert.AreEqual("Newer", viewModel.PreviousSessionName);
        }

        [TestMethod]
        public void DeleteOnlySession_ClearsPreviousSession()
        {
            var session = CreateSession("Only");
            _sessionData.PreviousSessionId = session.Id;

            var viewModel = CreateViewModel();
            viewModel.TriggerSwitchToSessionView();
            viewModel.SelectedSession = viewModel.FilteredSessions.First(s => s.Id == session.Id);
            viewModel.TriggerDeleteSelectedSession();

            Assert.IsNull(_sessionData.PreviousSessionId);
            Assert.IsFalse(viewModel.CanResumePreviousSession);
            Assert.IsNull(viewModel.PreviousSession);
        }

        [TestMethod]
        public async Task LoadDataAsync_ResolvesStalePreviousSessionId()
        {
            var session1 = CreateSession("One");
            session1.CreatedTime = new DateTime(2020, 1, 1);
            var session2 = CreateSession("Two");
            session2.CreatedTime = new DateTime(2021, 6, 15);

            await _repository.SaveSessionMetadataAsync(session1);
            await _repository.SaveSessionMetadataAsync(session2);
            await _repository.SavePreviousSessionIdAsync("stale-id");

            var viewModel = CreateViewModel();
            await viewModel.LoadDataAsync();

            Assert.AreEqual(session2.Id, _sessionData.PreviousSessionId);
            Assert.IsTrue(viewModel.CanResumePreviousSession);
            Assert.AreEqual("Two", viewModel.PreviousSessionName);
        }

        [TestMethod]
        public void ResetActiveSession_KeepsActiveNullButResolvesPreviousFallback()
        {
            var older = CreateSession("Older");
            older.CreatedTime = new DateTime(2020, 1, 1);
            var newer = CreateSession("Newer");
            newer.CreatedTime = new DateTime(2021, 6, 15);

            var viewModel = CreateViewModel();
            viewModel.SetActiveSession(older);
            viewModel.TriggerSwitchToSessionView();
            viewModel.TriggerResetActiveSession();

            Assert.IsNull(viewModel.ActiveSession);
            Assert.AreEqual(newer.Id, _sessionData.PreviousSessionId);
            Assert.AreEqual("Newer", viewModel.PreviousSessionName);
            Assert.IsFalse(viewModel.FilteredSessions.Any(s => s.IsActive));
        }

        [TestMethod]
        public void BuildTreeNodes_CollapseSemantics_DefaultExpanded()
        {
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack);

            var nodes = _treeBuilder.BuildTreeNodes(session);

            Assert.IsTrue(nodes[0].IsExpanded);
            Assert.IsTrue(nodes[0].Children[0].IsExpanded);
        }

        [TestMethod]
        public void BuildTreeNodes_CollapseSemantics_CollapsedWhenStored()
        {
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack);

            var frame = callstack.Frames[0];
            session.CollapsedNodes[frame.HashCode] = true;

            var nodes = _treeBuilder.BuildTreeNodes(session);

            Assert.IsFalse(nodes[0].IsExpanded);
        }

        [TestMethod]
        public void BuildTreeNodes_CollapseSemantics_ExpandedWhenNotInCollapsedNodes()
        {
            var session = new CallstackSession("Test");
            var callstack = CreateTestCallstack("main", "Run", "DoWork");
            AddOrUpdateCallstack(session, callstack);

            var nodes = _treeBuilder.BuildTreeNodes(session);

            Assert.IsTrue(nodes[0].IsExpanded);
            Assert.IsTrue(nodes[0].Children[0].IsExpanded);
            Assert.IsTrue(nodes[0].Children[0].Children[0].IsExpanded);
        }

        [TestMethod]
        public void SaveExpansionState_InvertedLogic()
        {
            var session = new CallstackSession("Test");

            _treeBuilder.SaveExpansionState(session, 100, false);
            Assert.IsTrue(session.CollapsedNodes[100]);

            _treeBuilder.SaveExpansionState(session, 200, true);
            Assert.IsFalse(session.CollapsedNodes[200]);
        }

        [TestMethod]
        public async Task LoadDataAsync_SetsIsLoadedFalse()
        {
            var session = CreateSession("TestSession");
            await _repository.SaveSessionMetadataAsync(session);

            var loaded = await new SessionRepository(_solutionInfo).LoadDataAsync();

            var loadedSession = loaded.Sessions[0];
            Assert.IsFalse(loadedSession.IsLoaded);
            Assert.IsNotNull(loadedSession.Callstacks);
            Assert.IsNotNull(loadedSession.NodeColors);
            Assert.IsNotNull(loadedSession.CollapsedNodes);
            Assert.IsNotNull(loadedSession.HiddenAncestorNodes);
            Assert.IsNotNull(loadedSession.NodeNotes);
            Assert.AreEqual(0, loadedSession.Callstacks.Count);
        }

        [TestMethod]
        public async Task LoadSessionDetailsAsync_DoesNotReloadIfAlreadyLoaded()
        {
            var session = CreateSession("TestSession");
            var callstack = CreateTestCallstack("main", "Run");
            AddOrUpdateCallstack(session, callstack);
            await _repository.SaveSessionMetadataAsync(session);
            await _repository.SaveCallstacksAsync(session);

            session.IsLoaded = true;
            session.Callstacks.Clear();

            await _repository.LoadSessionDetailsAsync(session);

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
            var bookmarks = engine.CreateAll(new[] { 5 }, path);
            var bookmark = bookmarks[0];

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

            var resolver = new BookmarkResolver(engine);
            var session = new CallstackSession("Test");
            session.Callstacks.Add(new CallstackData(new List<CallstackFrame> { frame }));
            await resolver.ResolveSessionAsync(session);

            Assert.AreEqual(6, frame.LineNumber);
        }

        [TestMethod]
        public async Task ResolveFrameLineNumberAsync_WithoutBookmark_ReturnsStoredLineNumber()
        {
            var frame = new CallstackFrame("Main", "source.cs", 42);

            var session = new CallstackSession("Test");
            session.Callstacks.Add(new CallstackData(new List<CallstackFrame> { frame }));

            var resolver = new BookmarkResolver(new FuzzyBookmarkEngine());
            await resolver.ResolveSessionAsync(session);

            Assert.AreEqual(42, frame.LineNumber);
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

            var session = new CallstackSession("Test");
            session.Callstacks.Add(new CallstackData(new List<CallstackFrame> { frame }));

            var resolver = new BookmarkResolver(engine);
            await resolver.ResolveSessionAsync(session);

            Assert.AreEqual(42, frame.LineNumber);
        }

        private SessionViewModel CreateViewModel()
        {
            return new SessionViewModel(
                _solutionInfo,
                _sessionData,
                _repository,
                _captureService,
                null,
                _treeBuilder,
                null,
                null,
                null);
        }

        private CallstackSession CreateSession(string name)
        {
            var session = new CallstackSession(name)
            {
                FolderName = _repository.GenerateSessionFolderName()
            };
            _sessionData.Sessions.Add(session);
            return session;
        }

        private void AddOrUpdateCallstack(CallstackSession session, CallstackData callstack)
        {
            var existing = session.Callstacks.FirstOrDefault(c => c.LeafHashCode == callstack.LeafHashCode);
            if (existing != null)
            {
                var index = session.Callstacks.IndexOf(existing);
                session.Callstacks[index] = callstack;
            }
            else
            {
                session.Callstacks.Add(callstack);
            }
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
