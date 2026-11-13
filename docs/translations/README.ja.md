<p align="center"><sub>
  <a href="README.ar.md">العربية</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="../../README.md">English</a> ·
  <a href="README.es.md">Español</a> ·
  <a href="README.fr.md">Français</a> ·
  <a href="README.it.md">Italiano</a> ·
  <b>日本語</b> ·
  <a href="README.ko.md">한국어</a> ·
  <a href="README.nl.md">Nederlands</a> ·
  <a href="README.pl.md">Polski</a> ·
  <a href="README.pt-BR.md">Português (BR)</a> ·
  <a href="README.ru.md">Русский</a> ·
  <a href="README.tr.md">Türkçe</a> ·
  <a href="README.zh-CN.md">简体中文</a>
</sub></p>

<p align="center">
  <a href="https://escalated.dev"><img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/logo.png" width="72" height="72" alt="Escalated"></a>
</p>

<h1 align="center">Escalated for ASP.NET Core</h1>

<p align="center">
  ASP.NET Core アプリに本格的なヘルプデスクを追加できます。<br>
  チケット、SLA、エスカレーションルール、エージェント用の受信トレイ、カスタマーポータルを、EF Core を通じて任意のデータベースに保存します。
</p>

<p align="center">
  <a href="https://escalated.dev"><b>ウェブサイト</b></a> ·
  <a href="https://escalated.dev/docs"><b>ドキュメント</b></a> ·
  <a href="https://escalated.dev/cloud"><b>Cloud</b></a> ·
  <a href="../../CHANGELOG.md"><b>変更履歴</b></a>
</p>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-dark.png">
  <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-light.png" alt="Escalated のエージェント用受信トレイ: 件数付きの保存済みビュー、SLA のカウントダウン、各チケットの優先度" width="100%">
</picture>

## できること

<table>
  <tr>
    <td width="40%">
      <h3>エージェントが一日中作業できる受信トレイ</h3>
      会話、内部メモ、定型返信、SLA タイマーを 1 つの画面にまとめています。チケットは Postmark、Mailgun、AWS SES 経由のメールでも届きます。
    </td>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-light.png" alt="返信エディター付きのエージェントワークスペースで開いたチケット">
      </picture>
    </td>
  </tr>
  <tr>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-light.png" alt="応答時間、CSAT、SLA 違反を表示する管理画面のレポートページ">
      </picture>
    </td>
    <td width="40%">
      <h3>ひと目でわかる SLA とエスカレーションルール</h3>
      営業時間を考慮した優先度ごとの応答・解決目標、目標に遅れたときにエスカレーションや再割り当てを行うルール、違反に関するレポートを備えています。
    </td>
  </tr>
</table>

## 機能

<table>
<tr>
<td width="33%" valign="top">

**チケットと受信トレイ**
- ステータス遷移を含むライフサイクル全体
- キューのフィルターと一括操作
- 内部メモと定型返信
- ワンクリックで一括操作できるマクロ
- チケット上のサイド会話
- チケットの統合、リンク、分割
- 自動で再開するスヌーズ
- 個人用と共有の保存済みビュー
- 自分のアプリのエンティティとチケットを関連付け
- サイズ制限付きの添付ファイル
- 各チケットのアクティビティタイムライン

</td>
<td width="33%" valign="top">

**SLA とルーティング**
- 優先度ごとの応答・解決目標
- 営業時間と違反の検出
- エスカレーション、優先度変更、再割り当てを行うルール
- 時間ベースの自動化
- スキルベースのルーティング
- エージェントごとの対応上限

</td>
<td width="33%" valign="top">

**メール**
- Postmark、Mailgun、AWS SES からの受信
- 署名付き Reply-To の検証
- 依頼者からの返信のみ受け付け
- Message-ID ヘッダーによるスレッド化

</td>
</tr>
<tr>
<td width="33%" valign="top">

