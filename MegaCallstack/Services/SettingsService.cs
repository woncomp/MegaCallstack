using System;
using System.IO;
using MegaCallstack.Models;
using Newtonsoft.Json;

namespace MegaCallstack.Services
{
    public sealed class SettingsService : ISettingsService
    {
        private const string SettingsFolderName = "MegaCallstack";
        private const string SettingsFileName = "settings.json";

        private static readonly string _settingsDirectory;
        private static readonly string _settingsFilePath;

        public MegaCallstackSettings Current { get; private set; }
        public static MegaCallstackSettings CurrentSettings { get; set; }

        static SettingsService()
        {
            _settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), SettingsFolderName);
            _settingsFilePath = Path.Combine(_settingsDirectory, SettingsFileName);
        }

        public SettingsService()
        {
            Current = LoadInternal();
            CurrentSettings = Current;
        }

        public void Save(MegaCallstackSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            ValidateAndClamp(settings);

            if (!Directory.Exists(_settingsDirectory))
                Directory.CreateDirectory(_settingsDirectory);

            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(_settingsFilePath, json);

            Current = settings;
            CurrentSettings = Current;
        }

        private static MegaCallstackSettings LoadInternal()
        {
            if (!File.Exists(_settingsFilePath))
                return CreateDefaultSettings();

            try
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonConvert.DeserializeObject<MegaCallstackSettings>(json);
                if (settings == null)
                    return CreateDefaultSettings();

                ValidateAndClamp(settings);
                return settings;
            }
            catch
            {
                return CreateDefaultSettings();
            }
        }

        private static MegaCallstackSettings CreateDefaultSettings()
        {
            return new MegaCallstackSettings();
        }

        private static void ValidateAndClamp(MegaCallstackSettings settings)
        {
            settings.LeafNodeDisplayMaxLength = Clamp(settings.LeafNodeDisplayMaxLength, 10, 1000);
            settings.MaxUserCodeRoots = Clamp(settings.MaxUserCodeRoots, 1, 100);
            settings.MaxSolutionFilesToScan = Clamp(settings.MaxSolutionFilesToScan, 1, int.MaxValue);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
