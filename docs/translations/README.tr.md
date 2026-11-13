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
  <a href="README.ru.md">Русский</a> ·
  <b>Türkçe</b> ·
  <a href="README.zh-CN.md">简体中文</a>
</sub></p>

<p align="center">
  <a href="https://escalated.dev"><img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/logo.png" width="72" height="72" alt="Escalated"></a>
</p>

<h1 align="center">Escalated for ASP.NET Core</h1>

<p align="center">
  ASP.NET Core uygulamanıza eksiksiz bir yardım masası ekleyin.<br>
  Talepler, SLA'lar, eskalasyon kuralları, temsilci gelen kutusu ve müşteri portalı, EF Core ile seçtiğiniz veritabanında saklanır.
</p>

<p align="center">
  <a href="https://escalated.dev"><b>Web sitesi</b></a> ·
  <a href="https://escalated.dev/docs"><b>Dokümantasyon</b></a> ·
  <a href="https://escalated.dev/cloud"><b>Cloud</b></a> ·
  <a href="../../CHANGELOG.md"><b>Değişiklik günlüğü</b></a>
</p>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-dark.png">
  <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-light.png" alt="Escalated temsilci gelen kutusu: sayaçlı kayıtlı görünümler, SLA geri sayımları ve her talepte öncelikler" width="100%">
</picture>

## Neler yapar

<table>
  <tr>
    <td width="40%">
      <h3>Temsilcilerinizin gün boyu çalışabileceği bir gelen kutusu</h3>
      Yazışma, dahili notlar, hazır yanıtlar ve SLA sayaçları tek ekranda. Talepler Postmark, Mailgun veya AWS SES üzerinden e-postayla da gelir.
    </td>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-light.png" alt="Temsilci çalışma alanında yanıt düzenleyicisiyle açık bir talep">
      </picture>
    </td>
  </tr>
  <tr>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-light.png" alt="Yanıt süreleri, CSAT ve SLA ihlallerini gösteren yönetici raporları sayfası">
      </picture>
    </td>
    <td width="40%">
      <h3>Görebildiğiniz SLA'lar ve eskalasyon kuralları</h3>
      Mesai saatlerini dikkate alan, önceliğe göre yanıt ve çözüm hedefleri, hedef kaçtığında eskalasyon yapan veya talebi yeniden atayan kurallar ve ihlal raporları.
    </td>
  </tr>
</table>

## Özellikler

<table>
<tr>
<td width="33%" valign="top">

**Talepler ve gelen kutusu**
- Durum geçişleriyle tam yaşam döngüsü
- Kuyruk filtreleri ve toplu işlemler
- Dahili notlar ve hazır yanıtlar
- Tek tıkla toplu işlemler için makrolar
- Taleplerde yan yazışmalar
- Talepleri birleştirme, bağlama ve bölme
- Otomatik uyandırmayla erteleme
- Kişisel ve paylaşılan kayıtlı görünümler
- Talepleri kendi uygulamanızdaki varlıklara bağlama
- Boyut sınırlı ekler
- Her talepte etkinlik zaman çizelgesi

</td>
<td width="33%" valign="top">

**SLA'lar ve yönlendirme**
- Önceliğe göre yanıt ve çözüm hedefleri
- Mesai saatleri ve ihlal tespiti
- Eskalasyon yapan, önceliği değiştiren veya yeniden atayan kurallar
- Zamana dayalı otomasyonlar
- Beceriye dayalı yönlendirme
- Temsilci başına kapasite sınırları

</td>
<td width="33%" valign="top">

**E-posta**
- Postmark, Mailgun ve AWS SES üzerinden gelen e-posta
- İmzalı Reply-To doğrulaması
- Yanıtlar yalnızca talep sahibinden kabul edilir
- Message-ID başlıklarıyla yazışma zinciri

</td>
</tr>
<tr>
<td width="33%" valign="top">

**Müşteriler**
- Self servis müşteri portalı
- Sihirli token ile sorgulanan misafir talepleri
- Gömülebilir widget API'si
- Çözülen taleplerde CSAT puanları
- Arama ve geri bildirim içeren bilgi bankası

