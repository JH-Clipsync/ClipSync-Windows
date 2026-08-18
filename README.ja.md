<p align="center">
  <img src="src/ClipSync.App/Resources/app.png" width="128" alt="ClipSync アイコン"/>
</p>

<h1 align="center">ClipSync for Windows</h1>

<p align="center">
  <b>スマホの認証コード＆クリップボード → Windows のポップアップ、ワンクリックでコピー。</b><br/>
  <a href="README.md">简体中文</a> ·
  <a href="README.en.md">English</a> ·
  <a href="README.ja.md">日本語</a>
</p>

---

ClipSync は、自己ホスト型のクロスプラットフォームメッセージ同期ツールです。本リポジトリは Windows デスクトップクライアントで、**.NET 8 + WPF (C#)** で開発されています。

主なユースケース：**スマホで認証コードを受信したりコンテンツをコピーしたりすると、Windows 上で即座にポップアップが表示され、ワンクリックでクリップボードにコピーできます。逆に Windows でコピーした内容も、リアルタイムで他のデバイスに同期されます。**

サードパーティのプッシュサービスには依存せず、通信はすべて自分で用意した WebSocket 中継サーバー経由で行われ、エンドツーエンド暗号化も任意で有効化できるため、プライバシーは自分自身で管理できます。

---

## ✨ 主な機能

| モジュール | 説明 |
|------|------|
| 📩 **SMS 認証コードポップアップ** | スマホが認証コードを受信すると、Windows の右上に通知が即座に表示。コードまたは全文をワンクリックでコピー |
| 📋 **クリップボード双方向同期** | ローカルでコピーしたテキスト/画像を自動アップロード。他のデバイスでコピーした内容をローカルのクリップボードに自動書き込み |
| 🛡️ **エンドツーエンド暗号化（任意）** | AES-256-GCM 暗号、PBKDF2-HMAC-SHA256（20万回）で鍵導出。サーバーは暗号文を転送するだけ |
| 🔔 **Toast 通知バナー** | フォーカスを奪わず、5秒で自動消滅、最大3件までスタック。認証コードはスマート検出され専用ボタンを表示 |
| 🖥️ **トレイ常駐** | メインウィンドウを閉じるとシステムトレイに格納。左クリックで復帰、右クリックでメニュー。トレイへの最小化に対応 |
| 👥 **オンラインデバイス一覧** | ホーム画面に同じアカウントのオンラインデバイス、プラットフォーム、IP、同期機能をリアルタイム表示 |
| 🚀 **自動起動** | インストーラーとアプリ内のどちらからでもワンクリックで自動起動を有効化（`HKCU\...\Run` に書き込み） |
| 🧭 **初回起動ウィザード** | サーバーアドレス、アカウント/パスワード、エンドツーエンド暗号化の設定をステップバイステップで案内 |
| 📜 **履歴** | 最新 500 件のメッセージをローカルに永続化。検索、コピー、削除、カテゴリフィルタに対応 |
| 🔄 **自動再接続** | ネットワークの揺らぎで自動再接続。トークンが失効した場合はローカル保存のアカウント/パスワードで再取得 |
| 🔒 **シングルインスタンス** | Global Mutex でガード。重ねて開くと既に実行中のメインウィンドウが前面に表示 |

---

## 🖼️ 画面プレビュー

| ホーム | SMS 履歴 | クリップボード履歴 |
|------|----------|------------|
| 接続状態、アカウント、暗号化、同期スイッチ、オンラインデバイス、最近のメッセージ | 新しい順に表示。認証コードのコピー/削除に対応 | テキストと画像のプレビュー。コピー/削除に対応 |

---

## 📦 ダウンロードとインストール

[GitHub Releases](https://github.com/JH-Clipsync/ClipSync-Windows/releases) から、お使いのアーキテクチャに合ったファイルをダウンロードしてください：

| ファイル | 用途 |
|------|----------|
| `ClipSync-Setup-<バージョン>-win-x64.exe` | ほとんどの Intel/AMD 64ビット Windows PC（推奨） |
| `ClipSync-Setup-<バージョン>-win-arm64.exe` | Surface Pro X、Snapdragon 搭載ノートなど ARM64 デバイス |
| `ClipSync-<バージョン>-win-x64.zip` | ポータブル版（解凍してすぐ使える、レジストリ書き込みなし） |

> インストーラーは**セルフコンテインドデプロイ**で、.NET 8 ランタイムを同梱しているため、**インストール先のマシンに別途 .NET をインストールする必要はありません**。既定では現在のユーザー向けに `%LocalAppData%\Programs\ClipSync` へインストールされ、管理者権限は不要です。

システム要件：**Windows 10 1809 (17763) 以降 / Windows 11**。

---

## 🚀 クイックスタート

1. ClipSync をインストールして起動します
2. 初回起動時はオンボーディングウィザードが開きます：
   - **サーバーアドレス**を入力（例: `192.168.1.10:8080`、`ws://` / `wss://` に対応）
   - 管理者から割り当てられた**ユーザー名 / パスワード**を入力（初回接続時に自動的に token と交換されます）
   - **エンドツーエンド暗号化**を有効にするか選択し、同期パスワードを入力（両端で一致する必要があります）
3. 「接続」をクリック — トレイアイコンが緑色になれば接続成功です
4. スマホ側（[ClipSync-Android](https://github.com/JH-Clipsync/ClipSync-Android)）に同じアカウントを設定すれば、同期が始まります

---

## 🧩 プロジェクト構成

```
ClipSync-Windows/
├── src/
│   ├── ClipSync.Core/              # クロスプラットフォームコア（プロトコル/暗号/ネットワーク/ストレージ）
│   │   ├── Crypto/                 # AES-256-GCM + PBKDF2（E2EECrypto / PayloadCipher）
│   │   ├── Net/                    # WSClient / AuthClient / ServerAddress / ConnectionState
│   │   ├── Protocol/               # Models（SyncMessage / MessagePayload）+ SmsCodeExtractor
│   │   ├── Storage/                # SettingsStore / HistoryStore / AppPaths
│   │   └── Diagnostics/            # 日次ローテーションログ（Log）
│   └── ClipSync.App/               # WPF クライアント
│       ├── App.xaml / App.xaml.cs  # エントリ（シングルインスタンス、トレイ、Dispatcher 注入）
│       ├── MainWindow.xaml(.cs)    # メインウィンドウ（左ナビ + コンテンツエリア）
│       ├── GlobalUsings.cs         # グローバル using
│       ├── Services/
│       │   ├── ClipboardMonitor.cs # 600ms ポーリング監視 + 画像圧縮 + 2段階重複除去
│       │   ├── ClipboardWriter.cs  # リモートメッセージをローカルクリップボードに書き込み
│       │   └── AutoStartService.cs # 自動起動（レジストリ）
│       ├── UI/
│       │   ├── HomeView.cs         # ホーム（ステータスカード + オンラインデバイス + 最近のメッセージ）
│       │   ├── HistoryView.cs      # SMS / クリップボード履歴
│       │   ├── SettingsView.cs     # 設定ページ
│       │   ├── OnboardingWizard.cs # 初回起動ガイド
│       │   ├── ToastWindow.xaml(.cs) # 右上の通知バナー
│       │   ├── InfoToastWindow.xaml.cs # 情報系 Toast（認証コードのクイックボタンなど）
│       │   ├── ToastManager.cs     # 複数 Toast のスタック管理
│       │   ├── ImagePreviewWindow.xaml.cs # 画像ビューア
│       │   ├── AppColors.cs        # グローバル配色
│       │   ├── AppDialog.cs        # 共通ダイアログ
│       │   ├── PasswordInput.cs    # パスワード入力コントロール
│       │   ├── FocusBehavior.cs    # オートフォーカス添付ビヘイビア
│       │   └── SmsPayloadSanitizer.cs # SMS メッセージのクリーニング/マスキング
│       └── Resources/
│           ├── app.png             # アプリアイコン（PNG、README/通知用）
│           └── app.ico             # アプリアイコン（ICO、ウィンドウ/トレイ用）
├── installer/
│   ├── ClipSync.iss                # Inno Setup インストーラースクリプト（中国語/英語ウィザード）
│   └── assets/
└── .github/workflows/release.yml   # GitHub Actions：x64/arm64 デュアルアーキテクチャ自動リリース
```

### 技術スタック

- **.NET 8** + **WPF**（`net8.0-windows`）
- **C# 12**、`Nullable` と `ImplicitUsings` を有効化
- `System.Drawing.Common` / `Microsoft.Windows.Compatibility`（クリップボード画像 + トレイアイコン）
- JSON シリアライズ：`System.Text.Json`（ソースジェネレータ対応、リフレクションなし）
- インストーラー：[Inno Setup 6](https://jrsoftware.org/isinfo.php)

---

## 🔧 ソースからビルド

### 前提条件

- [.NET 8 SDK](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)
- Windows 10/11（WPF は Windows 上でビルドする必要があります）

### コマンドライン

```powershell
# 依存関係を復元
dotnet restore ClipSync.sln

# Debug ビルド
dotnet build ClipSync.sln -c Debug

# 実行
dotnet run --project src/ClipSync.App/ClipSync.App.csproj

# セルフコンテインド単一ファイルとして発行（x64 の例）
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

Visual Studio 2022 17.8 以降で `ClipSync.sln` を開き、F5 でデバッグ実行します。

### インストーラーのビルド（任意）

1. [Inno Setup 6](https://jrsoftware.org/isdl.php) をインストール
2. 先に上記の `dotnet publish` を実行します（x64 および/または arm64）
3. リポジトリルートで以下を実行します：

```powershell
# x64
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer\ClipSync.iss" /DArch=x64
# arm64
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer\ClipSync.iss" /DArch=arm64
```

成果物は `installer/Output/ClipSync-Setup-<バージョン>-win-<アーキテクチャ>.exe` に出力されます。

---

## 🔐 プライバシーとセキュリティ

| 項目 | 設計 |
|------|------|
| データ通信 | 自分のサーバー経由で送受信。第三者を介さない |
| データ保存 | サーバーはデータを保存しない。Windows 側のデータは `%APPDATA%\ClipSync\` に保存 |
| 設定ファイル | `settings.json`（token とパスワードを含む、ローカル保存） |
| 履歴 | `history.json`（最新 500 件。アプリ内から消去可能） |
| ログ | `logs/clipsync-YYYY-MM-DD.log`（日次ローテーション） |
| エンドツーエンド暗号化 | AES-256-GCM。鍵は同期パスワードから PBKDF2-HMAC-SHA256（20万回）で導出し、ローカルのみに保持 |
| 最小権限 | ブラウザやファイルシステムは読み取らず、クリップボードとネットワークのみ使用 |
| 本番環境の推奨 | Nginx/Caddy でリバースプロキシ + TLS を構成し、`wss://` を使用 |

---

## 🐛 トラブルシューティング

| 現象 | 確認事項 |
|------|------|
| ダブルクリックしても反応しない | `%APPDATA%\ClipSync\startup-trace.log` と `crash.log` を確認 |
| サーバーに接続できない | アドレス/ポート、ファイアウォール、サーバーが起動しているか確認。`localhost` ではなく `ws://IP:ポート` を推奨 |
| メッセージを受信するが復号できない | 両端の「同期パスワード」が一致しない、または片方が E2EE 無効。ホームの「復号失敗」表示を確認 |
| クリップボードが同期しない | 「クリップボードを自動同期」スイッチがオンになっているか確認。Windows 10/11 のクリップボード履歴が干渉する可能性あり |
| 画像が表示されない | 「メッセージ内容を表示」スイッチを確認。画像は長辺が 1600px に圧縮されます |
| ウィンドウが重複して開く | タスクマネージャーに残存する `ClipSync.App.exe` プロセスがないか確認 |

ログの場所：`%APPDATA%\ClipSync\logs\`  
起動トレース：`%APPDATA%\ClipSync\startup-trace.log`  
クラッシュログ：`%APPDATA%\ClipSync\crash.log`

---

## 🛣️ Roadmap

- [ ] ダークテーマ
- [ ] グローバルホットキー（ショートカット一発で現在のクリップボードをプッシュ）
- [ ] ファイル/フォルダ同期
- [ ] Windows 自動起動状態の自己診断と修復
- [ ] 自動更新（Squirrel / Velopack）

---

## 🤝 関連プロジェクト

| プロジェクト | 技術スタック | リンク |
|------|--------|------|
| サーバー | Go + gorilla/websocket | [JH-Clipsync/ClipSync-Server](https://github.com/JH-Clipsync/ClipSync-Server) |
| 管理バックエンド | Go + Gin + GORM | [JH-Clipsync/ClipSync-Admin](https://github.com/JH-Clipsync/ClipSync-Admin) |
| 管理コンソールフロント | Vue 3 + Vite + Element Plus | [JH-Clipsync/ClipSync-Admin-Web](https://github.com/JH-Clipsync/ClipSync-Admin-Web) |
| macOS クライアント | Swift + SwiftUI | [JH-Clipsync/ClipSync-Mac](https://github.com/JH-Clipsync/ClipSync-Mac) |
| Android クライアント | Kotlin + OkHttp | [JH-Clipsync/ClipSync-Android](https://github.com/JH-Clipsync/ClipSync-Android) |

---

## 📄 License

個人利用のプロジェクトです。コードは自由に参考・改変いただけます。

---

**Made with ❤️ · 3 クライアントすべて自作 · プライバシーはあなた自身のもの**
