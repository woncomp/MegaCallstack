using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MegaCallstack.Models;

namespace MegaCallstack.Services
{
    public interface IBookmarkResolver
    {
        Task CreateBookmarksForCallstackAsync(CallstackData callstack);
        Task ResolveSessionAsync(CallstackSession session);
        Task ResolveFilesAsync(IEnumerable<string> filePaths, CallstackSession session);
    }

    public sealed class BookmarkResolver : IBookmarkResolver
    {
        private readonly FuzzyBookmarkEngine _bookmarkEngine;

        public BookmarkResolver(FuzzyBookmarkEngine bookmarkEngine)
        {
            _bookmarkEngine = bookmarkEngine ?? throw new ArgumentNullException(nameof(bookmarkEngine));
        }

        public Task CreateBookmarksForCallstackAsync(CallstackData callstack)
        {
            if (callstack?.Frames == null || callstack.Frames.Count == 0)
                return Task.CompletedTask;

            var groups = callstack.Frames
                .Select((frame, index) => new { frame, index })
                .Where(x => !string.IsNullOrEmpty(x.frame.FileName) && x.frame.LineNumber > 0)
                .GroupBy(x => x.frame.FileName)
                .ToList();

            foreach (var group in groups)
            {
                string filePath = group.Key;
                var ordered = group.OrderBy(x => x.index).ToList();
                var lineNumbers = ordered.Select(x => x.frame.LineNumber);

                try
                {
                    var bookmarks = _bookmarkEngine.CreateAll(lineNumbers, filePath);
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        ordered[i].frame.Bookmark = bookmarks[i];
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"BookmarkResolver: Failed to create bookmarks for {filePath}: {ex.Message}");
                }
            }

            return Task.CompletedTask;
        }

        public Task ResolveSessionAsync(CallstackSession session)
        {
            if (session?.Callstacks == null || session.Callstacks.Count == 0)
                return Task.CompletedTask;

            var frames = session.Callstacks.SelectMany(c => c.Frames)
                .Where(f => f.Bookmark != null && !string.IsNullOrEmpty(f.FileName))
                .ToList();

            return ResolveFramesAsync(frames, session);
        }

        public Task ResolveFilesAsync(IEnumerable<string> filePaths, CallstackSession session)
        {
            if (session?.Callstacks == null || filePaths == null)
                return Task.CompletedTask;

            var fileSet = new HashSet<string>(filePaths);
            var frames = session.Callstacks.SelectMany(c => c.Frames)
                .Where(f => f.Bookmark != null && !string.IsNullOrEmpty(f.FileName) && fileSet.Contains(f.FileName))
                .ToList();

            return ResolveFramesAsync(frames, session);
        }

        private Task ResolveFramesAsync(List<CallstackFrame> frames, CallstackSession session)
        {
            if (frames.Count == 0)
                return Task.CompletedTask;

            var groups = frames.GroupBy(f => f.FileName).ToList();
            foreach (var group in groups)
            {
                string filePath = group.Key;
                if (!File.Exists(filePath))
                    continue;

                var bookmarks = group.Select(f => f.Bookmark).ToList();
                try
                {
                    var results = _bookmarkEngine.ResolveAll(bookmarks, filePath);
                    var framesInGroup = group.ToList();
                    for (int i = 0; i < framesInGroup.Count; i++)
                    {
                        if (results[i]?.Line > 0)
                            framesInGroup[i].LineNumber = results[i].Line;
                    }
                    session.ResolvedFileWriteTimes[filePath] = File.GetLastWriteTimeUtc(filePath).Ticks;
                }
                catch (Exception ex)
                {
                    Logger.Log($"BookmarkResolver: Failed to resolve bookmarks for {filePath}: {ex.Message}");
                }
            }

            return Task.CompletedTask;
        }
    }
}
