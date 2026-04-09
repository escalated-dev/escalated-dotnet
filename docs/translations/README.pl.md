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
  <b>Polski</b> •
  <a href="README.pt-BR.md">Português (BR)</a> •
  <a href="README.ru.md">Русский</a> •
  <a href="README.tr.md">Türkçe</a> •
  <a href="README.zh-CN.md">简体中文</a>
</p>

# Escalated dla ASP.NET Core

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

W pełni funkcjonalny, osadzalny system zgłoszeń wsparcia dla ASP.NET Core. Dodaj go do dowolnej aplikacji i uzyskaj kompletny helpdesk ze śledzeniem SLA, regułami eskalacji, przepływami pracy agentów i portalem klienta. Nie wymaga zewnętrznych usług.

> **[escalated.dev](https://escalated.dev)** -- Dowiedz się więcej, obejrzyj dema i porównaj opcje Cloud i Self-Hosted.

## Funkcje

- **Cykl życia zgłoszenia** -- Tworzenie, przypisywanie, odpowiadanie, rozwiązywanie, zamykanie, ponowne otwieranie z konfigurowalnymi przejściami statusów
- **Silnik SLA** -- Cele odpowiedzi i rozwiązania według priorytetu, obliczanie godzin pracy, automatyczne wykrywanie naruszeń
- **Reguły eskalacji** -- Reguły warunkowe automatycznie eskalujące, zmieniające priorytet, ponownie przypisujące lub powiadamiające
- **Automatyzacje** -- Reguły oparte na czasie z warunkami i akcjami
- **Panel agenta** -- Kolejka zgłoszeń z filtrami, akcjami zbiorczymi, notatkami wewnętrznymi, szablonowymi odpowiedziami
- **Portal klienta** -- Samoobsługowe tworzenie zgłoszeń, odpowiedzi i śledzenie statusu
- **Panel administracyjny** -- Zarządzanie działami, politykami SLA, regułami eskalacji, tagami i więcej
- **Makra i szablonowe odpowiedzi** -- Akcje zbiorcze i wielokrotnie używane szablony odpowiedzi
- **Pola niestandardowe** -- Dynamiczne metadane z warunkową logiką wyświetlania
- **Baza wiedzy** -- Artykuły, kategorie, wyszukiwanie i opinie
- **Załączniki plików** -- Obsługa przesyłania z konfigurowalnym magazynem i limitami rozmiaru
- **Oś czasu aktywności** -- Pełny dziennik audytu każdej akcji na każdym zgłoszeniu
- **Webhooks** -- Podpisane HMAC-SHA256 z logiką ponownych prób
- **Tokeny API** -- Uwierzytelnianie Bearer z zakresem opartym na możliwościach
- **Role i uprawnienia** -- Szczegółowa kontrola dostępu
- **Logowanie audytu** -- Wszystkie mutacje rejestrowane ze starymi/nowymi wartościami
- **System importu** -- Wieloetapowy kreator z podłączalnymi adapterami
- **Konwersacje poboczne** -- Wewnętrzne wątki zespołu na zgłoszeniach
- **Scalanie i łączenie zgłoszeń** -- Scalanie duplikatów i łączenie powiązanych problemów
- **Dzielenie zgłoszeń** -- Rozdzielenie odpowiedzi na nowe zgłoszenie
- **Odkładanie zgłoszeń** -- Odłożenie do przyszłej daty z usługą budzenia w tle
- **Wątkowanie e-mail** -- Nagłówki In-Reply-To/References/Message-ID dla poprawnego wątkowania
- **Zapisane widoki** -- Osobiste i współdzielone predefiniowane filtry
- **API osadzalnego widżetu** -- Publiczne endpointy do wyszukiwania KB, zgłoszeń gości, sprawdzania statusu
- **Aktualizacje w czasie rzeczywistym** -- Huby SignalR do aktualizacji zgłoszeń na żywo (opcjonalnie)
- **Zarządzanie pojemnością** -- Limity obciążenia na agenta według kanału
- **Routing oparty na umiejętnościach** -- Dopasowywanie agentów do zgłoszeń według tagów umiejętności
- **Oceny CSAT** -- Ankiety satysfakcji przy rozwiązanych zgłoszeniach
- **2FA** -- Konfiguracja i weryfikacja TOTP z kodami odzyskiwania
- **Dostęp gościnny** -- Anonimowe tworzenie zgłoszeń z wyszukiwaniem po magicznym tokenie
- **Inertia.js + Vue 3 UI** -- Współdzielony frontend przez [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)

## Wymagania

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server, SQLite lub PostgreSQL
- Node.js 18+ (dla zasobów frontend)

## Szybki Start

### 1. Zainstaluj Pakiet

```bash
dotnet add package Escalated
```

### 2. Zarejestruj Usługi

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

### 3. Skonfiguruj

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

### 4. Uruchom Migracje

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

Odwiedź `/support` -- działa.

## Integracja Frontend

Escalated dostarcza bibliotekę komponentów Vue i domyślne strony przez pakiet npm [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated). Zintegruj z Inertia.js dla bezproblemowego renderowania SPA w istniejącym układzie.

```bash
npm install @escalated-dev/escalated
```

## Architektura

```
src/Escalated/
  Models/           # Ponad 40 modeli encji EF Core
  Data/             # EscalatedDbContext z pełnym mapowaniem relacji
  Services/         # Logika biznesowa (zgłoszenia, SLA, scalanie, dzielenie, odkładanie, itp.)
  Controllers/
    Admin/          # API panelu administracyjnego (CRUD dla wszystkich ustawień)
    Agent/          # Kolejka zgłoszeń i akcje agenta
    Customer/       # Samoobsługowy portal klienta
    Widget/         # Publiczny API widżetu (wyszukiwanie KB, zgłoszenia gości)
  Middleware/       # Uwierzytelnianie tokenem API, uprawnienia, ograniczanie szybkości
  Events/           # Zdarzenia domenowe (TicketCreated, SlaBreached, itp.)
  Notifications/    # Interfejsy i szablony powiadomień e-mail
  Configuration/    # Rejestracja DI, opcje, mapowanie endpointów
  Hubs/             # Hub SignalR dla aktualizacji w czasie rzeczywistym
  Enums/            # TicketStatus, TicketPriority, ActivityType
```

## Modele

Escalated zawiera ponad 40 encji EF Core obejmujących pełną domenę helpdesk:

| Kategoria | Modele |
|----------|--------|
| Podstawowe | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| Agenci | AgentProfile, AgentCapacity, Skill, AgentSkill |
| Wiadomości | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| Administracja | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| Niestandardowe | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| Import | ImportJob, ImportSourceMap |
| Konfiguracja | EscalatedSettings, SavedView |
| Baza Wiedzy | Article, ArticleCategory |

Wszystkie modele mają poprawnie skonfigurowane relacje, indeksy i filtry zapytań w `EscalatedDbContext`.

## Usługi

| Usługa | Odpowiedzialność |
|---------|---------------|
| `TicketService` | Pełny CRUD zgłoszeń, przejścia statusów, odpowiedzi, tagi, działy |
| `SlaService` | Przypisanie polityk, wykrywanie naruszeń, sprawdzanie ostrzeżeń, rejestracja pierwszej odpowiedzi |
| `AssignmentService` | Przypisywanie agentów, cofanie przypisania, automatyczne przypisywanie według obciążenia |
| `EscalationService` | Ewaluacja reguł warunkowych, wykonywanie akcji eskalacji |
| `AutomationRunner` | Ewaluacja automatyzacji czasowych i wykonywanie akcji |
| `MacroService` | Stosowanie sekwencji akcji makr do zgłoszeń |
| `TicketMergeService` | Scalanie źródła do celu z transferem odpowiedzi |
| `TicketSplitService` | Rozdzielenie odpowiedzi na nowe powiązane zgłoszenie |
| `TicketSnoozeService` | Odkładanie/budzenie z usługą budzenia w tle |
| `WebhookDispatcher` | Dostarczanie webhooków podpisanych HMAC z logiką ponownych prób |
| `CapacityService` | Limity jednoczesnych zgłoszeń na agenta |
| `SkillRoutingService` | Dopasowywanie agentów według umiejętności do tagów zgłoszeń |
| `BusinessHoursCalculator` | Obliczenia dat w godzinach pracy z obsługą świąt |
| `TwoFactorService` | Generowanie sekretów TOTP, weryfikacja, kody odzyskiwania |
| `AuditLogService` | Logowanie i odpytywanie mutacji encji |
| `KnowledgeBaseService` | CRUD artykułów/kategorii, wyszukiwanie, opinie |
| `SavedViewService` | Osobiste i współdzielone predefiniowane filtry |
| `SideConversationService` | Wewnętrzne wątkowe rozmowy na zgłoszeniach |
| `ImportService` | Wieloetapowy import z podłączalnymi adapterami |
| `SettingsService` | Magazyn ustawień klucz-wartość |

## Zdarzenia

Każda akcja na zgłoszeniu emituje zdarzenie domenowe:

| Zdarzenie | Kiedy |
|-------|------|
| `TicketCreatedEvent` | Nowe zgłoszenie utworzone |
| `TicketStatusChangedEvent` | Przejście statusu |
| `TicketAssignedEvent` | Agent przypisany |
| `TicketUnassignedEvent` | Agent usunięty |
| `ReplyCreatedEvent` | Publiczna odpowiedź dodana |
| `InternalNoteAddedEvent` | Notatka agenta dodana |
| `SlaBreachedEvent` | Termin SLA przekroczony |
| `SlaWarningEvent` | Termin SLA zbliża się |
| `TicketEscalatedEvent` | Zgłoszenie eskalowane |
| `TicketResolvedEvent` | Zgłoszenie rozwiązane |
| `TicketClosedEvent` | Zgłoszenie zamknięte |
| `TicketReopenedEvent` | Zgłoszenie ponownie otwarte |
| `TicketPriorityChangedEvent` | Priorytet zmieniony |
| `DepartmentChangedEvent` | Dział zmieniony |
| `TagAddedEvent` | Tag dodany |
| `TagRemovedEvent` | Tag usunięty |

Zaimplementuj `IEscalatedEventDispatcher`, aby otrzymywać te zdarzenia w aplikacji hosta:

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

## Endpointy API

### Klient

| Metoda | Trasa | Opis |
|--------|-------|-------------|
| GET | `/support/tickets` | Lista zgłoszeń klienta |
| POST | `/support/tickets` | Utwórz zgłoszenie |
| GET | `/support/tickets/{id}` | Zobacz zgłoszenie |
| POST | `/support/tickets/{id}/reply` | Odpowiedz na zgłoszenie |
| POST | `/support/tickets/{id}/close` | Zamknij zgłoszenie |
| POST | `/support/tickets/{id}/reopen` | Otwórz ponownie zgłoszenie |

### Agent

| Metoda | Trasa | Opis |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | Kolejka zgłoszeń z filtrami |
| GET | `/support/agent/tickets/{id}` | Szczegóły zgłoszenia |
| POST | `/support/agent/tickets/{id}/reply` | Odpowiedz |
| POST | `/support/agent/tickets/{id}/note` | Notatka wewnętrzna |
| POST | `/support/agent/tickets/{id}/assign` | Przypisz agenta |
| POST | `/support/agent/tickets/{id}/status` | Zmień status |
| POST | `/support/agent/tickets/{id}/priority` | Zmień priorytet |
| POST | `/support/agent/tickets/{id}/macro` | Zastosuj makro |
| POST | `/support/agent/tickets/bulk` | Akcje zbiorcze |
| GET | `/support/agent/tickets/dashboard` | Obciążenie agenta |

### Administracja

| Metoda | Trasa | Opis |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | Zarządzaj działami |
| GET/POST | `/support/admin/tags` | Zarządzaj tagami |
| GET/POST | `/support/admin/sla-policies` | Zarządzaj politykami SLA |
| GET/POST | `/support/admin/escalation-rules` | Zarządzaj regułami eskalacji |
| GET/POST | `/support/admin/webhooks` | Zarządzaj webhookami |
| GET/POST | `/support/admin/api-tokens` | Zarządzaj tokenami API |
| GET/POST | `/support/admin/macros` | Zarządzaj makrami |
| GET/POST | `/support/admin/automations` | Zarządzaj automatyzacjami |
| GET/POST | `/support/admin/custom-fields` | Zarządzaj polami niestandardowymi |
| GET/POST | `/support/admin/business-hours` | Godziny pracy |
| GET/POST | `/support/admin/skills` | Zarządzaj umiejętnościami |
| GET/POST | `/support/admin/roles` | Zarządzaj rolami |
| GET | `/support/admin/audit-logs` | Przeglądaj dzienniki audytu |
| GET/POST | `/support/admin/settings` | Ustawienia aplikacji |
| POST | `/support/admin/tickets/{id}/merge` | Scal zgłoszenia |
| POST | `/support/admin/tickets/{id}/split` | Podziel zgłoszenie |
| POST | `/support/admin/tickets/{id}/snooze` | Odłóż zgłoszenie |
| POST | `/support/admin/tickets/{id}/link` | Połącz zgłoszenia |

### Widżet (Publiczny)

| Metoda | Trasa | Opis |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | Wyszukaj w bazie wiedzy |
| POST | `/support/widget/tickets` | Utwórz zgłoszenie gościnne |
| GET | `/support/widget/tickets/{token}` | Wyszukaj po tokenie gościa |
| POST | `/support/widget/tickets/{token}/reply` | Odpowiedź gościa |
| POST | `/support/widget/tickets/{token}/rate` | Prześlij ocenę CSAT |
| POST | `/support/widget/kb/articles/{id}/feedback` | Opinia o artykule |

## Aktualizacje w Czasie Rzeczywistym

Włącz SignalR dla aktualizacji zgłoszeń na żywo:

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

Klienci dołączają do grup specyficznych dla zgłoszeń, aby otrzymywać aktualizacje:

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## Middleware

### Uwierzytelnianie Tokenem API

Chroń endpointy API uwierzytelnianiem tokenem Bearer:

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

Tokeny są przechowywane jako skróty SHA-256. Twórz tokeny przez endpoint administracyjny API.

### Ograniczanie Szybkości

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## Testy

```bash
dotnet test
```

Testy używają xUnit z Moq i dostawcą InMemory EF Core. Pokrycie obejmuje:
- CRUD zgłoszeń i przejścia statusów
- Wykrywanie naruszeń i ostrzeżenia SLA
- Dzielenie, scalanie i odkładanie zgłoszeń
- Przypisywanie i obliczanie obciążenia
- Dopasowywanie subskrypcji webhooków
- Generowanie i weryfikacja sekretów 2FA
- Zarządzanie pojemnością
- Walidacja modeli i zachowanie enumów

## Dostępne Również Dla

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- Pakiet Composer dla Laravel
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- Silnik Ruby on Rails
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- Aplikacja Django wielokrotnego użytku
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- Pakiet AdonisJS v6
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- Pakiet ASP.NET Core (jesteś tutaj)
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- Komponenty UI Vue 3 + Inertia.js

Ta sama architektura, ten sam interfejs Vue -- dla każdego głównego frameworka backendowego.

## Licencja

MIT
