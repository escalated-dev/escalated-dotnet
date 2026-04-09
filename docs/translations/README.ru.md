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
  <b>Русский</b> •
  <a href="README.tr.md">Türkçe</a> •
  <a href="README.zh-CN.md">简体中文</a>
</p>

# Escalated для ASP.NET Core

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Полнофункциональная встраиваемая система тикетов поддержки для ASP.NET Core. Добавьте в любое приложение и получите полноценный хелпдеск с отслеживанием SLA, правилами эскалации, рабочими процессами агентов и клиентским порталом. Внешние сервисы не требуются.

> **[escalated.dev](https://escalated.dev)** -- Узнайте больше, посмотрите демо и сравните варианты Cloud и Self-Hosted.

## Возможности

- **Жизненный цикл тикета** -- Создание, назначение, ответ, решение, закрытие, повторное открытие с настраиваемыми переходами статусов
- **Движок SLA** -- Цели ответа и решения по приоритетам, расчёт рабочих часов, автоматическое обнаружение нарушений
- **Правила эскалации** -- Правила на основе условий для автоматической эскалации, смены приоритета, переназначения или уведомления
- **Автоматизации** -- Правила на основе времени с условиями и действиями
- **Панель агента** -- Очередь тикетов с фильтрами, массовые действия, внутренние заметки, шаблонные ответы
- **Клиентский портал** -- Самостоятельное создание тикетов, ответы и отслеживание статуса
- **Панель администратора** -- Управление отделами, политиками SLA, правилами эскалации, тегами и многим другим
- **Макросы и шаблонные ответы** -- Пакетные действия и многоразовые шаблоны ответов
- **Пользовательские поля** -- Динамические метаданные с условной логикой отображения
- **База знаний** -- Статьи, категории, поиск и обратная связь
- **Вложения файлов** -- Поддержка загрузки с настраиваемым хранилищем и ограничениями размера
- **Хронология активности** -- Полный журнал аудита каждого действия по каждому тикету
- **Webhooks** -- Подписанные HMAC-SHA256 с логикой повторных попыток
- **API-токены** -- Bearer-аутентификация с ограничением по возможностям
- **Роли и разрешения** -- Детальный контроль доступа
- **Журнал аудита** -- Все мутации записываются со старыми/новыми значениями
- **Система импорта** -- Многошаговый мастер с подключаемыми адаптерами
- **Побочные разговоры** -- Внутренние потоки команды в тикетах
- **Объединение и связывание тикетов** -- Объединение дубликатов и связывание инцидентов
- **Разделение тикетов** -- Выделение ответа в новый тикет
- **Отложенные тикеты** -- Откладывание до будущей даты с фоновой службой пробуждения
- **Потоки электронной почты** -- Заголовки In-Reply-To/References/Message-ID для правильной группировки
- **Сохранённые представления** -- Личные и общие предустановки фильтров
- **API встраиваемого виджета** -- Публичные эндпоинты для поиска по KB, гостевых тикетов, проверки статуса
- **Обновления в реальном времени** -- Хабы SignalR для live-обновлений тикетов (опционально)
- **Управление ёмкостью** -- Лимиты нагрузки на агента по каналам
- **Маршрутизация по навыкам** -- Назначение агентов на тикеты по тегам навыков
- **Оценки CSAT** -- Опросы удовлетворённости по решённым тикетам
- **2FA** -- Настройка и проверка TOTP с кодами восстановления
- **Гостевой доступ** -- Анонимное создание тикетов с поиском по магическому токену
- **Inertia.js + Vue 3 UI** -- Общий фронтенд через [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)

## Требования

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server, SQLite или PostgreSQL
- Node.js 18+ (для фронтенд-ресурсов)

## Быстрый Старт

### 1. Установить Пакет

```bash
dotnet add package Escalated
```

### 2. Зарегистрировать Сервисы

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

### 3. Настроить

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

### 4. Выполнить Миграции

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

Перейдите на `/support` -- всё работает.

## Интеграция Фронтенда

Escalated предоставляет библиотеку Vue-компонентов и страницы по умолчанию через npm-пакет [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated). Интегрируйте с Inertia.js для бесшовного SPA-рендеринга в вашем существующем макете.

```bash
npm install @escalated-dev/escalated
```

## Архитектура

```
src/Escalated/
  Models/           # Более 40 моделей сущностей EF Core
  Data/             # EscalatedDbContext с полным маппингом связей
  Services/         # Бизнес-логика (тикет, SLA, объединение, разделение, откладывание и т.д.)
  Controllers/
    Admin/          # API панели администратора (CRUD для всех настроек)
    Agent/          # Очередь тикетов и действия агента
    Customer/       # Клиентский портал самообслуживания
    Widget/         # Публичный API виджета (поиск по KB, гостевые тикеты)
  Middleware/       # Аутентификация по API-токену, разрешения, ограничение скорости
  Events/           # Доменные события (TicketCreated, SlaBreached и т.д.)
  Notifications/    # Интерфейсы и шаблоны email-уведомлений
  Configuration/    # Регистрация DI, опции, маппинг эндпоинтов
  Hubs/             # Хаб SignalR для обновлений в реальном времени
  Enums/            # TicketStatus, TicketPriority, ActivityType
```

## Модели

Escalated включает более 40 сущностей EF Core, охватывающих весь домен хелпдеска:

| Категория | Модели |
|----------|--------|
| Основные | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| Агенты | AgentProfile, AgentCapacity, Skill, AgentSkill |
| Сообщения | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| Администрирование | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| Пользовательские | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| Импорт | ImportJob, ImportSourceMap |
| Конфигурация | EscalatedSettings, SavedView |
| База Знаний | Article, ArticleCategory |

Все модели имеют правильно настроенные связи, индексы и фильтры запросов в `EscalatedDbContext`.

## Сервисы

| Сервис | Ответственность |
|---------|---------------|
| `TicketService` | Полный CRUD тикетов, переходы статусов, ответы, теги, отделы |
| `SlaService` | Назначение политик, обнаружение нарушений, проверка предупреждений, запись первого ответа |
| `AssignmentService` | Назначение агентов, снятие назначения, автоназначение по нагрузке |
| `EscalationService` | Оценка правил на основе условий, выполнение действий эскалации |
| `AutomationRunner` | Оценка автоматизаций на основе времени и выполнение действий |
| `MacroService` | Применение последовательностей действий макросов к тикетам |
| `TicketMergeService` | Объединение источника с целью с переносом ответов |
| `TicketSplitService` | Выделение ответа в новый связанный тикет |
| `TicketSnoozeService` | Откладывание/пробуждение с фоновой службой пробуждения |
| `WebhookDispatcher` | Доставка webhook с HMAC-подписью и логикой повторов |
| `CapacityService` | Лимиты одновременных тикетов на агента |
| `SkillRoutingService` | Назначение агентов по навыкам к тегам тикетов |
| `BusinessHoursCalculator` | Расчёт дат в рабочих часах с поддержкой праздников |
| `TwoFactorService` | Генерация TOTP-секретов, проверка, коды восстановления |
| `AuditLogService` | Логирование и запрос мутаций сущностей |
| `KnowledgeBaseService` | CRUD статей/категорий, поиск, обратная связь |
| `SavedViewService` | Личные и общие предустановки фильтров |
| `SideConversationService` | Внутренние потоковые разговоры в тикетах |
| `ImportService` | Многошаговый импорт с подключаемыми адаптерами |
| `SettingsService` | Хранилище настроек ключ-значение |

## События

Каждое действие с тикетом генерирует доменное событие:

| Событие | Когда |
|-------|------|
| `TicketCreatedEvent` | Новый тикет создан |
| `TicketStatusChangedEvent` | Переход статуса |
| `TicketAssignedEvent` | Агент назначен |
| `TicketUnassignedEvent` | Агент удалён |
| `ReplyCreatedEvent` | Публичный ответ добавлен |
| `InternalNoteAddedEvent` | Заметка агента добавлена |
| `SlaBreachedEvent` | Срок SLA нарушен |
| `SlaWarningEvent` | Срок SLA приближается |
| `TicketEscalatedEvent` | Тикет эскалирован |
| `TicketResolvedEvent` | Тикет решён |
| `TicketClosedEvent` | Тикет закрыт |
| `TicketReopenedEvent` | Тикет открыт повторно |
| `TicketPriorityChangedEvent` | Приоритет изменён |
| `DepartmentChangedEvent` | Отдел изменён |
| `TagAddedEvent` | Тег добавлен |
| `TagRemovedEvent` | Тег удалён |

Реализуйте `IEscalatedEventDispatcher` для получения этих событий в вашем хост-приложении:

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

## Эндпоинты API

### Клиент

| Метод | Маршрут | Описание |
|--------|-------|-------------|
| GET | `/support/tickets` | Список тикетов клиента |
| POST | `/support/tickets` | Создать тикет |
| GET | `/support/tickets/{id}` | Просмотр тикета |
| POST | `/support/tickets/{id}/reply` | Ответить на тикет |
| POST | `/support/tickets/{id}/close` | Закрыть тикет |
| POST | `/support/tickets/{id}/reopen` | Повторно открыть тикет |

### Агент

| Метод | Маршрут | Описание |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | Очередь тикетов с фильтрами |
| GET | `/support/agent/tickets/{id}` | Детали тикета |
| POST | `/support/agent/tickets/{id}/reply` | Ответить |
| POST | `/support/agent/tickets/{id}/note` | Внутренняя заметка |
| POST | `/support/agent/tickets/{id}/assign` | Назначить агента |
| POST | `/support/agent/tickets/{id}/status` | Изменить статус |
| POST | `/support/agent/tickets/{id}/priority` | Изменить приоритет |
| POST | `/support/agent/tickets/{id}/macro` | Применить макрос |
| POST | `/support/agent/tickets/bulk` | Массовые действия |
| GET | `/support/agent/tickets/dashboard` | Нагрузка агента |

### Администрирование

| Метод | Маршрут | Описание |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | Управление отделами |
| GET/POST | `/support/admin/tags` | Управление тегами |
| GET/POST | `/support/admin/sla-policies` | Управление политиками SLA |
| GET/POST | `/support/admin/escalation-rules` | Управление правилами эскалации |
| GET/POST | `/support/admin/webhooks` | Управление webhooks |
| GET/POST | `/support/admin/api-tokens` | Управление API-токенами |
| GET/POST | `/support/admin/macros` | Управление макросами |
| GET/POST | `/support/admin/automations` | Управление автоматизациями |
| GET/POST | `/support/admin/custom-fields` | Управление пользовательскими полями |
| GET/POST | `/support/admin/business-hours` | Рабочие часы |
| GET/POST | `/support/admin/skills` | Управление навыками |
| GET/POST | `/support/admin/roles` | Управление ролями |
| GET | `/support/admin/audit-logs` | Запрос журналов аудита |
| GET/POST | `/support/admin/settings` | Настройки приложения |
| POST | `/support/admin/tickets/{id}/merge` | Объединить тикеты |
| POST | `/support/admin/tickets/{id}/split` | Разделить тикет |
| POST | `/support/admin/tickets/{id}/snooze` | Отложить тикет |
| POST | `/support/admin/tickets/{id}/link` | Связать тикеты |

### Виджет (Публичный)

| Метод | Маршрут | Описание |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | Поиск по базе знаний |
| POST | `/support/widget/tickets` | Создать гостевой тикет |
| GET | `/support/widget/tickets/{token}` | Поиск по гостевому токену |
| POST | `/support/widget/tickets/{token}/reply` | Гостевой ответ |
| POST | `/support/widget/tickets/{token}/rate` | Отправить оценку CSAT |
| POST | `/support/widget/kb/articles/{id}/feedback` | Обратная связь по статье |

## Обновления в Реальном Времени

Включите SignalR для live-обновлений тикетов:

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

Клиенты присоединяются к группам тикетов для получения обновлений:

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## Middleware

### Аутентификация по API-Токену

Защитите эндпоинты API с помощью Bearer-токен аутентификации:

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

Токены хранятся как SHA-256 хеши. Создавайте токены через эндпоинт администрирования API.

### Ограничение Скорости

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## Тестирование

```bash
dotnet test
```

Тесты используют xUnit с Moq и InMemory-провайдер EF Core. Покрытие включает:
- CRUD тикетов и переходы статусов
- Обнаружение нарушений и предупреждения SLA
- Разделение, объединение и откладывание тикетов
- Назначение и расчёт нагрузки
- Сопоставление подписок webhook
- Генерация и проверка секретов 2FA
- Управление ёмкостью
- Валидация моделей и поведение enum

## Также Доступно Для

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- Composer-пакет для Laravel
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- Engine Ruby on Rails
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- Многоразовое приложение Django
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- Пакет AdonisJS v6
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- Пакет ASP.NET Core (вы здесь)
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- UI-компоненты Vue 3 + Inertia.js

Одна архитектура, один Vue UI -- для всех основных бэкенд-фреймворков.

## Лицензия

MIT
