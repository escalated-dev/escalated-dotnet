<p align="center"><sub>
  <a href="README.ar.md">العربية</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="../../README.md">English</a> ·
  <b>Español</b> ·
  <a href="README.fr.md">Français</a> ·
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
  Añade un helpdesk completo a tu aplicación ASP.NET Core.<br>
  Tickets, SLA, reglas de escalado, una bandeja de entrada para agentes y un portal de clientes, almacenados con EF Core en la base de datos que elijas.
</p>

<p align="center">
  <a href="https://escalated.dev"><b>Sitio web</b></a> ·
  <a href="https://escalated.dev/docs"><b>Documentación</b></a> ·
  <a href="https://escalated.dev/cloud"><b>Cloud</b></a> ·
  <a href="../../CHANGELOG.md"><b>Registro de cambios</b></a>
</p>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-dark.png">
  <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-light.png" alt="La bandeja de entrada de agentes de Escalated: vistas guardadas con contadores, cuentas atrás de SLA y prioridades en cada ticket" width="100%">
</picture>

## Qué hace

<table>
  <tr>
    <td width="40%">
      <h3>Una bandeja de entrada en la que tus agentes pueden trabajar todo el día</h3>
      Conversación, notas internas, respuestas predefinidas y temporizadores de SLA en una sola pantalla. Los tickets también llegan por correo electrónico a través de Postmark, Mailgun o AWS SES.
    </td>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-light.png" alt="Un ticket abierto en el espacio de trabajo del agente con el editor de respuestas">
      </picture>
    </td>
  </tr>
  <tr>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-light.png" alt="La página de informes de administración con tiempos de respuesta, CSAT e incumplimientos de SLA">
      </picture>
    </td>
    <td width="40%">
      <h3>SLA y reglas de escalado que puedes ver</h3>
      Objetivos de respuesta y resolución por prioridad con horario laboral, reglas que escalan o reasignan cuando no se cumplen, e informes sobre incumplimientos.
    </td>
  </tr>
</table>

## Funciones

<table>
<tr>
<td width="33%" valign="top">

**Tickets y bandeja de entrada**
- Ciclo de vida completo con transiciones de estado
- Filtros de cola y acciones masivas
- Notas internas y respuestas predefinidas
- Macros para acciones por lotes con un clic
- Conversaciones paralelas en los tickets
- Fusionar, vincular y dividir tickets
- Posponer con reactivación automática
- Vistas guardadas personales y compartidas
- Tickets vinculados a entidades de tu propia aplicación
- Adjuntos con límites de tamaño
- Historial de actividad en cada ticket

</td>
<td width="33%" valign="top">

**SLA y enrutamiento**
- Objetivos de respuesta y resolución por prioridad
- Horario laboral y detección de incumplimientos
- Reglas que escalan, cambian la prioridad o reasignan
- Automatizaciones basadas en tiempo
- Enrutamiento por habilidades
- Límites de capacidad por agente

</td>
<td width="33%" valign="top">

**Correo electrónico**
- Entrada desde Postmark, Mailgun y AWS SES
- Verificación de Reply-To firmado
- Solo se aceptan respuestas del solicitante
- Hilos con cabeceras Message-ID

</td>
</tr>
<tr>
<td width="33%" valign="top">

**Clientes**
- Portal de autoservicio para clientes
- Tickets de invitados con consulta por token mágico
- API de widget integrable
- Valoraciones CSAT en tickets resueltos
- Base de conocimiento con búsqueda y valoraciones

</td>
<td width="33%" valign="top">

**Administración y seguridad**
- Departamentos, SLA, reglas y etiquetas
- Roles y permisos
- Doble factor TOTP con códigos de recuperación
- Inicio de sesión con SAML 2, OAuth 2 / OIDC y JWT
- Registro de auditoría con valores anteriores y nuevos
- Tokens de API con permisos acotados

</td>
<td width="33%" valign="top">

**Ampliar e integrar**
- Webhooks firmados con HMAC y reintentos
- Campos personalizados con visualización condicional
- Asistente de importación con adaptadores intercambiables
- Actualizaciones en vivo con SignalR (opcional)
- Interfaz compartida con Vue 3 + Inertia

</td>
</tr>
</table>

<p align="center"><sub><a href="../features.md">Todas las funciones en detalle</a></sub></p>

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET"></a>
  <a href="https://vuejs.org"><img src="https://img.shields.io/badge/Vue.js-4FC08D?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue.js"></a>
  <a href="https://inertiajs.com"><img src="https://img.shields.io/badge/Inertia.js-9553E9?style=for-the-badge&logo=inertia&logoColor=white" alt="Inertia.js"></a>
  <a href="https://www.postgresql.org"><img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL"></a>
  <a href="https://www.sqlite.org"><img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite"></a>
</p>

## Inicio rápido

```bash
dotnet add package Escalated
dotnet ef database update --context EscalatedDbContext
```

Regístralo en `Program.cs` con `builder.Services.AddEscalated(...)` y `app.MapEscalated()`, y después visita `/support`. Requiere .NET 8+ y SQL Server, SQLite o PostgreSQL. La [guía de primeros pasos](../getting-started.md) explica la configuración y la autorización, y [la interfaz Inertia](../inertia-ui.md) activa las pantallas compartidas.

## Más información

- [Todas las funciones](../features.md) · [Primeros pasos](../getting-started.md) · [La interfaz Inertia](../inertia-ui.md)
- [Personalización, tiempo real y localización](../customization.md) · [Arquitectura, modelos y eventos](../architecture.md) · [Endpoints de la API](../api-endpoints.md)
- [Control de acceso](../access-control.md) · [Acceso a tickets y enrutamiento](../ticket-access-and-routing.md) · [Workflows](../workflows.md)
- [Correo entrante](../inbound-email.md) · [Correos de notificación](../notification-emails.md) · [Acceso de invitados](../guest-access.md)
- [Insertar el widget](../embedding-the-widget.md) · [Insertar la administración](../embedding-admin.md) · [Panel del agente](../agent-panel.md)
- [Plugins](../plugins.md) · [Boletines](../newsletters.md) · [Desarrollo y pruebas](../development.md)

Escalated también está disponible para [Laravel](https://github.com/escalated-dev/escalated-laravel), [Rails](https://github.com/escalated-dev/escalated-rails), [Django](https://github.com/escalated-dev/escalated-django), [NestJS](https://github.com/escalated-dev/escalated-nestjs), [AdonisJS](https://github.com/escalated-dev/escalated-adonis), [Symfony](https://github.com/escalated-dev/escalated-symfony), [Phoenix](https://github.com/escalated-dev/escalated-phoenix), [Go](https://github.com/escalated-dev/escalated-go), [Spring](https://github.com/escalated-dev/escalated-spring), [WordPress](https://github.com/escalated-dev/escalated-wordpress) y [Filament](https://github.com/escalated-dev/escalated-filament). La interfaz está en [escalated](https://github.com/escalated-dev/escalated).

## Licencia

MIT. Copyright (c) 2026 Escalated.dev. Consulta [LICENSE](../../LICENSE).
