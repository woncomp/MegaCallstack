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
    public class NodeNoteTests
    {
        private string _tempDirectory;

        [TestInitialize]
        public void Setup()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "MegaCallstackNoteTests_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
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
        public void NodeNote_Serialization_RoundTrip()
        {
            var note = new NodeNote { Emoji = "📝", Text = "Initial investigation note" };
            var json = JsonConvert.SerializeObject(note);
            var deserialized = JsonConvert.DeserializeObject<NodeNote>(json);

            Assert.AreEqual(note.Emoji, deserialized.Emoji);
            Assert.AreEqual(note.Text, deserialized.Text);
        }

        [TestMethod]
        public async Task LoadNotesAsync_LoadsNotesFromJson()
        {
            var manager = CreateManager();
            InjectDataDirectory(manager);

            var session = manager.CreateSession("TestSession");
            var folder = Path.Combine(_tempDirectory, session.FolderName);
            Directory.CreateDirectory(folder);

            var notes = new Dictionary<int, List<NodeNote>>
            {
                [42] = new List<NodeNote>
                {
                    new NodeNote { Emoji = "📝", Text = "Note one" },
                    new NodeNote { Emoji = "🐛", Text = "Bug here" }
                }
            };
            File.WriteAllText(Path.Combine(folder, Constants.NotesFileName), JsonConvert.SerializeObject(notes));

            await manager.LoadSessionDetailsAsync(session);

            Assert.IsTrue(session.NodeNotes.ContainsKey(42));
            Assert.AreEqual(2, session.NodeNotes[42].Count);
            Assert.AreEqual("📝", session.NodeNotes[42][0].Emoji);
            Assert.AreEqual("Bug here", session.NodeNotes[42][1].Text);
        }

        [TestMethod]
        public async Task SaveNotesAsync_WritesNotesToJson()
        {
            var manager = CreateManager();
            InjectDataDirectory(manager);

            var session = manager.CreateSession("TestSession");
            session.NodeNotes[99] = new List<NodeNote>
            {
                new NodeNote { Emoji = "💡", Text = "Idea" }
            };

            await manager.SaveNotesAsync(session);

            var folder = Path.Combine(_tempDirectory, session.FolderName);
            var filePath = Path.Combine(folder, Constants.NotesFileName);
            Assert.IsTrue(File.Exists(filePath));

            var json = File.ReadAllText(filePath);
            var deserialized = JsonConvert.DeserializeObject<Dictionary<int, List<NodeNote>>>(json);
            Assert.IsTrue(deserialized.ContainsKey(99));
            Assert.AreEqual(1, deserialized[99].Count);
            Assert.AreEqual("💡", deserialized[99][0].Emoji);
            Assert.AreEqual("Idea", deserialized[99][0].Text);
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
    }
}
