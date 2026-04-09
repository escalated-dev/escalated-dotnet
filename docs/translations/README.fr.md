<p align="center">
  <a href="README.ar.md">العربية</a> •
  <a href="README.de.md">Deutsch</a> •
  <a href="../../README.md">English</a> •
  <a href="README.es.md">Español</a> •
  <b>Français</b> •
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

# Escalated pour ASP.NET Core

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Un système complet de tickets de support intégrable pour ASP.NET Core. Ajoutez-le à n'importe quelle application et obtenez un helpdesk complet avec suivi des SLA, règles d'escalade, workflows des agents et portail client. Aucun service externe requis.

> **[escalated.dev](https://escalated.dev)** -- En savoir plus, voir les démos et comparer les options Cloud et Self-Hosted.

## Fonctionnalités

- **Cycle de vie du ticket** -- Créer, assigner, répondre, résoudre, fermer, rouvrir avec des transitions d'état configurables
- **Moteur SLA** -- Objectifs de réponse et de résolution par priorité, calcul des heures ouvrées, détection automatique des violations
- **Règles d'escalade** -- Règles basées sur des conditions qui escaladent, repriorisent, réassignent ou notifient automatiquement
- **Automatisations** -- Règles basées sur le temps avec conditions et actions
- **Tableau de bord de l'agent** -- File d'attente de tickets avec filtres, actions groupées, notes internes, réponses prédéfinies
- **Portail client** -- Création de tickets en libre-service, réponses et suivi de l'état
- **Panneau d'administration** -- Gérer les départements, politiques SLA, règles d'escalade, tags et plus
- **Macros et réponses prédéfinies** -- Actions groupées et modèles de réponse réutilisables
- **Champs personnalisés** -- Métadonnées dynamiques avec logique d'affichage conditionnel
- **Base de connaissances** -- Articles, catégories, recherche et retours
- **Pièces jointes** -- Support de téléversement avec stockage configurable et limites de taille
- **Chronologie d'activité** -- Journal d'audit complet de chaque action sur chaque ticket
- **Webhooks** -- Signés HMAC-SHA256 avec logique de réessai
- **Tokens API** -- Authentification Bearer avec portée basée sur les capacités
- **Rôles et permissions** -- Contrôle d'accès détaillé
- **Journal d'audit** -- Toutes les mutations enregistrées avec les valeurs avant/après
- **Système d'importation** -- Assistant multi-étapes avec adaptateurs connectables
- **Conversations latérales** -- Fils de discussion internes de l'équipe sur les tickets
- **Fusion et liaison de tickets** -- Fusionner les tickets en double et lier les problèmes
- **Division de tickets** -- Séparer une réponse en un nouveau ticket
- **Mise en veille de tickets** -- Reporter jusqu'à une date future avec service de réveil en arrière-plan
- **Fils de discussion email** -- En-têtes In-Reply-To/References/Message-ID pour un threading correct
- **Vues enregistrées** -- Filtres prédéfinis personnels et partagés
- **API de widget intégrable** -- Endpoints publics pour recherche KB, tickets invités, consultation d'état
- **Mises à jour en temps réel** -- Hubs SignalR pour les mises à jour de tickets en direct (optionnel)
- **Gestion de capacité** -- Limites de charge de travail par agent et par canal
- **Routage basé sur les compétences** -- Assigner les agents aux tickets par tags de compétences
- **Notes CSAT** -- Enquêtes de satisfaction sur les tickets résolus
- **2FA** -- Configuration et vérification TOTP avec codes de récupération
- **Accès invité** -- Création anonyme de tickets avec recherche par token magique
- **Inertia.js + Vue 3 UI** -- Frontend partagé via [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)

## Prérequis

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server, SQLite ou PostgreSQL
- Node.js 18+ (pour les ressources frontend)

## Démarrage Rapide

### 1. Installer le Paquet

```bash
dotnet add package Escalated
```

### 2. Enregistrer les Services

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

### 3. Configurer

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

### 4. Exécuter les Migrations

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

Visitez `/support` -- c'est en ligne.

## Intégration Frontend

Escalated fournit une bibliothèque de composants Vue et des pages par défaut via le paquet npm [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated). Intégrez avec Inertia.js pour un rendu SPA fluide dans votre mise en page existante.

```bash
npm install @escalated-dev/escalated
```

## Architecture

```
src/Escalated/
  Models/           # Plus de 40 modèles d'entités EF Core
  Data/             # EscalatedDbContext avec mappage complet des relations
  Services/         # Logique métier (ticket, SLA, fusion, division, mise en veille, etc.)
  Controllers/
    Admin/          # API du panneau d'administration (CRUD pour toute la configuration)
    Agent/          # File d'attente et actions de l'agent
    Customer/       # Portail client en libre-service
    Widget/         # API publique du widget (recherche KB, tickets invités)
  Middleware/       # Authentification par token API, permissions, limitation de débit
  Events/           # Événements de domaine (TicketCreated, SlaBreached, etc.)
  Notifications/    # Interfaces et modèles de notifications par email
  Configuration/    # Enregistrement DI, options, mappage des endpoints
  Hubs/             # Hub SignalR pour les mises à jour en temps réel
  Enums/            # TicketStatus, TicketPriority, ActivityType
```

## Modèles

Escalated inclut plus de 40 entités EF Core couvrant tout le domaine du helpdesk :

| Catégorie | Modèles |
|----------|--------|
| Principal | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| Agents | AgentProfile, AgentCapacity, Skill, AgentSkill |
| Messagerie | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| Administration | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| Personnalisé | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| Importation | ImportJob, ImportSourceMap |
| Configuration | EscalatedSettings, SavedView |
| Base de Connaissances | Article, ArticleCategory |

Tous les modèles ont des relations, des index et des filtres de requête correctement configurés dans `EscalatedDbContext`.

## Services

| Service | Responsabilité |
|---------|---------------|
| `TicketService` | CRUD complet des tickets, transitions d'état, réponses, tags, départements |
| `SlaService` | Attachement de politiques, détection de violations, vérification des avertissements, enregistrement de la première réponse |
| `AssignmentService` | Assignation d'agents, désassignation, auto-assignation par charge de travail |
| `EscalationService` | Évaluation des règles basées sur conditions, exécution des actions d'escalade |
| `AutomationRunner` | Évaluation des automatisations basées sur le temps et exécution des actions |
| `MacroService` | Appliquer des séquences d'actions de macro aux tickets |
| `TicketMergeService` | Fusionner la source dans la cible avec transfert des réponses |
| `TicketSplitService` | Séparer une réponse en un nouveau ticket lié |
| `TicketSnoozeService` | Mettre en veille/réveiller avec service de réveil en arrière-plan |
| `WebhookDispatcher` | Livraison de webhooks signés HMAC avec logique de réessai |
| `CapacityService` | Limites de tickets simultanés par agent |
| `SkillRoutingService` | Assigner les agents par compétences aux tags de tickets |
| `BusinessHoursCalculator` | Calculs de dates en heures ouvrées avec support des jours fériés |
| `TwoFactorService` | Génération de secrets TOTP, vérification, codes de récupération |
| `AuditLogService` | Enregistrer et interroger les mutations d'entités |
| `KnowledgeBaseService` | CRUD d'articles/catégories, recherche, retours |
| `SavedViewService` | Filtres prédéfinis personnels et partagés |
| `SideConversationService` | Conversations internes avec fils de discussion sur les tickets |
| `ImportService` | Importation multi-étapes avec adaptateurs connectables |
| `SettingsService` | Magasin de configuration clé-valeur |

## Événements

Chaque action de ticket émet un événement de domaine :

| Événement | Quand |
|-------|------|
| `TicketCreatedEvent` | Nouveau ticket créé |
| `TicketStatusChangedEvent` | Transition d'état |
| `TicketAssignedEvent` | Agent assigné |
| `TicketUnassignedEvent` | Agent retiré |
| `ReplyCreatedEvent` | Réponse publique ajoutée |
| `InternalNoteAddedEvent` | Note de l'agent ajoutée |
| `SlaBreachedEvent` | Délai SLA dépassé |
| `SlaWarningEvent` | Délai SLA approchant |
| `TicketEscalatedEvent` | Ticket escaladé |
| `TicketResolvedEvent` | Ticket résolu |
| `TicketClosedEvent` | Ticket fermé |
| `TicketReopenedEvent` | Ticket rouvert |
| `TicketPriorityChangedEvent` | Priorité modifiée |
| `DepartmentChangedEvent` | Département modifié |
| `TagAddedEvent` | Tag ajouté |
| `TagRemovedEvent` | Tag supprimé |

Implémentez `IEscalatedEventDispatcher` pour recevoir ces événements dans votre application hôte :

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

## Endpoints API

### Client

| Méthode | Route | Description |
|--------|-------|-------------|
| GET | `/support/tickets` | Lister les tickets du client |
| POST | `/support/tickets` | Créer un ticket |
| GET | `/support/tickets/{id}` | Voir le ticket |
| POST | `/support/tickets/{id}/reply` | Répondre au ticket |
| POST | `/support/tickets/{id}/close` | Fermer le ticket |
| POST | `/support/tickets/{id}/reopen` | Rouvrir le ticket |

### Agent

| Méthode | Route | Description |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | File d'attente avec filtres |
| GET | `/support/agent/tickets/{id}` | Détail du ticket |
| POST | `/support/agent/tickets/{id}/reply` | Répondre |
| POST | `/support/agent/tickets/{id}/note` | Note interne |
| POST | `/support/agent/tickets/{id}/assign` | Assigner un agent |
| POST | `/support/agent/tickets/{id}/status` | Changer l'état |
| POST | `/support/agent/tickets/{id}/priority` | Changer la priorité |
| POST | `/support/agent/tickets/{id}/macro` | Appliquer une macro |
| POST | `/support/agent/tickets/bulk` | Actions groupées |
| GET | `/support/agent/tickets/dashboard` | Charge de travail de l'agent |

### Administration

| Méthode | Route | Description |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | Gérer les départements |
| GET/POST | `/support/admin/tags` | Gérer les tags |
| GET/POST | `/support/admin/sla-policies` | Gérer les politiques SLA |
| GET/POST | `/support/admin/escalation-rules` | Gérer les règles d'escalade |
| GET/POST | `/support/admin/webhooks` | Gérer les webhooks |
| GET/POST | `/support/admin/api-tokens` | Gérer les tokens API |
| GET/POST | `/support/admin/macros` | Gérer les macros |
| GET/POST | `/support/admin/automations` | Gérer les automatisations |
| GET/POST | `/support/admin/custom-fields` | Gérer les champs personnalisés |
| GET/POST | `/support/admin/business-hours` | Horaires de travail |
| GET/POST | `/support/admin/skills` | Gérer les compétences |
| GET/POST | `/support/admin/roles` | Gérer les rôles |
| GET | `/support/admin/audit-logs` | Consulter les journaux d'audit |
| GET/POST | `/support/admin/settings` | Paramètres de l'application |
| POST | `/support/admin/tickets/{id}/merge` | Fusionner des tickets |
| POST | `/support/admin/tickets/{id}/split` | Diviser un ticket |
| POST | `/support/admin/tickets/{id}/snooze` | Mettre en veille un ticket |
| POST | `/support/admin/tickets/{id}/link` | Lier des tickets |

### Widget (Public)

| Méthode | Route | Description |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | Rechercher dans la base de connaissances |
| POST | `/support/widget/tickets` | Créer un ticket invité |
| GET | `/support/widget/tickets/{token}` | Rechercher par token invité |
| POST | `/support/widget/tickets/{token}/reply` | Réponse invité |
| POST | `/support/widget/tickets/{token}/rate` | Soumettre une note CSAT |
| POST | `/support/widget/kb/articles/{id}/feedback` | Retour sur un article |

## Mises à Jour en Temps Réel

Activez SignalR pour les mises à jour de tickets en direct :

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

Les clients rejoignent des groupes spécifiques aux tickets pour recevoir les mises à jour :

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## Middleware

### Authentification par Token API

Protégez les endpoints API avec l'authentification par token Bearer :

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

Les tokens sont stockés sous forme de hashes SHA-256. Créez des tokens via l'endpoint d'administration API.

### Limitation de Débit

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## Tests

```bash
dotnet test
```

Les tests utilisent xUnit avec Moq et le fournisseur InMemory d'EF Core. La couverture inclut :
- CRUD des tickets et transitions d'état
- Détection des violations et avertissements SLA
- Division, fusion et mise en veille des tickets
- Assignation et calcul de charge de travail
- Correspondance des abonnements aux webhooks
- Génération et vérification des secrets 2FA
- Gestion de capacité
- Validation des modèles et comportement des enums

## Également Disponible Pour

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- Paquet Composer pour Laravel
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- Moteur Ruby on Rails
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- Application réutilisable Django
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- Paquet AdonisJS v6
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- Paquet ASP.NET Core (vous êtes ici)
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- Composants UI Vue 3 + Inertia.js

La même architecture, la même UI Vue -- pour tous les principaux frameworks backend.

## Licence

MIT
