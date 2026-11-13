<p align="center"><sub>
  <a href="README.ar.md">العربية</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="../../README.md">English</a> ·
  <a href="README.es.md">Español</a> ·
  <a href="README.fr.md">Français</a> ·
  <a href="README.it.md">Italiano</a> ·
  <a href="README.ja.md">日本語</a> ·
  <a href="README.ko.md">한국어</a> ·
  <a href="README.nl.md">Nederlands</a> ·
  <a href="README.pl.md">Polski</a> ·
  <a href="README.pt-BR.md">Português (BR)</a> ·
  <a href="README.ru.md">Русский</a> ·
  <a href="README.tr.md">Türkçe</a> ·
  <b>简体中文</b>
</sub></p>

<p align="center">
  <a href="https://escalated.dev"><img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/logo.png" width="72" height="72" alt="Escalated"></a>
</p>

<h1 align="center">Escalated for ASP.NET Core</h1>

<p align="center">
  为你的 ASP.NET Core 应用添加一套完整的帮助台。<br>
  工单、SLA、升级规则、客服收件箱和客户门户，通过 EF Core 存储在你选择的数据库中。
</p>

<p align="center">
  <a href="https://escalated.dev"><b>官网</b></a> ·
  <a href="https://escalated.dev/docs"><b>文档</b></a> ·
  <a href="https://escalated.dev/cloud"><b>Cloud</b></a> ·
  <a href="../../CHANGELOG.md"><b>更新日志</b></a>
</p>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-dark.png">
  <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-light.png" alt="Escalated 客服收件箱：带计数的已保存视图、SLA 倒计时以及每个工单的优先级" width="100%">
</picture>

## 功能概览

<table>
  <tr>
    <td width="40%">
      <h3>客服可以整天使用的收件箱</h3>
      对话、内部备注、预设回复和 SLA 计时器都在同一个界面上。工单也可以通过 Postmark、Mailgun 或 AWS SES 以邮件形式进入。
    </td>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-light.png" alt="在客服工作区中打开的工单及回复编辑器">
      </picture>
    </td>
  </tr>
  <tr>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-light.png" alt="管理后台的报表页面，显示响应时间、CSAT 和 SLA 违约情况">
      </picture>
    </td>
    <td width="40%">
      <h3>清晰可见的 SLA 和升级规则</h3>
      按优先级设定响应和解决目标并支持工作时间，目标延误时自动升级或重新分配，还提供违约报表。
    </td>
  </tr>
</table>

## 功能

<table>
<tr>
<td width="33%" valign="top">

**工单与收件箱**
- 包含状态流转的完整生命周期
- 队列筛选和批量操作
- 内部备注和预设回复
- 一键批量操作的宏
- 工单上的旁路对话
- 合并、关联和拆分工单
- 暂缓并自动唤醒
- 个人和共享的已保存视图
- 将工单关联到你应用中的实体
- 带大小限制的附件
- 每个工单的活动时间线

</td>
<td width="33%" valign="top">

**SLA 与路由**
- 按优先级设定的响应和解决目标
- 工作时间和违约检测
- 升级、调整优先级或重新分配的规则
- 基于时间的自动化
- 基于技能的路由
- 每个客服的容量上限

</td>
<td width="33%" valign="top">

**邮件**
- 通过 Postmark、Mailgun 和 AWS SES 接收邮件
- 签名 Reply-To 验证
- 只接受请求人的回复
- 基于 Message-ID 头的邮件线程

</td>
</tr>
<tr>
<td width="33%" valign="top">

**客户**
- 自助式客户门户
- 通过魔法令牌查询的访客工单
- 可嵌入的小部件 API
- 已解决工单的 CSAT 评分
- 支持搜索和反馈的知识库

</td>
<td width="33%" valign="top">

**管理与安全**
- 部门、SLA、规则和标签
- 角色和权限
- 带恢复码的 TOTP 双因素认证
- SAML 2、OAuth 2 / OIDC 和 JWT 登录
- 记录新旧值的审计日志
- 限定权限范围的 API 令牌

</td>
<td width="33%" valign="top">

**扩展与集成**
- 带重试的 HMAC 签名 Webhook
- 支持条件显示的自定义字段
- 适配器可替换的导入向导
- 通过 SignalR 实时更新（可选）
- 共享的 Vue 3 + Inertia 界面

</td>
</tr>
</table>

<p align="center"><sub><a href="../features.md">查看全部功能详情</a></sub></p>

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET"></a>
  <a href="https://vuejs.org"><img src="https://img.shields.io/badge/Vue.js-4FC08D?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue.js"></a>
  <a href="https://inertiajs.com"><img src="https://img.shields.io/badge/Inertia.js-9553E9?style=for-the-badge&logo=inertia&logoColor=white" alt="Inertia.js"></a>
  <a href="https://www.postgresql.org"><img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL"></a>
  <a href="https://www.sqlite.org"><img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite"></a>
</p>

## 快速开始

```bash
dotnet add package Escalated
dotnet ef database update --context EscalatedDbContext
```

在 `Program.cs` 中通过 `builder.Services.AddEscalated(...)` 和 `app.MapEscalated()` 注册，然后访问 `/support`。需要 .NET 8+ 以及 SQL Server、SQLite 或 PostgreSQL。[入门指南](../getting-started.md)介绍了配置和授权，[Inertia 界面](../inertia-ui.md)用于启用共享页面。

## 了解更多

- [全部功能](../features.md) · [入门](../getting-started.md) · [Inertia 界面](../inertia-ui.md)
- [自定义、实时更新和本地化](../customization.md) · [架构、模型和事件](../architecture.md) · [API 端点](../api-endpoints.md)
- [访问控制](../access-control.md) · [工单访问与路由](../ticket-access-and-routing.md) · [工作流](../workflows.md)
- [入站邮件](../inbound-email.md) · [通知邮件](../notification-emails.md) · [访客访问](../guest-access.md)
- [嵌入小部件](../embedding-the-widget.md) · [嵌入管理后台](../embedding-admin.md) · [客服面板](../agent-panel.md)
- [插件](../plugins.md) · [邮件通讯](../newsletters.md) · [开发与测试](../development.md)

Escalated 也提供 [Laravel](https://github.com/escalated-dev/escalated-laravel)、[Rails](https://github.com/escalated-dev/escalated-rails)、[Django](https://github.com/escalated-dev/escalated-django)、[NestJS](https://github.com/escalated-dev/escalated-nestjs)、[AdonisJS](https://github.com/escalated-dev/escalated-adonis)、[Symfony](https://github.com/escalated-dev/escalated-symfony)、[Phoenix](https://github.com/escalated-dev/escalated-phoenix)、[Go](https://github.com/escalated-dev/escalated-go)、[Spring](https://github.com/escalated-dev/escalated-spring)、[WordPress](https://github.com/escalated-dev/escalated-wordpress) 和 [Filament](https://github.com/escalated-dev/escalated-filament) 版本。界面代码位于 [escalated](https://github.com/escalated-dev/escalated)。

## 许可证

MIT。Copyright (c) 2026 Escalated.dev。详见 [LICENSE](../../LICENSE)。
