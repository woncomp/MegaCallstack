using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Models;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class CallstackSessionTests
    {
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
        public void GetActiveSession_ReturnsFirstWhenNoneSet()
        {
            var manager = CreateManager();
            var session = manager.CreateSession("Test");

            var active = manager.GetActiveSession();
            Assert.AreEqual(session.Id, active.Id);
        }

        private Services.CallstackManager CreateManager()
        {
            return new Services.CallstackManager(null);
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
