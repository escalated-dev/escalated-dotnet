<p align="center">
  <a href="README.ar.md">العربية</a> •
  <a href="README.de.md">Deutsch</a> •
  <a href="../../README.md">English</a> •
  <a href="README.es.md">Español</a> •
  <a href="README.fr.md">Français</a> •
  <a href="README.it.md">Italiano</a> •
  <b>日本語</b> •
  <a href="README.ko.md">한국어</a> •
  <a href="README.nl.md">Nederlands</a> •
  <a href="README.pl.md">Polski</a> •
  <a href="README.pt-BR.md">Português (BR)</a> •
  <a href="README.ru.md">Русский</a> •
  <a href="README.tr.md">Türkçe</a> •
  <a href="README.zh-CN.md">简体中文</a>
</p>

# Escalated - ASP.NET Core向け

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

ASP.NET Core向けのフル機能を備えた埋め込み可能なサポートチケットシステム。任意のアプリに追加するだけで、SLA追跡、エスカレーションルール、エージェントワークフロー、カスタマーポータルを備えた完全なヘルプデスクを利用できます。外部サービスは不要です。

> **[escalated.dev](https://escalated.dev)** -- 詳細、デモ、CloudとSelf-Hostedオプションの比較はこちら。

## 機能

- **チケットのライフサイクル** -- 設定可能なステータス遷移による作成、割り当て、返信、解決、クローズ、再オープン
- **SLAエンジン** -- 優先度別の応答・解決目標、営業時間計算、自動違反検出
- **エスカレーションルール** -- 自動的にエスカレート、優先度変更、再割り当て、通知する条件ベースのルール
- **自動化** -- 条件とアクションを持つ時間ベースのルール
- **エージェントダッシュボード** -- フィルター、一括操作、内部メモ、定型応答付きのチケットキュー
- **カスタマーポータル** -- セルフサービスのチケット作成、返信、ステータス追跡
- **管理パネル** -- 部門、SLAポリシー、エスカレーションルール、タグなどの管理
- **マクロと定型応答** -- バッチアクションと再利用可能な返信テンプレート
- **カスタムフィールド** -- 条件付き表示ロジックを持つ動的メタデータ
- **ナレッジベース** -- 記事、カテゴリ、検索、フィードバック
- **ファイル添付** -- 設定可能なストレージとサイズ制限付きのアップロードサポート
- **アクティビティタイムライン** -- すべてのチケットのすべてのアクションの完全な監査ログ
- **Webhooks** -- リトライロジック付きHMAC-SHA256署名
- **APIトークン** -- 能力ベースのスコーピング付きBearer認証
- **ロールと権限** -- きめ細かなアクセス制御
- **監査ログ** -- 旧値/新値付きですべての変更を記録
- **インポートシステム** -- プラグ可能なアダプター付きの多段階ウィザード
- **サイドカンバセーション** -- チケット上の内部チームスレッド
- **チケットの統合とリンク** -- 重複チケットの統合と問題の関連付け
- **チケットの分割** -- 返信を新しいチケットに分割
- **チケットのスヌーズ** -- バックグラウンドのウェイクサービス付きで将来の日付までスヌーズ
- **メールスレッディング** -- 正しいスレッディングのためのIn-Reply-To/References/Message-IDヘッダー
- **保存済みビュー** -- 個人および共有のフィルタープリセット
- **埋め込み可能なウィジェットAPI** -- KB検索、ゲストチケット、ステータス照会用のパブリックエンドポイント
- **リアルタイム更新** -- ライブチケット更新用のSignalRハブ（オプトイン）
- **キャパシティ管理** -- チャネル別のエージェントごとのワークロード制限
- **スキルベースルーティング** -- スキルタグによるエージェントとチケットのマッチング
- **CSAT評価** -- 解決済みチケットの満足度調査
- **2FA** -- リカバリーコード付きTOTPセットアップと検証
- **ゲストアクセス** -- マジックトークン検索による匿名チケット作成
- **Inertia.js + Vue 3 UI** -- [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)経由の共有フロントエンド

## 要件

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server、SQLite、またはPostgreSQL
- Node.js 18+（フロントエンドアセット用）

## クイックスタート

### 1. パッケージをインストール

```bash
dotnet add package Escalated
```

### 2. サービスを登録

```csharp
// Program.cs
using Escalated.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEscalated(builder.Configuration, options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Escalated")));

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.MapEscalated();

app.Run();
```

### 3. 設定

```json
// appsettings.json
{
  "ConnectionStrings": {
    "Escalated": "Server=localhost;Database=MyApp;Trusted_Connection=true;"
  },
  "Escalated": {
    "RoutePrefix": "support",
    "TicketReferencePrefix": "ESC",
    "DefaultPriority": "medium",
    "AllowCustomerClose": true,
    "AutoCloseResolvedAfterDays": 7,
    "Sla": {
      "Enabled": true,
      "BusinessHoursOnly": false,
      "BusinessHours": {
        "Start": "09:00",
        "End": "17:00",
        "Timezone": "UTC",
        "Days": [1, 2, 3, 4, 5]
      }
    },
    "EnableRealTime": false
  }
}
```

### 4. マイグレーションを実行

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

`/support`にアクセス -- 稼働開始です。

## フロントエンド統合

Escalatedは、npmパッケージ[`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)を通じてVueコンポーネントライブラリとデフォルトページを提供します。Inertia.jsと統合して、既存のレイアウト内でシームレスなSPAレンダリングを実現します。

```bash
npm install @escalated-dev/escalated
```

## アーキテクチャ

```
src/Escalated/
  Models/           # 40以上のEF Coreエンティティモデル
  Data/             # 完全なリレーションシップマッピング付きEscalatedDbContext
  Services/         # ビジネスロジック（チケット、SLA、統合、分割、スヌーズなど）
  Controllers/
    Admin/          # 管理パネルAPI（全設定のCRUD）
    Agent/          # チケットキューとエージェントアクション
    Customer/       # カスタマーセルフサービスポータル
    Widget/         # パブリックウィジェットAPI（KB検索、ゲストチケット）
  Middleware/       # APIトークン認証、権限、レート制限
  Events/           # ドメインイベント（TicketCreated、SlaBreachedなど）
  Notifications/    # メール通知インターフェースとテンプレート
  Configuration/    # DI登録、オプション、エンドポイントマッピング
  Hubs/             # リアルタイム更新用SignalRハブ
  Enums/            # TicketStatus、TicketPriority、ActivityType
```

## モデル

Escalatedには、ヘルプデスクドメイン全体をカバーする40以上のEF Coreエンティティが含まれています：

| カテゴリ | モデル |
|----------|--------|
| コア | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| エージェント | AgentProfile, AgentCapacity, Skill, AgentSkill |
| メッセージング | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| 管理 | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| カスタム | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| インポート | ImportJob, ImportSourceMap |
| 設定 | EscalatedSettings, SavedView |
| ナレッジベース | Article, ArticleCategory |

`EscalatedDbContext`ですべてのモデルに適切なリレーションシップ、インデックス、クエリフィルターが設定されています。

## サービス

| サービス | 責務 |
|---------|---------------|
| `TicketService` | チケットの完全なCRUD、ステータス遷移、返信、タグ、部門 |
| `SlaService` | ポリシー割り当て、違反検出、警告チェック、初回応答記録 |
| `AssignmentService` | エージェント割り当て、割り当て解除、ワークロードによる自動割り当て |
| `EscalationService` | 条件ベースのルール評価、エスカレーションアクションの実行 |
| `AutomationRunner` | 時間ベースの自動化評価とアクション実行 |
| `MacroService` | チケットへのマクロアクションシーケンスの適用 |
| `TicketMergeService` | 返信転送付きでソースをターゲットに統合 |
| `TicketSplitService` | 返信を新しいリンクされたチケットに分割 |
| `TicketSnoozeService` | バックグラウンドウェイクサービス付きのスヌーズ/解除 |
| `WebhookDispatcher` | リトライロジック付きHMAC署名webhook配信 |
| `CapacityService` | エージェントあたりの同時チケット制限 |
| `SkillRoutingService` | スキルによるエージェントとチケットタグのマッチング |
| `BusinessHoursCalculator` | 祝日対応の営業時間日付計算 |
| `TwoFactorService` | TOTPシークレット生成、検証、リカバリーコード |
| `AuditLogService` | エンティティの変更のログ記録とクエリ |
| `KnowledgeBaseService` | 記事/カテゴリのCRUD、検索、フィードバック |
| `SavedViewService` | 個人および共有のフィルタープリセット |
| `SideConversationService` | チケット上の内部スレッドカンバセーション |
| `ImportService` | プラグ可能なアダプター付きの多段階インポート |
| `SettingsService` | キーバリュー設定ストア |

## イベント

すべてのチケットアクションはドメインイベントを発行します：

| イベント | タイミング |
|-------|------|
| `TicketCreatedEvent` | 新しいチケットが作成された |
| `TicketStatusChangedEvent` | ステータス遷移 |
| `TicketAssignedEvent` | エージェントが割り当てられた |
| `TicketUnassignedEvent` | エージェントが削除された |
| `ReplyCreatedEvent` | 公開返信が追加された |
| `InternalNoteAddedEvent` | エージェントメモが追加された |
| `SlaBreachedEvent` | SLA期限超過 |
| `SlaWarningEvent` | SLA期限が接近中 |
| `TicketEscalatedEvent` | チケットがエスカレートされた |
| `TicketResolvedEvent` | チケットが解決された |
| `TicketClosedEvent` | チケットがクローズされた |
| `TicketReopenedEvent` | チケットが再オープンされた |
| `TicketPriorityChangedEvent` | 優先度が変更された |
| `DepartmentChangedEvent` | 部門が変更された |
| `TagAddedEvent` | タグが追加された |
| `TagRemovedEvent` | タグが削除された |

ホストアプリケーションでこれらのイベントを受信するには`IEscalatedEventDispatcher`を実装してください：

```csharp
public class MyEventHandler : IEscalatedEventDispatcher
{
    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct) where TEvent : class
    {
        if (@event is TicketCreatedEvent created)
        {
            // Handle new ticket
        }
    }
}

// Register in DI
services.AddSingleton<IEscalatedEventDispatcher, MyEventHandler>();
```

## APIエンドポイント

### カスタマー

| メソッド | ルート | 説明 |
|--------|-------|-------------|
| GET | `/support/tickets` | カスタマーチケットの一覧 |
| POST | `/support/tickets` | チケットを作成 |
| GET | `/support/tickets/{id}` | チケットを表示 |
| POST | `/support/tickets/{id}/reply` | チケットに返信 |
| POST | `/support/tickets/{id}/close` | チケットをクローズ |
| POST | `/support/tickets/{id}/reopen` | チケットを再オープン |

### エージェント

| メソッド | ルート | 説明 |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | フィルター付きチケットキュー |
| GET | `/support/agent/tickets/{id}` | チケット詳細 |
| POST | `/support/agent/tickets/{id}/reply` | 返信 |
| POST | `/support/agent/tickets/{id}/note` | 内部メモ |
| POST | `/support/agent/tickets/{id}/assign` | エージェントを割り当て |
| POST | `/support/agent/tickets/{id}/status` | ステータスを変更 |
| POST | `/support/agent/tickets/{id}/priority` | 優先度を変更 |
| POST | `/support/agent/tickets/{id}/macro` | マクロを適用 |
| POST | `/support/agent/tickets/bulk` | 一括操作 |
| GET | `/support/agent/tickets/dashboard` | エージェントのワークロード |

### 管理

| メソッド | ルート | 説明 |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | 部門を管理 |
| GET/POST | `/support/admin/tags` | タグを管理 |
| GET/POST | `/support/admin/sla-policies` | SLAポリシーを管理 |
| GET/POST | `/support/admin/escalation-rules` | エスカレーションルールを管理 |
| GET/POST | `/support/admin/webhooks` | Webhooksを管理 |
| GET/POST | `/support/admin/api-tokens` | APIトークンを管理 |
| GET/POST | `/support/admin/macros` | マクロを管理 |
| GET/POST | `/support/admin/automations` | 自動化を管理 |
| GET/POST | `/support/admin/custom-fields` | カスタムフィールドを管理 |
| GET/POST | `/support/admin/business-hours` | 営業時間 |
| GET/POST | `/support/admin/skills` | スキルを管理 |
| GET/POST | `/support/admin/roles` | ロールを管理 |
| GET | `/support/admin/audit-logs` | 監査ログを照会 |
| GET/POST | `/support/admin/settings` | アプリ設定 |
| POST | `/support/admin/tickets/{id}/merge` | チケットを統合 |
| POST | `/support/admin/tickets/{id}/split` | チケットを分割 |
| POST | `/support/admin/tickets/{id}/snooze` | チケットをスヌーズ |
| POST | `/support/admin/tickets/{id}/link` | チケットをリンク |

### ウィジェット（パブリック）

| メソッド | ルート | 説明 |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | ナレッジベースを検索 |
| POST | `/support/widget/tickets` | ゲストチケットを作成 |
| GET | `/support/widget/tickets/{token}` | ゲストトークンで検索 |
| POST | `/support/widget/tickets/{token}/reply` | ゲスト返信 |
| POST | `/support/widget/tickets/{token}/rate` | CSAT評価を送信 |
| POST | `/support/widget/kb/articles/{id}/feedback` | 記事フィードバック |

## リアルタイム更新

ライブチケット更新のためにSignalRを有効にします：

```json
{
  "Escalated": {
    "EnableRealTime": true
  }
}
```

```csharp
// Program.cs
app.MapHub<EscalatedHub>("/support/hub");
```

クライアントはチケット固有のグループに参加して更新を受信します：

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## ミドルウェア

### APIトークン認証

Bearerトークン認証でAPIエンドポイントを保護します：

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

トークンはSHA-256ハッシュとして保存されます。管理APIエンドポイント経由でトークンを作成してください。

### レート制限

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## テスト

```bash
dotnet test
```

テストはxUnitとMoqおよびEF Core InMemoryプロバイダーを使用しています。カバレッジには以下が含まれます：
- チケットのCRUDとステータス遷移
- SLA違反検出と警告
- チケットの分割、統合、スヌーズ
- 割り当てとワークロード計算
- Webhookサブスクリプションマッチング
- 2FAシークレットの生成と検証
- キャパシティ管理
- モデルバリデーションとenum動作

## 他のフレームワーク向けも提供

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- Laravel Composerパッケージ
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- Ruby on Railsエンジン
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- Django再利用可能アプリ
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- AdonisJS v6パッケージ
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- ASP.NET Coreパッケージ（現在のページ）
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- Vue 3 + Inertia.js UIコンポーネント

同じアーキテクチャ、同じVue UI -- すべての主要バックエンドフレームワーク向け。

## ライセンス

MIT
