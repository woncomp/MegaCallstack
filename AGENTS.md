# MegaCallstack Project Onboarding Guide

This guide is written for agents (and developers) who need to ramp up on the MegaCallstack Visual Studio extension quickly. It explains how the project is organized, how to navigate the codebase, and where to look when making changes.

## ⚠️ HIGH PRIORITY RULES

- **All code and documentation must be written in English.** This includes code comments, commit messages, doc files, and any text the agent produces.
- **Do not commit unless the user explicitly asks.** Even when the user explicitly requests a commit, that request applies only to the current conversation turn. Never treat commit as a default action after making changes.
- **Never leave "Merge branch" commits in main** Follow Branch Merging Guidelines, use **Rebase + Fast-Forward** approach whenever not requesting a squash merge.

## 1. What This Extension Does

MegaCallstack is a Visual Studio tool window that captures debugger callstacks while debugging is paused at a breakpoint. It merges callstacks into a tree view, lets users highlight nodes with colors, search, navigate to source code, and manage multiple named sessions per solution.

## 2. Repository Layout

The repository is a single Visual Studio solution with two projects:

```
D:\Workspace\MegaCallstack
├── MegaCallstack.csproj              # Main VSIX extension project
├── MegaCallstack.sln
├── Constants.cs                      # Shared constants (folder/file names)
├── Logger.cs                         # Simple logging helper
├── MegaCallstackPackage.cs           # VS package / command registration
├── MegaCallstack.vsct                # Command/tool-window definitions
├── source.extension.vsixmanifest     # VSIX metadata
├── Models/                           # Plain data models
│   ├── CallstackFrame.cs             # One frame in a callstack
│   ├── CallstackData.cs              # One captured callstack
│   ├── CallstackSession.cs           # Session metadata + runtime data
│   ├── SessionState.cs               # Per-session state (colors, collapse)
│   ├── SolutionSessionData.cs        # In-memory list of session metadata
│   └── TreeViewNode.cs               # WPF tree node with color/bold logic
├── Services/
│   └── CallstackManager.cs           # Capture, persistence, tree building
├── ViewModels/
│   └── MegaCallstackViewModel.cs     # Main view model + commands
├── ToolWindows/
│   ├── MegaCallstackToolWindow.cs    # ToolWindowPane host
│   └── MegaCallstackToolWindowControl.xaml/.cs  # WPF UI + event handlers
├── Controls/
│   ├── CallstackTreeView.cs          # Custom TreeView
│   ├── CallstackTreeViewItem.cs      # Custom TreeViewItem
│   └── HighlightTextBlock.cs         # Search-highlight text control
└── MegaCallstack.Tests/              # MSTest unit-test project
    ├── CallstackFrameTests.cs
    ├── CallstackSessionTests.cs
    └── TreeViewNodeTests.cs
```

## 3. How to Build and Test

1. Open `MegaCallstack.sln` in Visual Studio 2022 (any edition with VSSDK).
2. Build solution (`Debug|Any CPU`).
3. To run unit tests from command line:

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" "D:\Workspace\MegaCallstack\MegaCallstack.sln" /p:Configuration=Debug /p:Platform="Any CPU" /restore
& "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" "D:\Workspace\MegaCallstack\MegaCallstack.Tests\bin\Debug\MegaCallstack.Tests.dll"
```

4. To launch the extension in an experimental VS instance, set the startup project to `MegaCallstack` and press F5.

## 4. Data Flow

```
VS Debugger (EnvDTE)
        |
        v
CallstackManager.CaptureCurrentCallstackAsync()
        |
        v
CallstackData / CallstackFrame (computed hash codes)
        |
        v
CallstackSession (grouped by session)
        |
        v
Disk: .vs/{SolutionName}/MegaCallstack/{yyyy-MM-dd-HHmm-xxx}/
      session.json      -> metadata only
      callstacks.json   -> captured callstacks
      state.json        -> node colors + collapsed nodes
        |
        v
