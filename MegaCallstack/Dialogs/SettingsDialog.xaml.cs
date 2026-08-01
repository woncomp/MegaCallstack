using System;
using System.Globalization;
using System.Windows;
using MegaCallstack.Models;
using MegaCallstack.Services;

namespace MegaCallstack.Dialogs
{
    public partial class SettingsDialog : Window
    {
        private readonly ISettingsService _settingsService;

        public SettingsDialog(ISettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            LoadSettings(_settingsService.Current);
        }

        private void LoadSettings(MegaCallstackSettings settings)
        {
            OutputPaneLoggingCheckBox.IsChecked = settings.DiagnosticLoggingEnabled;
            BookmarkFileDiagnosticsCheckBox.IsChecked = settings.BookmarkFileDiagnosticsEnabled;
            LeafNodeDisplayMaxLengthTextBox.Text = settings.LeafNodeDisplayMaxLength.ToString(CultureInfo.InvariantCulture);
            MaxUserCodeRootsTextBox.Text = settings.MaxUserCodeRoots.ToString(CultureInfo.InvariantCulture);
            MaxSolutionFilesToScanTextBox.Text = settings.MaxSolutionFilesToScan.ToString(CultureInfo.InvariantCulture);
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseSettings(out var settings))
                return;

            _settingsService.Save(settings);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool TryParseSettings(out MegaCallstackSettings settings)
        {
            settings = null;
            ValidationTextBlock.Text = string.Empty;

            if (!TryParseInt(LeafNodeDisplayMaxLengthTextBox.Text, "Leaf node display max length", out var leafNodeDisplayMaxLength))
                return false;
            if (!TryParseInt(MaxUserCodeRootsTextBox.Text, "Max user code roots", out var maxUserCodeRoots))
                return false;
            if (!TryParseInt(MaxSolutionFilesToScanTextBox.Text, "Max solution files to scan", out var maxSolutionFilesToScan))
                return false;

            if (leafNodeDisplayMaxLength < 10 || leafNodeDisplayMaxLength > 1000)
            {
                ShowValidation("Leaf node display max length* must be between 10 and 1000.");
                return false;
            }

            if (maxUserCodeRoots < 1 || maxUserCodeRoots > 100)
            {
                ShowValidation("Max user code roots* must be between 1 and 100.");
                return false;
            }

            if (maxSolutionFilesToScan < 1)
            {
                ShowValidation("Max solution files to scan* must be at least 1.");
                return false;
            }

            settings = new MegaCallstackSettings
            {
                DiagnosticLoggingEnabled = OutputPaneLoggingCheckBox.IsChecked ?? false,
                BookmarkFileDiagnosticsEnabled = BookmarkFileDiagnosticsCheckBox.IsChecked ?? false,
                LeafNodeDisplayMaxLength = leafNodeDisplayMaxLength,
                MaxUserCodeRoots = maxUserCodeRoots,
                MaxSolutionFilesToScan = maxSolutionFilesToScan
            };

            return true;
        }

        private static bool TryParseInt(string text, string fieldName, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            return true;
        }

        private void ShowValidation(string message)
        {
            ValidationTextBlock.Text = message;
        }
    }
}
