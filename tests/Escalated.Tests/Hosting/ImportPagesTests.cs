using System.Net;
using System.Text;
using System.Text.Json;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The admin Import wizard the shared frontend drives
/// (<c>src/pages/Admin/Import/{Index,Connect,Mapping,Review,Progress}.vue</c>), on
/// escalated-laravel's paths and props (<c>Admin\ImportController</c>,
/// <c>ImportService</c>, <c>ImportJob</c>, <c>ImportContext</c>). The source is
/// <see cref="FakeImportAdapter"/>, registered in DI the way a host registers a
/// Zendesk or Freshdesk adapter.
/// </summary>
public class ImportPagesTests
{
    private const string Admin = "admin-1";
    private const string Base = "/support/admin/import";
    private const string Token = "secret-token";

    private static readonly Dictionary<string, string> Credentials = new() { ["subdomain"] = "acme", ["api_token"] = Token };
    private static readonly object FieldMappings = new { tickets = new { subject = "title", state = "status" } };

    public sealed class FakeImportAdapter : IImportAdapter
    {
        private static readonly Dictionary<string, List<Dictionary<string, object?>>> Records = new()
        {
            ["contacts"] = new()
            {
                new() { ["source_id"] = "c1", ["email"] = "ada@customer.test", ["name"] = "Ada Customer" },
                new() { ["source_id"] = "c2", ["email"] = "bob@customer.test", ["name"] = "Bob Customer" },
            },
            ["tags"] = new() { new() { ["source_id"] = "t1", ["name"] = "Billing" } },
            ["tickets"] = new()
            {
                new()
                {
                    ["source_id"] = "k1", ["title"] = "Refund please", ["description"] = "Charged twice.", ["status"] = "open",
                    ["priority"] = "high", ["requester_source_id"] = "c1", ["tag_source_ids"] = new List<object?> { "t1" },
                    ["created_at"] = "2024-01-02T03:04:05Z",
                },
                new() { ["source_id"] = "k2", ["title"] = "Login broken", ["status"] = "resolved", ["priority"] = "urgent", ["requester_source_id"] = "c2" },
                new() { ["source_id"] = "k3", ["title"] = "Feature idea", ["status"] = "not-a-status", ["requester_source_id"] = "c1" },
            },
            ["replies"] = new()
            {
                new() { ["source_id"] = "r1", ["ticket_source_id"] = "k1", ["body"] = "Looking into it", ["author_source_id"] = "c1" },
                new() { ["source_id"] = "r2", ["ticket_source_id"] = "k2", ["body"] = "Internal note", ["is_internal_note"] = true },
            },
        };

        public List<(string Type, string? Cursor, bool Importing)> Calls { get; } = new();
        public Func<string, string?, Task>? OnExtract { get; set; }
        public string? SourceFieldCredentials { get; private set; }

        public string Name => "fakedesk";
        public string DisplayName => "FakeDesk";
        public string[] EntityTypes => new[] { "contacts", "tags", "tickets", "replies" };

        public IReadOnlyList<IReadOnlyDictionary<string, object?>> CredentialFields => new[]
        {
            new Dictionary<string, object?> { ["name"] = "subdomain", ["label"] = "Subdomain", ["type"] = "text", ["help"] = "acme" },
            new Dictionary<string, object?> { ["name"] = "api_token", ["label"] = "API token", ["type"] = "password", ["help"] = "" },
        };

        public Task<bool> TestConnectionAsync(string credentials, CancellationToken ct)
        {
            var c = JsonSerializer.Deserialize<Dictionary<string, string>>(credentials)!;
            if (c.GetValueOrDefault("subdomain") == "down") throw new InvalidOperationException("FakeDesk is unreachable");
            return Task.FromResult(c.GetValueOrDefault("api_token") == Token);
        }

