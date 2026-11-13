# Development

## Testing

```bash
dotnet test
```

Integration tests use SQLite by default. CI runs the suite against PostgreSQL 17
as well; isolated unit tests also use Moq and EF Core InMemory.

To run the PostgreSQL profile against a disposable local test database:

```bash
ESCALATED_TEST_DATABASE=postgres \
ESCALATED_TEST_CONNECTION="Host=127.0.0.1;Port=5432;Database=escalated_test;Username=postgres;Password=postgres" \
dotnet test
```

The demo host has a separate project, so validate it explicitly:

```bash
dotnet build docker/host-app/EscalatedDemoHost.csproj
```

CI audits direct and transitive packages for both the solution and demo host.
Known advisories and failures to obtain vulnerability data fail that job. Run
the same checks locally with:

```bash
dotnet restore Escalated.sln -p:NuGetAuditMode=all -warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905
dotnet restore docker/host-app/EscalatedDemoHost.csproj -p:NuGetAuditMode=all -warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905
```

Coverage includes:
- Ticket CRUD and status transitions
- SLA breach detection and warnings
- Ticket split, merge, and snooze
- Assignment and workload calculation
- Webhook subscription matching
- 2FA secret generation and verification
- Capacity management
- Model validation and enum behavior
