<p align="center">
  <a href="README.ar.md">العربية</a> •
  <a href="README.de.md">Deutsch</a> •
  <a href="../../README.md">English</a> •
  <a href="README.es.md">Español</a> •
  <a href="README.fr.md">Français</a> •
  <a href="README.it.md">Italiano</a> •
  <a href="README.ja.md">日本語</a> •
  <a href="README.ko.md">한국어</a> •
  <a href="README.nl.md">Nederlands</a> •
  <a href="README.pl.md">Polski</a> •
  <a href="README.pt-BR.md">Português (BR)</a> •
  <a href="README.ru.md">Русский</a> •
  <a href="README.tr.md">Türkçe</a> •
  <b>简体中文</b>
</p>

# Escalated - ASP.NET Core 版

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

一个功能完备的、可嵌入的 ASP.NET Core 支持工单系统。将其添加到任何应用中，即可获得包含 SLA 跟踪、升级规则、客服工作流和客户门户的完整服务台。无需外部服务。

> **[escalated.dev](https://escalated.dev)** -- 了解更多、查看演示、比较 Cloud 和 Self-Hosted 选项。

## 功能特性

- **工单生命周期** -- 创建、分配、回复、解决、关闭、重新打开，支持可配置的状态转换
- **SLA 引擎** -- 按优先级的响应和解决目标、工作时间计算、自动违规检测
- **升级规则** -- 基于条件的规则，自动升级、重新排列优先级、重新分配或通知
- **自动化** -- 带条件和操作的基于时间的规则
- **客服面板** -- 带过滤器、批量操作、内部备注、预设回复的工单队列
- **客户门户** -- 自助工单创建、回复和状态跟踪
- **管理面板** -- 管理部门、SLA 策略、升级规则、标签等
- **宏和预设回复** -- 批量操作和可重用的回复模板
- **自定义字段** -- 带条件显示逻辑的动态元数据
- **知识库** -- 文章、分类、搜索和反馈
- **文件附件** -- 支持上传，可配置存储和大小限制
- **活动时间线** -- 每个工单上每个操作的完整审计日志
- **Webhooks** -- HMAC-SHA256 签名，带重试逻辑
- **API 令牌** -- 基于能力范围的 Bearer 认证
- **角色和权限** -- 细粒度的访问控制
- **审计日志** -- 记录所有变更及旧值/新值
- **导入系统** -- 带可插拔适配器的多步骤向导
- **附属对话** -- 工单上的内部团队讨论串
- **工单合并和关联** -- 合并重复工单并关联问题
- **工单拆分** -- 将回复拆分为新工单
- **工单暂停** -- 暂停到未来日期，带后台唤醒服务
- **邮件线程** -- In-Reply-To/References/Message-ID 头部，用于正确的线程化
- **保存的视图** -- 个人和共享的过滤器预设
- **可嵌入小部件 API** -- 用于知识库搜索、访客工单、状态查询的公共端点
- **实时更新** -- 用于实时工单更新的 SignalR 集线器（可选）
- **容量管理** -- 按渠道的每个客服工作负载限制
- **基于技能的路由** -- 按技能标签将客服匹配到工单
- **CSAT 评分** -- 已解决工单的满意度调查
- **2FA** -- 带恢复码的 TOTP 设置和验证
- **访客访问** -- 通过魔法令牌查找进行匿名工单创建
- **Inertia.js + Vue 3 UI** -- 通过 [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated) 共享前端

## 环境要求

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server、SQLite 或 PostgreSQL
- Node.js 18+（用于前端资源）

## 快速开始

### 1. 安装包

```bash
dotnet add package Escalated
```

### 2. 注册服务

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

### 3. 配置

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

### 4. 运行迁移

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

访问 `/support` -- 已上线运行。

## 前端集成

Escalated 通过 npm 包 [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated) 提供 Vue 组件库和默认页面。与 Inertia.js 集成，在现有布局中实现无缝 SPA 渲染。

```bash
npm install @escalated-dev/escalated
```

## 架构

```
src/Escalated/
  Models/           # 40+ EF Core 实体模型
  Data/             # 具有完整关系映射的 EscalatedDbContext
  Services/         # 业务逻辑（工单、SLA、合并、拆分、暂停等）
  Controllers/
    Admin/          # 管理面板 API（所有设置的 CRUD）
    Agent/          # 工单队列和客服操作
    Customer/       # 客户自助服务门户
    Widget/         # 公共小部件 API（知识库搜索、访客工单）
  Middleware/       # API 令牌认证、权限、速率限制
  Events/           # 领域事件（TicketCreated、SlaBreached 等）
  Notifications/    # 邮件通知接口和模板
  Configuration/    # DI 注册、选项、端点映射
  Hubs/             # 用于实时更新的 SignalR 集线器
  Enums/            # TicketStatus、TicketPriority、ActivityType
```

## 模型

Escalated 包含 40 多个 EF Core 实体，覆盖完整的服务台领域：

| 类别 | 模型 |
|----------|--------|
| 核心 | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| 客服 | AgentProfile, AgentCapacity, Skill, AgentSkill |
| 消息 | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| 管理 | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| 自定义 | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| 导入 | ImportJob, ImportSourceMap |
| 配置 | EscalatedSettings, SavedView |
| 知识库 | Article, ArticleCategory |

所有模型在 `EscalatedDbContext` 中都配置了正确的关系、索引和查询过滤器。

## 服务

| 服务 | 职责 |
|---------|---------------|
| `TicketService` | 完整的工单 CRUD、状态转换、回复、标签、部门 |
| `SlaService` | 策略附加、违规检测、警告检查、首次响应记录 |
| `AssignmentService` | 客服分配、取消分配、按工作负载自动分配 |
| `EscalationService` | 评估基于条件的规则、执行升级操作 |
| `AutomationRunner` | 基于时间的自动化评估和操作执行 |
| `MacroService` | 将宏操作序列应用于工单 |
| `TicketMergeService` | 将源合并到目标并转移回复 |
| `TicketSplitService` | 将回复拆分为新的关联工单 |
| `TicketSnoozeService` | 带后台唤醒服务的暂停/取消暂停 |
| `WebhookDispatcher` | 带重试逻辑的 HMAC 签名 webhook 投递 |
| `CapacityService` | 每个客服的并发工单限制 |
| `SkillRoutingService` | 按技能将客服匹配到工单标签 |
| `BusinessHoursCalculator` | 支持节假日的工作时间日期计算 |
| `TwoFactorService` | TOTP 密钥生成、验证、恢复码 |
| `AuditLogService` | 记录和查询实体变更 |
| `KnowledgeBaseService` | 文章/分类 CRUD、搜索、反馈 |
| `SavedViewService` | 个人和共享的过滤器预设 |
| `SideConversationService` | 工单上的内部线程对话 |
| `ImportService` | 带可插拔适配器的多步骤导入 |
| `SettingsService` | 键值设置存储 |

## 事件

每个工单操作都会触发一个领域事件：

| 事件 | 触发时机 |
|-------|------|
| `TicketCreatedEvent` | 新工单已创建 |
| `TicketStatusChangedEvent` | 状态转换 |
| `TicketAssignedEvent` | 客服已分配 |
| `TicketUnassignedEvent` | 客服已移除 |
| `ReplyCreatedEvent` | 公开回复已添加 |
| `InternalNoteAddedEvent` | 客服备注已添加 |
| `SlaBreachedEvent` | SLA 截止期限已违反 |
| `SlaWarningEvent` | SLA 截止期限即将到来 |
| `TicketEscalatedEvent` | 工单已升级 |
| `TicketResolvedEvent` | 工单已解决 |
| `TicketClosedEvent` | 工单已关闭 |
| `TicketReopenedEvent` | 工单已重新打开 |
| `TicketPriorityChangedEvent` | 优先级已更改 |
| `DepartmentChangedEvent` | 部门已更改 |
| `TagAddedEvent` | 标签已添加 |
| `TagRemovedEvent` | 标签已移除 |

实现 `IEscalatedEventDispatcher` 以在您的宿主应用程序中接收这些事件：

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

## API 端点

### 客户

| 方法 | 路由 | 描述 |
|--------|-------|-------------|
| GET | `/support/tickets` | 列出客户工单 |
| POST | `/support/tickets` | 创建工单 |
| GET | `/support/tickets/{id}` | 查看工单 |
| POST | `/support/tickets/{id}/reply` | 回复工单 |
| POST | `/support/tickets/{id}/close` | 关闭工单 |
| POST | `/support/tickets/{id}/reopen` | 重新打开工单 |

### 客服

| 方法 | 路由 | 描述 |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | 带过滤器的工单队列 |
| GET | `/support/agent/tickets/{id}` | 工单详情 |
| POST | `/support/agent/tickets/{id}/reply` | 回复 |
| POST | `/support/agent/tickets/{id}/note` | 内部备注 |
| POST | `/support/agent/tickets/{id}/assign` | 分配客服 |
| POST | `/support/agent/tickets/{id}/status` | 更改状态 |
| POST | `/support/agent/tickets/{id}/priority` | 更改优先级 |
| POST | `/support/agent/tickets/{id}/macro` | 应用宏 |
| POST | `/support/agent/tickets/bulk` | 批量操作 |
| GET | `/support/agent/tickets/dashboard` | 客服工作负载 |

### 管理

| 方法 | 路由 | 描述 |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | 管理部门 |
| GET/POST | `/support/admin/tags` | 管理标签 |
| GET/POST | `/support/admin/sla-policies` | 管理 SLA 策略 |
| GET/POST | `/support/admin/escalation-rules` | 管理升级规则 |
| GET/POST | `/support/admin/webhooks` | 管理 webhooks |
| GET/POST | `/support/admin/api-tokens` | 管理 API 令牌 |
| GET/POST | `/support/admin/macros` | 管理宏 |
| GET/POST | `/support/admin/automations` | 管理自动化 |
| GET/POST | `/support/admin/custom-fields` | 管理自定义字段 |
| GET/POST | `/support/admin/business-hours` | 工作时间 |
| GET/POST | `/support/admin/skills` | 管理技能 |
| GET/POST | `/support/admin/roles` | 管理角色 |
| GET | `/support/admin/audit-logs` | 查询审计日志 |
| GET/POST | `/support/admin/settings` | 应用设置 |
| POST | `/support/admin/tickets/{id}/merge` | 合并工单 |
| POST | `/support/admin/tickets/{id}/split` | 拆分工单 |
| POST | `/support/admin/tickets/{id}/snooze` | 暂停工单 |
| POST | `/support/admin/tickets/{id}/link` | 关联工单 |

### 小部件（公共）

| 方法 | 路由 | 描述 |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | 搜索知识库 |
| POST | `/support/widget/tickets` | 创建访客工单 |
| GET | `/support/widget/tickets/{token}` | 通过访客令牌查找 |
| POST | `/support/widget/tickets/{token}/reply` | 访客回复 |
| POST | `/support/widget/tickets/{token}/rate` | 提交 CSAT 评分 |
| POST | `/support/widget/kb/articles/{id}/feedback` | 文章反馈 |

## 实时更新

启用 SignalR 以获取实时工单更新：

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

客户端加入特定工单的组以接收更新：

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## 中间件

### API 令牌认证

使用 Bearer 令牌认证保护 API 端点：

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

令牌以 SHA-256 哈希形式存储。通过管理 API 端点创建令牌。

### 速率限制

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## 测试

```bash
dotnet test
```

测试使用 xUnit 配合 Moq 和 EF Core InMemory 提供程序。覆盖范围包括：
- 工单 CRUD 和状态转换
- SLA 违规检测和警告
- 工单拆分、合并和暂停
- 分配和工作负载计算
- Webhook 订阅匹配
- 2FA 密钥生成和验证
- 容量管理
- 模型验证和枚举行为

## 其他框架版本

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- Laravel Composer 包
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- Ruby on Rails 引擎
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- Django 可重用应用
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- AdonisJS v6 包
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- ASP.NET Core 包（当前页面）
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- Vue 3 + Inertia.js UI 组件

相同的架构，相同的 Vue UI -- 适用于所有主要后端框架。

## 许可证

MIT
