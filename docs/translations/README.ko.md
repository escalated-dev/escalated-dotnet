<p align="center"><sub>
  <a href="README.ar.md">العربية</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="../../README.md">English</a> ·
  <a href="README.es.md">Español</a> ·
  <a href="README.fr.md">Français</a> ·
  <a href="README.it.md">Italiano</a> ·
  <a href="README.ja.md">日本語</a> ·
  <b>한국어</b> ·
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
  ASP.NET Core 앱에 완전한 헬프데스크를 추가하세요.<br>
  티켓, SLA, 에스컬레이션 규칙, 상담원 받은편지함, 고객 포털을 EF Core를 통해 원하는 데이터베이스에 저장합니다.
</p>

<p align="center">
  <a href="https://escalated.dev"><b>웹사이트</b></a> ·
  <a href="https://escalated.dev/docs"><b>문서</b></a> ·
  <a href="https://escalated.dev/cloud"><b>Cloud</b></a> ·
  <a href="../../CHANGELOG.md"><b>변경 내역</b></a>
</p>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-dark.png">
  <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/inbox-light.png" alt="Escalated 상담원 받은편지함: 개수가 표시된 저장된 보기, SLA 카운트다운, 모든 티켓의 우선순위" width="100%">
</picture>

## 주요 기능

<table>
  <tr>
    <td width="40%">
      <h3>상담원이 하루 종일 일할 수 있는 받은편지함</h3>
      대화, 내부 메모, 상용구 답변, SLA 타이머를 한 화면에서 볼 수 있습니다. 티켓은 Postmark, Mailgun 또는 AWS SES를 통해 이메일로도 들어옵니다.
    </td>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/ticket-light.png" alt="답장 작성기가 있는 상담원 작업 공간에서 열린 티켓">
      </picture>
    </td>
  </tr>
  <tr>
    <td width="60%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-dark.png">
        <img src="https://raw.githubusercontent.com/escalated-dev/escalated/main/docs/assets/readme/dashboard-light.png" alt="응답 시간, CSAT, SLA 위반을 보여 주는 관리자 보고서 페이지">
      </picture>
    </td>
    <td width="40%">
      <h3>한눈에 보이는 SLA와 에스컬레이션 규칙</h3>
      영업시간을 반영한 우선순위별 응답 및 해결 목표, 목표가 지연되면 에스컬레이션하거나 재할당하는 규칙, 위반 보고서를 제공합니다.
    </td>
  </tr>
</table>

## 기능

<table>
<tr>
<td width="33%" valign="top">

**티켓과 받은편지함**
- 상태 전환을 포함한 전체 수명 주기
- 대기열 필터와 일괄 작업
- 내부 메모와 상용구 답변
- 클릭 한 번으로 일괄 작업하는 매크로
- 티켓의 사이드 대화
- 티켓 병합, 연결, 분할
- 자동으로 다시 열리는 일시 중지
- 개인 및 공유 저장된 보기
- 내 앱의 엔티티와 티켓 연결
- 크기 제한이 있는 첨부 파일
- 모든 티켓의 활동 타임라인

</td>
<td width="33%" valign="top">

**SLA와 라우팅**
- 우선순위별 응답 및 해결 목표
- 영업시간과 위반 감지
- 에스컬레이션, 우선순위 변경, 재할당 규칙
- 시간 기반 자동화
- 스킬 기반 라우팅
- 상담원별 처리 한도

</td>
<td width="33%" valign="top">

**이메일**
- Postmark, Mailgun, AWS SES로 수신
- 서명된 Reply-To 검증
- 요청자의 답장만 수락
- Message-ID 헤더 기반 스레드

</td>
</tr>
<tr>
<td width="33%" valign="top">

**고객**
- 셀프 서비스 고객 포털
- 매직 토큰으로 조회하는 게스트 티켓
- 삽입 가능한 위젯 API
- 해결된 티켓의 CSAT 평가
- 검색과 피드백을 지원하는 지식 베이스

