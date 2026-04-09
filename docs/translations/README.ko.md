<p align="center">
  <a href="README.ar.md">العربية</a> •
  <a href="README.de.md">Deutsch</a> •
  <a href="../../README.md">English</a> •
  <a href="README.es.md">Español</a> •
  <a href="README.fr.md">Français</a> •
  <a href="README.it.md">Italiano</a> •
  <a href="README.ja.md">日本語</a> •
  <b>한국어</b> •
  <a href="README.nl.md">Nederlands</a> •
  <a href="README.pl.md">Polski</a> •
  <a href="README.pt-BR.md">Português (BR)</a> •
  <a href="README.ru.md">Русский</a> •
  <a href="README.tr.md">Türkçe</a> •
  <a href="README.zh-CN.md">简体中文</a>
</p>

# ASP.NET Core용 Escalated

[![Tests](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml/badge.svg)](https://github.com/escalated-dev/escalated-dotnet/actions/workflows/test.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

ASP.NET Core를 위한 완전한 기능의 임베드 가능한 지원 티켓 시스템입니다. 어떤 앱에든 추가하면 SLA 추적, 에스컬레이션 규칙, 에이전트 워크플로우, 고객 포털을 갖춘 완전한 헬프데스크를 이용할 수 있습니다. 외부 서비스가 필요 없습니다.

> **[escalated.dev](https://escalated.dev)** -- 자세히 알아보기, 데모 보기, Cloud와 Self-Hosted 옵션 비교.

## 기능

- **티켓 라이프사이클** -- 구성 가능한 상태 전환으로 생성, 할당, 답변, 해결, 닫기, 재개
- **SLA 엔진** -- 우선순위별 응답 및 해결 목표, 업무 시간 계산, 자동 위반 감지
- **에스컬레이션 규칙** -- 자동으로 에스컬레이트, 우선순위 변경, 재할당 또는 알림하는 조건 기반 규칙
- **자동화** -- 조건과 액션이 포함된 시간 기반 규칙
- **에이전트 대시보드** -- 필터, 대량 작업, 내부 메모, 정형 응답이 포함된 티켓 큐
- **고객 포털** -- 셀프서비스 티켓 생성, 답변, 상태 추적
- **관리자 패널** -- 부서, SLA 정책, 에스컬레이션 규칙, 태그 등 관리
- **매크로 및 정형 응답** -- 일괄 작업 및 재사용 가능한 답변 템플릿
- **커스텀 필드** -- 조건부 표시 로직이 포함된 동적 메타데이터
- **지식 베이스** -- 기사, 카테고리, 검색, 피드백
- **파일 첨부** -- 구성 가능한 스토리지 및 크기 제한 업로드 지원
- **활동 타임라인** -- 모든 티켓의 모든 작업에 대한 전체 감사 로그
- **Webhooks** -- 재시도 로직 포함 HMAC-SHA256 서명
- **API 토큰** -- 능력 기반 스코핑 Bearer 인증
- **역할 및 권한** -- 세밀한 접근 제어
- **감사 로깅** -- 이전/이후 값과 함께 모든 변경 기록
- **가져오기 시스템** -- 플러그 가능한 어댑터가 포함된 다단계 마법사
- **사이드 대화** -- 티켓의 내부 팀 스레드
- **티켓 병합 및 연결** -- 중복 티켓 병합 및 문제 연관
- **티켓 분할** -- 답변을 새 티켓으로 분할
- **티켓 스누즈** -- 백그라운드 깨우기 서비스로 미래 날짜까지 스누즈
- **이메일 스레딩** -- 올바른 스레딩을 위한 In-Reply-To/References/Message-ID 헤더
- **저장된 뷰** -- 개인 및 공유 필터 프리셋
- **임베드 가능한 위젯 API** -- KB 검색, 게스트 티켓, 상태 조회용 퍼블릭 엔드포인트
- **실시간 업데이트** -- 라이브 티켓 업데이트용 SignalR 허브 (옵트인)
- **용량 관리** -- 채널별 에이전트당 워크로드 제한
- **스킬 기반 라우팅** -- 스킬 태그로 에이전트를 티켓에 매칭
- **CSAT 평가** -- 해결된 티켓에 대한 만족도 설문
- **2FA** -- 복구 코드 포함 TOTP 설정 및 검증
- **게스트 접근** -- 매직 토큰 조회를 통한 익명 티켓 생성
- **Inertia.js + Vue 3 UI** -- [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)를 통한 공유 프론트엔드

## 요구 사항

- .NET 8.0+
- Entity Framework Core 8.0+
- SQL Server, SQLite 또는 PostgreSQL
- Node.js 18+ (프론트엔드 에셋용)

## 빠른 시작

### 1. 패키지 설치

```bash
dotnet add package Escalated
```

### 2. 서비스 등록

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

### 3. 설정

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

### 4. 마이그레이션 실행

```bash
dotnet ef migrations add InitialEscalated --context EscalatedDbContext
dotnet ef database update --context EscalatedDbContext
```

`/support`를 방문하세요 -- 가동 중입니다.

## 프론트엔드 통합

Escalated는 npm 패키지 [`@escalated-dev/escalated`](https://github.com/escalated-dev/escalated)를 통해 Vue 컴포넌트 라이브러리와 기본 페이지를 제공합니다. 기존 레이아웃 내에서 원활한 SPA 렌더링을 위해 Inertia.js와 통합하세요.

```bash
npm install @escalated-dev/escalated
```

## 아키텍처

```
src/Escalated/
  Models/           # 40개 이상의 EF Core 엔티티 모델
  Data/             # 전체 관계 매핑이 포함된 EscalatedDbContext
  Services/         # 비즈니스 로직 (티켓, SLA, 병합, 분할, 스누즈 등)
  Controllers/
    Admin/          # 관리자 패널 API (모든 설정의 CRUD)
    Agent/          # 티켓 큐 및 에이전트 작업
    Customer/       # 고객 셀프서비스 포털
    Widget/         # 퍼블릭 위젯 API (KB 검색, 게스트 티켓)
  Middleware/       # API 토큰 인증, 권한, 속도 제한
  Events/           # 도메인 이벤트 (TicketCreated, SlaBreached 등)
  Notifications/    # 이메일 알림 인터페이스 및 템플릿
  Configuration/    # DI 등록, 옵션, 엔드포인트 매핑
  Hubs/             # 실시간 업데이트용 SignalR 허브
  Enums/            # TicketStatus, TicketPriority, ActivityType
```

## 모델

Escalated에는 전체 헬프데스크 도메인을 커버하는 40개 이상의 EF Core 엔티티가 포함되어 있습니다:

| 카테고리 | 모델 |
|----------|--------|
| 핵심 | Ticket, Reply, Attachment, TicketActivity, TicketStatusModel, TicketLink, TicketTag, Tag, Department, SatisfactionRating |
| SLA | SlaPolicy, EscalationRule, BusinessSchedule, Holiday, Automation |
| 에이전트 | AgentProfile, AgentCapacity, Skill, AgentSkill |
| 메시징 | CannedResponse, Macro, SideConversation, SideConversationReply, InboundEmail |
| 관리 | Role, Permission, ApiToken, Webhook, WebhookDelivery, Plugin, AuditLog |
| 커스텀 | CustomField, CustomFieldValue, CustomObject, CustomObjectRecord |
| 가져오기 | ImportJob, ImportSourceMap |
| 설정 | EscalatedSettings, SavedView |
| 지식 베이스 | Article, ArticleCategory |

모든 모델은 `EscalatedDbContext`에서 적절한 관계, 인덱스, 쿼리 필터가 구성되어 있습니다.

## 서비스

| 서비스 | 책임 |
|---------|---------------|
| `TicketService` | 전체 티켓 CRUD, 상태 전환, 답변, 태그, 부서 |
| `SlaService` | 정책 연결, 위반 감지, 경고 확인, 첫 응답 기록 |
| `AssignmentService` | 에이전트 할당, 할당 해제, 워크로드별 자동 할당 |
| `EscalationService` | 조건 기반 규칙 평가, 에스컬레이션 액션 실행 |
| `AutomationRunner` | 시간 기반 자동화 평가 및 액션 실행 |
| `MacroService` | 티켓에 매크로 액션 시퀀스 적용 |
| `TicketMergeService` | 답변 전송과 함께 소스를 대상으로 병합 |
| `TicketSplitService` | 답변을 새 연결된 티켓으로 분할 |
| `TicketSnoozeService` | 백그라운드 깨우기 서비스로 스누즈/해제 |
| `WebhookDispatcher` | 재시도 로직 포함 HMAC 서명 웹훅 전달 |
| `CapacityService` | 에이전트당 동시 티켓 제한 |
| `SkillRoutingService` | 스킬로 에이전트를 티켓 태그에 매칭 |
| `BusinessHoursCalculator` | 공휴일 지원 업무 시간 날짜 계산 |
| `TwoFactorService` | TOTP 시크릿 생성, 검증, 복구 코드 |
| `AuditLogService` | 엔티티 변경 기록 및 쿼리 |
| `KnowledgeBaseService` | 기사/카테고리 CRUD, 검색, 피드백 |
| `SavedViewService` | 개인 및 공유 필터 프리셋 |
| `SideConversationService` | 티켓의 내부 스레드 대화 |
| `ImportService` | 플러그 가능한 어댑터를 사용한 다단계 가져오기 |
| `SettingsService` | 키-값 설정 저장소 |

## 이벤트

모든 티켓 작업은 도메인 이벤트를 발생시킵니다:

| 이벤트 | 시점 |
|-------|------|
| `TicketCreatedEvent` | 새 티켓 생성됨 |
| `TicketStatusChangedEvent` | 상태 전환 |
| `TicketAssignedEvent` | 에이전트 할당됨 |
| `TicketUnassignedEvent` | 에이전트 제거됨 |
| `ReplyCreatedEvent` | 공개 답변 추가됨 |
| `InternalNoteAddedEvent` | 에이전트 메모 추가됨 |
| `SlaBreachedEvent` | SLA 기한 초과 |
| `SlaWarningEvent` | SLA 기한 임박 |
| `TicketEscalatedEvent` | 티켓 에스컬레이트됨 |
| `TicketResolvedEvent` | 티켓 해결됨 |
| `TicketClosedEvent` | 티켓 닫힘 |
| `TicketReopenedEvent` | 티켓 재개됨 |
| `TicketPriorityChangedEvent` | 우선순위 변경됨 |
| `DepartmentChangedEvent` | 부서 변경됨 |
| `TagAddedEvent` | 태그 추가됨 |
| `TagRemovedEvent` | 태그 제거됨 |

호스트 애플리케이션에서 이러한 이벤트를 수신하려면 `IEscalatedEventDispatcher`를 구현하세요:

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

## API 엔드포인트

### 고객

| 메서드 | 경로 | 설명 |
|--------|-------|-------------|
| GET | `/support/tickets` | 고객 티켓 목록 |
| POST | `/support/tickets` | 티켓 생성 |
| GET | `/support/tickets/{id}` | 티켓 보기 |
| POST | `/support/tickets/{id}/reply` | 티켓에 답변 |
| POST | `/support/tickets/{id}/close` | 티켓 닫기 |
| POST | `/support/tickets/{id}/reopen` | 티켓 재개 |

### 에이전트

| 메서드 | 경로 | 설명 |
|--------|-------|-------------|
| GET | `/support/agent/tickets` | 필터 포함 티켓 큐 |
| GET | `/support/agent/tickets/{id}` | 티켓 상세 |
| POST | `/support/agent/tickets/{id}/reply` | 답변 |
| POST | `/support/agent/tickets/{id}/note` | 내부 메모 |
| POST | `/support/agent/tickets/{id}/assign` | 에이전트 할당 |
| POST | `/support/agent/tickets/{id}/status` | 상태 변경 |
| POST | `/support/agent/tickets/{id}/priority` | 우선순위 변경 |
| POST | `/support/agent/tickets/{id}/macro` | 매크로 적용 |
| POST | `/support/agent/tickets/bulk` | 대량 작업 |
| GET | `/support/agent/tickets/dashboard` | 에이전트 워크로드 |

### 관리

| 메서드 | 경로 | 설명 |
|--------|-------|-------------|
| GET/POST | `/support/admin/departments` | 부서 관리 |
| GET/POST | `/support/admin/tags` | 태그 관리 |
| GET/POST | `/support/admin/sla-policies` | SLA 정책 관리 |
| GET/POST | `/support/admin/escalation-rules` | 에스컬레이션 규칙 관리 |
| GET/POST | `/support/admin/webhooks` | 웹훅 관리 |
| GET/POST | `/support/admin/api-tokens` | API 토큰 관리 |
| GET/POST | `/support/admin/macros` | 매크로 관리 |
| GET/POST | `/support/admin/automations` | 자동화 관리 |
| GET/POST | `/support/admin/custom-fields` | 커스텀 필드 관리 |
| GET/POST | `/support/admin/business-hours` | 업무 시간 |
| GET/POST | `/support/admin/skills` | 스킬 관리 |
| GET/POST | `/support/admin/roles` | 역할 관리 |
| GET | `/support/admin/audit-logs` | 감사 로그 조회 |
| GET/POST | `/support/admin/settings` | 앱 설정 |
| POST | `/support/admin/tickets/{id}/merge` | 티켓 병합 |
| POST | `/support/admin/tickets/{id}/split` | 티켓 분할 |
| POST | `/support/admin/tickets/{id}/snooze` | 티켓 스누즈 |
| POST | `/support/admin/tickets/{id}/link` | 티켓 연결 |

### 위젯 (퍼블릭)

| 메서드 | 경로 | 설명 |
|--------|-------|-------------|
| GET | `/support/widget/kb/search` | 지식 베이스 검색 |
| POST | `/support/widget/tickets` | 게스트 티켓 생성 |
| GET | `/support/widget/tickets/{token}` | 게스트 토큰으로 조회 |
| POST | `/support/widget/tickets/{token}/reply` | 게스트 답변 |
| POST | `/support/widget/tickets/{token}/rate` | CSAT 평가 제출 |
| POST | `/support/widget/kb/articles/{id}/feedback` | 기사 피드백 |

## 실시간 업데이트

라이브 티켓 업데이트를 위해 SignalR을 활성화합니다:

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

클라이언트는 티켓별 그룹에 참여하여 업데이트를 수신합니다:

```javascript
connection.invoke("JoinTicket", ticketId);
connection.on("TicketUpdated", (data) => { /* handle */ });
```

## 미들웨어

### API 토큰 인증

Bearer 토큰 인증으로 API 엔드포인트를 보호합니다:

```csharp
app.UseMiddleware<ApiTokenAuthMiddleware>();
```

토큰은 SHA-256 해시로 저장됩니다. 관리 API 엔드포인트를 통해 토큰을 생성하세요.

### 속도 제한

```csharp
app.UseMiddleware<EscalatedRateLimitMiddleware>(60, 60); // 60 requests per 60 seconds
```

## 테스트

```bash
dotnet test
```

테스트는 xUnit과 Moq 및 EF Core InMemory 프로바이더를 사용합니다. 커버리지에는 다음이 포함됩니다:
- 티켓 CRUD 및 상태 전환
- SLA 위반 감지 및 경고
- 티켓 분할, 병합, 스누즈
- 할당 및 워크로드 계산
- 웹훅 구독 매칭
- 2FA 시크릿 생성 및 검증
- 용량 관리
- 모델 유효성 검사 및 enum 동작

## 다른 프레임워크에서도 이용 가능

- **[Escalated for Laravel](https://github.com/escalated-dev/escalated-laravel)** -- Laravel Composer 패키지
- **[Escalated for Rails](https://github.com/escalated-dev/escalated-rails)** -- Ruby on Rails 엔진
- **[Escalated for Django](https://github.com/escalated-dev/escalated-django)** -- Django 재사용 가능 앱
- **[Escalated for AdonisJS](https://github.com/escalated-dev/escalated-adonis)** -- AdonisJS v6 패키지
- **[Escalated for ASP.NET Core](https://github.com/escalated-dev/escalated-dotnet)** -- ASP.NET Core 패키지 (현재 페이지)
- **[Shared Frontend](https://github.com/escalated-dev/escalated)** -- Vue 3 + Inertia.js UI 컴포넌트

동일한 아키텍처, 동일한 Vue UI -- 모든 주요 백엔드 프레임워크를 위해.

## 라이선스

MIT