**顧客**
- セルフサービスのカスタマーポータル
- マジックトークンで照会できるゲストチケット
- 埋め込み可能なウィジェット API
- 解決済みチケットの CSAT 評価
- 検索とフィードバック付きのナレッジベース

</td>
<td width="33%" valign="top">

**管理とセキュリティ**
- 部署、SLA、ルール、タグ
- ロールと権限
- リカバリーコード付きの TOTP 二要素認証
- SAML 2、OAuth 2 / OIDC、JWT によるサインイン
- 変更前後の値を記録する監査ログ
- スコープ付き権限の API トークン

</td>
<td width="33%" valign="top">

**拡張と連携**
- 再試行付きの HMAC 署名 Webhook
- 条件付き表示に対応したカスタムフィールド
- アダプターを差し替えられるインポートウィザード
- SignalR によるリアルタイム更新 (オプトイン)
- 共通の Vue 3 + Inertia UI

</td>
</tr>
</table>

<p align="center"><sub><a href="../features.md">すべての機能の詳細</a></sub></p>

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET"></a>
  <a href="https://vuejs.org"><img src="https://img.shields.io/badge/Vue.js-4FC08D?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue.js"></a>
  <a href="https://inertiajs.com"><img src="https://img.shields.io/badge/Inertia.js-9553E9?style=for-the-badge&logo=inertia&logoColor=white" alt="Inertia.js"></a>
  <a href="https://www.postgresql.org"><img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL"></a>
  <a href="https://www.sqlite.org"><img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite"></a>
</p>

## クイックスタート

```bash
dotnet add package Escalated
dotnet ef database update --context EscalatedDbContext
```

`Program.cs` で `builder.Services.AddEscalated(...)` と `app.MapEscalated()` を使って登録し、`/support` にアクセスしてください。.NET 8 以上と、SQL Server、SQLite、PostgreSQL のいずれかが必要です。設定と認可については[スタートガイド](../getting-started.md)を、共通画面の有効化については [Inertia UI](../inertia-ui.md) を参照してください。

## 詳しく知る

- [すべての機能](../features.md) · [はじめに](../getting-started.md) · [Inertia UI](../inertia-ui.md)
- [カスタマイズ、リアルタイム、ローカライズ](../customization.md) · [アーキテクチャ、モデル、イベント](../architecture.md) · [API エンドポイント](../api-endpoints.md)
- [アクセス制御](../access-control.md) · [チケットのアクセスとルーティング](../ticket-access-and-routing.md) · [ワークフロー](../workflows.md)
- [受信メール](../inbound-email.md) · [通知メール](../notification-emails.md) · [ゲストアクセス](../guest-access.md)
- [ウィジェットの埋め込み](../embedding-the-widget.md) · [管理画面の埋め込み](../embedding-admin.md) · [エージェントパネル](../agent-panel.md)
- [プラグイン](../plugins.md) · [ニュースレター](../newsletters.md) · [開発とテスト](../development.md)

Escalated は [Laravel](https://github.com/escalated-dev/escalated-laravel)、[Rails](https://github.com/escalated-dev/escalated-rails)、[Django](https://github.com/escalated-dev/escalated-django)、[NestJS](https://github.com/escalated-dev/escalated-nestjs)、[AdonisJS](https://github.com/escalated-dev/escalated-adonis)、[Symfony](https://github.com/escalated-dev/escalated-symfony)、[Phoenix](https://github.com/escalated-dev/escalated-phoenix)、[Go](https://github.com/escalated-dev/escalated-go)、[Spring](https://github.com/escalated-dev/escalated-spring)、[WordPress](https://github.com/escalated-dev/escalated-wordpress)、[Filament](https://github.com/escalated-dev/escalated-filament) 向けにも提供しています。UI は [escalated](https://github.com/escalated-dev/escalated) にあります。

## ライセンス

MIT。Copyright (c) 2026 Escalated.dev。詳細は [LICENSE](../../LICENSE) を参照してください。