</td>
<td width="33%" valign="top">

**관리와 보안**
- 부서, SLA, 규칙, 태그
- 역할과 권한
- 복구 코드를 포함한 TOTP 2단계 인증
- SAML 2, OAuth 2 / OIDC, JWT 로그인
- 이전 값과 새 값을 기록하는 감사 로그
- 범위가 지정된 권한의 API 토큰

</td>
<td width="33%" valign="top">

**확장과 연동**
- 재시도를 지원하는 HMAC 서명 웹훅
- 조건부 표시를 지원하는 사용자 정의 필드
- 교체 가능한 어댑터를 갖춘 가져오기 마법사
- SignalR 실시간 업데이트 (선택 사항)
- 공용 Vue 3 + Inertia UI

</td>
</tr>
</table>

<p align="center"><sub><a href="../features.md">모든 기능 자세히 보기</a></sub></p>

<p align="center">
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET"></a>
  <a href="https://vuejs.org"><img src="https://img.shields.io/badge/Vue.js-4FC08D?style=for-the-badge&logo=vuedotjs&logoColor=white" alt="Vue.js"></a>
  <a href="https://inertiajs.com"><img src="https://img.shields.io/badge/Inertia.js-9553E9?style=for-the-badge&logo=inertia&logoColor=white" alt="Inertia.js"></a>
  <a href="https://www.postgresql.org"><img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL"></a>
  <a href="https://www.sqlite.org"><img src="https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white" alt="SQLite"></a>
</p>

## 빠른 시작

```bash
dotnet add package Escalated
dotnet ef database update --context EscalatedDbContext
```

`Program.cs`에서 `builder.Services.AddEscalated(...)`와 `app.MapEscalated()`로 등록한 다음 `/support`에 접속하세요. .NET 8 이상과 SQL Server, SQLite 또는 PostgreSQL이 필요합니다. 설정과 권한 부여는 [시작 가이드](../getting-started.md)에서 다루며, [Inertia UI](../inertia-ui.md)에서 공용 화면을 켤 수 있습니다.

## 더 알아보기

- [전체 기능](../features.md) · [시작하기](../getting-started.md) · [Inertia UI](../inertia-ui.md)
- [커스터마이징, 실시간, 현지화](../customization.md) · [아키텍처, 모델, 이벤트](../architecture.md) · [API 엔드포인트](../api-endpoints.md)
- [접근 제어](../access-control.md) · [티켓 접근과 라우팅](../ticket-access-and-routing.md) · [워크플로](../workflows.md)
- [수신 이메일](../inbound-email.md) · [알림 이메일](../notification-emails.md) · [게스트 접근](../guest-access.md)
- [위젯 삽입](../embedding-the-widget.md) · [관리자 화면 삽입](../embedding-admin.md) · [상담원 패널](../agent-panel.md)
- [플러그인](../plugins.md) · [뉴스레터](../newsletters.md) · [개발과 테스트](../development.md)

Escalated는 [Laravel](https://github.com/escalated-dev/escalated-laravel), [Rails](https://github.com/escalated-dev/escalated-rails), [Django](https://github.com/escalated-dev/escalated-django), [NestJS](https://github.com/escalated-dev/escalated-nestjs), [AdonisJS](https://github.com/escalated-dev/escalated-adonis), [Symfony](https://github.com/escalated-dev/escalated-symfony), [Phoenix](https://github.com/escalated-dev/escalated-phoenix), [Go](https://github.com/escalated-dev/escalated-go), [Spring](https://github.com/escalated-dev/escalated-spring), [WordPress](https://github.com/escalated-dev/escalated-wordpress), [Filament](https://github.com/escalated-dev/escalated-filament)용으로도 제공됩니다. UI는 [escalated](https://github.com/escalated-dev/escalated) 저장소에 있습니다.

## 라이선스

MIT. Copyright (c) 2026 Escalated.dev. [LICENSE](../../LICENSE)를 참고하세요.