        public IReadOnlyDictionary<string, string> DefaultFieldMappings(string entityType) =>
            entityType == "tickets" ? new Dictionary<string, string> { ["subject"] = "title", ["state"] = "status" } : new Dictionary<string, string>();

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AvailableSourceFieldsAsync(string entityType, string credentials, CancellationToken ct)
        {
            SourceFieldCredentials = credentials;
            IReadOnlyList<IReadOnlyDictionary<string, object?>> fields = entityType == "tickets"
                ? new[]
                {
                    new Dictionary<string, object?> { ["name"] = "subject", ["label"] = "Subject", ["escalated_options"] = new[] { "title", "description" } },
                    new Dictionary<string, object?> { ["name"] = "state", ["label"] = "State", ["escalated_options"] = new[] { "status" } },
                }
                : Array.Empty<IReadOnlyDictionary<string, object?>>();
            return Task.FromResult(fields);
        }

        public async Task<ImportBatch> ExtractAsync(string entityType, string credentials, string? cursor, CancellationToken ct)
        {
            if (!credentials.Contains(Token)) throw new InvalidOperationException("bad credentials");
            lock (Calls) Calls.Add((entityType, cursor, ImportContext.IsImporting));
            if (OnExtract is not null) await OnExtract(entityType, cursor);

            var records = Records[entityType];
            var offset = cursor is null ? 0 : int.Parse(cursor);
            var next = offset + 2;
            return new ImportBatch
            {
                Records = records.Skip(offset).Take(2).ToList(),
                Cursor = next < records.Count ? next.ToString() : null,
                TotalCount = records.Count,
            };
        }
    }

    private sealed class Directory : IUserDirectory
    {
        private static readonly UserDirectoryEntry[] Users =
        {
            new(Admin, "Ada Admin", "admin@example.com"),
            new("501", "Ada Customer", "ada@customer.test"),
        };

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default)
        {
            var hits = Users.Where(u => search is null || (u.Email ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
            return Task.FromResult(new UserDirectoryPage(hits, hits.Length, page, pageSize));
        }

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Users.FirstOrDefault(u => u.Id == id));
    }

    /// <summary>A browser: an admin's client that keeps the cookies it is given.</summary>
    private sealed class Browser
    {
        private readonly HttpClient _client;
        private readonly Dictionary<string, string> _cookies = new();

