<p align="center">
  <img src="src/ClipSync.App/Resources/app.png" width="128" alt="ClipSync icon"/>
</p>

<h1 align="center">ClipSync for Windows</h1>

<p align="center">
  <b>Phone verification codes & clipboard → Windows toast, one click to copy.</b><br/>
  <a href="README.md">简体中文</a> ·
  <a href="README.en.md">English</a> ·
  <a href="README.ja.md">日本語</a>
</p>

---

ClipSync is a self-hosted, cross-platform message sync tool. This repository is the Windows desktop client, built with **.NET 8 + WPF (C#)**.

Core scenario: **when you receive a verification code on your phone or copy something, a Windows toast pops up instantly with one-click copy to your clipboard; conversely, anything you copy on Windows is synced to other devices in real time.**

No third-party push services are involved — traffic goes through your own WebSocket relay, with optional end-to-end encryption, so your privacy stays under your control.

---

## ✨ Core Features

| Module | Description |
|------|------|
| 📩 **SMS code toast** | When the phone receives a verification code, a Windows notification pops up in the top-right corner; copy the code or the full text with one click |
| 📋 **Two-way clipboard sync** | Text/images copied locally are uploaded automatically; content copied on other devices is written to the local clipboard automatically |
| 🛡️ **End-to-end encryption (optional)** | AES-256-GCM encryption with PBKDF2-HMAC-SHA256 (200,000 iterations) key derivation; the server only forwards ciphertext |
| 🔔 **Toast banners** | Don't steal focus, auto-dismiss after 5 seconds, stack up to 3; verification codes are smart-detected and exposed via a dedicated button |
| 🖥️ **Tray resident** | Closing the main window sends it to the system tray; left-click to restore, right-click for the menu; supports minimizing to tray |
| 👥 **Online device list** | The home page shows real-time online devices under the same account, including platform, IP and sync capabilities |
| 🚀 **Auto-start on boot** | Enable with one click from the installer or the app (writes to `HKCU\...\Run`) |
| 🧭 **First-run wizard** | Guides users through server address, account/password and end-to-end encryption setup in one go |
| 📜 **History** | Locally persists the latest 500 messages, with search, copy, delete and category filtering |
| 🔄 **Auto reconnect** | Reconnects automatically on network flakiness; when the token expires it is re-exchanged using the locally saved account/password |
| 🔒 **Single instance** | Guarded by a Global Mutex; re-launching brings the already-running main window to the foreground |

---

## 🖼️ UI Preview

| Home | SMS History | Clipboard History |
|------|----------|------------|
| Connection status, account, encryption, sync toggles, online devices, recent messages | Reverse chronological order; copy code / delete supported | Text and image preview; copy / delete supported |

---

## 📦 Download & Install

Head to [GitHub Releases](https://github.com/JH-Clipsync/ClipSync-Windows/releases) and pick the architecture you need:

| File | Use case |
|------|----------|
| `ClipSync-Setup-<version>-win-x64.exe` | Most Intel/AMD 64-bit Windows PCs (recommended) |
| `ClipSync-Setup-<version>-win-arm64.exe` | Surface Pro X, Snapdragon laptops and other ARM64 devices |
| `ClipSync-<version>-win-x64.zip` | Portable green build (unzip and run; no registry writes) |

> The installer is a **self-contained deployment** that bundles the .NET 8 runtime, so **the target machine does not need .NET installed separately**. By default it installs per-user to `%LocalAppData%\Programs\ClipSync` and does not require administrator privileges.

System requirements: **Windows 10 1809 (17763) or later / Windows 11**.

---

## 🚀 Quick Start

1. Install and launch ClipSync
2. The first run opens the onboarding wizard:
   - Enter the **server address** (e.g. `192.168.1.10:8080`, supports `ws://` / `wss://`)
   - Enter the **username / password** assigned by the admin (a token is exchanged automatically on first connect)
   - Choose whether to enable **end-to-end encryption** and enter the sync password (both ends must match)
3. Click "Connect" — the tray icon turns green when connected
4. On your phone ([ClipSync-Android](https://github.com/JH-Clipsync/ClipSync-Android)) log in with the same account, and syncing begins

---

## 🧩 Project Architecture

```
ClipSync-Windows/
├── src/
│   ├── ClipSync.Core/              # Cross-platform core (protocol/crypto/net/storage)
│   │   ├── Crypto/                 # AES-256-GCM + PBKDF2 (E2EECrypto / PayloadCipher)
│   │   ├── Net/                    # WSClient / AuthClient / ServerAddress / ConnectionState
│   │   ├── Protocol/               # Models (SyncMessage / MessagePayload) + SmsCodeExtractor
│   │   ├── Storage/                # SettingsStore / HistoryStore / AppPaths
│   │   └── Diagnostics/            # Daily-rolling logs (Log)
│   └── ClipSync.App/               # WPF client
│       ├── App.xaml / App.xaml.cs  # Entry (single instance, tray, Dispatcher injection)
│       ├── MainWindow.xaml(.cs)    # Main window (left nav + content area)
│       ├── GlobalUsings.cs         # Global usings
│       ├── Services/
│       │   ├── ClipboardMonitor.cs # 600ms polling + image compression + double de-dup
│       │   ├── ClipboardWriter.cs  # Writes remote messages into the local clipboard
│       │   └── AutoStartService.cs # Auto-start on boot (registry)
│       ├── UI/
│       │   ├── HomeView.cs         # Home (status cards + online devices + recent messages)
│       │   ├── HistoryView.cs      # SMS / clipboard history
│       │   ├── SettingsView.cs     # Settings page
│       │   ├── OnboardingWizard.cs # First-run onboarding
│       │   ├── ToastWindow.xaml(.cs) # Top-right toast banner
│       │   ├── InfoToastWindow.xaml.cs # Info toasts (quick action for verification codes, etc.)
│       │   ├── ToastManager.cs     # Multi-toast stacking manager
│       │   ├── ImagePreviewWindow.xaml.cs # Image viewer
│       │   ├── AppColors.cs        # Global palette
│       │   ├── AppDialog.cs        # Common dialog
│       │   ├── PasswordInput.cs    # Password input control
│       │   ├── FocusBehavior.cs    # Attached behavior for auto-focus
│       │   └── SmsPayloadSanitizer.cs # SMS message cleaning / redaction
│       └── Resources/
│           ├── app.png             # App icon (PNG, used in README/notifications)
│           └── app.ico             # App icon (ICO, used in windows/tray)
├── installer/
│   ├── ClipSync.iss                # Inno Setup installer script (Chinese/English wizard)
│   └── assets/
└── .github/workflows/release.yml   # GitHub Actions: automated x64/arm64 dual-arch release
```

### Tech Stack

- **.NET 8** + **WPF** (`net8.0-windows`)
- **C# 12** with `Nullable` and `ImplicitUsings` enabled
- `System.Drawing.Common` / `Microsoft.Windows.Compatibility` (clipboard images + tray icon)
- JSON serialization: `System.Text.Json` (source-generator friendly, no reflection)
- Installer: [Inno Setup 6](https://jrsoftware.org/isinfo.php)

---

## 🔧 Building from Source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)
- Windows 10/11 (WPF must be compiled on Windows)

### Command Line

```powershell
# Restore dependencies
dotnet restore ClipSync.sln

# Build Debug
dotnet build ClipSync.sln -c Debug

# Run
dotnet run --project src/ClipSync.App/ClipSync.App.csproj

# Publish self-contained single-file (x64 example)
dotnet publish src/ClipSync.App/ClipSync.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:IncludeAllContentForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:PublishTrimmed=false `
  -o publish/x64
```

### Visual Studio

Open `ClipSync.sln` with Visual Studio 2022 17.8+ and press F5 to debug.

### Build the Installer (Optional)

1. Install [Inno Setup 6](https://jrsoftware.org/isdl.php)
2. Run the `dotnet publish` steps above first (x64 and/or arm64)
3. From the repo root, run:

```powershell
# x64
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer\ClipSync.iss" /DArch=x64
# arm64
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer\ClipSync.iss" /DArch=arm64
```

The output appears at `installer/Output/ClipSync-Setup-<version>-win-<arch>.exe`.

---

## 🔐 Privacy & Security

| Aspect | Design |
|------|------|
| Data in transit | Goes through your own server; no third parties involved |
| Data storage | The server stores nothing; Windows data lives under `%APPDATA%\ClipSync\` |
| Config file | `settings.json` (contains token and password; stored locally) |
| History | `history.json` (latest 500 entries; can be cleared from within the app) |
| Logs | `logs/clipsync-YYYY-MM-DD.log` (daily rolling) |
| End-to-end encryption | AES-256-GCM; key derived from the sync password via PBKDF2-HMAC-SHA256 (200,000 iterations), kept only on the local machine |
| Least privilege | Does not read the browser or file system; only clipboard and network access are used |
| Production advice | Reverse-proxy with Nginx/Caddy and add TLS, use `wss://` |

---

## 🐛 Troubleshooting

| Symptom | What to check |
|------|------|
| Double-click does nothing | Check `%APPDATA%\ClipSync\startup-trace.log` and `crash.log` |
| Cannot connect to server | Check address/port, firewall and whether the server is running; prefer `ws://IP:port` over `localhost` |
| Message received but can't be decrypted | The "sync password" differs between ends, or one side has E2EE disabled; see the "decryption failed" hint on the home page |
| Clipboard not syncing | Make sure the "auto-sync clipboard" toggle is on; Windows 10/11 clipboard history may intercept |
| Images don't show | Check the "show message content" toggle; the longest edge of images is compressed to 1600px |
| Multiple windows open repeatedly | Check Task Manager for leftover `ClipSync.App.exe` processes |

Log location: `%APPDATA%\ClipSync\logs\`  
Startup trace: `%APPDATA%\ClipSync\startup-trace.log`  
Crash log: `%APPDATA%\ClipSync\crash.log`

---

## 🛣️ Roadmap

- [ ] Dark theme
- [ ] Global hotkey (push current clipboard with one shortcut)
- [ ] File/folder sync
- [ ] Self-check & repair for Windows auto-start status
- [ ] Auto-update (Squirrel / Velopack)

---

## 🤝 Related Projects

| Project | Stack | Link |
|------|--------|------|
| Server | Go + gorilla/websocket | [JH-Clipsync/ClipSync-Server](https://github.com/JH-Clipsync/ClipSync-Server) |
| Admin Backend | Go + Gin + GORM | [JH-Clipsync/ClipSync-Admin](https://github.com/JH-Clipsync/ClipSync-Admin) |
| Admin Console Frontend | Vue 3 + Vite + Element Plus | [JH-Clipsync/ClipSync-Admin-Web](https://github.com/JH-Clipsync/ClipSync-Admin-Web) |
| macOS Client | Swift + SwiftUI | [JH-Clipsync/ClipSync-Mac](https://github.com/JH-Clipsync/ClipSync-Mac) |
| Android Client | Kotlin + OkHttp | [JH-Clipsync/ClipSync-Android](https://github.com/JH-Clipsync/ClipSync-Android) |

---

## 📄 License

A personal, self-use project. Feel free to reference and modify the code.

---

**Made with ❤️ · All three platforms built in-house · Your privacy stays yours**
