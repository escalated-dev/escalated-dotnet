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
  <b>Português (BR)</b> •
  <a href="README.ru.md">Русский</a> •
  <a href="README.tr.md">Türkçe</a> •
  <a href="README.zh-CN.md">简体中文</a>
</p>

# Escalated para ASP.NET Core

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Um sistema completo de tickets de suporte integrável para ASP.NET Core. Adicione-o a qualquer aplicação e obtenha um helpdesk completo com rastreamento de SLA, regras de escalonamento, fluxos de trabalho de agentes e portal do cliente. Nenhum serviço externo necessário.

> **[escalated.dev](https://escalated.dev)** -- Saiba mais, veja demos e compare opções Cloud e Self-Hosted.

## Funcionalidades

- **Ciclo de vida do ticket** -- Criar, atribuir, responder, resolver, fechar, reabrir com transições de status configuráveis
- **Motor de SLA** -- Metas de resposta e resolução por prioridade, cálculo de horário comercial, detecção automática de violações
- **Regras de escalonamento** -- Regras baseadas em condições que escalonam, repriorizam, reatribuem ou notificam automaticamente
- **Automações** -- Regras baseadas em tempo com condições e ações
- **Painel do agente** -- Fila de tickets com filtros, ações em massa, notas internas, respostas predefinidas
- **Portal do cliente** -- Criação de tickets em autoatendimento, respostas e acompanhamento de status
- **Painel de administração** -- Gerenciar departamentos, políticas de SLA, regras de escalonamento, tags e mais
- **Macros e respostas predefinidas** -- Ações em lote e modelos de resposta reutilizáveis
- **Campos personalizados** -- Metadados dinâmicos com lógica de exibição condicional
- **Base de conhecimento** -- Artigos, categorias, busca e feedback
- **Anexos de arquivos** -- Suporte a upload com armazenamento configurável e limites de tamanho
- **Linha do tempo de atividades** -- Log de auditoria completo de cada ação em cada ticket
- **Webhooks** -- Assinados com HMAC-SHA256 com lógica de retry
- **Tokens de API** -- Autenticação Bearer com escopo baseado em capacidades
- **Papéis e permissões** -- Controle de acesso granular
- **Log de auditoria** -- Todas as mutações registradas com valores antigos/novos
- **Sistema de importação** -- Assistente multi-etapas com adaptadores plugáveis
- **Conversas laterais** -- Threads internos da equipe em tickets
- **Mesclagem e vinculação de tickets** -- Mesclar tickets duplicados e relacionar problemas
- **Divisão de tickets** -- Dividir uma resposta em um novo ticket
- **Adiamento de tickets** -- Adiar até uma data futura com serviço de despertar em segundo plano
- **Threading de email** -- Cabeçalhos In-Reply-To/References/Message-ID para threading correto
- **Visualizações salvas** -- Presets de filtros pessoais e compartilhados
- **API de widget integrável** -- Endpoints públicos para busca na KB, tickets de visitantes, consulta de status
- **Atualizações em tempo real** -- Hubs SignalR para atualizações de tickets ao vivo (opcional)
- **Gerenciamento de capacidade** -- Limites de carga de trabalho por agente e por canal
- **Roteamento baseado em habilidades** -- Atribuir agentes a tickets por tags de habilidades
- **Avaliações CSAT** -- Pesquisas de satisfação em tickets resolvidos
- **2FA** -- Configuração e verificação TOTP com códigos de recuperação
- **Acesso de visitante** -- Criação anônima de tickets com busca por token mágico
- **Inertia.js + Vue 3 UI** -- Frontend compartilhado via [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)

## Requisitos

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server, SQLite ou PostgreSQL
- Node.js 18+ (para recursos do frontend)

## Início Rápido

### 1. Instalar o Pacote

```bash
dotnet add package Escalated
```

### 2. Registrar Serviços

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

### 4. Executar Migrações

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

Visite `/support` -- está no ar.

## Integração Frontend

O Escalated fornece uma biblioteca de componentes Vue e páginas padrão via pacote npm [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated). Integre com Inertia.js para renderização SPA transparente dentro do seu layout existente.

```bash
npm install @escalated-dev/escalated
```

## Arquitetura

```
src/Escalated/
  Models/           # Mais de 40 modelos de entidades EF Core
  Data/             # EscalatedDbContext com mapeamento completo de relacionamentos
  Services/         # Lógica de negócio (ticket, SLA, mesclagem, divisão, adiamento, etc.)
  Controllers/
    Admin/          # API do painel admin (CRUD para todas as configurações)
    Agent/          # Fila de tickets e ações do agente
    Customer/       # Portal de autoatendimento do cliente
    Widget/         # API pública do widget (busca KB, tickets de visitantes)
  Middleware/       # Autenticação por token API, permissões, limitação de taxa
  Events/           # Eventos de domínio (TicketCreated, SlaBreached, etc.)
  Notifications/    # Interfaces e modelos de notificação por email
  Configuration/    # Registro DI, opções, mapeamento de endpoints
  Hubs/             # Hub SignalR para atualizações em tempo real
  Enums/            # TicketStatus, TicketPriority, ActivityType
```

## Modelos

O Escalated inclui mais de 40 entidades EF Core cobrindo todo o domínio de helpdesk:

| Categoria | Modelos |
|----------|--------|
| Principal | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| Agentes | AgentProfile, AgentCapacity, Skill, AgentSkill |
| Mensagens | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| Administração | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| Personalizado | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| Importação | ImportJob, ImportSourceMap |
| Configuração | EscalatedSettings, SavedView |
| Base de Conhecimento | Article, ArticleCategory |

Todos os modelos têm relacionamentos, índices e filtros de consulta corretamente configurados em `EscalatedDbContext`.

## Serviços

| Serviço | Responsabilidade |
|---------|---------------|
| `TicketService` | CRUD completo de tickets, transições de status, respostas, tags, departamentos |
| `SlaService` | Atribuição de políticas, detecção de violações, verificação de avisos, registro da primeira resposta |
| `AssignmentService` | Atribuição de agentes, remoção, auto-atribuição por carga de trabalho |
| `EscalationService` | Avaliação de regras baseadas em condições, execução de ações de escalonamento |
| `AutomationRunner` | Avaliação de automações baseadas em tempo e execução de ações |
| `MacroService` | Aplicar sequências de ações de macro a tickets |
| `TicketMergeService` | Mesclar origem no destino com transferência de respostas |
| `TicketSplitService` | Dividir uma resposta em um novo ticket vinculado |
| `TicketSnoozeService` | Adiar/despertar com serviço de despertar em segundo plano |
| `WebhookDispatcher` | Entrega de webhooks assinados com HMAC com lógica de retry |
| `CapacityService` | Limites de tickets simultâneos por agente |
| `SkillRoutingService` | Atribuir agentes por habilidades a tags de tickets |
| `BusinessHoursCalculator` | Cálculos de datas em horário comercial com suporte a feriados |
| `TwoFactorService` | Geração de secrets TOTP, verificação, códigos de recuperação |
| `AuditLogService` | Registrar e consultar mutações de entidades |
| `KnowledgeBaseService` | CRUD de artigos/categorias, busca, feedback |
| `SavedViewService` | Presets de filtros pessoais e compartilhados |
| `SideConversationService` | Conversas internas com threads em tickets |
| `ImportService` | Importação multi-etapas com adaptadores plugáveis |
| `SettingsService` | Armazenamento de configurações chave-valor |

## Eventos

Cada ação de ticket emite um evento de domínio:

| Evento | Quando |
|-------|------|
| `TicketCreatedEvent` | Novo ticket criado |
| `TicketStatusChangedEvent` | Transição de status |
| `TicketAssignedEvent` | Agente atribuído |
| `TicketUnassignedEvent` | Agente removido |
| `ReplyCreatedEvent` | Resposta pública adicionada |
| `InternalNoteAddedEvent` | Nota do agente adicionada |
| `SlaBreachedEvent` | Prazo do SLA ultrapassado |
| `SlaWarningEvent` | Prazo do SLA se aproximando |
| `TicketEscalatedEvent` | Ticket escalonado |
| `TicketResolvedEvent` | Ticket resolvido |
| `TicketClosedEvent` | Ticket fechado |
| `TicketReopenedEvent` | Ticket reaberto |
| `TicketPriorityChangedEvent` | Prioridade alterada |
| `DepartmentChangedEvent` | Departamento alterado |
| `TagAddedEvent` | Tag adicionada |
| `TagRemovedEvent` | Tag removida |

Implemente `IEscalatedEventDispatcher` para receber esses eventos na sua aplicação host:

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

| Método | Rota | Descrição |
|--------|-------|-------------|
| GET | `/support/tickets` | Listar tickets do cliente |
| POST | `/support/tickets` | Criar ticket |
| GET | `/support/tickets/{id}` | Visualizar ticket |
| POST | `/support/tickets/{id}/reply` | Responder ao ticket |
| POST | `/support/tickets/{id}/close` | Fechar ticket |
| POST | `/support/tickets/{id}/reopen` | Reabrir ticket |

### Agente

| Método | Rota | Descrição |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | Fila de tickets com filtros |
| GET | `/support/agent/tickets/{id}` | Detalhe do ticket |
| POST | `/support/agent/tickets/{id}/reply` | Responder |
| POST | `/support/agent/tickets/{id}/note` | Nota interna |
| POST | `/support/agent/tickets/{id}/assign` | Atribuir agente |
| POST | `/support/agent/tickets/{id}/status` | Alterar status |
| POST | `/support/agent/tickets/{id}/priority` | Alterar prioridade |
| POST | `/support/agent/tickets/{id}/macro` | Aplicar macro |
| POST | `/support/agent/tickets/bulk` | Ações em massa |
| GET | `/support/agent/tickets/dashboard` | Carga de trabalho do agente |

### Administração

| Método | Rota | Descrição |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | Gerenciar departamentos |
| GET/POST | `/support/admin/tags` | Gerenciar tags |
| GET/POST | `/support/admin/sla-policies` | Gerenciar políticas de SLA |
| GET/POST | `/support/admin/escalation-rules` | Gerenciar regras de escalonamento |
| GET/POST | `/support/admin/webhooks` | Gerenciar webhooks |
| GET/POST | `/support/admin/api-tokens` | Gerenciar tokens de API |
| GET/POST | `/support/admin/macros` | Gerenciar macros |
| GET/POST | `/support/admin/automations` | Gerenciar automações |
| GET/POST | `/support/admin/custom-fields` | Gerenciar campos personalizados |
| GET/POST | `/support/admin/business-hours` | Horários comerciais |
| GET/POST | `/support/admin/skills` | Gerenciar habilidades |
| GET/POST | `/support/admin/roles` | Gerenciar papéis |
| GET | `/support/admin/audit-logs` | Consultar logs de auditoria |
| GET/POST | `/support/admin/settings` | Configurações do aplicativo |
| POST | `/support/admin/tickets/{id}/merge` | Mesclar tickets |
| POST | `/support/admin/tickets/{id}/split` | Dividir ticket |
| POST | `/support/admin/tickets/{id}/snooze` | Adiar ticket |
| POST | `/support/admin/tickets/{id}/link` | Vincular tickets |

### Widget (Público)

| Método | Rota | Descrição |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | Buscar na base de conhecimento |
| POST | `/support/widget/tickets` | Criar ticket de visitante |
| GET | `/support/widget/tickets/{token}` | Buscar por token de visitante |
| POST | `/support/widget/tickets/{token}/reply` | Resposta de visitante |
| POST | `/support/widget/tickets/{token}/rate` | Enviar avaliação CSAT |
| POST | `/support/widget/kb/articles/{id}/feedback` | Feedback do artigo |

## Atualizações em Tempo Real

Ative o SignalR para atualizações de tickets ao vivo:

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

Os clientes entram em grupos específicos de tickets para receber atualizações:

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## Middleware

### Autenticação por Token de API

Proteja os endpoints de API com autenticação por token Bearer:

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

Os tokens são armazenados como hashes SHA-256. Crie tokens pelo endpoint de administração de API.

### Limitação de Taxa

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## Testes

```bash
dotnet test
```

Os testes usam xUnit com Moq e o provider InMemory do EF Core. A cobertura inclui:
- CRUD de tickets e transições de status
- Detecção de violações e avisos de SLA
- Divisão, mesclagem e adiamento de tickets
- Atribuição e cálculo de carga de trabalho
- Correspondência de assinaturas de webhooks
- Geração e verificação de secrets 2FA
- Gerenciamento de capacidade
- Validação de modelos e comportamento de enums

## Também Disponível Para

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- Pacote Composer para Laravel
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- Engine Ruby on Rails
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- Aplicação reutilizável Django
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- Pacote AdonisJS v6
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- Pacote ASP.NET Core (você está aqui)
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- Componentes UI Vue 3 + Inertia.js

A mesma arquitetura, a mesma UI Vue -- para todos os principais frameworks backend.

## Licença

MIT
