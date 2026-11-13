<p align="center"><sub>
  <a href="README.ar.md">العربية</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="../../README.md">English</a> ·
  <a href="README.es.md">Español</a> ·
  <a href="README.fr.md">Français</a> ·
  <b>Italiano</b> ·
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
  Aggiungi un helpdesk completo alla tua app ASP.NET Core.<br>
  Ticket, SLA, regole di escalation, una inbox per gli agenti e un portale clienti, salvati tramite EF Core nel database che scegli.
</p>

<p align="center">
  <a href="https://escalated.dev"><b>Sito web</b></a> ·
  <a href="https://escalated.dev/docs"><b>Documentazione</b></a> ·
  <a href="https://escalated.dev/cloud"><b>Cloud</b></a> ·
  <a href="../../CHANGELOG.md"><b>Registro delle modifiche</b></a>
</p>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-dark.png">
  <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-light.png" alt="La inbox degli agenti di Escalated: viste salvate con contatori, conti alla rovescia SLA e priorità su ogni ticket" width="100%">
</picture>

## Cosa fa

<table>
  <tr>
    <td width="40%">
      <h3>Una inbox in cui i tuoi agenti possono lavorare tutto il giorno</h3>
      Conversazione, note interne, risposte predefinite e timer SLA in un'unica schermata. I ticket arrivano anche via email tramite Postmark, Mailgun o AWS SES.
    </td>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-light.png" alt="Un ticket aperto nell'area di lavoro dell'agente con l'editor di risposta">
      </picture>
    </td>
  </tr>
  <tr>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-light.png" alt="La pagina dei report di amministrazione con tempi di risposta, CSAT e violazioni degli SLA">
      </picture>
    </td>
    <td width="40%">
      <h3>SLA e regole di escalation sempre visibili</h3>
      Obiettivi di risposta e risoluzione per priorità con orari lavorativi, regole che eseguono l'escalation o riassegnano quando un obiettivo slitta, e report sulle violazioni.
    </td>
  </tr>
</table>

## Funzionalità

<table>
<tr>
<td width="33%" valign="top">

**Ticket e inbox**
- Ciclo di vita completo con transizioni di stato
- Filtri della coda e azioni di massa
- Note interne e risposte predefinite
- Macro per azioni in blocco con un clic
- Conversazioni secondarie sui ticket
- Unire, collegare e dividere i ticket
- Posticipo con riattivazione automatica
- Viste salvate personali e condivise
- Ticket collegati a entità della tua app
- Allegati con limiti di dimensione
- Cronologia delle attività su ogni ticket

</td>
<td width="33%" valign="top">

**SLA e instradamento**
- Obiettivi di risposta e risoluzione per priorità
- Orari lavorativi e rilevamento delle violazioni
- Regole che eseguono l'escalation, cambiano priorità o riassegnano
- Automazioni basate sul tempo
- Instradamento per competenze
- Limiti di capacità per agente

</td>
<td width="33%" valign="top">

**Email**
- Ricezione da Postmark, Mailgun e AWS SES
- Verifica del Reply-To firmato
- Risposte accettate solo dal richiedente
- Thread tramite intestazioni Message-ID

</td>
</tr>
<tr>
<td width="33%" valign="top">

**Clienti**
- Portale clienti self-service
- Ticket ospite con consultazione tramite magic token
- API del widget incorporabile
- Valutazioni CSAT sui ticket risolti
- Knowledge base con ricerca e feedback

</td>
<td width="33%" valign="top">

**Amministrazione e sicurezza**
- Reparti, SLA, regole e tag
- Ruoli e permessi
- Autenticazione a due fattori TOTP con codici di recupero
- Accesso con SAML 2, OAuth 2 / OIDC e JWT
- Registro di audit con valori vecchi e nuovi
- Token API con permessi limitati

</td>
<td width="33%" valign="top">

**Estendere e integrare**
- Webhook firmati HMAC con nuovi tentativi
- Campi personalizzati con visualizzazione condizionale
- Procedura guidata di importazione con adattatori sostituibili
- Aggiornamenti in tempo reale tramite SignalR (opzionale)
- Interfaccia condivisa Vue 3 + Inertia

</td>
</tr>
</table>

<p align="center"><sub><a href="../features.md">Tutte le funzionalità nel dettaglio</a></sub></p>

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET"></a>
  <a href="https://vuejs.org"><img src="https://img.shields.io/badge/Vue.js-4FC08D?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue.js"></a>
  <a href="https://inertiajs.com"><img src="https://img.shields.io/badge/Inertia.js-9553E9?style=for-the-badge&logo=inertia&logoColor=white" alt="Inertia.js"></a>
  <a href="https://www.postgresql.org"><img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL"></a>
  <a href="https://www.sqlite.org"><img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite"></a>
</p>

## Avvio rapido

```bash
dotnet add package Escalated
dotnet ef database update --context EscalatedDbContext
```

Registralo in `Program.cs` con `builder.Services.AddEscalated(...)` e `app.MapEscalated()`, poi visita `/support`. Richiede .NET 8+ e SQL Server, SQLite o PostgreSQL. La [guida introduttiva](../getting-started.md) spiega configurazione e autorizzazione, e [l'interfaccia Inertia](../inertia-ui.md) attiva le schermate condivise.

## Per saperne di più

- [Tutte le funzionalità](../features.md) · [Per iniziare](../getting-started.md) · [L'interfaccia Inertia](../inertia-ui.md)
- [Personalizzazione, tempo reale e localizzazione](../customization.md) · [Architettura, modelli ed eventi](../architecture.md) · [Endpoint API](../api-endpoints.md)
- [Controllo degli accessi](../access-control.md) · [Accesso ai ticket e instradamento](../ticket-access-and-routing.md) · [Workflow](../workflows.md)
- [Email in entrata](../inbound-email.md) · [Email di notifica](../notification-emails.md) · [Accesso ospite](../guest-access.md)
- [Incorporare il widget](../embedding-the-widget.md) · [Incorporare l'amministrazione](../embedding-admin.md) · [Pannello agente](../agent-panel.md)
- [Plugin](../plugins.md) · [Newsletter](../newsletters.md) · [Sviluppo e test](../development.md)

Escalated è disponibile anche per [Laravel](https://github.com/escalated-dev/escalated-laravel), [Rails](https://github.com/escalated-dev/escalated-rails), [Django](https://github.com/escalated-dev/escalated-django), [NestJS](https://github.com/escalated-dev/escalated-nestjs), [AdonisJS](https://github.com/escalated-dev/escalated-adonis), [Symfony](https://github.com/escalated-dev/escalated-symfony), [Phoenix](https://github.com/escalated-dev/escalated-phoenix), [Go](https://github.com/escalated-dev/escalated-go), [Spring](https://github.com/escalated-dev/escalated-spring), [WordPress](https://github.com/escalated-dev/escalated-wordpress) e [Filament](https://github.com/escalated-dev/escalated-filament). L'interfaccia si trova in [escalated](https://github.com/escalated-dev/escalated).

## Licenza

MIT. Copyright (c) 2026 Escalated.dev. Vedi [LICENSE](../../LICENSE).
