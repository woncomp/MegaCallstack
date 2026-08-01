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
    public class SettingsServiceTests
    {
        private string _tempDirectory;
        private string _originalSettingsFilePath;
        private string _originalSettingsDirectory;

        [TestInitialize]
        public void TestInitialize()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);

            var settingsType = typeof(SettingsService);
            var directoryField = settingsType.GetField("_settingsDirectory", BindingFlags.NonPublic | BindingFlags.Static);
            var filePathField = settingsType.GetField("_settingsFilePath", BindingFlags.NonPublic | BindingFlags.Static);

            _originalSettingsDirectory = (string)directoryField.GetValue(null);
            _originalSettingsFilePath = (string)filePathField.GetValue(null);

            var newFilePath = Path.Combine(_tempDirectory, "settings.json");
            directoryField.SetValue(null, _tempDirectory);
            filePathField.SetValue(null, newFilePath);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            var settingsType = typeof(SettingsService);
            var directoryField = settingsType.GetField("_settingsDirectory", BindingFlags.NonPublic | BindingFlags.Static);
            var filePathField = settingsType.GetField("_settingsFilePath", BindingFlags.NonPublic | BindingFlags.Static);

            directoryField.SetValue(null, _originalSettingsDirectory);
            filePathField.SetValue(null, _originalSettingsFilePath);

            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [TestMethod]
        public void SettingsService_WhenFileMissing_ReturnsDefaults()
        {
            var service = new SettingsService();

            Assert.IsFalse(service.Current.DiagnosticLoggingEnabled);
            Assert.AreEqual(120, service.Current.LeafNodeDisplayMaxLength);
            Assert.AreEqual(8, service.Current.MaxUserCodeRoots);
            Assert.AreEqual(100000, service.Current.MaxSolutionFilesToScan);
            Assert.IsNotNull(service.Current.SkipRootFunctions);
            Assert.AreEqual(0, service.Current.SkipRootFunctions.Count);
        }

        [TestMethod]
        public void SettingsService_Save_PersistsValues()
        {
            var service = new SettingsService();
            var settings = new MegaCallstackSettings
            {
                DiagnosticLoggingEnabled = true,
                BookmarkFileDiagnosticsEnabled = true,
                LeafNodeDisplayMaxLength = 250,
                MaxUserCodeRoots = 15,
                MaxSolutionFilesToScan = 200000,
                SkipRootFunctions = new System.Collections.Generic.List<string> { "ThreadMainFunc", "RenderThread_Main" }
            };

            service.Save(settings);

            var json = File.ReadAllText(Path.Combine(_tempDirectory, "settings.json"));
            var deserialized = JsonConvert.DeserializeObject<MegaCallstackSettings>(json);

            Assert.IsTrue(deserialized.DiagnosticLoggingEnabled);
            Assert.AreEqual(250, deserialized.LeafNodeDisplayMaxLength);
            Assert.AreEqual(15, deserialized.MaxUserCodeRoots);
            Assert.AreEqual(200000, deserialized.MaxSolutionFilesToScan);
            CollectionAssert.AreEqual(settings.SkipRootFunctions, deserialized.SkipRootFunctions);
        }

        [TestMethod]
        public void SettingsService_Save_ClampsOutOfRangeValues()
        {
            var service = new SettingsService();
            var settings = new MegaCallstackSettings
            {
                LeafNodeDisplayMaxLength = 5,
                MaxUserCodeRoots = 0,
                MaxSolutionFilesToScan = 0
            };

            service.Save(settings);

            Assert.AreEqual(10, service.Current.LeafNodeDisplayMaxLength);
            Assert.AreEqual(1, service.Current.MaxUserCodeRoots);
            Assert.AreEqual(1, service.Current.MaxSolutionFilesToScan);
        }

        [TestMethod]
        public void SettingsService_Save_NormalizesSkipRootFunctions()
        {
            var service = new SettingsService();
            var settings = new MegaCallstackSettings
            {
                SkipRootFunctions = new System.Collections.Generic.List<string>
                {
                    " ThreadMainFunc ",
                    "RenderThread_Main",
                    "",
                    "threadmainfunc",
                    null
                }
            };

            service.Save(settings);

            Assert.AreEqual(2, service.Current.SkipRootFunctions.Count);
            Assert.AreEqual("ThreadMainFunc", service.Current.SkipRootFunctions[0]);
            Assert.AreEqual("RenderThread_Main", service.Current.SkipRootFunctions[1]);
        }

        [TestMethod]
        public void SettingsService_Load_LoadsExistingFile()
        {
            var settings = new MegaCallstackSettings
            {
                DiagnosticLoggingEnabled = true,
                BookmarkFileDiagnosticsEnabled = false,
                LeafNodeDisplayMaxLength = 500,
                MaxUserCodeRoots = 50,
                MaxSolutionFilesToScan = int.MaxValue,
                SkipRootFunctions = new System.Collections.Generic.List<string> { "ThreadMainFunc" }
            };

            File.WriteAllText(Path.Combine(_tempDirectory, "settings.json"), JsonConvert.SerializeObject(settings));

            var service = new SettingsService();

            Assert.IsTrue(service.Current.DiagnosticLoggingEnabled);
            Assert.IsFalse(service.Current.BookmarkFileDiagnosticsEnabled);
            Assert.AreEqual(500, service.Current.LeafNodeDisplayMaxLength);
            Assert.AreEqual(50, service.Current.MaxUserCodeRoots);
            Assert.AreEqual(int.MaxValue, service.Current.MaxSolutionFilesToScan);
            CollectionAssert.AreEqual(settings.SkipRootFunctions, service.Current.SkipRootFunctions);
        }

        [TestMethod]
        public void SettingsService_Load_InvalidJson_ReturnsDefaults()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "settings.json"), "not json");

            var service = new SettingsService();

            Assert.AreEqual(120, service.Current.LeafNodeDisplayMaxLength);
            Assert.IsNotNull(service.Current.SkipRootFunctions);
            Assert.AreEqual(0, service.Current.SkipRootFunctions.Count);
        }

        [TestMethod]
        public void SettingsService_Save_UpdatesCurrentSettings()
        {
            var originalSettings = SettingsService.CurrentSettings;

            try
            {
                var service = new SettingsService();
                service.Save(new MegaCallstackSettings
                {
                    LeafNodeDisplayMaxLength = 300,
                    MaxUserCodeRoots = 20,
                    MaxSolutionFilesToScan = 12345,
                    SkipRootFunctions = new System.Collections.Generic.List<string> { "ThreadMainFunc" }
                });

                Assert.AreEqual(300, SettingsService.CurrentSettings.LeafNodeDisplayMaxLength);
                Assert.AreEqual(20, SettingsService.CurrentSettings.MaxUserCodeRoots);
                Assert.AreEqual(12345, SettingsService.CurrentSettings.MaxSolutionFilesToScan);
                CollectionAssert.AreEqual(new[] { "ThreadMainFunc" }, SettingsService.CurrentSettings.SkipRootFunctions);
            }
            finally
            {
                SettingsService.CurrentSettings = originalSettings;
            }
        }
    }
}
