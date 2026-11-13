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
  <b>Polski</b> ·
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
  Dodaj kompletny helpdesk do swojej aplikacji ASP.NET Core.<br>
  Zgłoszenia, SLA, reguły eskalacji, skrzynka dla agentów i portal klienta, zapisywane przez EF Core w wybranej przez Ciebie bazie danych.
</p>

<p align="center">
  <a href="https://escalated.dev"><b>Strona</b></a> ·
  <a href="https://escalated.dev/docs"><b>Dokumentacja</b></a> ·
  <a href="https://escalated.dev/cloud"><b>Cloud</b></a> ·
  <a href="../../CHANGELOG.md"><b>Lista zmian</b></a>
</p>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-dark.png">
  <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-light.png" alt="Skrzynka agentów w Escalated: zapisane widoki z licznikami, odliczanie SLA i priorytety przy każdym zgłoszeniu" width="100%">
</picture>

## Co robi

<table>
  <tr>
    <td width="40%">
      <h3>Skrzynka, w której Twoi agenci mogą pracować cały dzień</h3>
      Rozmowa, notatki wewnętrzne, gotowe odpowiedzi i liczniki SLA na jednym ekranie. Zgłoszenia przychodzą też e-mailem przez Postmark, Mailgun lub AWS SES.
    </td>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-light.png" alt="Otwarte zgłoszenie w obszarze roboczym agenta z edytorem odpowiedzi">
      </picture>
    </td>
  </tr>
  <tr>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-light.png" alt="Strona raportów w panelu administratora z czasami odpowiedzi, CSAT i naruszeniami SLA">
      </picture>
    </td>
    <td width="40%">
      <h3>SLA i reguły eskalacji, które widać</h3>
      Cele czasu odpowiedzi i rozwiązania dla każdego priorytetu z uwzględnieniem godzin pracy, reguły, które eskalują lub ponownie przypisują zgłoszenie, gdy termin się przesuwa, oraz raporty naruszeń.
    </td>
  </tr>
</table>

## Funkcje

<table>
<tr>
<td width="33%" valign="top">

**Zgłoszenia i skrzynka**
- Pełny cykl życia ze zmianami statusu
- Filtry kolejki i akcje zbiorcze
- Notatki wewnętrzne i gotowe odpowiedzi
- Makra do akcji zbiorczych jednym kliknięciem
- Rozmowy poboczne przy zgłoszeniach
- Scalanie, łączenie i dzielenie zgłoszeń
- Odkładanie z automatycznym wybudzeniem
- Osobiste i współdzielone zapisane widoki
- Zgłoszenia powiązane z obiektami z Twojej aplikacji
- Załączniki z limitami rozmiaru
- Oś czasu aktywności przy każdym zgłoszeniu

</td>
<td width="33%" valign="top">

**SLA i routing**
- Cele odpowiedzi i rozwiązania dla każdego priorytetu
- Godziny pracy i wykrywanie naruszeń
- Reguły, które eskalują, zmieniają priorytet lub przypisują ponownie
- Automatyzacje oparte na czasie
- Routing według umiejętności
- Limity obciążenia dla każdego agenta

</td>
<td width="33%" valign="top">

**E-mail**
- Poczta przychodząca z Postmark, Mailgun i AWS SES
- Weryfikacja podpisanego Reply-To
- Odpowiedzi przyjmowane tylko od zgłaszającego
- Wątki na podstawie nagłówków Message-ID

</td>
</tr>
<tr>
<td width="33%" valign="top">

**Klienci**
- Samoobsługowy portal klienta
- Zgłoszenia gości z wyszukiwaniem po magicznym tokenie
- Osadzane API widżetu
- Oceny CSAT dla rozwiązanych zgłoszeń
- Baza wiedzy z wyszukiwaniem i opiniami

</td>
<td width="33%" valign="top">

**Administracja i bezpieczeństwo**
- Działy, SLA, reguły i tagi
- Role i uprawnienia
- Dwuskładnikowe logowanie TOTP z kodami odzyskiwania
- Logowanie przez SAML 2, OAuth 2 / OIDC i JWT
- Dziennik audytu ze starymi i nowymi wartościami
- Tokeny API z ograniczonymi uprawnieniami

</td>
<td width="33%" valign="top">

**Rozszerzenia i integracje**
- Webhooki podpisane HMAC z ponawianiem
- Pola niestandardowe z warunkowym wyświetlaniem
- Kreator importu z wymiennymi adapterami
- Aktualizacje na żywo przez SignalR (opcjonalnie)
- Wspólny interfejs Vue 3 + Inertia

</td>
</tr>
</table>

<p align="center"><sub><a href="../features.md">Wszystkie funkcje w szczegółach</a></sub></p>

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET"></a>
  <a href="https://vuejs.org"><img src="https://img.shields.io/badge/Vue.js-4FC08D?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue.js"></a>
  <a href="https://inertiajs.com"><img src="https://img.shields.io/badge/Inertia.js-9553E9?style=for-the-badge&logo=inertia&logoColor=white" alt="Inertia.js"></a>
  <a href="https://www.postgresql.org"><img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL"></a>
  <a href="https://www.sqlite.org"><img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite"></a>
</p>

## Szybki start

```bash
dotnet add package Escalated
dotnet ef database update --context EscalatedDbContext
```

Zarejestruj go w `Program.cs` za pomocą `builder.Services.AddEscalated(...)` i `app.MapEscalated()`, a następnie otwórz `/support`. Wymaga .NET 8+ oraz SQL Server, SQLite lub PostgreSQL. [Przewodnik na start](../getting-started.md) opisuje konfigurację i autoryzację, a [interfejs Inertia](../inertia-ui.md) włącza wspólne ekrany.

## Dowiedz się więcej

- [Wszystkie funkcje](../features.md) · [Pierwsze kroki](../getting-started.md) · [Interfejs Inertia](../inertia-ui.md)
- [Dostosowywanie, czas rzeczywisty i lokalizacja](../customization.md) · [Architektura, modele i zdarzenia](../architecture.md) · [Endpointy API](../api-endpoints.md)
- [Kontrola dostępu](../access-control.md) · [Dostęp do zgłoszeń i routing](../ticket-access-and-routing.md) · [Workflowy](../workflows.md)
- [Poczta przychodząca](../inbound-email.md) · [E-maile z powiadomieniami](../notification-emails.md) · [Dostęp dla gości](../guest-access.md)
- [Osadzanie widżetu](../embedding-the-widget.md) · [Osadzanie panelu administratora](../embedding-admin.md) · [Panel agenta](../agent-panel.md)
- [Wtyczki](../plugins.md) · [Newslettery](../newsletters.md) · [Rozwój i testy](../development.md)

Escalated jest dostępny również dla [Laravel](https://github.com/escalated-dev/escalated-laravel), [Rails](https://github.com/escalated-dev/escalated-rails), [Django](https://github.com/escalated-dev/escalated-django), [NestJS](https://github.com/escalated-dev/escalated-nestjs), [AdonisJS](https://github.com/escalated-dev/escalated-adonis), [Symfony](https://github.com/escalated-dev/escalated-symfony), [Phoenix](https://github.com/escalated-dev/escalated-phoenix), [Go](https://github.com/escalated-dev/escalated-go), [Spring](https://github.com/escalated-dev/escalated-spring), [WordPress](https://github.com/escalated-dev/escalated-wordpress) i [Filament](https://github.com/escalated-dev/escalated-filament). Interfejs znajduje się w [escalated](https://github.com/escalated-dev/escalated).

## Licencja

MIT. Copyright (c) 2026 Escalated.dev. Zobacz [LICENSE](../../LICENSE).
