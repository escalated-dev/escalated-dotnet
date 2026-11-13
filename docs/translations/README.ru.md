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
  <b>Русский</b> ·
  <a href="README.tr.md">Türkçe</a> ·
  <a href="README.zh-CN.md">简体中文</a>
</sub></p>

<p align="center">
  <a href="https://escalated.dev"><img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/logo.png" width="72" height="72" alt="Escalated"></a>
</p>

<h1 align="center">Escalated for ASP.NET Core</h1>

<p align="center">
  Добавьте полноценный хелпдеск в ваше приложение на ASP.NET Core.<br>
  Тикеты, SLA, правила эскалации, входящие для агентов и клиентский портал, с хранением через EF Core в выбранной вами базе данных.
</p>

<p align="center">
  <a href="https://escalated.dev"><b>Сайт</b></a> ·
  <a href="https://escalated.dev/docs"><b>Документация</b></a> ·
  <a href="https://escalated.dev/cloud"><b>Cloud</b></a> ·
  <a href="../../CHANGELOG.md"><b>Список изменений</b></a>
</p>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-dark.png">
  <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-light.png" alt="Входящие агентов в Escalated: сохранённые представления со счётчиками, обратный отсчёт SLA и приоритеты у каждого тикета" width="100%">
</picture>

## Что он делает

<table>
  <tr>
    <td width="40%">
      <h3>Входящие, в которых агентам удобно работать весь день</h3>
      Переписка, внутренние заметки, шаблоны ответов и таймеры SLA на одном экране. Тикеты также приходят по электронной почте через Postmark, Mailgun или AWS SES.
    </td>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-light.png" alt="Открытый тикет в рабочем пространстве агента с редактором ответа">
      </picture>
    </td>
  </tr>
  <tr>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-light.png" alt="Страница отчётов в админке со временем ответа, CSAT и нарушениями SLA">
      </picture>
    </td>
    <td width="40%">
      <h3>SLA и правила эскалации у вас на виду</h3>
      Цели по времени ответа и решения для каждого приоритета с учётом рабочих часов, правила, которые эскалируют или переназначают тикет при отставании, и отчёты о нарушениях.
    </td>
  </tr>
</table>

## Возможности

<table>
<tr>
<td width="33%" valign="top">

**Тикеты и входящие**
- Полный жизненный цикл с переходами статусов
- Фильтры очереди и массовые действия
- Внутренние заметки и шаблоны ответов
- Макросы для пакетных действий в один клик
- Побочные обсуждения в тикетах
- Объединение, связывание и разделение тикетов
- Откладывание с автоматическим возвратом
- Личные и общие сохранённые представления
- Привязка тикетов к сущностям вашего приложения
- Вложения с ограничением размера
- Лента активности у каждого тикета

</td>
<td width="33%" valign="top">

**SLA и маршрутизация**
- Цели ответа и решения для каждого приоритета
- Рабочие часы и обнаружение нарушений
- Правила, которые эскалируют, меняют приоритет или переназначают
- Автоматизации по времени
- Маршрутизация по навыкам
- Ограничения нагрузки для каждого агента

</td>
<td width="33%" valign="top">

**Почта**
- Входящая почта через Postmark, Mailgun и AWS SES
- Проверка подписанного Reply-To
- Ответы принимаются только от автора запроса
- Цепочки писем по заголовкам Message-ID

</td>
</tr>
<tr>
<td width="33%" valign="top">

**Клиенты**
- Клиентский портал самообслуживания
- Гостевые тикеты с поиском по magic-токену
- Встраиваемый API виджета
- Оценки CSAT для решённых тикетов
- База знаний с поиском и отзывами

</td>
<td width="33%" valign="top">

**Администрирование и безопасность**
- Отделы, SLA, правила и теги
- Роли и права доступа
- Двухфакторная аутентификация TOTP с кодами восстановления
- Вход через SAML 2, OAuth 2 / OIDC и JWT
- Журнал аудита со старыми и новыми значениями
- API-токены с ограниченными правами

</td>
<td width="33%" valign="top">

**Расширение и интеграции**
- Вебхуки с подписью HMAC и повторными попытками
- Пользовательские поля с условным отображением
- Мастер импорта со сменными адаптерами
- Обновления в реальном времени через SignalR (по желанию)
- Общий интерфейс на Vue 3 + Inertia

</td>
</tr>
</table>

<p align="center"><sub><a href="../features.md">Все возможности подробно</a></sub></p>

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET"></a>
  <a href="https://vuejs.org"><img src="https://img.shields.io/badge/Vue.js-4FC08D?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue.js"></a>
  <a href="https://inertiajs.com"><img src="https://img.shields.io/badge/Inertia.js-9553E9?style=for-the-badge&logo=inertia&logoColor=white" alt="Inertia.js"></a>
  <a href="https://www.postgresql.org"><img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL"></a>
  <a href="https://www.sqlite.org"><img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite"></a>
</p>

## Быстрый старт

```bash
dotnet add package Escalated
dotnet ef database update --context EscalatedDbContext
```

Зарегистрируйте его в `Program.cs` через `builder.Services.AddEscalated(...)` и `app.MapEscalated()`, затем откройте `/support`. Требуются .NET 8+ и SQL Server, SQLite или PostgreSQL. В [руководстве по началу работы](../getting-started.md) описаны настройка и авторизация, а [интерфейс на Inertia](../inertia-ui.md) включает общие экраны.

## Подробнее

- [Все возможности](../features.md) · [Начало работы](../getting-started.md) · [Интерфейс на Inertia](../inertia-ui.md)
- [Кастомизация, реальное время и локализация](../customization.md) · [Архитектура, модели и события](../architecture.md) · [Эндпоинты API](../api-endpoints.md)
- [Управление доступом](../access-control.md) · [Доступ к тикетам и маршрутизация](../ticket-access-and-routing.md) · [Workflows](../workflows.md)
- [Входящая почта](../inbound-email.md) · [Письма-уведомления](../notification-emails.md) · [Гостевой доступ](../guest-access.md)
- [Встраивание виджета](../embedding-the-widget.md) · [Встраивание админки](../embedding-admin.md) · [Панель агента](../agent-panel.md)
- [Плагины](../plugins.md) · [Рассылки](../newsletters.md) · [Разработка и тестирование](../development.md)

Escalated также доступен для [Laravel](https://github.com/escalated-dev/escalated-laravel), [Rails](https://github.com/escalated-dev/escalated-rails), [Django](https://github.com/escalated-dev/escalated-django), [NestJS](https://github.com/escalated-dev/escalated-nestjs), [AdonisJS](https://github.com/escalated-dev/escalated-adonis), [Symfony](https://github.com/escalated-dev/escalated-symfony), [Phoenix](https://github.com/escalated-dev/escalated-phoenix), [Go](https://github.com/escalated-dev/escalated-go), [Spring](https://github.com/escalated-dev/escalated-spring), [WordPress](https://github.com/escalated-dev/escalated-wordpress) и [Filament](https://github.com/escalated-dev/escalated-filament). Интерфейс находится в [escalated](https://github.com/escalated-dev/escalated).

## Лицензия

MIT. Copyright (c) 2026 Escalated.dev. См. [LICENSE](../../LICENSE).
