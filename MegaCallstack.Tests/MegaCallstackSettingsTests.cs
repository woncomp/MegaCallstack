using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MegaCallstack.Models;
using MegaCallstack.Services;
using Newtonsoft.Json;

namespace MegaCallstack.Tests
{
    [TestClass]
    public class MegaCallstackSettingsTests
    {
        [TestMethod]
        public void Settings_Defaults_AreAsExpected()
        {
            var settings = new MegaCallstackSettings();

            Assert.IsFalse(settings.DiagnosticLoggingEnabled);
            Assert.IsFalse(settings.BookmarkFileDiagnosticsEnabled);
            Assert.AreEqual(120, settings.LeafNodeDisplayMaxLength);
            Assert.AreEqual(8, settings.MaxUserCodeRoots);
            Assert.AreEqual(100000, settings.MaxSolutionFilesToScan);
        }

        [TestMethod]
        public void Settings_Serialization_RoundTrips()
        {
            var original = new MegaCallstackSettings
            {
                DiagnosticLoggingEnabled = true,
                BookmarkFileDiagnosticsEnabled = true,
                LeafNodeDisplayMaxLength = 200,
                MaxUserCodeRoots = 10,
                MaxSolutionFilesToScan = 50000
            };

            var json = JsonConvert.SerializeObject(original);
            var deserialized = JsonConvert.DeserializeObject<MegaCallstackSettings>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(original.DiagnosticLoggingEnabled, deserialized.DiagnosticLoggingEnabled);
            Assert.AreEqual(original.BookmarkFileDiagnosticsEnabled, deserialized.BookmarkFileDiagnosticsEnabled);
            Assert.AreEqual(original.LeafNodeDisplayMaxLength, deserialized.LeafNodeDisplayMaxLength);
            Assert.AreEqual(original.MaxUserCodeRoots, deserialized.MaxUserCodeRoots);
            Assert.AreEqual(original.MaxSolutionFilesToScan, deserialized.MaxSolutionFilesToScan);
        }
    }
}
