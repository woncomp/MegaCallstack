using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Models;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class SolutionInfoTests
    {
        [TestMethod]
        public void Constructor_SetsPropertiesFromFullPath()
        {
            var fullPath = @"C:\Projects\MyApp\MyApp.sln";
            var info = new SolutionInfo(fullPath);

            Assert.AreEqual(fullPath, info.FullPath);
            Assert.AreEqual(@"C:\Projects\MyApp", info.Directory);
            Assert.AreEqual("MyApp", info.Name);
            Assert.AreEqual(Path.Combine(@"C:\Projects\MyApp", ".vs", "MyApp", Constants.DataFolderName), info.DataDirectory);
        }

        [TestMethod]
        public void Constructor_EmptyOrNullPath_ThrowsArgumentException()
        {
            Assert.ThrowsException<ArgumentException>(() => new SolutionInfo(null));
            Assert.ThrowsException<ArgumentException>(() => new SolutionInfo(""));
            Assert.ThrowsException<ArgumentException>(() => new SolutionInfo("   "));
        }

        [TestMethod]
        public void IsReady_WithoutUserCodeRoots_ReturnsFalse()
        {
            var info = new SolutionInfo(@"C:\Projects\MyApp\MyApp.sln");

            Assert.IsFalse(info.IsReady);
        }

        [TestMethod]
        public void IsReady_WithUserCodeRoots_ReturnsTrue()
        {
            var info = new SolutionInfo(@"C:\Projects\MyApp\MyApp.sln", new[] { @"C:\Projects\MyApp\src" });

            Assert.IsTrue(info.IsReady);
            Assert.AreEqual(1, info.UserCodeRoots.Count);
            Assert.AreEqual(@"C:\Projects\MyApp\src", info.UserCodeRoots[0]);
        }
    }
}
