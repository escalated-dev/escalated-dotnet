<p align="center">
  <b>العربية</b> •
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
  <a href="README.zh-CN.md">简体中文</a>
</p>

# Escalated لـ ASP.NET Core

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

نظام تذاكر دعم كامل الميزات وقابل للتضمين لـ ASP.NET Core. أضفه إلى أي تطبيق واحصل على مكتب مساعدة كامل مع تتبع SLA وقواعد التصعيد وسير عمل الوكلاء وبوابة العملاء. لا حاجة لخدمات خارجية.

> **[escalated.dev](https://escalated.dev)** -- تعرف على المزيد، شاهد العروض التوضيحية، وقارن بين خيارات Cloud و Self-Hosted.

## الميزات

- **دورة حياة التذكرة** -- إنشاء، تعيين، رد، حل، إغلاق، إعادة فتح مع انتقالات حالة قابلة للتهيئة
- **محرك SLA** -- أهداف الاستجابة والحل حسب الأولوية، حساب ساعات العمل، كشف تلقائي للانتهاكات
- **قواعد التصعيد** -- قواعد قائمة على الشروط تقوم بالتصعيد وإعادة الترتيب وإعادة التعيين أو الإشعار تلقائياً
- **الأتمتة** -- قواعد قائمة على الوقت مع شروط وإجراءات
- **لوحة تحكم الوكيل** -- قائمة انتظار التذاكر مع فلاتر، إجراءات جماعية، ملاحظات داخلية، ردود جاهزة
- **بوابة العملاء** -- إنشاء تذاكر ذاتية الخدمة، ردود، وتتبع الحالة
- **لوحة الإدارة** -- إدارة الأقسام، سياسات SLA، قواعد التصعيد، العلامات والمزيد
- **الماكرو والردود الجاهزة** -- إجراءات مجمعة وقوالب رد قابلة لإعادة الاستخدام
- **حقول مخصصة** -- بيانات وصفية ديناميكية مع منطق عرض شرطي
- **قاعدة المعرفة** -- مقالات، فئات، بحث، وتغذية راجعة
- **مرفقات الملفات** -- دعم الرفع مع تخزين قابل للتهيئة وحدود الحجم
- **الجدول الزمني للنشاط** -- سجل تدقيق كامل لكل إجراء على كل تذكرة
- **Webhooks** -- موقعة بـ HMAC-SHA256 مع منطق إعادة المحاولة
- **رموز API** -- مصادقة Bearer مع نطاق قائم على القدرات
- **الأدوار والصلاحيات** -- تحكم دقيق في الوصول
- **سجل التدقيق** -- تسجيل جميع التغييرات مع القيم القديمة/الجديدة
- **نظام الاستيراد** -- معالج متعدد الخطوات مع محولات قابلة للتوصيل
- **المحادثات الجانبية** -- سلاسل نقاش داخلية للفريق على التذاكر
- **دمج وربط التذاكر** -- دمج التذاكر المكررة وربط المشكلات
- **تقسيم التذاكر** -- تقسيم رد إلى تذكرة جديدة
- **تأجيل التذاكر** -- تأجيل حتى تاريخ مستقبلي مع خدمة إيقاظ في الخلفية
- **ترابط البريد الإلكتروني** -- عناوين In-Reply-To/References/Message-ID للترابط الصحيح
- **العروض المحفوظة** -- إعدادات فلاتر مسبقة شخصية ومشتركة
- **API القطعة القابلة للتضمين** -- نقاط نهاية عامة للبحث في قاعدة المعرفة، تذاكر الضيوف، استعلام الحالة
- **تحديثات فورية** -- محاور SignalR لتحديثات التذاكر المباشرة (اختياري)
- **إدارة السعة** -- حدود عبء العمل لكل وكيل حسب القناة
- **التوجيه القائم على المهارات** -- مطابقة الوكلاء مع التذاكر حسب علامات المهارات
- **تقييمات CSAT** -- استطلاعات الرضا على التذاكر المحلولة
- **2FA** -- إعداد والتحقق من TOTP مع رموز الاسترداد
- **وصول الضيوف** -- إنشاء تذاكر مجهولة مع البحث بالرمز السحري
- **Inertia.js + Vue 3 UI** -- واجهة أمامية مشتركة عبر [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)

## المتطلبات

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server أو SQLite أو PostgreSQL
- Node.js 18+ (لموارد الواجهة الأمامية)

## البدء السريع

### 1. تثبيت الحزمة

```bash
dotnet add package Escalated
```

### 2. تسجيل الخدمات

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

### 3. التهيئة

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

### 4. تشغيل عمليات الترحيل

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

قم بزيارة `/support` -- النظام يعمل الآن.

## تكامل الواجهة الأمامية

يوفر Escalated مكتبة مكونات Vue وصفحات افتراضية عبر حزمة npm [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated). ادمج مع Inertia.js لعرض SPA سلس داخل تخطيطك الحالي.

```bash
npm install @escalated-dev/escalated
```

## البنية المعمارية

```
src/Escalated/
  Models/           # أكثر من 40 نموذج كيان EF Core
  Data/             # EscalatedDbContext مع تعيين كامل للعلاقات
  Services/         # منطق الأعمال (التذاكر، SLA، الدمج، التقسيم، التأجيل، إلخ)
  Controllers/
    Admin/          # API لوحة الإدارة (CRUD لجميع الإعدادات)
    Agent/          # قائمة انتظار التذاكر وإجراءات الوكيل
    Customer/       # بوابة الخدمة الذاتية للعملاء
    Widget/         # API القطعة العامة (بحث قاعدة المعرفة، تذاكر الضيوف)
  Middleware/       # مصادقة رمز API، الصلاحيات، تحديد المعدل
  Events/           # أحداث المجال (TicketCreated، SlaBreached، إلخ)
  Notifications/    # واجهات وقوالب إشعارات البريد الإلكتروني
  Configuration/    # تسجيل DI، الخيارات، تعيين نقاط النهاية
  Hubs/             # محور SignalR للتحديثات الفورية
  Enums/            # TicketStatus، TicketPriority، ActivityType
```

## النماذج

يتضمن Escalated أكثر من 40 كيان EF Core تغطي مجال مكتب المساعدة بالكامل:

| الفئة | النماذج |
|----------|--------|
| الأساسي | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| الوكلاء | AgentProfile, AgentCapacity, Skill, AgentSkill |
| المراسلة | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| الإدارة | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| مخصص | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| الاستيراد | ImportJob, ImportSourceMap |
| التهيئة | EscalatedSettings, SavedView |
| قاعدة المعرفة | Article, ArticleCategory |

جميع النماذج تحتوي على علاقات وفهارس وفلاتر استعلام مُهيأة بشكل صحيح في `EscalatedDbContext`.

## الخدمات

| الخدمة | المسؤولية |
|---------|---------------|
| `TicketService` | CRUD كامل للتذاكر، انتقالات الحالة، الردود، العلامات، الأقسام |
| `SlaService` | إرفاق السياسات، كشف الانتهاكات، فحص التحذيرات، تسجيل الاستجابة الأولى |
| `AssignmentService` | تعيين الوكلاء، إلغاء التعيين، التعيين التلقائي حسب عبء العمل |
| `EscalationService` | تقييم القواعد القائمة على الشروط، تنفيذ إجراءات التصعيد |
| `AutomationRunner` | تقييم الأتمتة القائمة على الوقت وتنفيذ الإجراءات |
| `MacroService` | تطبيق تسلسلات إجراءات الماكرو على التذاكر |
| `TicketMergeService` | دمج المصدر في الهدف مع نقل الردود |
| `TicketSplitService` | تقسيم رد إلى تذكرة مرتبطة جديدة |
| `TicketSnoozeService` | تأجيل/إيقاظ مع خدمة إيقاظ في الخلفية |
| `WebhookDispatcher` | تسليم webhooks موقعة بـ HMAC مع منطق إعادة المحاولة |
| `CapacityService` | حدود التذاكر المتزامنة لكل وكيل |
| `SkillRoutingService` | مطابقة الوكلاء حسب المهارات مع علامات التذاكر |
| `BusinessHoursCalculator` | حسابات تواريخ ساعات العمل مع دعم العطلات |
| `TwoFactorService` | توليد أسرار TOTP، التحقق، رموز الاسترداد |
| `AuditLogService` | تسجيل واستعلام تغييرات الكيانات |
| `KnowledgeBaseService` | CRUD المقالات/الفئات، البحث، التغذية الراجعة |
| `SavedViewService` | إعدادات فلاتر مسبقة شخصية ومشتركة |
| `SideConversationService` | محادثات داخلية مترابطة على التذاكر |
| `ImportService` | استيراد متعدد الخطوات مع محولات قابلة للتوصيل |
| `SettingsService` | مخزن إعدادات مفتاح-قيمة |

## الأحداث

كل إجراء على التذكرة يُصدر حدث مجال:

| الحدث | متى |
|-------|------|
| `TicketCreatedEvent` | تذكرة جديدة تم إنشاؤها |
| `TicketStatusChangedEvent` | انتقال الحالة |
| `TicketAssignedEvent` | تم تعيين وكيل |
| `TicketUnassignedEvent` | تم إزالة الوكيل |
| `ReplyCreatedEvent` | تمت إضافة رد عام |
| `InternalNoteAddedEvent` | تمت إضافة ملاحظة الوكيل |
| `SlaBreachedEvent` | تم تجاوز موعد SLA |
| `SlaWarningEvent` | موعد SLA يقترب |
| `TicketEscalatedEvent` | تم تصعيد التذكرة |
| `TicketResolvedEvent` | تم حل التذكرة |
| `TicketClosedEvent` | تم إغلاق التذكرة |
| `TicketReopenedEvent` | تم إعادة فتح التذكرة |
| `TicketPriorityChangedEvent` | تم تغيير الأولوية |
| `DepartmentChangedEvent` | تم تغيير القسم |
| `TagAddedEvent` | تمت إضافة علامة |
| `TagRemovedEvent` | تمت إزالة علامة |

نفّذ `IEscalatedEventDispatcher` لاستقبال هذه الأحداث في تطبيقك المضيف:

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

## نقاط نهاية API

### العميل

| الطريقة | المسار | الوصف |
|--------|-------|-------------|
| GET | `/support/tickets` | عرض قائمة تذاكر العميل |
| POST | `/support/tickets` | إنشاء تذكرة |
| GET | `/support/tickets/{id}` | عرض التذكرة |
| POST | `/support/tickets/{id}/reply` | الرد على التذكرة |
| POST | `/support/tickets/{id}/close` | إغلاق التذكرة |
| POST | `/support/tickets/{id}/reopen` | إعادة فتح التذكرة |

### الوكيل

| الطريقة | المسار | الوصف |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | قائمة انتظار التذاكر مع فلاتر |
| GET | `/support/agent/tickets/{id}` | تفاصيل التذكرة |
| POST | `/support/agent/tickets/{id}/reply` | الرد |
| POST | `/support/agent/tickets/{id}/note` | ملاحظة داخلية |
| POST | `/support/agent/tickets/{id}/assign` | تعيين وكيل |
| POST | `/support/agent/tickets/{id}/status` | تغيير الحالة |
| POST | `/support/agent/tickets/{id}/priority` | تغيير الأولوية |
| POST | `/support/agent/tickets/{id}/macro` | تطبيق ماكرو |
| POST | `/support/agent/tickets/bulk` | إجراءات جماعية |
| GET | `/support/agent/tickets/dashboard` | عبء عمل الوكيل |

### الإدارة

| الطريقة | المسار | الوصف |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | إدارة الأقسام |
| GET/POST | `/support/admin/tags` | إدارة العلامات |
| GET/POST | `/support/admin/sla-policies` | إدارة سياسات SLA |
| GET/POST | `/support/admin/escalation-rules` | إدارة قواعد التصعيد |
| GET/POST | `/support/admin/webhooks` | إدارة webhooks |
| GET/POST | `/support/admin/api-tokens` | إدارة رموز API |
| GET/POST | `/support/admin/macros` | إدارة الماكرو |
| GET/POST | `/support/admin/automations` | إدارة الأتمتة |
| GET/POST | `/support/admin/custom-fields` | إدارة الحقول المخصصة |
| GET/POST | `/support/admin/business-hours` | ساعات العمل |
| GET/POST | `/support/admin/skills` | إدارة المهارات |
| GET/POST | `/support/admin/roles` | إدارة الأدوار |
| GET | `/support/admin/audit-logs` | استعلام سجلات التدقيق |
| GET/POST | `/support/admin/settings` | إعدادات التطبيق |
| POST | `/support/admin/tickets/{id}/merge` | دمج التذاكر |
| POST | `/support/admin/tickets/{id}/split` | تقسيم التذكرة |
| POST | `/support/admin/tickets/{id}/snooze` | تأجيل التذكرة |
| POST | `/support/admin/tickets/{id}/link` | ربط التذاكر |

### القطعة (عام)

| الطريقة | المسار | الوصف |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | البحث في قاعدة المعرفة |
| POST | `/support/widget/tickets` | إنشاء تذكرة ضيف |
| GET | `/support/widget/tickets/{token}` | البحث برمز الضيف |
| POST | `/support/widget/tickets/{token}/reply` | رد الضيف |
| POST | `/support/widget/tickets/{token}/rate` | إرسال تقييم CSAT |
| POST | `/support/widget/kb/articles/{id}/feedback` | تغذية راجعة للمقال |

## التحديثات الفورية

فعّل SignalR لتحديثات التذاكر المباشرة:

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

ينضم العملاء إلى مجموعات خاصة بالتذاكر لاستقبال التحديثات:

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## البرمجيات الوسيطة

### مصادقة رمز API

احمِ نقاط نهاية API بمصادقة رمز Bearer:

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

يتم تخزين الرموز كتجزئات SHA-256. أنشئ الرموز عبر نقطة نهاية إدارة API.

### تحديد المعدل

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## الاختبارات

```bash
dotnet test
```

تستخدم الاختبارات xUnit مع Moq وموفر InMemory لـ EF Core. تشمل التغطية:
- CRUD التذاكر وانتقالات الحالة
- كشف انتهاكات SLA والتحذيرات
- تقسيم ودمج وتأجيل التذاكر
- التعيين وحساب عبء العمل
- مطابقة اشتراكات webhook
- توليد والتحقق من أسرار 2FA
- إدارة السعة
- التحقق من النماذج وسلوك التعدادات

## متوفر أيضاً لـ

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- حزمة Composer لـ Laravel
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- محرك Ruby on Rails
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- تطبيق Django قابل لإعادة الاستخدام
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- حزمة AdonisJS v6
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- حزمة ASP.NET Core (أنت هنا)
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- مكونات واجهة Vue 3 + Inertia.js

نفس البنية المعمارية، نفس واجهة Vue -- لجميع أطر العمل الخلفية الرئيسية.

## الرخصة

MIT