</td>
<td width="33%" valign="top">

**Yönetim ve güvenlik**
- Departmanlar, SLA'lar, kurallar ve etiketler
- Roller ve izinler
- Kurtarma kodlarıyla TOTP iki faktörlü doğrulama
- SAML 2, OAuth 2 / OIDC ve JWT ile oturum açma
- Eski ve yeni değerleri içeren denetim günlüğü
- Kapsamı sınırlı yetkilere sahip API token'ları

</td>
<td width="33%" valign="top">

**Genişletme ve entegrasyon**
- Yeniden denemeli, HMAC imzalı webhook'lar
- Koşullu gösterime sahip özel alanlar
- Değiştirilebilir bağdaştırıcılarla içe aktarma sihirbazı
- SignalR ile canlı güncellemeler (isteğe bağlı)
- Ortak Vue 3 + Inertia arayüzü

</td>
</tr>
</table>

<p align="center"><sub><a href="../features.md">Tüm özellikler ayrıntılı olarak</a></sub></p>

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET"></a>
  <a href="https://vuejs.org"><img src="https://img.shields.io/badge/Vue.js-4FC08D?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue.js"></a>
  <a href="https://inertiajs.com"><img src="https://img.shields.io/badge/Inertia.js-9553E9?style=for-the-badge&logo=inertia&logoColor=white" alt="Inertia.js"></a>
  <a href="https://www.postgresql.org"><img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL"></a>
  <a href="https://www.sqlite.org"><img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite"></a>
</p>

## Hızlı başlangıç

```bash
dotnet add package Escalated
dotnet ef database update --context EscalatedDbContext
```

`Program.cs` içinde `builder.Services.AddEscalated(...)` ve `app.MapEscalated()` ile kaydedin, ardından `/support` adresini ziyaret edin. .NET 8+ ve SQL Server, SQLite veya PostgreSQL gerekir. [Başlangıç kılavuzu](../getting-started.md) yapılandırmayı ve yetkilendirmeyi anlatır, [Inertia arayüzü](../inertia-ui.md) ise ortak ekranları açar.

## Daha fazla bilgi

- [Tüm özellikler](../features.md) · [Başlarken](../getting-started.md) · [Inertia arayüzü](../inertia-ui.md)
- [Özelleştirme, gerçek zamanlı güncellemeler ve yerelleştirme](../customization.md) · [Mimari, modeller ve olaylar](../architecture.md) · [API uç noktaları](../api-endpoints.md)
- [Erişim denetimi](../access-control.md) · [Talep erişimi ve yönlendirme](../ticket-access-and-routing.md) · [İş akışları](../workflows.md)
- [Gelen e-posta](../inbound-email.md) · [Bildirim e-postaları](../notification-emails.md) · [Misafir erişimi](../guest-access.md)
- [Widget'ı yerleştirme](../embedding-the-widget.md) · [Yönetim panelini yerleştirme](../embedding-admin.md) · [Temsilci paneli](../agent-panel.md)
- [Eklentiler](../plugins.md) · [Bültenler](../newsletters.md) · [Geliştirme ve test](../development.md)

Escalated, [Laravel](https://github.com/escalated-dev/escalated-laravel), [Rails](https://github.com/escalated-dev/escalated-rails), [Django](https://github.com/escalated-dev/escalated-django), [NestJS](https://github.com/escalated-dev/escalated-nestjs), [AdonisJS](https://github.com/escalated-dev/escalated-adonis), [Symfony](https://github.com/escalated-dev/escalated-symfony), [Phoenix](https://github.com/escalated-dev/escalated-phoenix), [Go](https://github.com/escalated-dev/escalated-go), [Spring](https://github.com/escalated-dev/escalated-spring), [WordPress](https://github.com/escalated-dev/escalated-wordpress) ve [Filament](https://github.com/escalated-dev/escalated-filament) için de kullanılabilir. Arayüz [escalated](https://github.com/escalated-dev/escalated) deposunda bulunur.

## Lisans

MIT. Copyright (c) 2026 Escalated.dev. Ayrıntılar için [LICENSE](../../LICENSE) dosyasına bakın.
