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
  <a href="README.pt-BR.md">Português (BR)</a> •
  <a href="README.ru.md">Русский</a> •
  <b>Türkçe</b> •
  <a href="README.zh-CN.md">简体中文</a>
</p>

# ASP.NET Core için Escalated

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

ASP.NET Core için tam özellikli, gömülebilir bir destek bilet sistemi. Herhangi bir uygulamaya ekleyin -- SLA takibi, yükseltme kuralları, temsilci iş akışları ve müşteri portalı ile eksiksiz bir yardım masası elde edin. Harici hizmet gerekmez.

> **[escalated.dev](https://escalated.dev)** -- Daha fazla bilgi edinin, demoları görüntüleyin ve Cloud ile Self-Hosted seçeneklerini karşılaştırın.

## Özellikler

- **Bilet yaşam döngüsü** -- Yapılandırılabilir durum geçişleri ile oluşturma, atama, yanıtlama, çözme, kapatma, yeniden açma
- **SLA motoru** -- Önceliğe göre yanıt ve çözüm hedefleri, iş saatleri hesaplaması, otomatik ihlal tespiti
- **Yükseltme kuralları** -- Otomatik olarak yükselten, yeniden önceliklendiren, yeniden atayan veya bildirim gönderen koşul tabanlı kurallar
- **Otomasyonlar** -- Koşullar ve eylemler içeren zaman tabanlı kurallar
- **Temsilci paneli** -- Filtreler, toplu işlemler, dahili notlar, hazır yanıtlar içeren bilet kuyruğu
- **Müşteri portalı** -- Self-servis bilet oluşturma, yanıtlar ve durum takibi
- **Yönetim paneli** -- Departmanları, SLA politikalarını, yükseltme kurallarını, etiketleri ve daha fazlasını yönetin
- **Makrolar ve hazır yanıtlar** -- Toplu eylemler ve yeniden kullanılabilir yanıt şablonları
- **Özel alanlar** -- Koşullu görüntüleme mantığına sahip dinamik meta veriler
- **Bilgi tabanı** -- Makaleler, kategoriler, arama ve geri bildirim
- **Dosya ekleri** -- Yapılandırılabilir depolama ve boyut limitleri ile yükleme desteği
- **Etkinlik zaman çizelgesi** -- Her biletteki her eylemin tam denetim günlüğü
- **Webhooks** -- Yeniden deneme mantığı ile HMAC-SHA256 imzalı
- **API token'ları** -- Yetenek tabanlı kapsam ile Bearer kimlik doğrulaması
- **Roller ve izinler** -- İnce taneli erişim kontrolü
- **Denetim günlüğü** -- Eski/yeni değerlerle tüm değişiklikler kaydedilir
- **İçe aktarma sistemi** -- Takılabilir adaptörlerle çok adımlı sihirbaz
- **Yan konuşmalar** -- Biletlerdeki dahili ekip başlıkları
- **Bilet birleştirme ve bağlama** -- Tekrarlanan biletleri birleştirme ve sorunları ilişkilendirme
- **Bilet bölme** -- Bir yanıtı yeni bir bilete ayırma
- **Bilet erteleme** -- Arka plan uyandırma servisi ile gelecekteki bir tarihe kadar erteleme
- **E-posta zincirleme** -- Doğru zincirleme için In-Reply-To/References/Message-ID başlıkları
- **Kaydedilmiş görünümler** -- Kişisel ve paylaşılan filtre ön ayarları
- **Gömülebilir widget API'si** -- KB arama, misafir biletleri, durum sorgulama için genel uç noktalar
- **Gerçek zamanlı güncellemeler** -- Canlı bilet güncellemeleri için SignalR hub'ları (opsiyonel)
- **Kapasite yönetimi** -- Kanal başına temsilci iş yükü limitleri
- **Beceri tabanlı yönlendirme** -- Beceri etiketlerine göre temsilcileri biletlerle eşleştirme
- **CSAT değerlendirmeleri** -- Çözülen biletlerde memnuniyet anketleri
- **2FA** -- Kurtarma kodları ile TOTP kurulumu ve doğrulaması
- **Misafir erişimi** -- Sihirli token araması ile anonim bilet oluşturma
- **Inertia.js + Vue 3 UI** -- [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated) üzerinden paylaşılan frontend

## Gereksinimler

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server, SQLite veya PostgreSQL
- Node.js 18+ (frontend kaynakları için)

## Hızlı Başlangıç

### 1. Paketi Yükleyin

```bash
dotnet add package Escalated
```

### 2. Servisleri Kaydedin

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

### 3. Yapılandırın

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

### 4. Migrasyonları Çalıştırın

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

`/support` adresini ziyaret edin -- hazır.

## Frontend Entegrasyonu

Escalated, npm paketi [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated) aracılığıyla bir Vue bileşen kütüphanesi ve varsayılan sayfalar sunar. Mevcut düzeninizde sorunsuz SPA oluşturma için Inertia.js ile entegre edin.

```bash
npm install @escalated-dev/escalated
```

## Mimari

```
src/Escalated/
  Models/           # 40'tan fazla EF Core varlık modeli
  Data/             # Tam ilişki eşlemesi ile EscalatedDbContext
  Services/         # İş mantığı (bilet, SLA, birleştirme, bölme, erteleme, vb.)
  Controllers/
    Admin/          # Yönetim paneli API'si (tüm ayarlar için CRUD)
    Agent/          # Bilet kuyruğu ve temsilci eylemleri
    Customer/       # Müşteri self-servis portalı
    Widget/         # Genel widget API'si (KB arama, misafir biletleri)
  Middleware/       # API token kimlik doğrulaması, izinler, hız sınırlaması
  Events/           # Alan olayları (TicketCreated, SlaBreached, vb.)
  Notifications/    # E-posta bildirim arayüzleri ve şablonları
  Configuration/    # DI kaydı, seçenekler, uç nokta eşlemesi
  Hubs/             # Gerçek zamanlı güncellemeler için SignalR hub'ı
  Enums/            # TicketStatus, TicketPriority, ActivityType
```

## Modeller

Escalated, tam yardım masası alanını kapsayan 40'tan fazla EF Core varlığı içerir:

| Kategori | Modeller |
|----------|--------|
| Çekirdek | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| Temsilciler | AgentProfile, AgentCapacity, Skill, AgentSkill |
| Mesajlaşma | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| Yönetim | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| Özel | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| İçe Aktarma | ImportJob, ImportSourceMap |
| Yapılandırma | EscalatedSettings, SavedView |
| Bilgi Tabanı | Article, ArticleCategory |

Tüm modeller `EscalatedDbContext`'te doğru şekilde yapılandırılmış ilişkilere, dizinlere ve sorgu filtrelerine sahiptir.

## Servisler

| Servis | Sorumluluk |
|---------|---------------|
| `TicketService` | Tam bilet CRUD, durum geçişleri, yanıtlar, etiketler, departmanlar |
| `SlaService` | Politika ekleme, ihlal tespiti, uyarı kontrolü, ilk yanıt kaydı |
| `AssignmentService` | Temsilci atama, atama kaldırma, iş yüküne göre otomatik atama |
| `EscalationService` | Koşul tabanlı kural değerlendirmesi, yükseltme eylemlerinin yürütülmesi |
| `AutomationRunner` | Zaman tabanlı otomasyon değerlendirmesi ve eylem yürütme |
| `MacroService` | Biletlere makro eylem dizileri uygulama |
| `TicketMergeService` | Kaynak yanıt aktarımı ile hedefe birleştirme |
| `TicketSplitService` | Yanıtı yeni bağlantılı bir bilete ayırma |
| `TicketSnoozeService` | Arka plan uyandırma servisi ile erteleme/uyandırma |
| `WebhookDispatcher` | Yeniden deneme mantığı ile HMAC imzalı webhook teslimi |
| `CapacityService` | Temsilci başına eşzamanlı bilet limitleri |
| `SkillRoutingService` | Becerilere göre temsilcileri bilet etiketleriyle eşleştirme |
| `BusinessHoursCalculator` | Tatil desteğiyle iş saatleri tarih hesaplaması |
| `TwoFactorService` | TOTP gizli anahtar oluşturma, doğrulama, kurtarma kodları |
| `AuditLogService` | Varlık mutasyonlarını günlüğe kaydetme ve sorgulama |
| `KnowledgeBaseService` | Makale/kategori CRUD, arama, geri bildirim |
| `SavedViewService` | Kişisel ve paylaşılan filtre ön ayarları |
| `SideConversationService` | Biletlerdeki dahili zincirlenmiş konuşmalar |
| `ImportService` | Takılabilir adaptörlerle çok adımlı içe aktarma |
| `SettingsService` | Anahtar-değer ayar deposu |

## Olaylar

Her bilet eylemi bir alan olayı yayar:

| Olay | Ne Zaman |
|-------|------|
| `TicketCreatedEvent` | Yeni bilet oluşturuldu |
| `TicketStatusChangedEvent` | Durum geçişi |
| `TicketAssignedEvent` | Temsilci atandı |
| `TicketUnassignedEvent` | Temsilci kaldırıldı |
| `ReplyCreatedEvent` | Genel yanıt eklendi |
| `InternalNoteAddedEvent` | Temsilci notu eklendi |
| `SlaBreachedEvent` | SLA süresi aşıldı |
| `SlaWarningEvent` | SLA süresi yaklaşıyor |
| `TicketEscalatedEvent` | Bilet yükseltildi |
| `TicketResolvedEvent` | Bilet çözüldü |
| `TicketClosedEvent` | Bilet kapatıldı |
| `TicketReopenedEvent` | Bilet yeniden açıldı |
| `TicketPriorityChangedEvent` | Öncelik değiştirildi |
| `DepartmentChangedEvent` | Departman değiştirildi |
| `TagAddedEvent` | Etiket eklendi |
| `TagRemovedEvent` | Etiket kaldırıldı |

Bu olayları ana uygulamanızda almak için `IEscalatedEventDispatcher` uygulayın:

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

## API Uç Noktaları

### Müşteri

| Yöntem | Yol | Açıklama |
|--------|-------|-------------|
| GET | `/support/tickets` | Müşteri biletlerini listele |
| POST | `/support/tickets` | Bilet oluştur |
| GET | `/support/tickets/{id}` | Bileti görüntüle |
| POST | `/support/tickets/{id}/reply` | Bilete yanıt ver |
| POST | `/support/tickets/{id}/close` | Bileti kapat |
| POST | `/support/tickets/{id}/reopen` | Bileti yeniden aç |

### Temsilci

| Yöntem | Yol | Açıklama |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | Filtreli bilet kuyruğu |
| GET | `/support/agent/tickets/{id}` | Bilet detayı |
| POST | `/support/agent/tickets/{id}/reply` | Yanıtla |
| POST | `/support/agent/tickets/{id}/note` | Dahili not |
| POST | `/support/agent/tickets/{id}/assign` | Temsilci ata |
| POST | `/support/agent/tickets/{id}/status` | Durumu değiştir |
| POST | `/support/agent/tickets/{id}/priority` | Önceliği değiştir |
| POST | `/support/agent/tickets/{id}/macro` | Makro uygula |
| POST | `/support/agent/tickets/bulk` | Toplu işlemler |
| GET | `/support/agent/tickets/dashboard` | Temsilci iş yükü |

### Yönetim

| Yöntem | Yol | Açıklama |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | Departmanları yönet |
| GET/POST | `/support/admin/tags` | Etiketleri yönet |
| GET/POST | `/support/admin/sla-policies` | SLA politikalarını yönet |
| GET/POST | `/support/admin/escalation-rules` | Yükseltme kurallarını yönet |
| GET/POST | `/support/admin/webhooks` | Webhook'ları yönet |
| GET/POST | `/support/admin/api-tokens` | API token'larını yönet |
| GET/POST | `/support/admin/macros` | Makroları yönet |
| GET/POST | `/support/admin/automations` | Otomasyonları yönet |
| GET/POST | `/support/admin/custom-fields` | Özel alanları yönet |
| GET/POST | `/support/admin/business-hours` | İş saatleri |
| GET/POST | `/support/admin/skills` | Becerileri yönet |
| GET/POST | `/support/admin/roles` | Rolleri yönet |
| GET | `/support/admin/audit-logs` | Denetim günlüklerini sorgula |
| GET/POST | `/support/admin/settings` | Uygulama ayarları |
| POST | `/support/admin/tickets/{id}/merge` | Biletleri birleştir |
| POST | `/support/admin/tickets/{id}/split` | Bileti böl |
| POST | `/support/admin/tickets/{id}/snooze` | Bileti ertele |
| POST | `/support/admin/tickets/{id}/link` | Biletleri bağla |

### Widget (Genel)

| Yöntem | Yol | Açıklama |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | Bilgi tabanında ara |
| POST | `/support/widget/tickets` | Misafir bileti oluştur |
| GET | `/support/widget/tickets/{token}` | Misafir tokeni ile ara |
| POST | `/support/widget/tickets/{token}/reply` | Misafir yanıtı |
| POST | `/support/widget/tickets/{token}/rate` | CSAT değerlendirmesi gönder |
| POST | `/support/widget/kb/articles/{id}/feedback` | Makale geri bildirimi |

## Gerçek Zamanlı Güncellemeler

Canlı bilet güncellemeleri için SignalR'ı etkinleştirin:

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

İstemciler güncellemeleri almak için bilete özel gruplara katılır:

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## Middleware

### API Token Kimlik Doğrulaması

API uç noktalarını Bearer token kimlik doğrulaması ile koruyun:

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

Token'lar SHA-256 hash olarak saklanır. Yönetim API uç noktası üzerinden token oluşturun.

### Hız Sınırlaması

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## Testler

```bash
dotnet test
```

Testler xUnit, Moq ve EF Core InMemory sağlayıcısı kullanır. Kapsam şunları içerir:
- Bilet CRUD ve durum geçişleri
- SLA ihlal tespiti ve uyarılar
- Bilet bölme, birleştirme ve erteleme
- Atama ve iş yükü hesaplaması
- Webhook abonelik eşleştirme
- 2FA gizli anahtar oluşturma ve doğrulama
- Kapasite yönetimi
- Model doğrulama ve enum davranışı

## Diğer Platformlarda da Mevcut

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- Laravel Composer paketi
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- Ruby on Rails motoru
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- Yeniden kullanılabilir Django uygulaması
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- AdonisJS v6 paketi
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- ASP.NET Core paketi (buradasınız)
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- Vue 3 + Inertia.js UI bileşenleri

Aynı mimari, aynı Vue UI -- tüm büyük backend framework'leri için.

## Lisans

MIT
