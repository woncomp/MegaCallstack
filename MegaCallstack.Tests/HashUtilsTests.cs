using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Models;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class HashUtilsTests
    {
        [TestMethod]
        public void FNV1a_SameInput_ProducesSameHash()
        {
            int hash1 = HashUtils.FNV1a(0, "test");
            int hash2 = HashUtils.FNV1a(0, "test");

            Assert.AreEqual(hash1, hash2);
        }

        [TestMethod]
        public void FNV1a_DifferentInput_ProducesDifferentHash()
        {
            int hash1 = HashUtils.FNV1a(0, "test1");
            int hash2 = HashUtils.FNV1a(0, "test2");

            Assert.AreNotEqual(hash1, hash2);
        }

        [TestMethod]
        public void FNV1a_ChainedCalls_AreStable()
        {
            int hash1 = HashUtils.FNV1a(0, "A");
            hash1 = HashUtils.FNV1a(hash1, "B");
            hash1 = HashUtils.FNV1a(hash1, "C");

            int hash2 = HashUtils.FNV1a(0, "A");
            hash2 = HashUtils.FNV1a(hash2, "B");
            hash2 = HashUtils.FNV1a(hash2, "C");

            Assert.AreEqual(hash1, hash2);
        }

        [TestMethod]
        public void FNV1a_DifferentParentHash_ProducesDifferentResult()
        {
            int hash1 = HashUtils.FNV1a(1, "test");
            int hash2 = HashUtils.FNV1a(2, "test");

            Assert.AreNotEqual(hash1, hash2);
        }

        [TestMethod]
        public void FNV1a_NullInput_ReturnsOriginalHash()
        {
            int original = 42;
            int result = HashUtils.FNV1a(original, null);

            Assert.AreEqual(original, result);
        }

        [TestMethod]
        public void FNV1a_EmptyInput_ReturnsOriginalHash()
        {
            int original = 42;
            int result = HashUtils.FNV1a(original, "");

            Assert.AreEqual(original, result);
        }

        [TestMethod]
        public void FNV1a_IsDifferentFromCallstackFrameHash()
        {
            string input = "TestFunction";
            int hashUtilsResult = HashUtils.FNV1a(0, input);
            int callstackFrameResult = CallstackFrame.ComputeFNV1aHash(0, input);

            Assert.AreEqual(hashUtilsResult, callstackFrameResult);
        }
    }
}
