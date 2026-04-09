<p align="center">
  <a href="README.ar.md">العربية</a> •
  <a href="README.de.md">Deutsch</a> •
  <a href="../../README.md">English</a> •
  <b>Español</b> •
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

# Escalated para ASP.NET Core

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Un sistema completo de tickets de soporte integrable para ASP.NET Core. Agrégalo a cualquier aplicación y obtén un helpdesk completo con seguimiento de SLA, reglas de escalamiento, flujos de trabajo para agentes y un portal de clientes. No se requieren servicios externos.

> **[escalated.dev](https://escalated.dev)** -- Más información, demos y comparación entre opciones Cloud y Self-Hosted.

## Características

- **Ciclo de vida del ticket** -- Crear, asignar, responder, resolver, cerrar, reabrir con transiciones de estado configurables
- **Motor de SLA** -- Objetivos de respuesta y resolución por prioridad, cálculo de horas laborales, detección automática de incumplimientos
- **Reglas de escalamiento** -- Reglas basadas en condiciones que escalan, repriorizan, reasignan o notifican automáticamente
- **Automatizaciones** -- Reglas basadas en tiempo con condiciones y acciones
- **Panel del agente** -- Cola de tickets con filtros, acciones masivas, notas internas, respuestas predefinidas
- **Portal del cliente** -- Creación de tickets en autoservicio, respuestas y seguimiento de estado
- **Panel de administración** -- Gestionar departamentos, políticas de SLA, reglas de escalamiento, etiquetas y más
- **Macros y respuestas predefinidas** -- Acciones en lote y plantillas de respuesta reutilizables
- **Campos personalizados** -- Metadatos dinámicos con lógica de visualización condicional
- **Base de conocimientos** -- Artículos, categorías, búsqueda y retroalimentación
- **Archivos adjuntos** -- Soporte de carga con almacenamiento y límites de tamaño configurables
- **Línea de actividad** -- Registro completo de auditoría de cada acción en cada ticket
- **Webhooks** -- Firmados con HMAC-SHA256 con lógica de reintentos
- **Tokens de API** -- Autenticación Bearer con alcance basado en capacidades
- **Roles y permisos** -- Control de acceso detallado
- **Registro de auditoría** -- Todas las mutaciones registradas con valores anteriores/nuevos
- **Sistema de importación** -- Asistente de múltiples pasos con adaptadores conectables
- **Conversaciones laterales** -- Hilos internos del equipo en tickets
- **Fusión y vinculación de tickets** -- Fusionar tickets duplicados y relacionar incidencias
- **División de tickets** -- Dividir una respuesta en un nuevo ticket
- **Posponer tickets** -- Posponer hasta una fecha futura con servicio de activación en segundo plano
- **Hilos de correo electrónico** -- Encabezados In-Reply-To/References/Message-ID para hilos correctos
- **Vistas guardadas** -- Filtros preestablecidos personales y compartidos
- **API de widget integrable** -- Endpoints públicos para búsqueda en KB, tickets de invitados, consulta de estado
- **Actualizaciones en tiempo real** -- Hubs SignalR para actualizaciones de tickets en vivo (opcional)
- **Gestión de capacidad** -- Límites de carga de trabajo por agente y por canal
- **Enrutamiento basado en habilidades** -- Asignar agentes a tickets por etiquetas de habilidades
- **Calificaciones CSAT** -- Encuestas de satisfacción en tickets resueltos
- **2FA** -- Configuración y verificación TOTP con códigos de recuperación
- **Acceso de invitados** -- Creación anónima de tickets con búsqueda por token mágico
- **Inertia.js + Vue 3 UI** -- Frontend compartido vía [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)

## Requisitos

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server, SQLite o PostgreSQL
- Node.js 18+ (para recursos del frontend)

## Inicio Rápido

### 1. Instalar el Paquete

```bash
dotnet add package Escalated
```

### 2. Registrar Servicios

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

### 3. Configurar

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

### 4. Ejecutar Migraciones

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

Visita `/support` -- ya está en funcionamiento.

## Integración Frontend

Escalated proporciona una biblioteca de componentes Vue y páginas predeterminadas a través del paquete npm [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated). Integra con Inertia.js para renderizado SPA sin interrupciones dentro de tu diseño existente.

```bash
npm install @escalated-dev/escalated
```

## Arquitectura

```
src/Escalated/
  Models/           # Más de 40 modelos de entidades EF Core
  Data/             # EscalatedDbContext con mapeo completo de relaciones
  Services/         # Lógica de negocio (ticket, SLA, fusión, división, posponer, etc.)
  Controllers/
    Admin/          # API del panel de administración (CRUD para toda la configuración)
    Agent/          # Cola de tickets y acciones del agente
    Customer/       # Portal de autoservicio del cliente
    Widget/         # API pública del widget (búsqueda KB, tickets de invitados)
  Middleware/       # Autenticación por token API, permisos, limitación de tasa
  Events/           # Eventos de dominio (TicketCreated, SlaBreached, etc.)
  Notifications/    # Interfaces y plantillas de notificaciones por correo
  Configuration/    # Registro DI, opciones, mapeo de endpoints
  Hubs/             # Hub SignalR para actualizaciones en tiempo real
  Enums/            # TicketStatus, TicketPriority, ActivityType
```

## Modelos

Escalated incluye más de 40 entidades EF Core que cubren todo el dominio de helpdesk:

| Categoría | Modelos |
|----------|--------|
| Principal | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| Agentes | AgentProfile, AgentCapacity, Skill, AgentSkill |
| Mensajería | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| Administración | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| Personalizado | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| Importación | ImportJob, ImportSourceMap |
| Configuración | EscalatedSettings, SavedView |
| Base de Conocimientos | Article, ArticleCategory |

Todos los modelos tienen relaciones, índices y filtros de consulta configurados correctamente en `EscalatedDbContext`.

## Servicios

| Servicio | Responsabilidad |
|---------|---------------|
| `TicketService` | CRUD completo de tickets, transiciones de estado, respuestas, etiquetas, departamentos |
| `SlaService` | Asignación de políticas, detección de incumplimientos, verificación de advertencias, registro de primera respuesta |
| `AssignmentService` | Asignación de agentes, desasignación, auto-asignación por carga de trabajo |
| `EscalationService` | Evaluación de reglas basadas en condiciones, ejecución de acciones de escalamiento |
| `AutomationRunner` | Evaluación de automatizaciones basadas en tiempo y ejecución de acciones |
| `MacroService` | Aplicar secuencias de acciones de macro a tickets |
| `TicketMergeService` | Fusionar origen en destino con transferencia de respuestas |
| `TicketSplitService` | Dividir una respuesta en un nuevo ticket vinculado |
| `TicketSnoozeService` | Posponer/activar con servicio de activación en segundo plano |
| `WebhookDispatcher` | Entrega de webhooks firmados con HMAC con lógica de reintentos |
| `CapacityService` | Límites de tickets concurrentes por agente |
| `SkillRoutingService` | Asignar agentes por habilidades a etiquetas de tickets |
| `BusinessHoursCalculator` | Cálculos de fechas en horas laborales con soporte de feriados |
| `TwoFactorService` | Generación de secretos TOTP, verificación, códigos de recuperación |
| `AuditLogService` | Registrar y consultar mutaciones de entidades |
| `KnowledgeBaseService` | CRUD de artículos/categorías, búsqueda, retroalimentación |
| `SavedViewService` | Filtros preestablecidos personales y compartidos |
| `SideConversationService` | Conversaciones internas con hilos en tickets |
| `ImportService` | Importación de múltiples pasos con adaptadores conectables |
| `SettingsService` | Almacén de configuración clave-valor |

## Eventos

Cada acción de ticket emite un evento de dominio:

| Evento | Cuándo |
|-------|------|
| `TicketCreatedEvent` | Nuevo ticket creado |
| `TicketStatusChangedEvent` | Transición de estado |
| `TicketAssignedEvent` | Agente asignado |
| `TicketUnassignedEvent` | Agente eliminado |
| `ReplyCreatedEvent` | Respuesta pública agregada |
| `InternalNoteAddedEvent` | Nota del agente agregada |
| `SlaBreachedEvent` | Plazo de SLA incumplido |
| `SlaWarningEvent` | Plazo de SLA próximo a vencer |
| `TicketEscalatedEvent` | Ticket escalado |
| `TicketResolvedEvent` | Ticket resuelto |
| `TicketClosedEvent` | Ticket cerrado |
| `TicketReopenedEvent` | Ticket reabierto |
| `TicketPriorityChangedEvent` | Prioridad cambiada |
| `DepartmentChangedEvent` | Departamento cambiado |
| `TagAddedEvent` | Etiqueta agregada |
| `TagRemovedEvent` | Etiqueta eliminada |

Implementa `IEscalatedEventDispatcher` para recibir estos eventos en tu aplicación anfitriona:

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

## Endpoints de API

### Cliente

| Método | Ruta | Descripción |
|--------|-------|-------------|
| GET | `/support/tickets` | Listar tickets del cliente |
| POST | `/support/tickets` | Crear ticket |
| GET | `/support/tickets/{id}` | Ver ticket |
| POST | `/support/tickets/{id}/reply` | Responder al ticket |
| POST | `/support/tickets/{id}/close` | Cerrar ticket |
| POST | `/support/tickets/{id}/reopen` | Reabrir ticket |

### Agente

| Método | Ruta | Descripción |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | Cola de tickets con filtros |
| GET | `/support/agent/tickets/{id}` | Detalle del ticket |
| POST | `/support/agent/tickets/{id}/reply` | Responder |
| POST | `/support/agent/tickets/{id}/note` | Nota interna |
| POST | `/support/agent/tickets/{id}/assign` | Asignar agente |
| POST | `/support/agent/tickets/{id}/status` | Cambiar estado |
| POST | `/support/agent/tickets/{id}/priority` | Cambiar prioridad |
| POST | `/support/agent/tickets/{id}/macro` | Aplicar macro |
| POST | `/support/agent/tickets/bulk` | Acciones masivas |
| GET | `/support/agent/tickets/dashboard` | Carga de trabajo del agente |

### Administración

| Método | Ruta | Descripción |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | Gestionar departamentos |
| GET/POST | `/support/admin/tags` | Gestionar etiquetas |
| GET/POST | `/support/admin/sla-policies` | Gestionar políticas de SLA |
| GET/POST | `/support/admin/escalation-rules` | Gestionar reglas de escalamiento |
| GET/POST | `/support/admin/webhooks` | Gestionar webhooks |
| GET/POST | `/support/admin/api-tokens` | Gestionar tokens de API |
| GET/POST | `/support/admin/macros` | Gestionar macros |
| GET/POST | `/support/admin/automations` | Gestionar automatizaciones |
| GET/POST | `/support/admin/custom-fields` | Gestionar campos personalizados |
| GET/POST | `/support/admin/business-hours` | Horarios laborales |
| GET/POST | `/support/admin/skills` | Gestionar habilidades |
| GET/POST | `/support/admin/roles` | Gestionar roles |
| GET | `/support/admin/audit-logs` | Consultar registros de auditoría |
| GET/POST | `/support/admin/settings` | Configuración de la aplicación |
| POST | `/support/admin/tickets/{id}/merge` | Fusionar tickets |
| POST | `/support/admin/tickets/{id}/split` | Dividir ticket |
| POST | `/support/admin/tickets/{id}/snooze` | Posponer ticket |
| POST | `/support/admin/tickets/{id}/link` | Vincular tickets |

### Widget (Público)

| Método | Ruta | Descripción |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | Buscar en la base de conocimientos |
| POST | `/support/widget/tickets` | Crear ticket de invitado |
| GET | `/support/widget/tickets/{token}` | Buscar por token de invitado |
| POST | `/support/widget/tickets/{token}/reply` | Respuesta de invitado |
| POST | `/support/widget/tickets/{token}/rate` | Enviar calificación CSAT |
| POST | `/support/widget/kb/articles/{id}/feedback` | Retroalimentación de artículo |

## Actualizaciones en Tiempo Real

Habilita SignalR para actualizaciones de tickets en vivo:

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

Los clientes se unen a grupos específicos de tickets para recibir actualizaciones:

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## Middleware

### Autenticación por Token de API

Protege los endpoints de API con autenticación de token Bearer:

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

Los tokens se almacenan como hashes SHA-256. Crea tokens a través del endpoint de administración de API.

### Limitación de Tasa

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## Pruebas

```bash
dotnet test
```

Las pruebas usan xUnit con Moq y el proveedor InMemory de EF Core. La cobertura incluye:
- CRUD de tickets y transiciones de estado
- Detección de incumplimientos y advertencias de SLA
- División, fusión y posposición de tickets
- Asignación y cálculo de carga de trabajo
- Coincidencia de suscripciones de webhooks
- Generación y verificación de secretos 2FA
- Gestión de capacidad
- Validación de modelos y comportamiento de enums

## También Disponible Para

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- Paquete Composer para Laravel
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- Motor de Ruby on Rails
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- Aplicación reutilizable de Django
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- Paquete AdonisJS v6
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- Paquete ASP.NET Core (estás aquí)
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- Componentes UI Vue 3 + Inertia.js

La misma arquitectura, la misma UI Vue -- para todos los principales frameworks backend.

## Licencia

MIT
