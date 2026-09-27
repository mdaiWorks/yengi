Read this in: [🇹🇷 Türkçe](README.tr.md) | 🇺🇸 English

---

<p align="center">
  <img src="assets/banner.png" alt="Yengi Banner" width="100%"/>
</p>

# 🚀 Yengi AI IDE

> **Y**our **E**ngineering **N**exus, **G**enerative **I**ntelligence.  
> *"Yengi: The rewarding milestone reached after long, dedicated effort."*

[![Build & Test](https://github.com/mdaiWorks/yengi/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/mdaiWorks/yengi/actions/workflows/build-and-test.yml)
[![Version: v1.07](https://img.shields.io/badge/Release-v1.07-blue.svg)](https://github.com/mdaiWorks/yengi/releases)
[![Framework: .NET 10 LTS](https://img.shields.io/badge/Framework-.NET%2010%20LTS-purple.svg)](https://dotnet.microsoft.com/)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6.svg)](https://microsoft.com/windows)
[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL--3.0-green.svg)](LICENSE)
[![HuggingFace: Yengi Router](https://img.shields.io/badge/HuggingFace-yengi--router%3A1.5b-FFD21E.svg)](https://huggingface.co/)

**Yengi is a free, open-source Windows desktop AI IDE powered by .NET 10 (LTS). It combines local LLMs (Ollama, Qwen, DeepSeek) and cloud APIs with autonomous tool execution, self-healing build verification, and live 3D/game engine copilots.**

---

## 🎬 Visual Showcase

| 🌐 Web App & Image Studio | 🧊 Live 3D Blender Copilot |
| :---: | :---: |
| ![Web App & Image Generation](assets/gorselUretimvetetris.gif) | ![Blender Copilot](assets/Blender.gif) |

| 🎮 Unity Game Engine Copilot | 🖥️ Modern IDE Workspace |
| :---: | :---: |
| ![Unity Copilot](assets/unity.gif) | ![Main Workspace](assets/arayuz.png) |

▶️ **[Watch the YouTube Video Walkthrough & Live Tetris Demo](https://youtu.be/l0tdhvCmwNI)**

---

## 🧠 Autonomous Agent Loop Architecture

Yengi goes beyond a standard AI chat box. It runs an end-to-end **Plan → Tool Call → Code Mutation → Verification → Auto-Fix** cycle directly on your workspace.

```mermaid
flowchart TD
    A["👤 User Prompt / Goal"] --> B["📝 Plan Mode & Task Breakdown"]
    B --> C["🤖 Agent Orchestrator & Tool Selection"]
    C --> D{"⚡ Tool Execution"}
    D -->|"File Mutation"| E["📝 Create / Edit Files"]
    D -->|"Terminal / Build"| F["⚙️ Run Build & Test Commands"]
    D -->|"Copilot Integration"| G["🧊 Blender / Unity Automation"]
    E --> H{"🔍 Verification Loop"}
    F --> H
    H -->|"✅ Success"| I["🎉 Complete & Report Result"]
    H -->|"❌ Build Error / Failed Test"| J["🩹 Self-Healing Repair Agent"]
    J --> C
```

---

## 🎨 Two-Stage 3D Asset & Component Architect Pipeline

Yengi features a specialized **2-Stage Autonomous AI Pipeline** for Blender 3D and Unity Engine copilots. Instead of producing naive primitive shapes, Yengi separates artistic design reasoning from code synthesis:

```mermaid
flowchart TD
    A["👤 User Request<br/>('Make a low poly oak tree' / 'Build camera follow script')"] --> B{"⚙️ 3D Prompt Architect Enabled?"}
    B -- "YES (Default - Toggleable)" --> C["🎨 Stage 1: 3D Asset/Component Architect"]
    C --> D["📝 Rich 3D Blueprint Specs<br/>(Metrics, Material Hex Colors, Polygon Budget, Topology)"]
    D --> E["⚡ Stage 2: Blender (bpy) / Unity (C#) Code Synthesizer"]
    B -- "NO (Direct Prompting)" --> E
    E --> F["🚀 Live TCP Socket Execution & Auto-Undo Checkpoint"]
    F --> G["🧊 Blender 3D Viewport / 🎮 Unity Scene Update"]
    G --> H{"🔍 Verification & Execution Check"}
    H -- "✅ Success" --> I["📸 Capture Viewport Snapshot & Present Result"]
    H -- "❌ Exception / Compile Error" --> J["🩹 Self-Healing Repair Agent Loop"]
    J --> E
```

- **Stage 1 (3D Artist / Architect)**: Deconstructs raw user requests into anatomical specifications, metric dimensions, flat shading rules, and Principled BSDF color palettes.
- **Stage 2 (Code Generator)**: Reads the generated blueprint and scene context RAG data to synthesize 100% bug-free `bpy` Python or Unity Editor C# code.
- **Toggleable Option**: Can be easily enabled/disabled in **Workspace Settings** (`⚙️`) for full developer control.

---

## 👁️ Live Viewport Vision — AI Eyes on Your 3D Scene

When using a **multimodal-capable model** (GPT-4o, Claude 3.5 Sonnet, Gemini 1.5/2.0), Yengi can literally **see** your Blender 3D viewport in real-time and make visually-informed corrections:

```mermaid
flowchart LR
    A["🧊 Blender OpenGL Viewport"] -->|"OpenGL Snapshot"| B["📸 yengi_viewport_preview.png<br/>(AppData/Local/Temp)"]
    B -->|"Base64 Encode + TCP Socket"| C["🖥️ Yengi AI Core"]
    C -->|"Vision API Call"| D["👁️ Multimodal Model<br/>(GPT-4o / Claude 3.5 / Gemini)"]
    D -->|"Visual Analysis"| E{"🔍 Scene OK?"}
    E -- "✅ Looks correct" --> F["🎉 Present Result to User"]
    E -- "❌ Wrong geometry / colors" --> G["🩹 Generate Corrective bpy Code"]
    G -->|"Auto-execute"| A
```

1. **Capture** — Blender captures an OpenGL viewport snapshot → saved to `yengi_viewport_preview.png`
2. **Transmit** — Image is Base64-encoded and sent to Yengi via live TCP socket (port 8181)
3. **Inspect** — The multimodal AI model examines the rendered 3D scene visually
4. **Correct** — If geometry, materials, or proportions are off, the AI auto-generates and executes corrective `bpy` code

> [!TIP]
> Use the **"Viewport Resmi Al"** button in Blender's N-Panel (press `N` in viewport) to trigger a snapshot at any time, or let the Two-Stage Pipeline capture it automatically after each operation.

---

## 💡 Why Yengi?

Yengi was created around a simple idea: **your AI development environment shouldn't depend on a single provider, model, or usage quota.**

Use local open-weights models through Ollama, connect your own API keys (Claude, OpenAI, Gemini), or leverage Yengi's custom fine-tuned local router. Build with autonomous agent workflows while maintaining total control over your models, tools, and privacy.

---

## ✨ Key Features

- 🧠 **Custom 1.5B Local AI Router**: Fine-tuned 1.5B routing model designed to select the appropriate AI workflow and tool context autonomously.
- 🎛️ **4 Specialized Development Modes**: **Code IDE**, **Image Generation Studio**, **Blender 3D Copilot**, and **Unity Engine Copilot**.
- 🔒 **Local-First & Model Agnostic**: Connects seamlessly to **Ollama (Qwen 35B, DeepSeek-R1)**, Claude 3.5, OpenAI, or Gemini. Keeps your project data on your machine when choosing local providers.
- 🛡️ **Self-Healing Verification Loop**: Automatically runs build/syntax checks (`dotnet build`, `npm test`, Python linters) and repairs errors autonomously.
- 🔄 **1-Click Auto-Updates**: Integrated GitHub Releases API updater detects and installs new setup releases automatically.
- 🧊 **Blender & Unity Copilots**: Live two-way JSON-RPC integration with Scene RAG, Two-Stage Architect Pipeline, self-healing loop, and auto-undo checkpoints.
- 👁️ **Live Viewport Vision**: Multimodal AI models (GPT-4o, Claude 3.5, Gemini) can **see** your Blender scene via OpenGL snapshots and autonomously correct geometry, materials, or proportions in a closed feedback loop.
- ⚡ **Built with .NET 10 (LTS)**: High-performance WPF architecture backed by **200+ passing unit tests**.
- 🧰 **30+ Native Agent Tools**: File system search, Git operations, terminal execution, RAG context indexing, and web browser previews.

---

## 📖 Detailed Documentation & Full Tool Reference

For a complete breakdown of all **30+ AI Agent Tools**, internal Verification Loop mechanisms, RAG architecture, and UI button references, see the **[Full Technical Documentation (DOCS_FULL.md)](DOCS_FULL.md)** | **[Detaylı Türkçe Dokümantasyon (DOCS_FULL.tr.md)](DOCS_FULL.tr.md)**.

---

## 🚀 Quick Start

### Option A: Install Executable (Recommended)
1. Download the latest `Yengi_Setup.exe` installer from [GitHub Releases](https://github.com/mdaiWorks/yengi/releases/latest).
2. Run the installer and launch Yengi.
3. Configure your preferred model in **Settings** (Ollama, Anthropic, OpenAI, or Gemini) and start building!

### Option B: Run from Source (.NET 10 SDK)
```bash
# Clone the repository
git clone https://github.com/mdaiWorks/yengi.git
cd yengi

# Build and run with .NET 10
dotnet run --project BasucuIDE/mdaiAgent.csproj
```

---

## 👤 Creator's Note & Philosophy

> *"I am a teacher, not a corporate C# developer."*

Yengi was built by acting as an **Architect and AI Orchestrator**. Using AI tools (Google Antigravity & GitHub Copilot) for code generation while designing the architecture, verification loops, security boundaries, and local router model myself.

Yengi is **100% Free, Open Source, and Telemetry-Free**. If Yengi inspires your workflow, consider giving it a ⭐ **Star on GitHub**!

---

## 📄 License

Distributed under the [AGPL-3.0 License](LICENSE).
