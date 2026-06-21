using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Models;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class CallstackFrameTests
    {
        [TestMethod]
        public void HashCode_Stability_SamePathProducesSameHash()
        {
            string[] path = { "main", "Program.Run", "Worker.DoWork" };
            int hash1 = CallstackFrame.ComputeHashForPath(path);
            int hash2 = CallstackFrame.ComputeHashForPath(path);

            Assert.AreEqual(hash1, hash2);
        }

        [TestMethod]
        public void HashCode_Stability_IgnoresLineNumbers()
        {
            var frames1 = new[]
            {
                new CallstackFrame("main", "Program.cs", 10),
                new CallstackFrame("Run", "Program.cs", 25),
                new CallstackFrame("DoWork", "Worker.cs", 50)
            };

            var frames2 = new[]
            {
                new CallstackFrame("main", "Program.cs", 99),
                new CallstackFrame("Run", "Program.cs", 100),
                new CallstackFrame("DoWork", "Worker.cs", 200)
            };

            int hash1 = CallstackFrame.ComputeHashForPath(new[] { frames1[0].FunctionName, frames1[1].FunctionName, frames1[2].FunctionName });
            int hash2 = CallstackFrame.ComputeHashForPath(new[] { frames2[0].FunctionName, frames2[1].FunctionName, frames2[2].FunctionName });

            Assert.AreEqual(hash1, hash2);
        }

        [TestMethod]
        public void HashCode_DifferentPathsProduceDifferentHashes()
        {
            string[] path1 = { "main", "Program.Run" };
            string[] path2 = { "main", "Program.Execute" };

            int hash1 = CallstackFrame.ComputeHashForPath(path1);
            int hash2 = CallstackFrame.ComputeHashForPath(path2);

            Assert.AreNotEqual(hash1, hash2);
        }

        [TestMethod]
        public void HashCode_RecursiveComputation_MatchesSequential()
        {
            string[] names = { "A", "B", "C" };
            int sequentialHash = CallstackFrame.ComputeHashForPath(names);

            int recursiveHash = 0;
            recursiveHash = CallstackFrame.ComputeFNV1aHash(recursiveHash, "A");
            recursiveHash = CallstackFrame.ComputeFNV1aHash(recursiveHash, "B");
            recursiveHash = CallstackFrame.ComputeFNV1aHash(recursiveHash, "C");

            Assert.AreEqual(sequentialHash, recursiveHash);
        }

        [TestMethod]
        public void ToString_FormatsCorrectly()
        {
            var frame = new CallstackFrame("MyFunc", "MyFile.cs", 42);
            Assert.AreEqual("MyFunc - MyFile.cs:42", frame.ToString());
        }

        [TestMethod]
        public void LineContent_CanBeSetAndRetrieved()
        {
            var frame = new CallstackFrame("MyFunc", "MyFile.cs", 42)
            {
                LineContent = "  var x = 1;  "
            };

            Assert.AreEqual("  var x = 1;  ", frame.LineContent);
        }

        [TestMethod]
        public void LineContent_DefaultsToNull()
        {
            var frame = new CallstackFrame("MyFunc", "MyFile.cs", 42);

            Assert.IsNull(frame.LineContent);
        }
    }
}
