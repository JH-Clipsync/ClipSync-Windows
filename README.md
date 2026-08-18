<p align="center">
  <img src="src/ClipSync.App/Resources/app.png" width="128" alt="ClipSync 图标"/>
</p>

<h1 align="center">ClipSync for Windows</h1>

<p align="center">
  <b>手机验证码 & 剪贴板 → Windows 弹窗，一键复制。</b><br/>
  <a href="README.md">简体中文</a> ·
  <a href="README.en.md">English</a> ·
  <a href="README.ja.md">日本語</a>
</p>

---

ClipSync 是一套自建的跨端消息同步工具。本仓库是 Windows 桌面客户端，使用 **.NET 8 + WPF (C#)** 开发。

核心场景：**手机上收到验证码或复制了内容后，Windows 上立即弹窗展示，一键复制到剪贴板；反之，Windows 上复制的内容也能实时同步到其他设备。**

不依赖任何第三方推送服务，通信走你自己的 WebSocket 中转，端到端加密可选，隐私自主可控。

---

## ✨ 核心功能

| 模块 | 说明 |
|------|------|
| 📩 **短信验证码弹窗** | 手机端收到验证码后，Windows 右上角立即弹出通知，一键复制验证码或全文 |
| 📋 **剪贴板双向同步** | 本机复制文本/图片自动上传；其他设备复制的内容自动写入本机剪贴板 |
| 🛡️ **端到端加密（可选）** | AES-256-GCM 加密，PBKDF2-HMAC-SHA256（20 万轮）派生密钥，服务端只转发密文 |
| 🔔 **Toast 通知横幅** | 不抢焦点、5 秒自动消失、最多堆叠 3 条；验证码智能识别并单独提供按钮 |
| 🖥️ **托盘常驻** | 关闭主窗口收进系统托盘，左键唤起、右键菜单，支持最小化到托盘 |
| 👥 **在线设备列表** | 主页实时显示同账号下的在线设备、平台、IP 与同步能力 |
| 🚀 **开机自启** | 安装包与应用内都可一键开启开机自启（写 `HKCU\...\Run`） |
| 🧭 **首次启动向导** | 引导用户配置服务器地址、账号密码、端到端加密，一步到位 |
| 📜 **历史记录** | 本地持久化最近 500 条消息，支持搜索、复制、删除、分类筛选 |
| 🔄 **自动重连** | 网络抖动自动重连；token 失效自动用本地保存的账号密码重新换取 |
| 🔒 **单实例运行** | Global Mutex 守卫，重复打开会唤起已在运行的主窗口 |

---

## 🖼️ 界面预览

| 主页 | 短信历史 | 剪贴板历史 |
|------|----------|------------|
| 连接状态、账号、加密、同步开关、在线设备、最近消息 | 按时间倒序，支持复制验证码/删除 | 文本与图片预览，支持复制/删除 |

---

## 📦 下载安装

前往 [GitHub Releases](https://github.com/JH-Clipsync/ClipSync-Windows/releases) 下载对应架构：

| 文件 | 适用场景 |
|------|----------|
| `ClipSync-Setup-<版本>-win-x64.exe` | 绝大多数 Intel/AMD 64 位 Windows 电脑（推荐） |
| `ClipSync-Setup-<版本>-win-arm64.exe` | Surface Pro X、骁龙本等 ARM64 设备 |
| `ClipSync-<版本>-win-x64.zip` | 免安装绿色版（解压即用，不写注册表） |

> 安装包为**自包含部署**，已内置 .NET 8 运行时，**目标机器无需额外安装 .NET**。默认按"当前用户"安装到 `%LocalAppData%\Programs\ClipSync`，不需要管理员权限。

系统要求：**Windows 10 1809 (17763) 及以上 / Windows 11**。

---

## 🚀 快速开始

1. 安装并启动 ClipSync
2. 首次启动会进入引导向导：
   - 填写**服务器地址**（如 `192.168.1.10:8080`，支持 `ws://` / `wss://`）
   - 填写管理员分配的**用户名 / 密码**（首次连接时会自动换取 token）
   - 选择是否启用**端到端加密**并填写同步密码（两端需保持一致）
3. 点击"连接"，托盘图标变绿即连接成功
4. 在手机端（[ClipSync-Android](https://github.com/JH-Clipsync/ClipSync-Android)）填写同一账号，即可开始同步

---

## 🧩 项目架构

```
ClipSync-Windows/
├── src/
│   ├── ClipSync.Core/              # 跨平台核心（协议/加密/网络/存储）
│   │   ├── Crypto/                 # AES-256-GCM + PBKDF2（E2EECrypto / PayloadCipher）
│   │   ├── Net/                    # WSClient / AuthClient / ServerAddress / ConnectionState
│   │   ├── Protocol/               # Models（SyncMessage / MessagePayload）+ SmsCodeExtractor
│   │   ├── Storage/                # SettingsStore / HistoryStore / AppPaths
│   │   └── Diagnostics/            # 按天滚动日志（Log）
│   └── ClipSync.App/               # WPF 客户端
│       ├── App.xaml / App.xaml.cs  # 入口（单实例、托盘、Dispatcher 注入）
│       ├── MainWindow.xaml(.cs)    # 主窗口（左侧导航 + 内容区）
│       ├── GlobalUsings.cs         # 全局 using
│       ├── Services/
│       │   ├── ClipboardMonitor.cs # 600ms 轮询监听 + 图片压缩 + 双重去重
│       │   ├── ClipboardWriter.cs  # 远端消息写入本机剪贴板
│       │   └── AutoStartService.cs # 开机自启（注册表）
│       ├── UI/
│       │   ├── HomeView.cs         # 主页（状态卡 + 在线设备 + 最近消息）
│       │   ├── HistoryView.cs      # 短信 / 剪贴板历史
│       │   ├── SettingsView.cs     # 设置页
│       │   ├── OnboardingWizard.cs # 首次启动引导
│       │   ├── ToastWindow.xaml(.cs) # 右上角通知横幅
│       │   ├── InfoToastWindow.xaml.cs # 信息类 Toast（验证码快捷按钮等）
│       │   ├── ToastManager.cs     # 多条 Toast 堆叠管理
│       │   ├── ImagePreviewWindow.xaml.cs # 图片查看器
│       │   ├── AppColors.cs        # 全局配色
│       │   ├── AppDialog.cs        # 通用对话框
│       │   ├── PasswordInput.cs    # 密码输入控件
│       │   ├── FocusBehavior.cs    # 自动获取焦点附加行为
│       │   └── SmsPayloadSanitizer.cs # 短信消息清洗/脱敏
│       └── Resources/
│           ├── app.png             # 应用图标（PNG，README/通知用）
│           └── app.ico             # 应用图标（ICO，窗口/托盘用）
├── installer/
│   ├── ClipSync.iss                # Inno Setup 安装包脚本（中文/英文向导）
│   └── assets/
└── .github/workflows/release.yml   # GitHub Actions：x64/arm64 双架构自动发布
```

### 技术栈

- **.NET 8** + **WPF**（`net8.0-windows`）
- **C# 12**，启用 `Nullable` 和 `ImplicitUsings`
- `System.Drawing.Common` / `Microsoft.Windows.Compatibility`（剪贴板图片 + 托盘图标）
- JSON 序列化：`System.Text.Json`（源生成器友好，无反射）
- 安装包：[Inno Setup 6](https://jrsoftware.org/isinfo.php)

---

## 🔧 从源码构建

### 前置条件

- [.NET 8 SDK](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)
- Windows 10/11（WPF 必须在 Windows 上编译）

### 命令行

```powershell
# 还原依赖
dotnet restore ClipSync.sln

# Debug 编译
dotnet build ClipSync.sln -c Debug

# 运行
dotnet run --project src/ClipSync.App/ClipSync.App.csproj

# 发布自包含单文件（以 x64 为例）
dotnet publish src/ClipSync.App/ClipSync.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:IncludeAllContentForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:PublishTrimmed=false `
  -o publish/x64
```

### 用 Visual Studio

用 Visual Studio 2022 17.8+ 打开 `ClipSync.sln`，按 F5 调试即可。

### 编译安装包（可选）

1. 安装 [Inno Setup 6](https://jrsoftware.org/isdl.php)
2. 先执行上面的 `dotnet publish`（x64 和/或 arm64）
3. 在仓库根目录执行：

```powershell
# x64
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer\ClipSync.iss" /DArch=x64
# arm64
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer\ClipSync.iss" /DArch=arm64
```

产物会出现在 `installer/Output/ClipSync-Setup-<版本>-win-<架构>.exe`。

---

## 🔐 隐私与安全

| 维度 | 设计 |
|------|------|
| 数据传输 | 走你自己的服务器，不经过任何第三方 |
| 数据存储 | 服务端不落库；Windows 端数据存于 `%APPDATA%\ClipSync\` |
| 配置文件 | `settings.json`（含 token 与密码，本机存储） |
| 历史记录 | `history.json`（最近 500 条，可在应用内清空） |
| 日志 | `logs/clipsync-YYYY-MM-DD.log`（按天滚动） |
| 端到端加密 | AES-256-GCM；密钥由同步密码经 PBKDF2-HMAC-SHA256（20 万轮）派生，仅留本机 |
| 权限最小化 | 不读浏览器、不读文件系统，只用剪贴板和网络 |
| 生产建议 | 用 Nginx/Caddy 反代加 TLS，走 `wss://` |

---

## 🐛 故障排查

| 现象 | 排查 |
|------|------|
| 双击没反应 | 查看 `%APPDATA%\ClipSync\startup-trace.log` 与 `crash.log` |
| 连不上服务器 | 检查地址/端口、防火墙、服务端是否启动；尽量用 `ws://IP:端口` 而非 `localhost` |
| 收到消息但解不开 | 两端「同步密码」不一致，或一端未开 E2EE；看主页"解密失败"提示 |
| 剪贴板不同步 | 确认"自动同步剪贴板"开关已打开；Windows 10/11 剪贴板历史可能拦截 |
| 图片不显示 | 检查"显示消息内容"开关；图片最大边会被压到 1600px |
| 重复打开多个窗口 | 检查任务管理器是否有残留 `ClipSync.App.exe` 进程 |

日志位置：`%APPDATA%\ClipSync\logs\`  
启动追踪：`%APPDATA%\ClipSync\startup-trace.log`  
崩溃日志：`%APPDATA%\ClipSync\crash.log`

---

## 🛣️ Roadmap

- [ ] 暗色主题
- [ ] 全局快捷键（一键推送当前剪贴板）
- [ ] 文件/文件夹同步
- [ ] Windows 开机自启状态自检与修复
- [ ] 自动更新（Squirrel / Velopack）

---

## 🤝 相关项目

| 项目 | 技术栈 | 链接 |
|------|--------|------|
| 服务端 | Go + gorilla/websocket | [JH-Clipsync/ClipSync-Server](https://github.com/JH-Clipsync/ClipSync-Server) |
| 管理后端 | Go + Gin + GORM | [JH-Clipsync/ClipSync-Admin](https://github.com/JH-Clipsync/ClipSync-Admin) |
| 管理后台前端 | Vue 3 + Vite + Element Plus | [JH-Clipsync/ClipSync-Admin-Web](https://github.com/JH-Clipsync/ClipSync-Admin-Web) |
| macOS 客户端 | Swift + SwiftUI | [JH-Clipsync/ClipSync-Mac](https://github.com/JH-Clipsync/ClipSync-Mac) |
| Android 客户端 | Kotlin + OkHttp | [JH-Clipsync/ClipSync-Android](https://github.com/JH-Clipsync/ClipSync-Android) |

---

## 📄 License

个人自用项目，代码可自由参考修改。

---

**Made with ❤️ · 三端全自研 · 隐私归你自己**