        public Browser(HttpClient client) => _client = client;

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null)
        {
            var request = Visit(method, url, body);
            if (_cookies.Count > 0) request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
            var response = await _client.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var cookie in values)
                {
                    var pair = cookie.Split(';')[0];
                    var name = pair[..pair.IndexOf('=')];
                    var value = pair[(pair.IndexOf('=') + 1)..];
                    if (value.Length == 0 || cookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)) _cookies.Remove(name);
                    else _cookies[name] = value;
                }
            }
            return response;
        }

        public async Task<JsonElement> PageAsync(string url, string component)
        {
            var response = await SendAsync(HttpMethod.Get, url);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {url}: {(int)response.StatusCode} {text}");
            var page = JsonDocument.Parse(text).RootElement.Clone();
            Assert.Equal(component, page.GetProperty("component").GetString());
            return page.GetProperty("props");
        }

        public async Task<(HttpStatusCode Status, JsonElement Body)> ConnectAsync(Dictionary<string, string> credentials)
        {
            var response = await SendAsync(HttpMethod.Post, $"{Base}/fakedesk/test-connection", new { credentials });
            return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
        }
    }

    private static async Task<(EscalatedTestHost Host, Browser Browser, FakeImportAdapter Adapter)> StartAsync()
    {
        var adapter = new FakeImportAdapter();
        var host = await EscalatedTestHost.StartAsync(
            options => options.Ui.Enabled = true,
            services =>
            {
                services.AddSingleton<IUserDirectory, Directory>();
                services.AddSingleton<IImportAdapter>(adapter);
            });
        await host.GrantRoleAsync(Admin, "escalated-admin");
        return (host, new Browser(host.ClientFor(Admin)), adapter);
    }

    private static Task WaitForImportsAsync(EscalatedTestHost host) =>
        host.Services.GetRequiredService<BackgroundImportDispatcher>().WaitAllAsync();

    private static async Task<ImportJob> StartImportAsync(EscalatedTestHost host, Browser browser)
    {
        var (status, body) = await browser.ConnectAsync(Credentials);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("success").GetBoolean());

        var response = await browser.SendAsync(HttpMethod.Post, $"{Base}/fakedesk/start", new { field_mappings = FieldMappings });
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith($"{Base}/progress/", location);
        await WaitForImportsAsync(host);

        var id = Guid.Parse(location[($"{Base}/progress/".Length)..]);
        return await host.QueryAsync(db => db.ImportJobs.AsNoTracking().SingleAsync(j => j.Id == id));
    }

    private static Task<List<Ticket>> ImportedTicketsAsync(EscalatedTestHost host, Guid jobId) => host.QueryAsync(async db =>
    {
        var ids = (await db.ImportSourceMaps.Where(m => m.ImportJobId == jobId && m.EntityType == "tickets").OrderBy(m => m.Id)
            .Select(m => m.EscalatedId).ToListAsync()).Select(int.Parse).ToList();
        var tickets = await db.Tickets.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync();
        return ids.Select(id => tickets.Single(t => t.Id == id)).ToList();
    });

    private static void AssertProps(JsonElement props, params string[] expected) =>
        Assert.Equal(expected.OrderBy(x => x), props.EnumerateObject().Select(p => p.Name)
            .Where(n => n is not ("errors" or "auth" or "current_user" or "flash" or "escalated" or "plugin_ui" or "page")).OrderBy(x => x));

    [Fact]
    public async Task Index_and_Connect_render_with_Laravels_props()
    {
        var (host, browser, _) = await StartAsync();
        await using var _host = host;
        await host.SeedAsync(async db =>
        {
            db.ImportJobs.Add(new ImportJob { Platform = "fakedesk", Status = "completed", Credentials = "sealed" });
            await db.SaveChangesAsync();
        });

        var index = await browser.PageAsync(Base, "Escalated/Admin/Import/Index");
        AssertProps(index, "adapters", "jobs");
        var adapter = index.GetProperty("adapters")[0];
        Assert.Equal("fakedesk", adapter.GetProperty("name").GetString());
        Assert.Equal("FakeDesk", adapter.GetProperty("display_name").GetString());
        Assert.Equal(2, adapter.GetProperty("credential_fields").GetArrayLength());
        var job = Keys(index.GetProperty("jobs")[0], "id", "platform", "status", "created_at");
        Assert.Equal("completed", job.GetProperty("status").GetString());
        Assert.False(job.TryGetProperty("credentials", out _));

        var connect = await browser.PageAsync($"{Base}/fakedesk/connect", "Escalated/Admin/Import/Connect");
        AssertProps(connect, "platform", "display_name", "credential_fields");
        Assert.Equal("api_token", connect.GetProperty("credential_fields")[1].GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await browser.SendAsync(HttpMethod.Get, $"{Base}/nowhere/connect")).StatusCode);
    }

    [Fact]
    public async Task Test_connection_answers_success_or_the_sources_error()
    {
        var (host, browser, _) = await StartAsync();
        await using var _host = host;

        var (status, body) = await browser.ConnectAsync(new() { ["subdomain"] = "acme", ["api_token"] = "wrong" });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("success").GetBoolean());

        (status, body) = await browser.ConnectAsync(new() { ["subdomain"] = "down" });
        Assert.Equal((HttpStatusCode)422, status);
        Assert.Equal("FakeDesk is unreachable", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Mapping_needs_a_tested_connection_and_reads_source_fields_with_it()
    {
        var (host, browser, adapter) = await StartAsync();
        await using var _host = host;

        var bounced = await browser.SendAsync(HttpMethod.Get, $"{Base}/fakedesk/mapping");
        Assert.Equal(HttpStatusCode.Found, bounced.StatusCode);
        Assert.Equal($"{Base}/fakedesk/connect", bounced.Headers.Location!.ToString());

        await browser.ConnectAsync(Credentials);
        var response = await browser.SendAsync(HttpMethod.Get, $"{Base}/fakedesk/mapping");
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Token, text);
        var props = JsonDocument.Parse(text).RootElement.GetProperty("props");
        // Laravel does not send the declared `credentials` either: they stay on the server.
        AssertProps(props, "platform", "display_name", "entity_types", "mappings");
        Assert.Equal(new[] { "contacts", "tags", "tickets", "replies" }, props.GetProperty("entity_types").EnumerateArray().Select(e => e.GetString()));
        var tickets = props.GetProperty("mappings").GetProperty("tickets");
        Assert.Equal("title", tickets.GetProperty("defaults").GetProperty("subject").GetString());
        Assert.Equal("state", tickets.GetProperty("source_fields")[1].GetProperty("name").GetString());
        Assert.Contains(Token, adapter.SourceFieldCredentials);
    }

    [Fact]
    public async Task Review_echoes_the_choices_and_writes_nothing()
    {
        var (host, browser, _) = await StartAsync();
        await using var _host = host;

        var response = await browser.SendAsync(HttpMethod.Post, $"{Base}/fakedesk/review",
            new { field_mappings = FieldMappings, entity_types = new[] { "tickets", "replies" } });
        var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Escalated/Admin/Import/Review", page.GetProperty("component").GetString());
        var props = page.GetProperty("props");
        AssertProps(props, "platform", "field_mappings", "entity_types");
        Assert.Equal("title", props.GetProperty("field_mappings").GetProperty("tickets").GetProperty("subject").GetString());
        Assert.Equal(2, props.GetProperty("entity_types").GetArrayLength());
        Assert.Equal(0, await host.QueryAsync(db => db.ImportJobs.CountAsync()));
    }

    [Fact]
    public async Task Start_refuses_without_a_tested_connection()
    {
        var (host, browser, _) = await StartAsync();
        await using var _host = host;

        var response = await browser.SendAsync(HttpMethod.Post, $"{Base}/fakedesk/start", new { field_mappings = FieldMappings });

        Assert.Equal($"{Base}/fakedesk/connect", response.Headers.Location!.ToString());
        Assert.Equal(0, await host.QueryAsync(db => db.ImportJobs.CountAsync()));
    }

    [Fact]
    public async Task The_run_imports_the_tickets_end_to_end_in_import_mode()
    {
        var (host, browser, adapter) = await StartAsync();
        await using var _host = host;
        await host.SeedAsync(async db =>
        {
            db.SlaPolicies.Add(new SlaPolicy { Name = "Default", IsDefault = true, FirstResponseHours = """{"high":1}""", ResolutionHours = """{"high":8}""" });
            await db.SaveChangesAsync();
        });

        var job = await StartImportAsync(host, browser);

        Assert.Equal("completed", job.Status);
        Assert.NotNull(job.CompletedAt);
        Assert.Null(job.Credentials);
        Assert.Equal(Admin, job.UserId);
        var progress = JsonDocument.Parse(job.Progress!).RootElement.GetProperty("tickets");
        Assert.Equal(3, progress.GetProperty("total").GetInt32());
        Assert.Equal(3, progress.GetProperty("processed").GetInt32());
        Assert.Equal("2", progress.GetProperty("cursor").GetString());
        Assert.Equal("[]", job.Errors);

        var tickets = await ImportedTicketsAsync(host, job.Id);
        Assert.Equal(new[] { "Refund please", "Login broken", "Feature idea" }, tickets.Select(t => t.Subject));
        Assert.Equal(new[] { TicketStatus.Open, TicketStatus.Resolved, TicketStatus.Open }, tickets.Select(t => t.Status));
        Assert.Equal(new[] { TicketPriority.High, TicketPriority.Urgent, TicketPriority.Medium }, tickets.Select(t => t.Priority));
        // Ada is a host user; Bob becomes an Escalated contact on a guest ticket.
        Assert.Equal("501", tickets[0].RequesterId);
        Assert.Equal("bob@customer.test", tickets[1].GuestEmail);
        Assert.NotNull(tickets[1].ContactId);
        Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), DateTime.SpecifyKind(tickets[0].CreatedAt, DateTimeKind.Utc));
        Assert.All(tickets, t => Assert.Null(t.SlaPolicyId));
        Assert.Equal(1, await host.QueryAsync(db => db.TicketTags.CountAsync(tt => tt.TicketId == tickets[0].Id)));
        var ids = tickets.Select(t => t.Id).ToList();
        Assert.Equal(2, await host.QueryAsync(db => db.Replies.CountAsync(r => ids.Contains(r.TicketId))));
        Assert.Equal(1, await host.QueryAsync(db => db.Replies.CountAsync(r => ids.Contains(r.TicketId) && r.IsInternalNote)));

        // Import mode held only inside the run.
        Assert.NotEmpty(adapter.Calls);
        Assert.All(adapter.Calls, c => Assert.True(c.Importing));
        Assert.False(ImportContext.IsImporting);

        // The session copy of the credentials is gone with the start.
        Assert.Equal(HttpStatusCode.Found, (await browser.SendAsync(HttpMethod.Get, $"{Base}/fakedesk/mapping")).StatusCode);

        // Progress and the status poll.
        var props = await browser.PageAsync($"{Base}/progress/{job.Id}", "Escalated/Admin/Import/Progress");
        AssertProps(props, "job");
        var sent = Keys(props.GetProperty("job"), "id", "platform", "status", "progress", "error_log", "field_mappings", "started_at", "completed_at", "created_at");
        Assert.False(sent.TryGetProperty("credentials", out _));
        Assert.Equal(3, sent.GetProperty("progress").GetProperty("tickets").GetProperty("processed").GetInt32());

        var poll = await browser.SendAsync(HttpMethod.Get, $"{Base}/status/{job.Id}");
        var body = JsonDocument.Parse(await poll.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(new[] { "completed_at", "error_log", "progress", "started_at", "status" }, body.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal("completed", body.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await browser.SendAsync(HttpMethod.Get, $"{Base}/status/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Credentials_are_sealed_at_rest()
    {
        var (host, browser, adapter) = await StartAsync();
        await using var _host = host;
        adapter.OnExtract = (_, _) => throw new InvalidOperationException("source down");

        var job = await StartImportAsync(host, browser);

        Assert.Equal("failed", job.Status);
        Assert.NotNull(job.Credentials);
        Assert.DoesNotContain(Token, job.Credentials);
    }

    [Fact]
    public async Task Pause_mid_run_then_resume_imports_nothing_twice()
    {
        var (host, browser, adapter) = await StartAsync();
        await using var _host = host;
        adapter.OnExtract = async (type, cursor) =>
        {
            if (type == "tickets" && cursor == "2")
            {
                await host.SeedAsync(db => db.ImportJobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, "paused")));
            }
        };

        var job = await StartImportAsync(host, browser);
        Assert.Equal("paused", job.Status);
        Assert.Equal(3, (await ImportedTicketsAsync(host, job.Id)).Count);
        Assert.False(JsonDocument.Parse(job.Progress!).RootElement.TryGetProperty("replies", out _));
        Assert.NotNull(job.Credentials);

        adapter.OnExtract = null;
        adapter.Calls.Clear();
        var response = await browser.SendAsync(HttpMethod.Post, $"{Base}/resume/{job.Id}", new { });
        await WaitForImportsAsync(host);

        Assert.Equal($"{Base}/progress/{job.Id}", response.Headers.Location!.ToString());
        var resumed = await host.QueryAsync(db => db.ImportJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id));
        Assert.Equal("completed", resumed.Status);
        var tickets = await ImportedTicketsAsync(host, job.Id);
        Assert.Equal(3, tickets.Count);
        var ids = tickets.Select(t => t.Id).ToList();
        Assert.Equal(2, await host.QueryAsync(db => db.Replies.CountAsync(r => ids.Contains(r.TicketId))));
        // The run picked tickets up from its saved cursor, not from the start.
        Assert.DoesNotContain(adapter.Calls, c => c.Type == "tickets" && c.Cursor is null);
        Assert.Contains(adapter.Calls, c => c.Type == "tickets" && c.Cursor == "2");
    }

    [Fact]
    public async Task Pause_cancel_and_resume_of_a_failed_job()
    {
        var (host, browser, _) = await StartAsync();
        await using var _host = host;
        var sealedCredentials = host.Services.GetRequiredService<ImportCredentials>().Seal(Credentials);
        Guid importing = Guid.Empty, failed = Guid.Empty;
        await host.SeedAsync(async db =>
        {
            var a = new ImportJob { Platform = "fakedesk", Status = "importing", Credentials = sealedCredentials };
            var b = new ImportJob { Platform = "fakedesk", Status = "failed", Credentials = sealedCredentials };
            db.ImportJobs.AddRange(a, b);
            await db.SaveChangesAsync();
            importing = a.Id;
            failed = b.Id;
        });

        await browser.SendAsync(HttpMethod.Post, $"{Base}/pause/{importing}", new { });
        Assert.Equal("paused", await host.QueryAsync(db => db.ImportJobs.Where(j => j.Id == importing).Select(j => j.Status).SingleAsync()));

        await browser.SendAsync(HttpMethod.Post, $"{Base}/cancel/{importing}", new { });
        var cancelled = await host.QueryAsync(db => db.ImportJobs.AsNoTracking().SingleAsync(j => j.Id == importing));
        Assert.Equal("failed", cancelled.Status);
        Assert.NotNull(cancelled.CompletedAt);
        Assert.Null(cancelled.Credentials);

        await browser.SendAsync(HttpMethod.Post, $"{Base}/resume/{failed}", new { });
        await WaitForImportsAsync(host);
        Assert.Equal("completed", await host.QueryAsync(db => db.ImportJobs.Where(j => j.Id == failed).Select(j => j.Status).SingleAsync()));
    }

    [Fact]
    public async Task The_wizard_is_admin_only()
    {
        var (host, _, _) = await StartAsync();
        await using var _host = host;
        var response = await host.ClientFor("someone").SendAsync(Visit(HttpMethod.Get, Base));
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Import_mode_holds_back_SLA_escalations_and_automations()
    {
        var (host, _, _) = await StartAsync();
        await using var _host = host;
        using var scope = host.Services.CreateScope();
        var sla = scope.ServiceProvider.GetRequiredService<SlaService>();
        var escalations = scope.ServiceProvider.GetRequiredService<EscalationService>();
        var automations = scope.ServiceProvider.GetRequiredService<AutomationRunner>();
        var ticket = new Ticket { Subject = "Old", Reference = "ESC-IMPORT-1" };

        var results = await ImportContext.SuppressAsync(async () =>
        {
            await sla.AttachPolicyAsync(ticket, new SlaPolicy { Id = 42, Name = "x", FirstResponseHours = "{}", ResolutionHours = "{}" });
            return new[] { await sla.CheckBreachesAsync(), await escalations.EvaluateRulesAsync(), await automations.RunAsync() };
        });

        Assert.Equal(new[] { 0, 0, 0 }, results);
        Assert.Null(ticket.SlaPolicyId);
        Assert.False(ImportContext.IsImporting);
    }
}
