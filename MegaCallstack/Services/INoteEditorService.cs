using MegaCallstack.Models;

namespace MegaCallstack.Services
{
    public class NoteEditorResult
    {
        public bool IsConfirmed { get; set; }
        public bool IsDeleted { get; set; }
        public NodeNote Note { get; set; }
    }

    public interface INoteEditorService
    {
        NoteEditorResult EditNote(NodeNote existingNote);
    }
}
