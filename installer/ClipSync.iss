; ============================================================
; ClipSync Windows Inno Setup 安装脚本
;
; 打包前提：先执行 release.yml 中的 dotnet publish 步骤（自包含单文件），
; 产物位于 publish\ 目录（最主要是 ClipSync.App.exe 单文件 + 必要资源）。
;
; 生成安装包：
;   iscc installer\ClipSync.iss
;   （或在 CI 中：先把目录切到 ClipSync-Windows，然后调 iscc）
;
; 发布策略：
;   · 由于主程序用 --self-contained true + PublishSingleFile=true 发布，
;     本脚本**不检测 .NET Desktop Runtime**，目标机器不需要装 .NET。
;   · 默认按"当前用户（Just Me）"安装，写入 %LocalAppData%\Programs\ClipSync，
;     无需管理员权限；用户也可以在安装 UI 里选择"所有用户"（此时需要 UAC）。
; ============================================================

#define MyAppName "ClipSync"
#define MyAppPublisher "ClipSync"
#define MyAppVersion "2026.8.7.2"      ; CI 构建时请替换为 Directory.Build.props 中的 Version
#define MyAppExeName "ClipSync.App.exe"

; 架构参数：通过 /DArch=x64 或 /DArch=arm64 传入（默认 x64）
#ifndef Arch
  #define Arch "x64"
#endif
; x64 → x64compatible（Intel/AMD 64 位及 Windows 11 on ARM 上的模拟层）
; arm64 → arm64（原生 ARM64，Inno Setup 6.5+ 支持）
#if Arch == "arm64"
  #define ArchInstallMode "arm64"
#else
  #define ArchInstallMode "x64compatible"
#endif
#define PubSrcDir "..\src\ClipSync.App\bin\Release\net8.0-windows\win-" + Arch + "\publish\*"

[Setup]
AppId={{E5534D78-9B0D-4A84-8F6D-7E55811AF96B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/JH-Clipsync
AppSupportURL=https://github.com/JH-Clipsync/ClipSync-Windows/issues
AppUpdatesURL=https://github.com/JH-Clipsync/ClipSync-Windows/releases
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
; OutputDir 是相对于本 .iss 文件（位于 installer\ 目录）解析的，所以直接写 Output 即可
OutputDir=Output
OutputBaseFilename=ClipSync-Setup-{#MyAppVersion}-win-{#Arch}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
ArchitecturesAllowed={#ArchInstallMode}
ArchitecturesInstallIn64BitMode={#ArchInstallMode}
; 低权限模式：默认按"Just Me"安装，不需要管理员
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; 安装器向导窗口左上角 & 生成的 Setup.exe 本身的图标
SetupIconFile=assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup ({#Arch})
VersionInfoTextVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}

[Languages]
; 简体中文为默认语言（需在安装 Inno Setup 时勾选"中文简体语言包"组件）
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; 桌面快捷方式：默认勾选（主动创建），用户可在安装向导里取消
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "autostart"; Description: "开机自动启动（推荐）"; GroupDescription: "附加选项:"; Flags: unchecked
Name: "runafter"; Description: "安装完成后运行 ClipSync"; GroupDescription: "附加选项:"; Flags: checkedonce

[Files]
; 把对应架构的 publish\ 目录全部拷贝进去（自包含单文件）
; 通过 /DArch=x64|arm64 切换，PubSrcDir 会自动指向对应的 publish 目录
Source: "{#PubSrcDir}"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 安装后启动：不等待；勾选 runafter 任务才跑
Filename: "{app}\{#MyAppExeName}"; Description: "运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent; Tasks: runafter

[UninstallDelete]
; 卸载时清掉用户数据目录（可选）→ 暂时不删 settings.json/history.json，防止用户误操作丢失
; Type: filesandordirs; Name: "{userappdata}\ClipSync"

[Registry]
; ------------------------------------------------------------
; autostart 任务：用户勾选则在 HKCU\...\Run 写一个值，下次开机自动起
; 注意：这里的写入与应用内 AutoStartService 写的是同一个键值名 "ClipSync"，
; 二者不冲突：安装器勾了就先写一次，应用里后续可以改开关覆盖。
; ------------------------------------------------------------
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ClipSync"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart
