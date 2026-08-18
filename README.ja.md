<p align="center">
  <img src="src/ClipSync.App/Resources/app.png" width="128" alt="ClipSync ロゴ"/>
</p>

<h1 align="center">ClipSync for Windows</h1>

<p align="center">
  <b>スマホの認証コード & クリップボード → Windows にポップアップ、ワンクリックでコピー。</b><br/>
  <a href="README.md">简体中文</a> ·
  <a href="README.en.md">English</a> ·
  <a href="README.ja.md">日本語</a>
</p>

---

ClipSync は、セルフホスト型のクロスデバイスメッセージ同期ツールです。このリポジトリは Windows デスクトップクライアントで、**.NET 8 + WPF (C#)** で開発されています。

主なユースケース：**スマホで認証コードを受信したりコンテンツをコピーしたりすると、Windows の画面右上にトーストが即座に表示され、ワンクリックでクリップボードへコピーできます。逆に Windows でコピーした内容も、他のデバイスへリアルタイムで同期されます。**

サードパーティのプッシュサービスには一切依存しません。通信はすべて自分で立てた WebSocket 中継サーバーを通り、エンドツーエンド暗号化もオプションで選択できるため、プライバシーは自分で管理できます。

---

## ✨ 主な機能

| 分類 | 内容 |
|------|------|
| 📩 **SMS 認証コードのポップアップ** | スマホが認証コードを受信すると、Windows の右上にトーストが表示され、「コードをコピー」「全文をコピー」ボタンでワンクリックコピー |
| 📋 **双方向クリップボード同期** | この PC でコピーしたテキスト/画像を自動アップロード。他のデバイスでコピーした内容はローカルのクリップボードへ自動書き込み |
| 🛡️ **オプションの E2E 暗号化** | AES-256-GCM、PBKDF2-HMAC-SHA256（20 万回反復）。サーバーには暗号文しか見えません |
| 🔔 **トースト通知** | フォーカスを奪わず、5 秒で自動消灯、最大 3 枚までスタック。認証コードを自動検出して専用ボタンを表示 |
| 🖥️ **トレイ常駐** | メインウィンドウを閉じるとシステムトレイに格納。左クリックで復帰、右クリックでメニュー |
| 👥 **オンライン端末一覧** | 同じアカウントで接続中の端末、プラットフォーム、IP、同期機能をリアルタイムに表示 |
| 🚀 **スタートアップ起動** | インストーラーまたはアプリ内のワンクリックで `HKCU\...\Run` に登録 |
| 🧭 **初回起動ウィザード** | サーバーアドレス、アカウント情報、E2E 暗号化をまとめてガイド |
| 📜 **履歴** | 直近 500 件をローカルに保存。コピー/削除/カテゴリフィルタに対応 |
| 🔄 **自動再接続** | ネットワーク切断時に自動再接続。トークン失効時は保存済みの認証情報で自動的に再取得 |
| 🔒 **シングルインスタンス** | グローバル Mutex で二重起動を防止。再度起動すると既存ウィンドウが前面に表示 |

---

## 🖼️ UI 概要

| ホーム | SMS 履歴 | クリップボード履歴 |
|--------|----------|--------------------|
| 接続状態、アカウント、暗号化、同期トグル、オンライン端末、最新メッセージ | 時系列順、「コードをコピー」/削除に対応 | テキストと画像のプレビュー、コピー/削除 |

---

## 📦 ダウンロードとインストール

[GitHub Releases](https://github.com/JH-Clipsync/ClipSync-Windows/releases) から環境に合ったビルドをダウンロードしてください。

| ファイル | 用途 |
|----------|------|
| `ClipSync-Setup-<バージョン>-win-x64.exe` | ほとんどの Intel/AMD 64bit Windows PC（推奨） |
| `ClipSync-Setup-<バージョン>-win-arm64.exe` | Surface Pro X、Snapdragon 搭載 PC など ARM64 デバイス |
| `ClipSync-<バージョン>-win-x64.zip` | ポータブル版（解凍してすぐ実行、レジストリ不使用） |

> インストーラーは**セルフコンテインド**です。.NET 8 ランタイムが同梱されているため、**別途 .NET をインストールする必要はありません**。既定の「現在のユーザーのみ」モードでは `%LocalAppData%\Programs\ClipSync` に管理者権限なしでインストールされます。

動作要件：**Windows 10 1809 (17763) 以降 / Windows 11**。

---

## 🚀 クイックスタート

1. ClipSync をインストールして起動
2. 初回はウィザードが表示されます：
   - **サーバーアドレス**（例: `192.168.1.10:8080`、`ws://` / `wss://` に対応）
   - 管理者から発行された**ユーザー名 / パスワード**（初回接続時に自動でトークンを取得）
   - **エンドツーエンド暗号化**を有効にして同期パスワードを設定（端末間で一致する必要あり）
3. 「接続」をクリック。トレイアイコンが緑になれば接続完了
4. スマホ版（[ClipSync-Android](https://github.com/JH-Clipsync/ClipSync-Android)）で同じアカウントを使ってサインインすれば同期開始

---

## 🧩 プロジェクト構成

```
ClipSync-Windows/
├── src/
│   ├── ClipSync.Core/              # クロスプラットフォーム基盤（プロトコル/暗号/ネット/ストレージ）
│   │   ├── Crypto/                 # AES-256-GCM + PBKDF2、キーキャッシュ付き
│   │   ├── Net/                    # WSClient / AuthClient / ServerAddress
│   │   ├── Protocol/               # SyncMessage / MessagePayload / 認証コード抽出
│   │   ├── Storage/                # SettingsStore / HistoryStore / AppPaths
│   │   └── Diagnostics/            # 日次ローテーションのファイルログ
│   └── ClipSync.App/               # WPF クライアント
│       ├── App.xaml / App.xaml.cs  # エントリ（シングルインスタンス、トレイ、Dispatcher）
│       ├── MainWindow.xaml(.cs)    # メインウィンドウ（左ナビ + コンテンツ領域）
│       ├── Services/
│       │   ├── ClipboardMonitor.cs # 600ms ポーリング + 画像圧縮 + 重複除去
│       │   ├── ClipboardWriter.cs  # リモートペイロードをローカルクリップボードへ書き込み
│       │   └── AutoStartService.cs # スタートアップ起動（レジストリ）
│       ├── UI/
│       │   ├── HomeView.cs         # ホーム（状態、端末一覧、最新メッセージ）
│       │   ├── HistoryView.cs      # SMS / クリップボード履歴
│       │   ├── SettingsView.cs     # 設定ページ
│       │   ├── OnboardingWizard.cs # 初回起動ウィザード
│       │   ├── ToastWindow.xaml(.cs) # 右上トースト
│       │   └── ToastManager.cs     # トーストのスタック管理
│       └── Resources/app.ico
├── installer/
│   ├── ClipSync.iss                # Inno Setup インストーラースクリプト（中/英ウィザード）
│   └── assets/
└── .github/workflows/release.yml   # GitHub Actions: x64/arm64 自動リリース
```

### 技術スタック

- **.NET 8** + **WPF**（`net8.0-windows`）
- **C# 12**、`Nullable` と `ImplicitUsings` を有効化
- `System.Drawing.Common` / `Microsoft.Windows.Compatibility`（クリップボード画像 + トレイアイコン）
- JSON シリアライズ: `System.Text.Json`（リフレクションなし）
- インストーラー: [Inno Setup 6](https://jrsoftware.org/isinfo.php)

---

## 🔧 ソースからビルド

### 前提条件

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10/11（WPF のビルドには Windows が必要です）

### コマンドライン

```powershell
# 依存関係の復元
dotnet restore ClipSync.sln

# Debug ビルド
dotnet build ClipSync.sln -c Debug

# 実行
dotnet run --project src/ClipSync.App/ClipSync.App.csproj

# セルフコンテインド単一ファイルとして発行（x64 の場合）
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

Visual Studio 2022 17.8 以降で `ClipSync.sln` を開き、F5 を押してください。

### インストーラーのビルド（任意）

1. [Inno Setup 6](https://jrsoftware.org/isdl.php) をインストール
2. 先に上記の `dotnet publish` を x64 / arm64 に対して実行
3. リポジトリのルートで以下を実行：

```powershell
# x64
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer\ClipSync.iss" /DArch=x64
# arm64
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer\ClipSync.iss" /DArch=arm64
```

出力先: `installer/Output/ClipSync-Setup-<バージョン>-win-<アーキテクチャ>.exe`

---

## 🔐 プライバシーとセキュリティ

| 項目 | 設計 |
|------|------|
| 通信 | 自分で用意したサーバー経由。サードパーティ不使用 |
| サーバー側の保存 | なし — メッセージはルーティングのみで永続化されません |
| クライアント保存先 | `%APPDATA%\ClipSync\` |
| 設定 | `settings.json`（トークン/認証情報、ローカルのみ） |
| 履歴 | `history.json`（直近 500 件、アプリから消去可能） |
| ログ | `logs/clipsync-YYYY-MM-DD.log`（日次ローテーション） |
| E2E 暗号化 | AES-256-GCM。鍵は同期パスワードから PBKDF2-HMAC-SHA256（20 万回）で導出、端末から外に出ません |
| 権限 | クリップボードとネットワークのみ。ブラウザやファイルシステムへのアクセスなし |
| 本番環境の推奨 | Nginx / Caddy で TLS（`wss://`）を終端 |

---

## 🐛 トラブルシューティング

| 症状 | 確認ポイント |
|------|--------------|
| ダブルクリックしても何も起きない | `%APPDATA%\ClipSync\startup-trace.log` と `crash.log` を確認 |
| サーバーに接続できない | アドレス/ポート、ファイアウォール、サーバー起動状態を確認。`localhost` より `ws://IP:ポート` を推奨 |
| メッセージは届くが復号できない | 端末間の「同期パスワード」が不一致、または片方が E2E オフ。ホームの「復号失敗」ヒントを確認 |
| クリップボードが同期されない | 「クリップボードを自動同期」がオンになっているか確認。Windows のクリップボード履歴が干渉する場合あり |
| 画像が表示されない | 「メッセージ内容を表示」がオンか確認。長辺は 1600px に圧縮されます |
| ウィンドウが複数開く | タスクマネージャーに残存する `ClipSync.App.exe` を確認 |

ログ: `%APPDATA%\ClipSync\logs\`  
起動トレース: `%APPDATA%\ClipSync\startup-trace.log`  
クラッシュログ: `%APPDATA%\ClipSync\crash.log`

---

## 🛣️ 今後の予定

- [ ] ダークテーマ
- [ ] グローバルホットキー（ワンキーで現在のクリップボードを送信）
- [ ] ファイル / フォルダ同期
- [ ] スタートアップ項目の自己診断と修復
- [ ] 自動アップデーター（Squirrel / Velopack）

---

## 🤝 関連プロジェクト

| プロジェクト | 技術スタック | リンク |
|--------------|--------------|--------|
| サーバー | Go + gorilla/websocket | [JH-Clipsync/ClipSync-Server](https://github.com/JH-Clipsync/ClipSync-Server) |
| macOS クライアント | Swift + SwiftUI | [JH-Clipsync/ClipSync-Mac](https://github.com/JH-Clipsync/ClipSync-Mac) |
| Android クライアント | Kotlin + OkHttp | [JH-Clipsync/ClipSync-Android](https://github.com/JH-Clipsync/ClipSync-Android) |

---

## 📄 License

個人利用のプロジェクトです。自由に学習、フォーク、改変いただけます。

---

**Made with ❤️ · 全プラットフォーム自前実装 · あなたのデータはあなたのもの**
