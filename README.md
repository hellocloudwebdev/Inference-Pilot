<div align="center">

# 🌌 ProviderPilot

**The Ultimate Inference Routing Engine for Claude Code & Codex**

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%20WPF-0078D7?style=for-the-badge&logo=windows&logoColor=white" alt="Platform: Windows WPF" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 8.0" />
</p>

*Seamlessly switch models, manage API keys, and route inference traffic with a modern, elegant GUI.*

[**Explore Features**](#-key-features) · [**Installation**](#-getting-started) · [**How It Works**](#-how-it-works)

</div>

<br/>

---

## 📖 Overview

**ProviderPilot** is a sophisticated Windows Presentation Foundation (WPF) application engineered for AI developers. It provides a centralized, visual control panel to manage inference providers and model selections for both **Claude Code** and **Codex**. 

Instead of manually hunting down and editing JSON and TOML files scattered across your system, ProviderPilot intelligently modifies your configuration surfaces, applies environment variables, and safely backs up your settings before any changes take effect.

## ✨ Key Features

<table>
  <tr>
    <td width="50%">
      <h3>🎯 Precision Routing</h3>
      <p>Configure model selections granularly. Assign different providers and models for default tasks, Opus, Sonnet, Haiku/background, and subagents.</p>
    </td>
    <td width="50%">
      <h3>🔌 Multi-Provider Management</h3>
      <p>Easily add and manage endpoints, API keys, HTTP headers, and query parameters for any inference provider (OpenRouter, Azure, NVIDIA NIM, Google, etc.).</p>
    </td>
  </tr>
  <tr>
    <td width="50%">
      <h3>🔄 Dynamic Model Discovery</h3>
      <p>Automatically fetch and load model catalogs from supported provider <code>/models</code> endpoints with zero manual configuration.</p>
    </td>
    <td width="50%">
      <h3>🛡️ Safe & Secure</h3>
      <p>Includes a robust <b>Preview</b> mode. Automatically creates safe, versioned backups of your configurations before applying changes.</p>
    </td>
  </tr>
</table>

## 📸 Interface & Workflow

> **Note:** Insert high-resolution application screenshots here to showcase the beautiful WPF UI.

The interface is meticulously designed with a sleek left sidebar dividing your workflow into three intuitive workspaces:
1. **Provider Settings:** Define your infrastructure.
2. **Claude Code Configuration:** Route your AI personas.
3. **Codex Configuration:** Manage execution engines.

## 🚀 Getting Started

### Prerequisites
- Windows 10 or 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Installation & Build

1. Clone this repository to your local machine:
   ```bash
   git clone https://github.com/hellocloudwebdev/provider-pilot.git
   cd provider-pilot
   ```

2. Build the project using the .NET CLI:
   ```bash
   dotnet build
   ```

3. Launch the application:
   ```bash
   .\bin\Debug\net8.0-windows\ProviderPilot.exe
   ```

## 🧠 How It Works

ProviderPilot acts as an intelligent orchestrator for your AI development environment by managing specific configuration files and system settings.

### What it Configures

| Target Engine | Configuration Path | Managed Elements |
| :--- | :--- | :--- |
| **Claude Code** | `%USERPROFILE%\.claude\settings.json` | Updates the `env` block and multi-provider routing settings. |
| **Codex** | `%USERPROFILE%\.codex\config.toml` | Generates a custom `model_provider` and manages `profiles.*` entries. |
| **Windows OS** | System Environment Variables | Sets API keys (e.g., `OPENROUTER_API_KEY`) seamlessly at the user level. |

### Configuration Workspaces

- **Provider Settings**: The foundation. Configure endpoints, auth env vars, model catalogs, and optional headers. These settings contain no specific Claude Code or Codex choices—they just define your available infrastructure.
- **Claude Code Configuration**: The router. Choose which provider and model to use for each Claude Code persona. *Example: Route Opus to NVIDIA NIM, and Haiku to OpenRouter.*
- **Codex Configuration**: The execution engine. Choose the specific provider and model for Codex operations independently of Claude Code.

## 📂 System Paths & Storage

ProviderPilot operates transparently, keeping your data local and secure.

- **App Settings**: `%APPDATA%\ProviderPilot\profiles.json` *(Stores UI state, endpoints, and configs. API keys are never saved here for security.)*
- **Automatic Backups**: `%APPDATA%\ProviderPilot\backups\` *(Created automatically before every apply action.)*

## ⚠️ Important Notes & Best Practices

> [!WARNING]
> Always restart your Claude Code or Codex terminals after applying changes that modify Windows user environment variables to ensure the new variables are loaded into your session.

- **Anthropic Compatibility:** For complex multi-provider routing in Claude Code, using an Anthropic-compatible LLM gateway is highly recommended. ProviderPilot natively sets `ANTHROPIC_BASE_URL` and maps model aliases appropriately.
- **Codex Isolation:** To prevent conflicts, ProviderPilot creates unique provider IDs prefixed with `providerpilot_` and strictly avoids overwriting built-in Codex provider definitions.
- **Reasoning Models:** ProviderPilot intentionally leaves Codex reasoning settings untouched. Manage reasoning capabilities directly from Codex or Claude Code.
- **Security First:** API keys are completely optional. If left blank, existing user environment variables remain undisturbed.

<br/>

---

<div align="center">
  <strong>ProviderPilot</strong> • Built with ❤️ for AI developers.
  <br/>
  <a href="https://github.com/yourusername/provider-pilot/issues">Report a Bug</a> • <a href="https://github.com/yourusername/provider-pilot/pulls">Request a Feature</a>
</div>
