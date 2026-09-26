Read this in: [🇹🇷 Türkçe](README.tr.md) | 🇺🇸 English

---

<p align="center">
  <img src="assets/banner.png" alt="Yengi Banner" width="100%"/>
</p>

# 🚀 Yengi AI IDE

> **Y**our **E**ngineering **N**exus, **G**enerative **I**ntelligence.  
> *"Yengi: The rewarding milestone reached after long, dedicated effort."*

[![Build & Test](https://github.com/mdaiWorks/yengi/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/mdaiWorks/yengi/actions/workflows/build-and-test.yml)
[![Version: v1.06](https://img.shields.io/badge/Release-v1.06-blue.svg)](https://github.com/mdaiWorks/yengi/releases)
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
- 🧊 **Blender & Unity Copilots**: Live two-way integration scripts to manipulate 3D scenes and game engine objects directly via AI instructions.
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
