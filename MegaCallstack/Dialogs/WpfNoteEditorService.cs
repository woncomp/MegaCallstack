using System.Windows;
using MegaCallstack.Models;
using MegaCallstack.Services;

namespace MegaCallstack.Dialogs
{
    public class WpfNoteEditorService : INoteEditorService
    {
        private readonly Window _owner;

        public WpfNoteEditorService(Window owner)
        {
            _owner = owner;
        }

        public NoteEditorResult EditNote(NodeNote existingNote)
        {
            bool isEditing = existingNote != null;
            var dialog = new NoteEditorDialog(existingNote, isEditing) { Owner = _owner };
            if (dialog.ShowDialog() == true)
            {
                if (dialog.IsDeleted)
                    return new NoteEditorResult { IsConfirmed = true, IsDeleted = true };
                return new NoteEditorResult
                {
                    IsConfirmed = true,
                    Note = new NodeNote { Emoji = dialog.SelectedEmoji, Text = dialog.NoteText }
                };
            }
            return new NoteEditorResult { IsConfirmed = false };
        }
    }
}
