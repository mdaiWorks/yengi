# 📖 Yengi AI IDE — Full Technical Documentation & Tool Reference

Read this in: [🇹🇷 Türkçe](DOCS_FULL.tr.md) | 🇺🇸 English

---

Welcome to the comprehensive technical reference for **Yengi AI IDE**. This document contains deep architectural details, tool definitions, verification loop specifications, RAG indexing mechanisms, and UI button references for power users and contributors.

---

## Table of Contents

- [1. Architecture Overview](#1-architecture-overview)
- [2. Complete Native Tool Registry (30+ Tools)](#2-complete-native-tool-registry-30-tools)
- [3. Verification Loop (Self-Healing System)](#3-verification-loop-self-healing-system)
- [4. RAG & Code Search Engine](#4-rag--code-search-engine)
- [5. Sub-Agent & Delegation Protocol](#5-sub-agent--delegation-protocol)
- [6. Local AI Router Model (1.5B)](#6-local-ai-router-model-15b)
- [7. Language Server Protocol (LSP) Entegration](#7-language-server-protocol-lsp-entegration)
- [8. Security Sockets & Path Traversal Guards](#8-security-sockets--path-traversal-guards)

---

## 1. Architecture Overview

Yengi is built on **.NET 10 (LTS)** using WPF. It follows an **Event-Driven Agentic Execution Pipeline**:

1. **User Goal Ingestion**: User enters prompt or selects a file task.
2. **Plan Mode Service**: Break down high-level intent into sequential logical steps.
3. **Tool Dispatcher**: Execute non-blocking file, terminal, or copilot operations.
4. **Verification Engine**: Capture runtime diagnostics, compiler outputs, or test results.
5. **Auto-Fix Loop**: Feed failure tracebacks back into the LLM context automatically for targeted code mutation.

---

## 2. Complete Native Tool Registry (30+ Tools)

| Tool Name | Risk Level | Description |
| :--- | :---: | :--- |
| `ReadFile` | Low | Reads raw text content of a file within project scope. |
| `CreateOrUpdateFile` | Medium | Creates a new file or updates an existing file with path validation and backup creation. |
| `ReplaceInFile` | Medium | Performs targeted line/chunk replacement in existing files. |
| `DeleteFile` | High | Safely removes a file with user approval. |
| `ListDirectory` | Low | Lists files and subfolders within a directory. |
| `SearchProject` | Low | Fast regex/string search across the workspace code tree. |
| `ExecuteTerminalCommand` | High | Runs non-blocking terminal processes with output streaming and 30s timeout safety. |
| `BuildProject` | Medium | Detects project type (C#, Node.js, Python, Flutter, Rust) and runs appropriate build command. |
| `RunTests` | Medium | Executes automated test suites (`dotnet test`, `npm test`, `pytest`). |
| `Generatelmage` | Low | Generates AI images (via Pollinations API) and saves to `generated_images/`. |
| `GitStatus` | Low | Returns current git status and changed files. |
| `GitCommit` | Medium | Stages and commits local changes with custom commit messages. |
| `GitPush` | High | Pushes committed changes to remote repository. |
| `GetProjectContext` | Low | Aggregates workspace file tree, tech stack, and key configuration entries into AI context. |
| `AskUserQuestion` | Low | Renders interactive WPF dialog for clarifying ambiguous requirements. |

---

## 3. Verification Loop (Self-Healing System)

When `BuildProject` or `RunTests` fails:
1. The error log is captured without truncation.
2. The **Repair Agent** is spawned with exact stack traces.
3. Code changes are proposed specifically addressing the broken assertions or NullReference Exceptions.
4. Build/Test is re-executed until `0 errors` are reported.

---

## 4. RAG & Code Search Engine

- **Indexing**: Extracts AST nodes, class definitions, function signatures, and Markdown documentation.
- **Search**: Hybrid semantic and keyword indexing enables lightning-fast symbol lookup across multi-thousand-file repositories.

---

## 5. Sub-Agent & Delegation Protocol

Yengi supports spawning concurrent sub-agents for isolated tasks:
- **Research Agent**: Explores repository files without modifying disk state.
- **Planner Agent**: Generates structured step-by-step implementation plans.
- **Coder Agent**: Performs high-speed multi-file modifications.

---

## 6. Local AI Router Model (1.5B)

Yengi includes a custom fine-tuned 1.5B parameter local LLM (`yengi-router:1.5b` on HuggingFace / Ollama).
- **Purpose**: Evaluates incoming user prompts and selects the optimal tool sequence with 95%+ confidence before invoking larger cloud/local models.
- **Benefits**: Cuts down latency and token costs by routing simple tasks locally.

---

## 📄 License

Distributed under the [AGPL-3.0 License](LICENSE).
