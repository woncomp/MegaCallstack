using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MegaCallstack.Models;
using Newtonsoft.Json;

namespace MegaCallstack.Services
{
    public class SessionRepository : ISessionRepository
    {
        private static readonly Random _random = new Random();

        public SolutionInfo SolutionInfo { get; }

        public SessionRepository(SolutionInfo solutionInfo)
        {
            SolutionInfo = solutionInfo ?? throw new ArgumentNullException(nameof(solutionInfo));
        }

        public async Task<SolutionSessionData> LoadDataAsync()
        {
            var data = new SolutionSessionData();
            var dataDirectory = SolutionInfo.DataDirectory;

            if (!Directory.Exists(dataDirectory))
                return data;

            foreach (var folder in Directory.GetDirectories(dataDirectory).OrderBy(d => d))
            {
                var sessionFile = Path.Combine(folder, Constants.SessionFileName);
                if (!File.Exists(sessionFile))
                    continue;

                try
                {
                    var json = File.ReadAllText(sessionFile);
                    var session = JsonConvert.DeserializeObject<CallstackSession>(json);
                    if (session != null)
                    {
                        session.FolderName = Path.GetFileName(folder);
                        session.Callstacks = new List<CallstackData>();
                        session.NodeColors = new Dictionary<int, string>();
                        session.CollapsedNodes = new Dictionary<int, bool>();
                session.HiddenAncestorNodes = new Dictionary<int, bool>();
                session.NodeNotes = new Dictionary<int, List<NodeNote>>();
                session.ResolvedFileWriteTimes = new Dictionary<string, long>();
                session.IsLoaded = false;
                        data.Sessions.Add(session);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"SessionRepository: Failed to load session from {folder}", ex);
                }
            }

            LoadActiveSessionId(data);
            return data;
        }

        private void LoadActiveSessionId(SolutionSessionData data)
        {
            var filePath = Path.Combine(SolutionInfo.DataDirectory, Constants.ActiveSessionFileName);
            if (!File.Exists(filePath))
                return;

            try
            {
                var json = File.ReadAllText(filePath);
                var activeId = JsonConvert.DeserializeObject<string>(json);
                if (!string.IsNullOrEmpty(activeId) && data.Sessions.Any(s => s.Id == activeId))
                    data.ActiveSessionId = activeId;
            }
            catch (Exception ex)
            {
                Logger.Error("SessionRepository: Failed to load active session id", ex);
            }
        }

        public async Task LoadSessionDetailsAsync(CallstackSession session)
        {
            if (session == null || session.IsLoaded)
                return;

            var folder = GetSessionFolderPath(session);
            if (folder == null || !Directory.Exists(folder))
                return;

            await LoadCallstacksAsync(session, folder);
            await LoadStateAsync(session, folder);
            await LoadNotesAsync(session, folder);

            session.IsLoaded = true;
        }

        private async Task LoadCallstacksAsync(CallstackSession session, string folder)
        {
            var filePath = Path.Combine(folder, Constants.CallstacksFileName);
            if (!File.Exists(filePath))
                return;

            try
            {
                var json = await Task.Run(() => File.ReadAllText(filePath));
                session.Callstacks = JsonConvert.DeserializeObject<List<CallstackData>>(json) ?? new List<CallstackData>();
            }
            catch (Exception ex)
            {
                Logger.Error($"SessionRepository: Failed to load callstacks from {filePath}", ex);
                session.Callstacks = new List<CallstackData>();
            }
        }

        private async Task LoadStateAsync(CallstackSession session, string folder)
        {
            var filePath = Path.Combine(folder, Constants.StateFileName);
            if (!File.Exists(filePath))
                return;

            try
            {
                var json = await Task.Run(() => File.ReadAllText(filePath));
                var state = JsonConvert.DeserializeObject<SessionState>(json);
                    if (state != null)
                    {
                        session.NodeColors = state.NodeColors ?? new Dictionary<int, string>();
                        session.CollapsedNodes = state.CollapsedNodes ?? new Dictionary<int, bool>();
                        session.HiddenAncestorNodes = state.HiddenAncestorNodes ?? new Dictionary<int, bool>();
                        session.ResolvedFileWriteTimes = state.ResolvedFileWriteTimes ?? new Dictionary<string, long>();
                    }
            }
            catch (Exception ex)
            {
                Logger.Error($"SessionRepository: Failed to load state from {filePath}", ex);
            }
        }

