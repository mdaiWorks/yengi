Read this in: [🇹🇷 Türkçe](README.tr.md) | 🇺🇸 English

---

<p align="center">
  <img src="assets/banner.png" alt="Yengi Banner" width="100%"/>
</p>

# 🚀 Yengi

> **Y**our **E**ngineering **N**exus, **G**enerative **I**ntelligence.  
> *"Yengi: the name for the happy outcome you finally reach after working on something for a long time — the reward for your effort."*

[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL--3.0-blue.svg)](LICENSE)
[![Framework: .NET 8 WPF](https://img.shields.io/badge/Framework-.NET%208%20WPF-purple.svg)](https://dotnet.microsoft.com/)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6.svg)](https://microsoft.com/windows)
[![HuggingFace: Yengi Router](https://img.shields.io/badge/HuggingFace-yengi--router%3A1.5b-FFD21E.svg)](https://huggingface.co/)
[![Support: BuyMeACoffee](https://img.shields.io/badge/Support-Buy%20Me%20A%20Coffee-FFDD00.svg)](https://buymeacoffee.com/mdaiyazilim)

**A Windows desktop AI software development assistant that works with local and cloud AI models, can read and modify project files, can run terminal/build/test operations, and provides agent-based workflows.**

Rather than being a classic "AI chat window," Yengi is a WPF/.NET 8 desktop application designed to carry out the full **code understanding → planning → tool selection → file modification → terminal/build/test → verification → result** loop end-to-end on a software project chosen by the user.

This README was prepared by directly scanning the project's source code (100+ files) and verifying every concrete numeric/technical claim in it (limits, file paths, default values) one by one against the code.

---

## 🎬 Video Showcase & Walkthrough

[![Yengi AI IDE Demo](https://img.youtube.com/vi/l0tdhvCmwNI/maxresdefault.jpg)](https://youtu.be/l0tdhvCmwNI)
> 👆 *Click above to watch the full video walkthrough: Local Yengi Router Setup, Live Web Tetris Game Creation & Multi-tool Execution!*

---

## ✨ Features Showcase

### 🌐 Web App Development & AI Image Generation
![Web App Development & AI Image Studio](assets/gorselUretimvetetris.gif)

### 🧊 Live 3D Modeling with Blender Copilot
![Blender Copilot](assets/Blender.gif)

### 🎮 Game Engine Integration with Unity Copilot
![Unity Copilot](assets/unity.gif)

---

## 📸 Screenshots & UI Tour

| Main IDE & Chat Workspace | Settings & Local Router Setup |
| :---: | :---: |
| ![Main UI Workspace](assets/arayuz.png) | ![Settings Window](assets/AyarlarSayfasi.png) |

---

## Table of Contents

- [1. Overview](#1-overview)
- [2. Core Features](#2-core-features)
- [3. Architecture](#3-architecture)
- [4. AI Agent Flow](#4-ai-agent-flow)
- [5. Tool System (31 Tools)](#5-tool-system-31-tools)
- [6. UI and Button Reference](#6-ui-and-button-reference)
- [7. Chat and Session System](#7-chat-and-session-system)
- [8. Plan Mode](#8-plan-mode)
- [9. Verification Loop (Self-Healing)](#9-verification-loop-self-healing)
- [10. Sub-Agent / Delegation](#10-sub-agent--delegation)
- [11. AI Router](#11-ai-router)
- [12. RAG](#12-rag)
- [13. Code Editor](#13-code-editor)
- [14. Terminal, Build, and Project Type Detection](#14-terminal-build-and-project-type-detection)
- [15. Git Integration](#15-git-integration)
- [16. Plugin and LSP System](#16-plugin-and-lsp-system)
- [17. Voice Command](#17-voice-command)
- [18. Provider Architecture](#18-provider-architecture)
- [19. Security](#19-security)
- [20. Checkpoint / Backup](#20-checkpoint--backup)
- [21. Context and Token Management](#21-context-and-token-management)
- [22. Project Constitution and Memory](#22-project-constitution-and-memory)
- [23. Multi-Language Support (i18n)](#23-multi-language-support-i18n)
- [24. Project File Structure](#24-project-file-structure)
- [25. Setup and Configuration](#25-setup-and-configuration)
- [26. Technical Dependencies](#26-technical-dependencies)
- [27. Current Status and Known Limitations](#27-current-status-and-known-limitations)
- [28. Completed Development Phases](#28-completed-development-phases)
- [29. Remaining Development Areas](#29-remaining-development-areas)

---

# 1. Overview

Yengi's core purpose is to combine the tasks a developer repeatedly performs on a project with AI. When a user says, for example:

> "Find the bug on the login screen, fix it, build the project, and run the tests."

the system doesn't just generate code; it discovers the required context, chooses the appropriate tools, modifies the files, and **verifies the change on its own.**

```text
User
   │
   ▼
Chat UI
   │
   ▼
ChatFlowService  (the single, actually-running orchestrator in real time — see Section 3.2)
   │
   ├── Context / History
   ├── RAG
   ├── Router
   ├── Planning
   └── Tool Definitions
   │
   ▼
AI Provider (Anthropic / Google / OpenAI / ApiService / LocalModel)
   │
   ▼
Tool Calls → ToolExecutor
   │
   ├── File Operations   ├── Terminal / Build / Test
   ├── Search             ├── Git
   ├── Planning           ├── Checkpoint
   ├── Web                ├── Screenshot
   └── Sub-Agent          └── User Interaction (AskUserOptions)
   │
   ▼
Verification / Recovery (self-healing)
   │
   ▼
Chat UI + Timeline + Todo List + File Changes panel
```

---

# 2. Core Features

- AI-assisted code chat, project folder selection, a full-featured code editor (AvalonEdit)
- **5 provider categories:** Anthropic (Claude), Google (Gemini), OpenAI directly, OpenAI-compatible cloud services (OpenRouter/DeepSeek/Nvidia NIM), local models (Ollama/LM Studio)
- AI Router (intelligent tool selection using a small model) + manual tool management (tool pruning)
- Evidence-based task completion: a project task is not considered complete without build/test verification
- Capability and user-approval gate for high-risk terminal/build/test tools
- Plan Mode, Task Graph, Retry/Recovery Plan
- Verification Loop (self-verification and self-repair after compilation)
- Checkpoint creation / rollback, automatic backups (Backup Viewer)
- File reading/creation/partial update, diff preview
- Running terminal, build, and tests; intelligent command validation based on project type
- Git integration (stage/commit/push, LibGit2Sharp)
- Project context discovery, code search, file search
- **RAG** (embedding-based semantic code search) + project watcher (automatic re-indexing)
- Chat session management, history, Timeline, Todo list, file-changes panel
- Sub-agent / task delegation (launching independent tasks in the background)
- Web search (Tavily), web page reading, screenshot capture (for UI-error analysis)
- Voice command / speech-to-text (STT, independent of the main model)
- LSP (Language Server Protocol) infrastructure + built-in static error checker plugins for 14 languages + external DLL plugin support
- Settings, TR/EN/ZH localization, keyboard shortcuts, Command Palette
- Safe Automation (safe mode), terminal command approval window, path/symlink security checks
- Toast notifications, token usage tracking, provider fallback infrastructure

---

# 3. Architecture

## 3.1 Main layers

```text
┌──────────────────────────────────────────────┐
│                   WPF UI                      │
│  MainWindow + Dialogs + Settings + Panels     │
└──────────────────────┬───────────────────────┘
                        │
┌──────────────────────▼───────────────────────┐
│         Chat / Orchestration                  │
│  ChatFlowService, AiActionService,            │
│  ChatSessionService                           │
└──────────────────────┬───────────────────────┘
                        │
           ┌────────────┼────────────┐
           ▼            ▼            ▼
        Router       Planning       RAG
           │            │            │
           └────────────┼────────────┘
                         ▼
┌──────────────────────────────────────────────┐
│                AI Providers                   │
│  Anthropic / Google / OpenAI / ApiService /   │
│  LocalModel  (common IAiProvider interface)   │
└──────────────────────┬───────────────────────┘
                        │
                        ▼
┌──────────────────────────────────────────────┐
│                 ToolExecutor                  │
│  Path/symlink security + approval flow + dispatch│
└──────────────────────┬───────────────────────┘
                        │
          ┌─────────────┼─────────────────┐
          ▼             ▼                 ▼
       Services       Git / LSP         Plugins
          │
          ▼
 Files / Terminal / Build / Test / Web / RAG / ...
```

## 3.2 Orchestration: `ChatFlowService` vs `AgentCoreRuntime`

**`ChatFlowService`** is the **single, actually-running center** of the chat flow. It is the one orchestrator instantiated when `MainWindow` starts up (`_chatFlowService = new ChatFlowService(...)`). Its responsibilities:

- Session management, message sending, the AI pipeline
- Executing tool calls and processing their results
- History management (see Section 21), RAG connection, triggering the verification service
- Determining changed files, forwarding the AI response to the UI, retry/recovery, agent workflow events (Timeline/Todo)

**`IAgentCore` / `AgentCoreRuntime`** is the boundary for UI-independent agent execution and verification (it includes interfaces for tool execution, verification, checkpoint, rollback, and event subscription). Safe runtime creation, production-readiness checks, tool capability validation, and verification-runner injection are all in place. `ChatFlowService` remains the main chat orchestrator; the core tool dispatch, Verification 2.0, checkpoint/rollback, and Timeline EventBus flows run through the production-safe runtime.

---

# 4. AI Agent Flow

```text
1. User message
2. Active project + chat history are prepared
3. RAG / project discovery runs if needed
4. Router / model selection is performed (if enabled)
5. A request with tool definitions is sent to the AI model
6. The AI produces a tool call
7. ToolExecutor handles the call → security/path/approval check
8. The tool is executed, the result is sent back to the AI
9. New tool calls if needed (loop)
10. Silent BuildProject after file changes (self-healing)
11. If there's an error: self-repair (max 3 attempts) → if still failing, investigate via WebSearch/WebFetch → try again
12. Final response + brief summary (what was done / points to note / next step)
```

This flow is dictated to the model in detail via the system prompt in `ToolDefinitions.cs` (`GetCoreSystemPrompt`): always read a file before editing it, never reveal internal tool names to the user, never proceed to code changes on architectural ambiguity without user approval, the TODO panel is updated automatically via events (`ReadingFile/EditingFile/.../Completed/Failed`), and so on.

---

# 5. Tool System (31 Tools)

Definitions live in `ToolDefinitions.cs`; execution lives in `ToolExecutor.cs` + `Services/*`.

| Category | Tools |
|---|---|
| **File** | `ReadFile` (optional startLine/endLine), `CreateOrUpdateFile`, `ReplaceFileContent` (partial/targeted change) |
| **Terminal** | `ExecuteTerminalCommand` (optional `workingDirectory`, defaults to project root if not specified), `ReadToolOutput` (reads a line range from the temporary full version of truncated long output) |
| **Visual analysis** | `TakeScreenshot` (file or base64 output, for UI-error analysis) |
| **Project discovery** | `FindFiles`, `SearchCode`, `ListDirectory`, `DiscoverProjectContext` |
| **Build/Test** | `BuildProject` (.csproj/.sln), `RunTests` |
| **Planning / Memory** | `CreatePlan`, `CreateTaskGraph`, `RetryPlan` (recovery), `GenerateDiff`, `ReadProjectMemory`, `WriteProjectMemory`, `SearchProjectMemory`, `ArchiveProjectMemory`, `WriteDecision`, `WriteTask` (`.mdai/memory.json`, `.mdai/decisions.json`, `.mdai/tasks/`) |
| **Checkpoint** | `CreateCheckpoint`, `RollbackToCheckpoint` |
| **Quick commands** | `CreateQuickCommand`, `ExecuteQuickCommand` (via `CommandService`) |
| **Web** | `WebSearch` (Tavily API), `WebFetch` |
| **Delegation** | `DelegateTask` (`role`, `prompt`, `context` parameters; works with `DelegationService` + `SubAgentResultCoordinator`; the main flow can wait for the result with `waitForCompletion=true`, and there is a timeout mechanism) |
| **User interaction** | `AskUserOptions` (a question with choices/free text in a popup; `allowMultiple` for multi-select), `SmartRecovery` (recovery plan after an error) |

> The `role` field in `DelegateTask` is a free-text field (not a fixed role list/enum); names like "Backend Developer", "Researcher", "Tester" are example roles the model chooses on its own.

### 5.1 Differences between closely related tools

| Tools | Difference |
|---|---|
| `CreatePlan` / `CreateTaskGraph` | `CreatePlan` plans the steps to be taken and the files to be examined. `CreateTaskGraph` models this work as task nodes with dependencies. |
| `CreatePlan` / `WriteTask` | `CreatePlan` produces what will be done. `WriteTask` persists the task's current status (`in-progress`, `completed`, `blocked`) and progress notes. |
| `WriteDecision` / `WriteProjectMemory` | `WriteDecision` appends what a specific architectural decision was and why it was made to a chronological log. `WriteProjectMemory` stores or updates persistent, reusable architecture/conventions information as key-value pairs. |
| `ReadProjectMemory` / `SearchProjectMemory` | `ReadProjectMemory` reads all versioned memory or a specific key. `SearchProjectMemory` searches text within architecture/conventions records. |
| `ArchiveProjectMemory` | Does not delete a memory record; it moves an architecture/conventions record that is no longer valid into the `archived` field. |
| `ReplaceFileContent` / `GenerateDiff` | `ReplaceFileContent` actually modifies the file. `GenerateDiff` produces a preview of the difference between old and new content. |
| `FindFiles` / `ListDirectory` | `FindFiles` searches by name/pattern. `ListDirectory` lists the direct contents of a specific folder. |
| `RetryPlan` / `SmartRecovery` | `RetryPlan` produces a new recovery plan for a technical error. `SmartRecovery` is the interactive flow that offers recovery options to the user. |
| `CreateQuickCommand` / `ExecuteQuickCommand` / `ExecuteTerminalCommand` | The first saves a command template, the second runs a saved template, the third executes a terminal command directly. |

---

# 6. UI and Button Reference

### Project / file panel
| Button (`x:Name`) | Function |
|---|---|
| `btnSelectFolder` | Selects the project folder |
| `btnShowBackups` | Displays automatic backups (Backup Viewer) |
| `btnGitHubSync` | Pushes the project to GitHub |
| `btnGitRefresh`, `btnStageAll`, `btnCommit` | Refresh Git status / stage all / commit |
| `btnSearchFiles`, `btnClearSearch` | Search by file name / clear |
| File tree right-click menu | Refresh, New File, New Folder, Delete, Show in Explorer |
| Git list right-click menu | Stage / Unstage |

### Editor toolbar
| Button | Function |
|---|---|
| `btnSaveFile` / `btnSaveAll` | Save (Ctrl+S) / Save all (Ctrl+Shift+S) |
| `btnFormatDocument` | Automatically formats the file |
| `btnQuickOpenFile` | Quick file open |
| `btnRunApp` / `btnHotReload` / `btnStopApp` | Run / Hot Reload / Stop (`MainWindow.RunDebug.cs`) |
| `btnCommandPalette` | Opens the command palette (Ctrl+P) |
| `btnSafeAutomation` ("Safe Mode") | Requires approval for all automation operations |
| `btnAiActions` + context menu | AI Refactor, Improve Comments, Explain File, Plan Mode, Generate Tests, Apply Last AI Response |
| `btnPlanMode` | Prepares a step-by-step implementation plan |
| `btnPluginManager` | Opens the plugin manager |
| `btnAbout` | About window |

### Terminal panel
| Button | Function |
|---|---|
| `btnOpenTerminalPath` | Opens the terminal's working directory |
| `btnClearTerminal` | Clears the terminal |
| `btnKillProcess` | Terminates the process |
| `btnRunCommand` | Runs the command |

`TerminalService` communicates with the UI via stdout/stderr, status, and busy-state events.

Long build/test/terminal outputs are truncated by `ContextOptimizerService` before being sent to the model. The full output is temporarily kept under `%TEMP%\mdaiAgent\tool-output`, and a `FULL_OUTPUT_ID` is provided with the result. If needed, the model can read a specific line range of the output using the `ReadToolOutput` tool. Temporary spool records are cleaned up under a 24-hour and 50-file limit; no spool file is created for short output.

### Diagnostics panel

The `Diagnostics` tab in the bottom panel shows the verification and tool telemetry of the active project. It shows the verification total, success/failure counts, average duration, last status, and per-tool call/success/duration information. `Refresh` re-reads the panel; `Clear Telemetry` deletes only tool and verification records, preserving token/cost totals.

### RAG panel
| Button | Function |
|---|---|
| `btnRagSettings` | Opens RAG settings (API key/URL/model, on/off) |
| `btnRagReindex` | Re-indexes the project |

### Top menu
| Button | Function |
|---|---|
| `btnTools` | Manages the tools the model can use (automatic/manual) |
| `btnAgents` | Manages specialized sub-agent modules |
| `btnKeyboardShortcuts` | Keyboard shortcuts |
| `btnSettings` | API keys, Router, theme settings |

The `Check for Updates` button inside `AboutWindow` is ready to open the configured GitHub Releases channel. If an update channel has not yet been set, the user is clearly informed; the application does not pretend there is a fake update available.

### Chat panel
| Button / Control | Function |
|---|---|
| `btnNewChatMain` | New chat |
| `ChatSessionRename_Click` / `ChatSessionDelete_Click` | Rename / delete chat |
| `btnShowChatHistory` / `btnShowTimeline` / `btnShowTodoList` / `btnShowFileChanges` | Chat history / workflow timeline / TODO list / changed-files panels |
| `btnClearHistory` | Clears history (project settings are preserved) |
| `@mention` chip tags | Manages files added in the message input field as removable chips (`[ Foo.cs ✕ ]`) |
| `cmbContextMode` | Quickly switches context mode (Auto, File, Selection, Project, RAG) |
| `cmbComposerModel` | Selects the main AI model or Router mode directly from the Composer |
| `btnAttach` / `btnAttach2` | Attach a file / item |
| `btnVoice` | Push-to-talk voice command |
| `btnSend` / `btnStop` | Send message / stop the AI |
| Code block `Apply ↓` | Applies AI code responses directly to the active file with a diff preview |
| `🧠 Working` activity card | Presents live tool steps inside the AI message bubble as a collapsible summary |

### Command Palette (example commands)
| Command | Function |
|---|---|
| Save File / Save All | Save the active/all files |
| View Backups | Backup screen |
| AI Refactor / Improve Comments / Explain File / Generate Tests / Plan Mode | AI Actions shortcuts |
| Apply Last AI Response | Applies the last response to the file |
| Format Document / Quick Open File | Editor shortcuts |

Separate windows: `SettingsWindow`, `PluginManagerWindow`, `RagSettingsWindow`, `RouterBlacklistWindow`, `KeyboardShortcutsWindow`, `VoiceSettingsWindow`, `BackupViewerWindow`, `DiffWindow`, `PlanWindow`, `AskUserDialog`, `ConfirmCommandWindow`, `InputDialog`, `MessageDialog`, `PreviewWindow`, `LanguageSelectionWindow`, `AboutWindow`.

---

# 7. Chat and Session System

Managed via `ChatSessionService` + `ChatFlowService`. The user can: create a new chat, select one, rename it, delete it, clear the history, edit messages, and load more history.

**Verified limits** (`ChatFlowService.cs`):
- `MaxHistoryMessagesPerSession = 200` — the maximum number of messages kept in a session
- `MaxHistoryMessagesToSend = 40` — the fallback limit on the number of messages sent to the AI
- A periodic memory-cleanup timer every 30 minutes (`TimeSpan.FromMinutes(30)`)

---

# 8. Plan Mode

Components: `PlanModeService`, `PlanModeHelper`, `PlanModeResult`, `PlanningService`, `AiPlanGenerator`, `PlanWindow`.

Purpose: instead of the AI making changes directly, it first produces an actionable roadmap. Once a plan is generated, the user can: review it, regenerate it, copy it, approve it and transfer it into the chat flow, or cancel it. `AiPlanGenerator` performs plan generation as a call **isolated** from the main chat context (so as not to bloat the main conversation history). `PlanningService` is also used for generating recovery plans and task graphs.

---

# 9. Verification Loop (Self-Healing)

`AgentVerificationLoopService`:

```text
AI made a change → Build/Test → Successful?
   ├─ YES → Final response
   └─ NO → Recovery (RetryPlan) → New operation → (max. 3 self-attempts)
                                                  → if still failing, investigate via WebSearch/WebFetch
```

It also intelligently skips/adapts the test step based on project type (e.g., it skips the test step in a Node project if there is no test script in `package.json`). With Verification 2.0, the flow includes build, test, static analysis, review of changed files, and automatic-fix steps. Static analysis warnings are counted separately from actual failures; the summary explicitly reports the `success / failure / warning` counts and a "successful with warnings" state. Changed files are tracked as part of the workflow result.

The completion gate for a project task does not rely solely on the model's textual claim of "done": a final completed status is not given unless the post-change verification succeeds. High-risk tools such as `ExecuteTerminalCommand`, `BuildProject`, and `RunTests` also go through a capability/approval gate.

---

# 10. Sub-Agent / Delegation

```text
DelegateTask → DelegationService → SubAgentResultCoordinator → Background Task → Timeline/EventBus → Result
```

A task is tracked with `role`, `prompt`, `context`, a task ID, and status. The main flow can wait for the result with `waitForCompletion=true`; there is a timeout mechanism. This infrastructure is decoupled in a way that can be extended in the future toward a more genuine "swarm" (multi-agent) architecture.

---

# 11. AI Router

Components: `IAiRouter`, `AiRouter`, `RouterContext`, `RouterDecision`, `RouterModelInfo`.

For the local Router, the default technical model is `mdai-router-1.5b-q4` and the default endpoint is `http://localhost:11434/v1`; both can be changed in settings. Cloud Router usage can also be configured.

Purpose: instead of sending the entire tool catalog to the main model on every request, a small/cheap model (default `gemini-2.5-flash`) decides in advance which tools are needed — this shortens the system prompt and lowers token cost. The default confidence threshold (`RouterConfidenceThreshold`): **0.80**. If confidence is insufficient or an error occurs, it falls back to all tools. `RouterBlacklistWindow` can exclude tools the router should never suggest; if `IsManualToolManagement=true`, the user makes the tool selection entirely on their own.

The Router can connect to different provider/model setups via OpenAI-compatible endpoint and model settings. It produces a single router call and a single decision per request; multi-teacher consensus is out of scope for this project. If an API error, timeout, low confidence, or invalid tool selection occurs while the Router is active, a limited fallback tool set (`ReadFile`, `FindFiles`, `SearchCode`, `ListDirectory`, `DiscoverProjectContext`, `CreateOrUpdateFile`, `ReplaceFileContent`, `BuildProject`, `RunTests`) is sent to the main model instead of the entire tool catalog. The Router blacklist also constrains the fallback selection.

When the local Router is selected, the `RouterModel` and `RouterBaseUrl` settings are used; they are not overridden by a hardcoded model name. The application keeps the downloaded GGUF file under `%APPDATA%\\mdaiAgent\\models` and registers it under the `mdai-router-1.5b-q4` alias via the Ollama `create` command. The model is not shown as ready until the Ollama registration is verified. Router decisions are written to `.mdai/router_telemetry.json` on a per-project basis; fallback, timeout, error, low-confidence, invalid-selection, empty-selection, and latency metrics are kept.

### ⚡ Performance and Dynamic Tool Management (Performance & Tool Safeguards)
- **Conditional Core Tools:** Unnecessary tools are pruned for pure file-reading/search requests to save tokens and latency; during coding/planning stages, critical tools such as `ExecuteTerminalCommand` are automatically included to prevent the agent from getting stuck.
- **Dynamic Re-Routing:** If the main model needs a tool that is not in the default catalog during a task, it can dynamically request the tool from the Yengi IDE via a `[REQUEST_TOOL: ToolName]` signal.
- **Runaway Thinking & UI Stream Protection:** Automatic truncation and 150ms scroll protection that prevent endless internal monologues from reasoning models and UI (WPF) freezes.

---

# 12. RAG

Works via `RagService` + `CodeChunker`.

- **Code chunking:** The Roslyn-based `CodeChunker` splits code into meaningful chunks at the namespace/type/method/property level; each chunk is hashed, and the same hash is not re-indexed.
- **Embedding:** Via an OpenAI-compatible `/embeddings` endpoint, with settings independent of the main model (default: `https://api.openai.com/v1`, `text-embedding-3-small`).
- **Vector search:** Query → Embedding → Cosine Similarity → Top-K (default `topK = 5`).
- **Cache:** A simple LRU-like cache for embedding results, **maximum 1000 entries** (`_maxCacheSize = 1000`).
- **Index storage:** Per-project JSON index files under `%APPDATA%\mdaiAgent\`.
- **Automatic update:** `ProjectWatcherService` watches changes to `*.cs` files with a 2-second debounce and automatically re-indexes the RAG (the `bin`, `obj`, `.git`, `node_modules`, `.mdai`, `publish` folders and `*.designer.cs`/`*.xaml.cs` files are excluded from watching).

---

# 13. Code Editor

Based on `AvalonEdit`. Features: syntax highlighting, opening/saving multiple files, quick open, inline edit, diff view, automatic formatting. Syntax highlighting definitions (`Themes/Highlighting/*.xshd`) are available for the following languages: **HTML, CSS, JavaScript, Python, C#, Dart, Generic.**

`DiffWindow` shows the old and proposed content side by side before a change is approved. File path, added/removed line statistics, original/proposed content headers, and Accept/Reject actions are all presented in a single review screen.

---

# 14. Terminal, Build, and Project Type Detection

`TerminalService` manages the process lifecycle, and `BuildService` manages build/test/terminal commands. Before running a command or a test, `ToolExecutor`/`AgentVerificationLoopService` detect the project type by looking at file signatures:

| Type | File to look for |
|---|---|
| .NET | `.csproj` / `.sln` |
| Node | `package.json` |
| Flutter | `pubspec.yaml` |
| Python | `*.py` files or `requirements.txt` |

This prevents the AI from running a command against the wrong project type (e.g., running `npm test` in a Flutter project).

---

# 15. Git Integration

`GitService` works via **LibGit2Sharp**. The UI has Refresh, Stage All, Commit, and GitHub Sync operations; Git status is made visible in the file tree and the change list.

---

# 16. Plugin and LSP System

There are **two separate extensibility approaches**:

### 16.1 Language error-checking plugins (`ILanguageErrorCheckerPlugin`)
**14 verified built-in plugins**: Python, C#, JavaScript, HTML/CSS, Java, PHP, Go, C++, Dart, SQL, Ruby, Rust, Kotlin, Swift.

### 16.2 External DLL plugins
`PluginManager.PluginsFolder` → **`%LOCALAPPDATA%\mdaiAgent\Plugins`**. DLLs in this folder are loaded via reflection; a plugin is expected to have a public parameterless constructor and to implement `ILanguageErrorCheckerPlugin`. Metadata via `PluginManifest` (JSON) and external download via `DownloadUrl` are supported — however, the URLs currently used for GitHub-based plugin discovery are still example/placeholder values (see Section 27).

### 16.3 LSP
`LanguageServerService` + `LanguageServerClient` manage language server processes via the `StreamJsonRpc` dependency: startup, reading stdout/stderr, JSON-RPC messaging, diagnostics, document open/change, completion, and notification/request-response.

---

# 17. Voice Command

`VoiceCommandService` provides an STT (speech-to-text) chain that is **independent** of the main AI model. Default: `https://api.groq.com/openai/v1`, model `whisper-large-v3`. Flow: Microphone (NAudio) → Recording → Silence Detection → Audio File → STT API → Transcription → Chat Input. The microphone button works with push-to-talk (press and hold/release) logic.

---

# 18. Provider Architecture

Abstraction: `IAiProvider`, `IProviderService`, `AiProviderFactory`, `ProviderService`, `ProviderFallbackStrategy`, `ProviderCapability`.

| `ActiveProvider` value | Description | Client |
|---|---|---|
| `Anthropic` | Claude API | `AnthropicApiClient` |
| `Google` | Gemini | `GeminiApiClient` |
| `ApiService` | OpenAI-compatible cloud services such as OpenRouter, DeepSeek, Nvidia NIM | `OpenAiCompatibleClient` |
| `LocalModel` | Ollama, LM Studio (default `http://localhost:11434/v1`) | `OpenAiCompatibleClient` |
| `OpenAI` | Direct OpenAI API | `OpenAiCompatibleClient` |

`ProviderFallbackStrategy` automatically switches to a suitable fallback provider when the selected provider doesn't support STT/Embedding (capability-based selection via `ProviderCapability`: a foundation that can be extended for categories such as Tool Calling, Vision, Streaming, JSON, Reasoning, and Embeddings).

---

# 19. Security

- **Path/project boundary:** File paths given to the AI are checked to remain within the project root; unapproved external paths are rejected. This does not block access to backup folders explicitly provided by the user.
- **Approved external folder access:** When the user requests access to an external backup or reference folder, the folder is presented for approval on first use. The approved root can only be read, listed, and searched within the current agent session; write operations additionally go through the diff/approval flow (`ExternalPathAccessManager`). The symlink/junction target is also checked; a target that escapes outside the approved root is rejected.
- **Symlink/junction check:** Not just a textual path check — `ToolExecutor.ResolveSymlinkTarget` and `FileOperationsService.IsSymlink` resolve the **actual target** of symbolic links/junctions to verify whether it is within the project boundaries; if it cannot be resolved, the safe side is chosen and the request is rejected.
- **Terminal risk policy:** Commands are classified into low, medium, high, and critical risk levels. Critical file/disk/Git operations are blocked without even going to the approval window; medium and high-risk commands require user approval even if Safe Automation is off. The approval window offers **Allow / Cancel / Skip / Dry Run** options (`TerminalCommandRiskAnalyzer`, `ToolExecutor.ConfirmResult`).
- **File-change approval:** Changes can be evaluated with Accept/Reject via the diff window.
- **Safe Automation** ("Safe Mode"): requires additional approval for all automation operations.
- **Secret storage:** API keys are not stored in plain text; they are stored encrypted with **Windows DPAPI** (`System.Security.Cryptography.ProtectedData`, `DataProtectionScope.CurrentUser`) in the `%APPDATA%\mdaiAgent\secrets.dat` file (`SecretStore.cs`).
- **Privacy and Telemetry (Opt-Out):** Yengi only collects an anonymous device GUID, OS type, and application version. No personal data, code, or chat information is ever collected. It can be fully disabled with a single click from the Settings page (*"Share anonymous usage statistics"*) (Opt-Out).

> Since mdaiAgent is a desktop agent capable of running terminal commands, its security model should not be reduced to just path/symlink protection. The terminal risk policy, approved network/marketplace sources, plugin integrity verification, and OS-level sandboxing should also be evaluated separately.

---

# 20. Checkpoint / Backup

`CheckpointManager` (atomic file operations) + `CheckpointService` (higher-level API). Also exposed to the AI workflow via the `CreateCheckpoint` / `RollbackToCheckpoint` tools. The UI has a separate **Backup Viewer** window: refresh, show in explorer, inspect files, and roll back to that backup.

---

# 21. Context and Token Management

- Chat history limits: the 200/40 limits noted in Section 7.
- **Token-based history budget** (`ChatFlowService.cs`): **~6,000 tokens** for the local model (`LocalModelHistoryTokenBudget = 6_000`), **~16,000 tokens** for the cloud model (`CloudModelHistoryTokenBudget = 16_000`) — the goal being to constrain context load more aggressively, especially on local models.
- `ContextOptimizerService`: a separate service that optimizes the context/prompt size.
- `ProjectContextDiscoveryService`: discovers information such as project type, key files, and source/test files when the AI first approaches the project.
- `ChatContextMode`: carries the Composer's Auto, File, Selection, Project, and RAG options into ChatFlow. In File mode, a limited window around the cursor is sent instead of the entire active file; automatic Project Context Discovery does not run in explicit File/Selection modes.
- Explicit RAG mode performs a real `RagService.SearchAsync` call and adds `topK=5` results to the prompt; it does not rely on a fake "use RAG" instruction.
- `TokenTrackerService`: tracks token usage/cost along with tool success status, error count, and runtime telemetry; records are stored per-project under `.mdai/token_stats.json` and `.mdai/tool_telemetry.json`. Verification runs are persisted in `.mdai/verification_telemetry.json`, model request/latency/retry information in `.mdai/model_telemetry.json`, and a central model/provider/tool/verification and session summary in `.mdai/telemetry_summary.json`. The last 100 sessions are kept; the Diagnostics panel shows provider failure rate, retry rate, model success rate, and session count. Clearing telemetry preserves token/cost totals.

---

# 22. Project Constitution and Memory

Before every task, mdaiAgent looks for the following files at the project root:

- **`.mdai/constitution.md`** — project-specific architecture/coding rules (there's a template for it in the repo: `constitution.md`; AI persona selection, universal rules, project-specific technology/style/no-touch-zone definitions). If it exists, its rules are strictly followed.
- **Persistent project memory 2.0:** `.mdai/memory.json` now includes `architecture`, `conventions`, and `archived` fields under `schemaVersion=2`; the old `lastTask`/`lastPlan` fields remain backward-compatible. Recording, searching, and archiving are done via the `ReadProjectMemory`, `WriteProjectMemory`, `SearchProjectMemory`, and `ArchiveProjectMemory` tools. Architectural decisions are kept in `.mdai/decisions.json`, and task statuses under `.mdai/tasks/`.

---

# 23. Multi-Language Support (i18n)

UI text is kept in `Resources/Strings.resx` (Turkish), `Resources/Strings.en.resx` (English), and `Resources/Strings.zh.resx` (Chinese). `LocExtension`/`Localization`/`LocalizationManager` resolve the language at runtime using the `{local:Loc Key=...}` syntax in XAML. Language selection is done via Settings and `LanguageSelectionWindow`. The Diagnostics panel also uses resource keys in all three languages.

---

# 24. Project File Structure

```text
mdaiAgent/
├── App.xaml(.cs)
├── MainWindow.xaml(.cs)
├── MainWindow.ChatPanel.cs / .RunDebug.cs / .SessionRecovery.cs / .Terminal.cs   (partial classes)
├── AgentCoreRuntime.cs, IAgentCore.cs        (production-safe runtime boundary — see Section 3.2)
├── AgentVerificationLoopService.cs, AiActionService.cs, AiPlanGenerator.cs
├── ChatFlowService.cs, ChatSession.cs, ChatSessionService.cs, ChatPanelModels.cs
├── ToolDefinitions.cs, ToolExecutor.cs
├── CheckpointManager.cs, PlanModeService.cs, PlanModeHelper.cs, PlanModeResult.cs
├── ProjectContextDiscoveryService.cs, ContextOptimizerService.cs, ProcessQueue.cs
├── GitService.cs, LanguageServerClient.cs, LanguageServerService.cs
├── PluginSystem.cs, SecretStore.cs, Logger.cs, TokenTrackerService.cs
├── Controllers/           → AgentController, ChatController, EditorController, GitController, PluginController, SessionController, TerminalController
├── Interfaces/            → IAiProvider.cs, IProviderService.cs
├── Providers/             → AiProviderFactory, AnthropicApiClient, GeminiApiClient,
│                             NvidiaApiClient, OpenAiCompatibleClient, ProviderFallbackStrategy, ProviderService
├── Services/              → BuildService, CheckpointService, CodeChunker, CommandService,
│                             DelegationService, FileOperationsService, PlanningService,
│                             ProjectWatcherService, RagService, SearchService, UtilityService,
│                             VoiceCommandService, WebService
│   └── Router/            → AiRouter, IAiRouter, RouterContext, RouterDecision
├── Resources/             → Strings.resx, Strings.en.resx
└── UI Windows/Dialogs     → SettingsWindow, PlanWindow, PluginManagerWindow, RagSettingsWindow,
                                 RouterBlacklistWindow, VoiceSettingsWindow, PreviewWindow, DiffWindow,
                                 BackupViewerWindow, KeyboardShortcutsWindow, AskUserDialog,
                                 ConfirmCommandWindow, InputDialog, MessageDialog, LanguageSelectionWindow, AboutWindow
```

---

# 25. Setup and Configuration

**Requirements:** Windows, .NET 8 SDK (`net8.0-windows`, WPF).

```bash
git clone <repo-url>
cd mdaiAgent
dotnet restore
dotnet build BasucuIDE/mdaiAgent.csproj
dotnet run --project BasucuIDE/mdaiAgent.csproj
# Release:
dotnet build BasucuIDE/mdaiAgent.csproj -c Release
```

**Settings file:** `%APPDATA%\mdaiAgent\settings.json` (except for sensitive fields — those are encrypted in `secrets.dat`, see Section 19).

| Group | Fields |
|---|---|
| Main model (ApiService) | `BaseUrl`, `ApiKey`, `Model`, `ApiTimeoutSeconds` |
| Local model | `LocalBaseUrl`, `LocalModel` |
| Major providers | Separate `ApiKey`/`BaseUrl`/`Model` for Anthropic / Google / OpenAI |
| Router | `RouterUseLocalModel`, `RouterEnabled`, `RouterUseMainModel`, `RouterConfidenceThreshold`, `RouterBaseUrl`, `RouterModel`, `RouterApiKey`, `RouterBlacklistedTools` |
| RAG | `RagEnabled`, `RagBaseUrl`, `RagModel`, `RagApiKey` |
| STT | `SttBaseUrl`, `SttModel`, `SttApiKey` |
| General | `SystemPrompt`, `AutoSaveEnabled`, `AutoSaveIntervalSeconds`, `QaAgentEnabled`, `UiAgentEnabled`, `Language`, `DisabledTools`, `IsManualToolManagement` |

### Example usage scenario
```text
User: "The login screen isn't opening in the project. Find the bug and fix it."

1. The active project + chat context are prepared
2. Relevant files are found via SearchCode / FindFiles and read via ReadFile
3. The AI analyzes the problem → applies the change via ReplaceFileContent / CreateOrUpdateFile
4. Verification via BuildProject; if there's an error, RetryPlan → change again
5. RunTests → Verification → Final response
```
(This order is task-dependent; the agent chooses the tools it deems necessary — this exact sequence is not guaranteed.)

### Local Router setup

The local Router option uses Ollama's OpenAI-compatible endpoint. The model-download flow on the Settings screen downloads the GGUF file into `%APPDATA%\\mdaiAgent\\models`, creates a Modelfile, and registers the Router alias via `ollama create`. Ollama must be installed and runnable on the machine for setup to complete. If the download address is still a placeholder in the source, downloading is not started until a real release address is configured.

---

# 26. Technical Dependencies

| Package | Version | Usage |
|---|---:|---|
| AvalonEdit | 6.3.0.90 | Code editor |
| LibGit2Sharp | 0.30.0 | Git |
| Microsoft.CodeAnalysis.CSharp | 4.9.2 | Roslyn — code analysis / RAG chunking |
| Microsoft.Web.WebView2 | 1.0.2210.55 | Embedded web view |
| NAudio | 2.2.1 | Audio capture (voice command) |
| StreamJsonRpc | 2.19.27 | LSP / JSON-RPC |

Framework: **.NET 8 / WPF** (`net8.0-windows`).

---

# 27. Current Status and Known Limitations

- **The plugin marketplace is not yet an operational catalog:** external DLLs go through manifest, identity, version, extension, and SHA-256 validation; downloads are restricted to HTTPS and trusted GitHub hosts; RSA/SHA-256 signed catalog verification is ready. A real catalog endpoint and a versioned distribution service are not yet running.
- **The application update channel has not been configured yet:** the update button in the About window is ready; it will open the release channel once a real GitHub Releases address is connected. Silent automatic updating of the installer has not been implemented yet.
- **Local Router distribution depends on release metadata:** if the GGUF download URL is a placeholder, the actual model download will not start. The model license, base-model license, GGUF checksum, and a versioned release address must be finalized before distribution.
- **Plugin sandboxing is not complete:** external DLLs are still loaded within the main application process. A separate Plugin Host process and an IPC layer are required for OS-level isolation.
- **Test coverage is limited:** there are xUnit tests under `tests/mdaiAgent.Tests` and UI smoke tests under `tests/UiTests`. Automated testing of all UI flows and a broad end-to-end test suite that runs by default have not yet been completed.
- **AI context selection has been improved:** `ProjectContextDiscoveryService` now has targeted search logic for keywords extracted from the message, relevant-file prioritization, and orientation toward test files; relevant code snippet output is also added to the context markdown.
- **Patch conflict recovery:** when the target text is not found in the current file, `ReplaceFileContent` returns a `PATCH_CONFLICT` result without writing to the file, and asks the model to regenerate the patch after obtaining the current context via `ReadFile`.
- **Build/test output summary:** when long terminal output is truncated, the selected error/warning context is preserved, and the total error/warning count is separately reported to the model.
- **Agent status indicator:** when the runtime clears a processing step, the active progress line and the agent status indicator are closed together; a completed operation does not mistakenly appear to still be running on screen.
- **Change review and rollback:** file review/restoration via `DiffWindow`/`GenerateDiff`, automatic backups, and `BackupViewerWindow`; project rollback via the checkpoint system already exists.
- **Startup and tool-mode guidance:** the three-step Quick Start on the empty editor screen, Manual/Automatic tool mode descriptions, Router constraints, and blacklist settings already exist.
- **Editor Go to Definition:** in LSP-supported files such as C# and Python, F12 opens the target file and line in the editor using the existing LSP definition request.
- **Editor references/symbols:** `Shift+F12` shows the LSP references of the current symbol, and `Ctrl+T` shows the symbols of the active file in the existing results list; double-clicking a result navigates to the relevant line.
- **Diagnostics presentation:** LSP and plugin diagnostics are now listed by file, line, and message in the Diagnostics tab; double-clicking a record navigates to the relevant code location.
- **Context file hints:** a source file path explicitly mentioned in the user's message is prioritized by context discovery and clearly shown in the context markdown.
- **Task documents:** `.txt` and `.md` task files can be read directly by context discovery; the raw model stream is not forwarded to the terminal during plan generation, and the plan result proceeds into the implementation steps.
- **Implementation/test pairing:** when an implementation file such as `OrderService.cs` is directly referenced, its conventional counterpart `OrderServiceTests.cs` is also prioritized in context selection.
- **RAG stale index cleanup:** old chunks of a changed file are replaced during re-indexing; chunks of deleted or no-longer-found files are cleaned up from the RAG index.
- **Context deduplication:** RAG chunks that fall within the same line range as Context Discovery snippets are not added to the prompt a second time; non-overlapping RAG context is preserved.
- **Long tool output:** the full version of truncated build/test/terminal output is kept in a temporary spool file; the model can read only the line range it needs via `ReadToolOutput`. Outputs are cleaned up under a 24-hour and 50-file limit.
- **Active project path:** relative file/folder tool paths are resolved against the active project root selected by the user, not against the folder where mdaiAgent is running.
- **Router confidence threshold:** the Router confidence setting normalizes inputs like `0.8`, `0,8`, `80`, or similar into the `0..1` range; culture-related malformed values such as `8.0` are safely corrected at runtime.
- **Router latency:** no router network call is made for short chit-chat that doesn't require a tool, such as a greeting; real router calls fall back to the fallback tool list after a 25-second timeout.
- **Router telemetry:** fallback, timeout, error, low-confidence, invalid-selection, empty-selection, and latency metrics are kept per-project under `.mdai/router_telemetry.json`; the Router Context is not yet extended with the active file/chat history.
- **UI decomposition is ongoing:** `MainWindow.xaml.cs` still contains a large number of event handlers. Although it is split into partial classes, full MVVM separation and broader UI/event decomposition have not been completed.
- **Router scope is deliberately singular:** multi-teacher consensus is out of scope. If a reliable selection cannot be obtained while the Router is active, a fallback list of nine tools is used; the Router is disabled in manual mode, where the user's selection has absolute priority.
- **Security policies are extensible:** path/symlink protection, approved external folder access, terminal risk classification, HTTPS/host allowlisting, plugin integrity verification, and DPAPI secret storage exist; however, the network policy and the Plugin Host sandbox still need to be completed separately.

---

# 28. Completed Development Phases

This section is a brief status summary of major completed developments. Technical details are described in the relevant sections.

1. **Unified Agent Runtime:** Tool dispatch, Verification 2.0, checkpoint/rollback, and Timeline EventBus flows run through the production-safe `AgentCoreRuntime`.
2. **Verification 2.0:** Build, test, static analysis, changed-file review, automatic fixing, and a final summary flow that distinguishes warnings from failures are all in place.
3. **Centralized agent telemetry:** Model latency, retry rate, verification success rate, provider failure rate, and a per-project summary of the last 100 sessions are kept persistently; shown in the Diagnostics panel.
4. **Persistent Memory 2.0:** A versioned memory schema, architecture/conventions records, search, archiving, and backward compatibility with the old format are all in place.
9. **Context-aware file and test selection:** relevant files and test targets are prioritized based on keywords extracted from the message and file/name matches; code snippets are also included in the context markdown.
10. **Router tool selection and fallback:** user selection in manual mode, a narrowed tool catalog in automatic Router mode, a blacklist filter, and a safe fallback list are all implemented.
11. **Production security foundation:** approved external backup access, symlink target verification, terminal risk policy, HTTPS/host allowlisting, mandatory SHA-256, and signed catalog verification are all in place.
12. **Multi-language infrastructure:** Turkish, English, and Chinese resource files, and moving the main window's hardcoded language assignments to resource keys, are complete.
13. **Safe patch conflict recovery:** missing or stale patch targets produce an explicit conflict result without modifying the file; whitespace-only targets are rejected while valid whitespace replacement content is preserved.
14. **Compact build/test output:** in truncated terminal output, error and warning counts are summarized so the model can decide quickly.
15. **Status flow cleanup:** when an agent operation ends, the active progress and status indicators are cleared together.
16. **Go to Definition:** the F12 flow for jumping to a symbol definition via LSP has been added.
17. **Find References and symbol search:** LSP references and document symbols are wired into the existing search results surface.
18. **Diagnostics presentation:** existing diagnostics data is wired into a file/line/message list, with direct navigation to the code location added.
19. **Tool output spool:** long tool output is stored with a temporary ID, can be read by line range, and is automatically capped.
20. **Cancellation Threading + Loop-Guard:** `CancellationToken` propagation to services was added, along with a `maxSameToolRetry = 3` Loop-Guard mechanism in `ChatFlowService` that stops consecutively failing tool calls.
21. **Extended Tool Output Spool:** the `ToolOutputStore.SaveIfTruncated` mechanism was also connected to terminal commands (`CommandService`) and web requests (`WebService`).
22. **Formatted Verification Signature:** a fixed-format verification signature containing `✓ Build / ✓ Test / ✓ Static Analysis` steps was added to the end of AI responses.
23. **Modular Controller Layer:** domain-based controllers (`ChatController`, `AgentController`, etc.) were created under the `Controllers/` folder to reduce the UI logic's dependency on `MainWindow`.
24. **Type-Safe Decision Log:** architectural decisions were made type-safe with `DecisionRecord`, and leftover temporary files at the project root were cleaned up.
25. **Chat Panel 2.0:** the unified Composer workspace card, removable `@mention` chip tags (`[ Foo.cs ✕ ]`), the `cmbContextMode` context selector, the `cmbComposerModel` quick model selector, the `🧠 Working` activity card inside the AI bubble, automatic chat titling, and the `Apply ↓` diff-preview action added to code blocks are all complete.
26. **Context Selector accuracy:** Active File cursor-window context, explicit Selection/Project behavior, genuine explicit RAG retrieval, and duplicate context reduction flows are complete.
27. **Router telemetry:** per-project fallback, timeout, error, low-confidence, invalid-selection, empty-selection, and latency metrics have been made persistent.
28. **Local Router deployment:** configurable Router endpoint/model selection, GGUF download, Ollama `create` registration, readiness verification, and legacy-setting migration flows are complete.
29. **Update and model identity foundation:** an update button was added to About; the Router model, GGUF file, and endpoint were centralized via `RouterModelInfo`.

---

# 29. Remaining Development Areas

1. **A real marketplace endpoint:** a trustworthy, versioned distribution service with revocation support must be operated for the signed catalog.
2. **Plugin Host sandbox:** DLL plugins should be taken out of the main WPF process and run in a separate process with IPC and permission boundaries.
3. **Broad UI/E2E test suite:** language switching, tool selection, Router blacklist, Diagnostics, and plugin flows should be automatically tested across different cultures/locales.
4. **UI architecture:** the event handlers and business logic inside `MainWindow.xaml.cs` should be split into smaller service/view-model boundaries.
5. **Localization audit:** remaining hardcoded user-facing messages in the terminal and helper services should be moved to resource keys; missing-key checking across the three languages should be automated.
6. **Release/update distribution:** the GitHub Releases manifest, version comparison, installer download/run flow, and safe rollback should be completed.
7. **Router model release package:** a real GGUF URL, model/base-model licenses, checksum, Ollama model-alias migration, and a benchmark report should be published.
8. **Router Context measurement:** once `.mdai/router_telemetry.json` data is collected from real user tasks, if the fallback threshold is exceeded, low-cost information such as the active file and the last agent step should be added to the Router.

---

## Conclusion

Yengi's architecture brings together four core ideas: **AI ↔ Agent (ChatFlowService) ↔ {Tools, RAG, Planning} ↔ Verification ↔ Real Project.** In short, describing Yengi merely as an "AI code-writing application" falls short — the real idea is to build an **agent runtime + tool execution + project context + verification infrastructure that connects an AI model to a real software development workspace.** The union of the UI, provider, tool, RAG, router, planning, delegation, terminal, Git, LSP, plugin, and verification layers within a single desktop application is what defines Yengi's core architectural character.

---

## 👤 About the Creator

Yengi was designed, coded, and refined by a **single developer** — a teacher by profession — who wanted a smarter way to work with AI on real software projects.

No team. No funding. No dedicated office hours.  
Just curiosity, late nights, and a belief that powerful developer tooling shouldn't require a corporate budget.

Every feature in Yengi — the fine-tuned local router, the Blender & Unity copilots, the self-healing agent loop, the scrollbar diagnostics — was built from scratch, one commit at a time, alongside a full-time teaching career.

> *"Yengi" means the reward you earn after long, patient effort. That's exactly what this project is.*

If Yengi saves you even one hour of work, consider leaving a ⭐ on GitHub — it genuinely means a lot to a solo builder.

---

## License / Contributing

This project is licensed under **AGPL-3.0 (GNU Affero General Public License v3.0)**.

It is a free and open-source project. You can use, inspect, and contribute to the project. If you'd like to support the developer:
- ☕ [Buy Me A Coffee](https://buymeacoffee.com/mdaiyazilim)
- 🧠 [Get the Yengi Router Model via Gumroad](https://gumroad.com)

---

*This README was prepared by scanning the source code of the Yengi project and verifying the numeric/technical claims within it directly against the code.*