MegaCallstackViewModel -> TreeViewNode -> WPF TreeView
```

### Important Persistence Rules

- **Startup:** Only `session.json` files are scanned; `callstacks.json` and `state.json` are loaded lazily when a session is activated.
- **Saves:** Each data type writes to its own file. Metadata changes do not rewrite callstacks or state.
- **Folder names:** `yyyy-MM-dd-HHmm-xxx` where `xxx` is a 3-character lowercase alphanumeric hash.
- **Collapse semantics:** `state.json` stores `CollapsedNodes` as a dictionary. A missing key means the node is expanded; `true` means collapsed.

## 5. Key Classes and Where to Find Things

| Task | File(s) |
|------|---------|
| Add/change persistence format | `Constants.cs`, `Services/CallstackManager.cs`, `Models/CallstackSession.cs`, `Models/SessionState.cs` |
| Change how callstacks are captured | `Services/CallstackManager.cs` (`CaptureCurrentCallstackAsync`, `TrimToUserCode`) |
| Change hashing logic | `Models/CallstackFrame.cs` |
| Change tree rendering, colors, bolding | `Models/TreeViewNode.cs`, `Services/CallstackManager.cs` (`BuildTreeNodes`) |
| Change UI commands or session workflow | `ViewModels/MegaCallstackViewModel.cs` |
| Change WPF layout/context menus | `ToolWindows/MegaCallstackToolWindowControl.xaml` and `.xaml.cs` |
| Add tests | `MegaCallstack.Tests/*.cs` |

## 6. Testing Notes

- The test project references `Microsoft.VisualStudio.Threading` directly so the runtime DLL is copied into the test bin folder. Without this reference, tests fail with `FileNotFoundException` for `Microsoft.VisualStudio.Threading`.
- `CallstackManager` skips the main-thread switch when `_dte` is null, which lets most tests run without initializing the VS threading context.
- Tests use reflection to inject a temporary `_dataDirectory` into `CallstackManager` instances.

## 7. Common Pitfalls

- **Main thread access:** All `EnvDTE` and `ThreadHelper` calls must stay on the main thread in production. Test code passes `null` for `DTE`, so guard such calls appropriately.
- **Lazy loading:** Do not assume `CallstackSession.Callstacks` or `NodeColors` are populated after `LoadDataAsync`. Call `CallstackManager.LoadSessionDetailsAsync(session)` first.
- **Color propagation:** `TreeViewNode.DisplayForeground` can be explicit or inherited. The `IsColorExplicitlySet` flag distinguishes the two; `ResolveColor` only recomputes inherited colors.
- **No automatic session activation:** On startup there is no active session. The first capture (or manual Create Session) creates and activates a session.

## 8. Useful Commands

Build:
```powershell
MSBuild MegaCallstack.sln /p:Configuration=Debug /p:Platform="Any CPU" /restore
```

Run tests:
```powershell
vstest.console MegaCallstack.Tests\bin\Debug\MegaCallstack.Tests.dll
```

## 9. Related Files for Context

- Original implementation plan: `mega_callstack_plan.md` (slightly outdated after the folder-per-session refactor).
- VS command/table definitions: `MegaCallstack.vsct`.
- VSIX metadata: `source.extension.vsixmanifest`.


## 10. Versioning

- **Version advancing during daily development** Then the user explictly asks a version bump, increase the build(tail) version by 1, e.g. `x.y.z.W` -> `x.y.z.(W+1)` Update both `Properties/AssemblyInfo.cs` (`AssemblyVersion` and `AssemblyFileVersion`) and `source.extension.vsixmanifest` (the `Version` attribute in the `Identity` element). Keep them in sync.
- **When the user requests a release, reset the build (tail) version to 0 and bump the version based on which version component the user requested:**
  - **major**: first component (e.g., `X.y.z.w` -> `(X+1).0.0.0`)
  - **minor**: second component (e.g., `x.Y.z.w` -> `x.(Y+1).0.0`)
  - **patch**: third component (e.g., `x.y.Z.w` -> `x.y.(Z+1).0`)
  - If the user did not mention the component, bump the **patch** version.
- **Release process workflow:**
  > [!IMPORTANT]
  > A request to commit changes (e.g., "commit your changes") is just a normal code commit. It does NOT mean the user wants a release. The release workflow (pushing to origin, tagging, etc.) should only be triggered if the user explicitly requests a **release** (e.g., "please release this version").
  1. Stage the changed files.
  2. Ask the user to review before committing.
  3. After the user approves, commit it, push to `origin`, create a tag for this new version (e.g., `vX.Y.Z.0`), and push the tag to `origin` to trigger the release workflow.


## 11. Branch Merging Guidelines
When merging changes from an agent-created branch or worktree back into `main`, adhere to these workflows:
  - By default, use **Rebase + Fast-Forward** merging to maintain a clean, linear commit history on `main` without creating merge commits. First, rebase the branch onto `main` (`git rebase main`), then checkout `main` and fast-forward merge the branch (`git merge <branch>`).
  - If the user explicitly requests a squash, use **Squash Merge** (`git merge --squash <branch>`). In this case, the agent must compose a clean, descriptive summary of the changes to be used as the commit message.