        private async Task LoadNotesAsync(CallstackSession session, string folder)
        {
            var filePath = Path.Combine(folder, Constants.NotesFileName);
            if (!File.Exists(filePath))
                return;

            try
            {
                var json = await Task.Run(() => File.ReadAllText(filePath));
                session.NodeNotes = JsonConvert.DeserializeObject<Dictionary<int, List<NodeNote>>>(json) ?? new Dictionary<int, List<NodeNote>>();
            }
            catch (Exception ex)
            {
                Logger.Error($"SessionRepository: Failed to load notes from {filePath}", ex);
                session.NodeNotes = new Dictionary<int, List<NodeNote>>();
            }
        }

        public async Task SaveSessionMetadataAsync(CallstackSession session)
        {
            var folder = GetOrCreateSessionFolder(session);
            if (folder == null)
                return;

            var filePath = Path.Combine(folder, Constants.SessionFileName);
            var metadata = new CallstackSession
            {
                Id = session.Id,
                Name = session.Name,
                CreatedTime = session.CreatedTime
            };
            await WriteJsonAsync(filePath, metadata);
        }

        public async Task SaveCallstacksAsync(CallstackSession session)
        {
            var folder = GetOrCreateSessionFolder(session);
            if (folder == null)
                return;

            var filePath = Path.Combine(folder, Constants.CallstacksFileName);
            await WriteJsonAsync(filePath, session.Callstacks);
        }

        public async Task SaveStateAsync(CallstackSession session)
        {
            var folder = GetOrCreateSessionFolder(session);
            if (folder == null)
                return;

            var filePath = Path.Combine(folder, Constants.StateFileName);
            var state = new SessionState
            {
                NodeColors = session.NodeColors,
                CollapsedNodes = session.CollapsedNodes,
                HiddenAncestorNodes = session.HiddenAncestorNodes,
                ResolvedFileWriteTimes = session.ResolvedFileWriteTimes
            };
            await WriteJsonAsync(filePath, state);
        }

        public async Task SaveNotesAsync(CallstackSession session)
        {
            var folder = GetOrCreateSessionFolder(session);
            if (folder == null)
                return;

            var filePath = Path.Combine(folder, Constants.NotesFileName);
            await WriteJsonAsync(filePath, session.NodeNotes);
        }

        public async Task SaveActiveSessionIdAsync(string activeSessionId)
        {
            var filePath = Path.Combine(SolutionInfo.DataDirectory, Constants.ActiveSessionFileName);
            await WriteJsonAsync(filePath, activeSessionId);
        }

        private async Task WriteJsonAsync(string filePath, object data)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = await Task.Run(() => JsonConvert.SerializeObject(data, Formatting.Indented));
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Logger.Error($"SessionRepository: Failed to write {filePath}", ex);
            }
        }

        public string GetOrCreateSessionFolder(CallstackSession session)
        {
            if (session == null)
                return null;

            if (string.IsNullOrEmpty(session.FolderName))
                session.FolderName = GenerateSessionFolderName();

            var folder = GetSessionFolderPath(session);
            if (folder == null)
                return null;

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            return folder;
        }

        public string GetSessionFolderPath(CallstackSession session)
        {
            if (session?.FolderName == null)
                return null;

            return Path.Combine(SolutionInfo.DataDirectory, session.FolderName);
        }

        public string GenerateSessionFolderName()
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
            var hash = GenerateRandomHash(3);
            var folderName = $"{timestamp}-{hash}";
            var dataDirectory = SolutionInfo.DataDirectory;

            if (Directory.Exists(dataDirectory))
            {
                int suffix = 1;
                var candidate = folderName;
                while (Directory.Exists(Path.Combine(dataDirectory, candidate)))
                {
                    var newHash = GenerateRandomHash(3);
                    candidate = $"{timestamp}-{newHash}";
                    suffix++;
                    if (suffix > 100)
                    {
                        candidate = $"{timestamp}-{hash}-{Guid.NewGuid().ToString("N").Substring(0, 4)}";
                        break;
                    }
                }
                folderName = candidate;
            }

            return folderName;
        }

        public bool HasCallstacks(CallstackSession session)
        {
            if (session == null)
                return false;

            if (session.IsLoaded)
                return session.Callstacks.Count > 0;

            var folder = GetSessionFolderPath(session);
            if (folder == null)
                return false;

            var filePath = Path.Combine(folder, Constants.CallstacksFileName);
            if (!File.Exists(filePath))
                return false;

            try
            {
                var json = File.ReadAllText(filePath);
                var callstacks = JsonConvert.DeserializeObject<List<CallstackData>>(json);
                return callstacks != null && callstacks.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private string GenerateRandomHash(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var sb = new StringBuilder(length);
            lock (_random)
            {
                for (int i = 0; i < length; i++)
                    sb.Append(chars[_random.Next(chars.Length)]);
            }
            return sb.ToString();
        }
    }
}
