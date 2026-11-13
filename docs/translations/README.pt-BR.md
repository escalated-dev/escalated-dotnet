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
  <b>Português (BR)</b> ·
  <a href="README.ru.md">Русский</a> ·
  <a href="README.tr.md">Türkçe</a> ·
  <a href="README.zh-CN.md">简体中文</a>
</sub></p>

<p align="center">
  <a href="https://escalated.dev"><img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/logo.png" width="72" height="72" alt="Escalated"></a>
</p>

<h1 align="center">Escalated for ASP.NET Core</h1>

<p align="center">
  Adicione um helpdesk completo ao seu app ASP.NET Core.<br>
  Tickets, SLAs, regras de escalonamento, uma caixa de entrada para agentes e um portal do cliente, armazenados via EF Core no banco de dados que você escolher.
</p>

<p align="center">
  <a href="https://escalated.dev"><b>Site</b></a> ·
  <a href="https://escalated.dev/docs"><b>Documentação</b></a> ·
  <a href="https://escalated.dev/cloud"><b>Cloud</b></a> ·
  <a href="../../CHANGELOG.md"><b>Histórico de mudanças</b></a>
</p>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-dark.png">
  <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-light.png" alt="A caixa de entrada dos agentes no Escalated: visualizações salvas com contadores, contagens regressivas de SLA e prioridades em cada ticket" width="100%">
</picture>

## O que ele faz

<table>
  <tr>
    <td width="40%">
      <h3>Uma caixa de entrada em que seus agentes conseguem trabalhar o dia todo</h3>
      Conversa, notas internas, respostas prontas e timers de SLA em uma única tela. Os tickets também chegam por e-mail via Postmark, Mailgun ou AWS SES.
    </td>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-light.png" alt="Um ticket aberto no espaço de trabalho do agente com o editor de resposta">
      </picture>
    </td>
  </tr>
  <tr>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-light.png" alt="A página de relatórios do admin com tempos de resposta, CSAT e violações de SLA">
      </picture>
    </td>
    <td width="40%">
      <h3>SLAs e regras de escalonamento que você consegue ver</h3>
      Metas de resposta e resolução por prioridade com horário comercial, regras que escalonam ou reatribuem quando uma meta atrasa e relatórios de violações.
    </td>
  </tr>
</table>

## Recursos

<table>
<tr>
<td width="33%" valign="top">

**Tickets e caixa de entrada**
- Ciclo de vida completo com transições de status
- Filtros de fila e ações em massa
- Notas internas e respostas prontas
- Macros para ações em lote com um clique
- Conversas paralelas nos tickets
- Mesclar, vincular e dividir tickets
- Adiar com reativação automática
- Visualizações salvas pessoais e compartilhadas
- Tickets vinculados a entidades do seu próprio app
- Anexos com limites de tamanho
- Linha do tempo de atividades em cada ticket

</td>
<td width="33%" valign="top">

**SLAs e roteamento**
- Metas de resposta e resolução por prioridade
- Horário comercial e detecção de violações
- Regras que escalonam, mudam a prioridade ou reatribuem
- Automações baseadas em tempo
- Roteamento por habilidades
- Limites de capacidade por agente

</td>
<td width="33%" valign="top">

**E-mail**
- Entrada via Postmark, Mailgun e AWS SES
- Verificação de Reply-To assinado
- Respostas aceitas somente do solicitante
- Threads com cabeçalhos Message-ID

</td>
</tr>
<tr>
<td width="33%" valign="top">

**Clientes**
- Portal do cliente com autoatendimento
- Tickets de convidados com consulta por token mágico
- API de widget incorporável
- Avaliações CSAT em tickets resolvidos
- Base de conhecimento com busca e feedback

</td>
<td width="33%" valign="top">

**Administração e segurança**
- Departamentos, SLAs, regras e tags
- Papéis e permissões
- Autenticação em dois fatores TOTP com códigos de recuperação
- Login com SAML 2, OAuth 2 / OIDC e JWT
- Log de auditoria com valores antigos e novos
- Tokens de API com permissões restritas

</td>
<td width="33%" valign="top">

**Extensão e integração**
- Webhooks assinados com HMAC e novas tentativas
- Campos personalizados com exibição condicional
- Assistente de importação com adaptadores substituíveis
- Atualizações ao vivo via SignalR (opcional)
- Interface compartilhada em Vue 3 + Inertia

</td>
</tr>
</table>

<p align="center"><sub><a href="../features.md">Todos os recursos em detalhes</a></sub></p>

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET"></a>
  <a href="https://vuejs.org"><img src="https://img.shields.io/badge/Vue.js-4FC08D?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue.js"></a>
  <a href="https://inertiajs.com"><img src="https://img.shields.io/badge/Inertia.js-9553E9?style=for-the-badge&logo=inertia&logoColor=white" alt="Inertia.js"></a>
  <a href="https://www.postgresql.org"><img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL"></a>
  <a href="https://www.sqlite.org"><img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite"></a>
</p>

## Início rápido

```bash
dotnet add package Escalated
dotnet ef database update --context EscalatedDbContext
```

Registre-o em `Program.cs` com `builder.Services.AddEscalated(...)` e `app.MapEscalated()` e depois acesse `/support`. Requer .NET 8+ e SQL Server, SQLite ou PostgreSQL. O [guia de primeiros passos](../getting-started.md) explica configuração e autorização, e [a interface Inertia](../inertia-ui.md) ativa as telas compartilhadas.

## Saiba mais

- [Todos os recursos](../features.md) · [Primeiros passos](../getting-started.md) · [A interface Inertia](../inertia-ui.md)
- [Personalização, tempo real e localização](../customization.md) · [Arquitetura, models e eventos](../architecture.md) · [Endpoints da API](../api-endpoints.md)
- [Controle de acesso](../access-control.md) · [Acesso a tickets e roteamento](../ticket-access-and-routing.md) · [Workflows](../workflows.md)
- [E-mail de entrada](../inbound-email.md) · [E-mails de notificação](../notification-emails.md) · [Acesso de convidados](../guest-access.md)
- [Incorporando o widget](../embedding-the-widget.md) · [Incorporando o admin](../embedding-admin.md) · [Painel do agente](../agent-panel.md)
- [Plugins](../plugins.md) · [Newsletters](../newsletters.md) · [Desenvolvimento e testes](../development.md)

O Escalated também está disponível para [Laravel](https://github.com/escalated-dev/escalated-laravel), [Rails](https://github.com/escalated-dev/escalated-rails), [Django](https://github.com/escalated-dev/escalated-django), [NestJS](https://github.com/escalated-dev/escalated-nestjs), [AdonisJS](https://github.com/escalated-dev/escalated-adonis), [Symfony](https://github.com/escalated-dev/escalated-symfony), [Phoenix](https://github.com/escalated-dev/escalated-phoenix), [Go](https://github.com/escalated-dev/escalated-go), [Spring](https://github.com/escalated-dev/escalated-spring), [WordPress](https://github.com/escalated-dev/escalated-wordpress) e [Filament](https://github.com/escalated-dev/escalated-filament). A interface fica em [escalated](https://github.com/escalated-dev/escalated).

## Licença

MIT. Copyright (c) 2026 Escalated.dev. Veja [LICENSE](../../LICENSE).
