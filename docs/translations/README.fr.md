<p align="center"><sub>
  <a href="README.ar.md">العربية</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="../../README.md">English</a> ·
  <a href="README.es.md">Español</a> ·
  <b>Français</b> ·
  <a href="README.it.md">Italiano</a> ·
  <a href="README.ja.md">日本語</a> ·
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
  Ajoutez un helpdesk complet à votre application ASP.NET Core.<br>
  Tickets, SLA, règles d'escalade, une boîte de réception pour les agents et un portail client, stockés via EF Core dans la base de données de votre choix.
</p>

<p align="center">
  <a href="https://escalated.dev"><b>Site web</b></a> ·
  <a href="https://escalated.dev/docs"><b>Documentation</b></a> ·
  <a href="https://escalated.dev/cloud"><b>Cloud</b></a> ·
  <a href="../../CHANGELOG.md"><b>Journal des modifications</b></a>
</p>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-dark.png">
  <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-light.png" alt="La boîte de réception des agents d'Escalated : vues enregistrées avec compteurs, comptes à rebours SLA et priorités sur chaque ticket" width="100%">
</picture>

## Ce qu'il fait

<table>
  <tr>
    <td width="40%">
      <h3>Une boîte de réception où vos agents peuvent travailler toute la journée</h3>
      Conversation, notes internes, réponses prédéfinies et minuteurs SLA sur un seul écran. Les tickets arrivent aussi par e-mail via Postmark, Mailgun ou AWS SES.
    </td>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-light.png" alt="Un ticket ouvert dans l'espace de travail de l'agent avec l'éditeur de réponse">
      </picture>
    </td>
  </tr>
  <tr>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-light.png" alt="La page des rapports d'administration avec les temps de réponse, le CSAT et les dépassements de SLA">
      </picture>
    </td>
    <td width="40%">
      <h3>Des SLA et des règles d'escalade bien visibles</h3>
      Des objectifs de réponse et de résolution par priorité avec heures ouvrées, des règles qui escaladent ou réattribuent quand un objectif glisse, et des rapports sur les dépassements.
    </td>
  </tr>
</table>

## Fonctionnalités

<table>
<tr>
<td width="33%" valign="top">

**Tickets et boîte de réception**
- Cycle de vie complet avec transitions de statut
- Filtres de file et actions groupées
- Notes internes et réponses prédéfinies
- Macros pour des actions groupées en un clic
- Conversations annexes sur les tickets
- Fusionner, lier et scinder des tickets
- Mise en attente avec réveil automatique
- Vues enregistrées personnelles et partagées
- Tickets liés aux entités de votre application
- Pièces jointes avec limites de taille
- Historique d'activité sur chaque ticket

</td>
<td width="33%" valign="top">

**SLA et routage**
- Objectifs de réponse et de résolution par priorité
- Heures ouvrées et détection des dépassements
- Règles qui escaladent, changent la priorité ou réattribuent
- Automatisations basées sur le temps
- Routage par compétences
- Limites de capacité par agent

</td>
<td width="33%" valign="top">

**E-mail**
- Réception via Postmark, Mailgun et AWS SES
- Vérification du Reply-To signé
- Réponses acceptées uniquement de la part du demandeur
- Fils de discussion via les en-têtes Message-ID

</td>
</tr>
<tr>
<td width="33%" valign="top">

**Clients**
- Portail client en libre-service
- Tickets invités avec consultation par jeton magique
- API de widget intégrable
- Notes CSAT sur les tickets résolus
- Base de connaissances avec recherche et retours

</td>
<td width="33%" valign="top">

**Administration et sécurité**
- Services, SLA, règles et tags
- Rôles et permissions
- Double authentification TOTP avec codes de récupération
- Connexion SAML 2, OAuth 2 / OIDC et JWT
- Journal d'audit avec anciennes et nouvelles valeurs
- Jetons d'API avec permissions limitées

</td>
<td width="33%" valign="top">

**Étendre et intégrer**
- Webhooks signés HMAC avec nouvelles tentatives
- Champs personnalisés avec affichage conditionnel
- Assistant d'import avec adaptateurs interchangeables
- Mises à jour en direct via SignalR (optionnel)
- Interface partagée Vue 3 + Inertia

</td>
</tr>
</table>

<p align="center"><sub><a href="../features.md">Toutes les fonctionnalités en détail</a></sub></p>

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET"></a>
  <a href="https://vuejs.org"><img src="https://img.shields.io/badge/Vue.js-4FC08D?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue.js"></a>
  <a href="https://inertiajs.com"><img src="https://img.shields.io/badge/Inertia.js-9553E9?style=for-the-badge&logo=inertia&logoColor=white" alt="Inertia.js"></a>
  <a href="https://www.postgresql.org"><img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL"></a>
  <a href="https://www.sqlite.org"><img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite"></a>
</p>

## Démarrage rapide

```bash
dotnet add package Escalated
dotnet ef database update --context EscalatedDbContext
```

Enregistrez-le dans `Program.cs` avec `builder.Services.AddEscalated(...)` et `app.MapEscalated()`, puis ouvrez `/support`. Nécessite .NET 8+ et SQL Server, SQLite ou PostgreSQL. Le [guide de démarrage](../getting-started.md) explique la configuration et les autorisations, et [l'interface Inertia](../inertia-ui.md) active les écrans partagés.

## En savoir plus

- [Toutes les fonctionnalités](../features.md) · [Premiers pas](../getting-started.md) · [L'interface Inertia](../inertia-ui.md)
- [Personnalisation, temps réel et localisation](../customization.md) · [Architecture, modèles et événements](../architecture.md) · [Endpoints de l'API](../api-endpoints.md)
- [Contrôle d'accès](../access-control.md) · [Accès aux tickets et routage](../ticket-access-and-routing.md) · [Workflows](../workflows.md)
- [E-mails entrants](../inbound-email.md) · [E-mails de notification](../notification-emails.md) · [Accès invité](../guest-access.md)
- [Intégrer le widget](../embedding-the-widget.md) · [Intégrer l'administration](../embedding-admin.md) · [Panneau agent](../agent-panel.md)
- [Plugins](../plugins.md) · [Newsletters](../newsletters.md) · [Développement et tests](../development.md)

Escalated est aussi disponible pour [Laravel](https://github.com/escalated-dev/escalated-laravel), [Rails](https://github.com/escalated-dev/escalated-rails), [Django](https://github.com/escalated-dev/escalated-django), [NestJS](https://github.com/escalated-dev/escalated-nestjs), [AdonisJS](https://github.com/escalated-dev/escalated-adonis), [Symfony](https://github.com/escalated-dev/escalated-symfony), [Phoenix](https://github.com/escalated-dev/escalated-phoenix), [Go](https://github.com/escalated-dev/escalated-go), [Spring](https://github.com/escalated-dev/escalated-spring), [WordPress](https://github.com/escalated-dev/escalated-wordpress) et [Filament](https://github.com/escalated-dev/escalated-filament). L'interface se trouve dans [escalated](https://github.com/escalated-dev/escalated).

## Licence

MIT. Copyright (c) 2026 Escalated.dev. Voir [LICENSE](../../LICENSE).
