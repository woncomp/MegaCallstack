using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCallstack.Models;
using System.Windows;
using MegaCallstack.ViewModels;

namespace MegaCallstack.Services
{
    /// <summary>
    /// A per-solution workspace that owns the session data, repository, tree builder,
    /// and view model for exactly one loaded solution. When the solution closes,
    /// this object is disposed.
    /// </summary>
    public class SolutionWorkspace : IDisposable
    {
        private readonly ISessionRepository _repository;
        private readonly ICallstackCaptureService _captureService;
        private readonly ICallstackTreeBuilder _treeBuilder;
        private readonly IColorPickerService _colorPickerService;
        private readonly INoteEditorService _noteEditorService;
        private readonly Window _window;

        public SolutionInfo SolutionInfo { get; }
        public SolutionSessionData SessionData { get; private set; }
        public SessionViewModel ViewModel { get; private set; }

        public SolutionWorkspace(
            SolutionInfo solutionInfo,
            ISessionRepository repository,
            ICallstackCaptureService captureService,
            ICallstackTreeBuilder treeBuilder,
            IColorPickerService colorPickerService,
            INoteEditorService noteEditorService,
            Window window)
        {
            SolutionInfo = solutionInfo ?? throw new ArgumentNullException(nameof(solutionInfo));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
            _treeBuilder = treeBuilder ?? throw new ArgumentNullException(nameof(treeBuilder));
            _colorPickerService = colorPickerService ?? throw new ArgumentNullException(nameof(colorPickerService));
            _noteEditorService = noteEditorService ?? throw new ArgumentNullException(nameof(noteEditorService));
            _window = window;
        }

        public async Task InitializeAsync()
        {
            SessionData = await _repository.LoadDataAsync();
            ViewModel = new SessionViewModel(
                SolutionInfo,
                SessionData,
                _repository,
                _captureService,
                _treeBuilder,
                _colorPickerService,
                _noteEditorService,
                _window);

            await ViewModel.LoadDataAsync();
        }

        public void Dispose()
        {
            ViewModel?.Dispose();
            ViewModel = null;
            SessionData = null;
        }
    }
}
