using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MegaCallstack.Models;
using Microsoft.VisualStudio.PlatformUI;

namespace MegaCallstack.Dialogs
{
    public partial class NoteEditorDialog : Window
    {
        private static readonly List<string> PresetEmojis = new List<string>
        {
            "📝", "⚠️", "🐛", "💡", "❓", "✅", "❌", "🔄", "⏱️", "🔒",
            "🚀", "📌", "🗑️", "🔍", "💻", "📊", "🏷️", "🔗", "📎", "🚩"
        };

        private readonly bool _isEditingExisting;
        private Button _selectedButton;

        public NoteEditorDialog(NodeNote initialNote, bool isEditingExisting)
        {
            InitializeComponent();
            Loaded += OnLoaded;

            _isEditingExisting = isEditingExisting;
            SelectedEmoji = initialNote?.Emoji ?? PresetEmojis[0];
            NoteText = initialNote?.Text ?? string.Empty;
        }

        public string SelectedEmoji { get; private set; }
        public string NoteText { get; private set; }
        public bool IsDeleted { get; private set; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildEmojiButtons();
            NoteTextBox.Text = NoteText;
            NoteTextBox.Focus();
            DeleteButton.Visibility = _isEditingExisting ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildEmojiButtons()
        {
            foreach (var emoji in PresetEmojis)
            {
                var button = new Button
                {
                    Content = emoji,
                    Style = (Style)FindResource("EmojiButton")
                };

                if (emoji == SelectedEmoji)
                {
                    HighlightButton(button);
                }

                button.Click += (s, args) =>
                {
                    if (_selectedButton != null)
                    {
                        _selectedButton.BorderBrush = Brushes.Transparent;
                        _selectedButton.Background = Brushes.Transparent;
                    }

                    SelectedEmoji = emoji;
                    HighlightButton(button);
                };

                EmojiPanel.Children.Add(button);
            }
        }

        private void HighlightButton(Button button)
        {
            _selectedButton = button;
            button.BorderBrush = (Brush)(TryFindResource(EnvironmentColors.ToolWindowBorderBrushKey) ?? Brushes.Gray);
            button.Background = (Brush)(TryFindResource(ThemedDialogColors.ActionButtonBackgroundActiveBrushKey) ?? Brushes.LightGray);
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            NoteText = NoteTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            IsDeleted = true;
            DialogResult = true;
            Close();
        }
    }
}
