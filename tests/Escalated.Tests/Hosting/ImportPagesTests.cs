using System.Net;
using System.Text;
using System.Text.Json;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Configuration;
using Escalated.Services;
using Escalated.Services.Email.Inbound;
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

    // A real PDF header and PNG signature.
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52 };

    /// <summary>Attachment storage that keeps the bytes it is given.</summary>
    public sealed class MemoryStorage : IAttachmentStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();

        public string Name => "memory";

        public async Task<string> PutAsync(string filename, Stream content, string contentType, CancellationToken ct = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            var path = $"memory/{Guid.NewGuid():N}/{filename}";
            lock (Files) Files[path] = buffer.ToArray();
            return path;
        }
    }

    public sealed class FakeImportAdapter : IImportAdapter
    {
        public MemoryStorage Storage { get; } = new();

        /// <summary>Replaces the default records when set.</summary>
        public Dictionary<string, List<Dictionary<string, object?>>>? Overrides { get; set; }

        private static readonly Dictionary<string, List<Dictionary<string, object?>>> Records = new()
        {
            ["contacts"] = new()
            {
                new() { ["source_id"] = "c1", ["email"] = "ada@customer.test", ["name"] = "Ada Customer" },
                new() { ["source_id"] = "c2", ["email"] = "bob@customer.test", ["name"] = "Bob Customer" },
            },
            ["tags"] = new() { new() { ["source_id"] = "t1", ["name"] = "Billing" } },
            ["custom_fields"] = new()
            {
                new() { ["source_id"] = "f1", ["name"] = "Plan", ["type"] = "select", ["options"] = new List<object?> { "free", "pro" } },
                new() { ["source_id"] = "f2", ["name"] = "Seats", ["type"] = "number" },
            },
            ["tickets"] = new()
            {
                new()
                {
                    ["source_id"] = "k1", ["title"] = "Refund please", ["description"] = "Charged twice.", ["status"] = "open",
                    ["priority"] = "high", ["requester_source_id"] = "c1", ["tag_source_ids"] = new List<object?> { "t1" },
                    ["created_at"] = "2024-01-02T03:04:05Z",
                    ["custom_fields"] = new Dictionary<string, object?> { ["f1"] = "pro", ["f2"] = 12 },
                },
                new() { ["source_id"] = "k2", ["title"] = "Login broken", ["status"] = "resolved", ["priority"] = "urgent", ["requester_source_id"] = "c2" },
                new() { ["source_id"] = "k3", ["title"] = "Feature idea", ["status"] = "not-a-status", ["requester_source_id"] = "c1" },
            },
            ["replies"] = new()
            {
                new() { ["source_id"] = "r1", ["ticket_source_id"] = "k1", ["body"] = "Looking into it", ["author_source_id"] = "c1" },
                new() { ["source_id"] = "r2", ["ticket_source_id"] = "k2", ["body"] = "Internal note", ["is_internal_note"] = true },
            },
            ["attachments"] = new()
            {
                new()
                {
                    ["source_id"] = "a1", ["parent_type"] = "ticket", ["parent_source_id"] = "k1", ["filename"] = "receipt.pdf",
                    ["mime_type"] = "application/pdf", ["content"] = Convert.ToBase64String(Pdf),
                },
                new() { ["source_id"] = "a2", ["parent_source_id"] = "r1", ["filename"] = "screen.png", ["mime_type"] = "image/png", ["content"] = Png },
            },
            ["satisfaction_ratings"] = new()
            {
                new() { ["source_id"] = "s1", ["ticket_source_id"] = "k2", ["rating"] = 4, ["comment"] = "Quick fix", ["created_at"] = "2024-02-03T04:05:06Z" },
            },
        };

        public List<(string Type, string? Cursor, bool Importing)> Calls { get; } = new();
        public Func<string, string?, Task>? OnExtract { get; set; }
        public string? SourceFieldCredentials { get; private set; }

        public string Name => "fakedesk";
        public string DisplayName => "FakeDesk";
        public string[] EntityTypes => new[] { "contacts", "tags", "custom_fields", "tickets", "replies", "attachments", "satisfaction_ratings" };

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

            var records = (Overrides ?? Records).GetValueOrDefault(entityType) ?? new();
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

    private static async Task<(EscalatedTestHost Host, Browser Browser, FakeImportAdapter Adapter)> StartAsync(Action<EscalatedOptions>? configure = null)
    {
        var adapter = new FakeImportAdapter();
        var host = await EscalatedTestHost.StartAsync(
            options =>
            {
                options.Ui.Enabled = true;
                configure?.Invoke(options);
            },
            services =>
            {
                services.AddSingleton<IUserDirectory, Directory>();
                services.AddSingleton<IImportAdapter>(adapter);
                services.AddSingleton<IAttachmentStorage>(adapter.Storage);
            });
        await host.GrantRoleAsync(Admin, "escalated-admin");
        return (host, new Browser(host.ClientFor(Admin)), adapter);
    }

    private static Task WaitForImportsAsync(EscalatedTestHost host) =>
        host.Services.GetRequiredService<BackgroundImportDispatcher>().WaitAllAsync();

    private static Task<ImportJob> StartImportAsync(EscalatedTestHost host, Browser browser) =>
        StartImportAsync(host, browser, FieldMappings);

    private static async Task<ImportJob> StartImportAsync(EscalatedTestHost host, Browser browser, object fieldMappings)
    {
        var (status, body) = await browser.ConnectAsync(Credentials);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("success").GetBoolean());

        var response = await browser.SendAsync(HttpMethod.Post, $"{Base}/fakedesk/start", new { field_mappings = fieldMappings });
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
        Assert.Equal(new[] { "contacts", "tags", "custom_fields", "tickets", "replies", "attachments", "satisfaction_ratings" }, props.GetProperty("entity_types").EnumerateArray().Select(e => e.GetString()));
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
        await AssertImportedRecordsAsync(host, adapter, job, tickets);

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

    private static Task<string?> MappedAsync(EscalatedTestHost host, Guid jobId, string entityType, string sourceId) =>
        host.QueryAsync(db => db.ImportSourceMaps.Where(m => m.ImportJobId == jobId && m.EntityType == entityType && m.SourceId == sourceId)
            .Select(m => m.EscalatedId).FirstOrDefaultAsync());

    private static Task<Dictionary<string, string?>> FieldValuesAsync(EscalatedTestHost host, int ticketId) => host.QueryAsync(db =>
        db.CustomFieldValues.Where(v => v.EntityType == "ticket" && v.EntityId == ticketId)
            .Join(db.CustomFields, v => v.CustomFieldId, f => f.Id, (v, f) => new { f.Slug, v.Value })
            .ToDictionaryAsync(x => x.Slug, x => x.Value));

    /// <summary>The job's error log as entity type -> source id -> messages.</summary>
    private static Dictionary<string, Dictionary<string, string>> ErrorsByType(ImportJob job)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        foreach (var e in JsonDocument.Parse(job.Errors ?? "[]").RootElement.EnumerateArray())
        {
            var type = e.GetProperty("entity_type").GetString()!;
            var source = e.GetProperty("source_id").GetString()!;
            if (!result.TryGetValue(type, out var bySource)) result[type] = bySource = new();
            bySource[source] = (bySource.GetValueOrDefault(source) is { } prior ? prior + "; " : "") + e.GetProperty("error").GetString();
        }
        return result;
    }

    private static string Progress(ImportJob job, string type)
    {
        var p = JsonDocument.Parse(job.Progress!).RootElement.GetProperty(type);
        return $"{p.GetProperty("processed").GetInt32()} {p.GetProperty("failed").GetInt32()}";
    }

    /// <summary>What the default fake records leave behind besides tickets and replies.</summary>
    private static async Task AssertImportedRecordsAsync(EscalatedTestHost host, FakeImportAdapter adapter, ImportJob job, List<Ticket> tickets)
    {
        // Definitions, as Laravel's persistCustomField: found or created by slug.
        Assert.NotNull(await MappedAsync(host, job.Id, "custom_fields", "f1"));
        var plan = await host.QueryAsync(db => db.CustomFields.AsNoTracking().SingleAsync(f => f.Slug == "plan"));
        Assert.Equal("select", plan.Type);
        Assert.Equal(new[] { "free", "pro" }, JsonSerializer.Deserialize<string[]>(plan.Options!));
        Assert.Equal("number", await host.QueryAsync(db => db.CustomFields.Where(f => f.Slug == "seats").Select(f => f.Type).SingleAsync()));

        // Ticket values, keyed by the field's source id.
        var values = await FieldValuesAsync(host, tickets[0].Id);
        Assert.Equal("pro", values["plan"]);
        Assert.Equal("12", values["seats"]);

        // Attachments: the bytes went through the attachment storage.
        var a1 = await host.QueryAsync(db => db.Attachments.AsNoTracking().SingleAsync(a => a.Id == int.Parse(MappedSync(db, job.Id, "a1"))));
        Assert.Equal(("ticket", tickets[0].Id, "receipt.pdf", "application/pdf", (long)Pdf.Length, "memory"), (a1.AttachableType, a1.AttachableId, a1.Filename, a1.MimeType, a1.Size, a1.Disk));
        Assert.Equal(Pdf, adapter.Storage.Files[a1.Path]);
        var replyId = int.Parse((await MappedAsync(host, job.Id, "replies", "r1"))!);
        var a2 = await host.QueryAsync(db => db.Attachments.AsNoTracking().SingleAsync(a => a.Id == int.Parse(MappedSync(db, job.Id, "a2"))));
        Assert.Equal(("reply", replyId), (a2.AttachableType, a2.AttachableId));
        Assert.Equal(Png, adapter.Storage.Files[a2.Path]);

        // The rating, on its ticket, at the source's time, with no rater.
        var rating = await host.QueryAsync(db => db.SatisfactionRatings.AsNoTracking().SingleAsync(r => r.TicketId == tickets[1].Id));
        Assert.Equal((4, "Quick fix", (string?)null), (rating.Rating, rating.Comment, rating.RaterType));
        Assert.Equal(new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc), DateTime.SpecifyKind(rating.CreatedAt, DateTimeKind.Utc));
    }

    private static string MappedSync(Escalated.Data.EscalatedDbContext db, Guid jobId, string sourceId) =>
        db.ImportSourceMaps.Where(m => m.ImportJobId == jobId && m.EntityType == "attachments" && m.SourceId == sourceId).Select(m => m.EscalatedId).Single();

    [Fact]
    public async Task Imported_records_are_held_to_the_live_rules()
    {
        var (host, browser, adapter) = await StartAsync(options => options.Attachments.MaxSizeKb = 1);
        await using var _host = host;
        string B64(byte[] bytes) => Convert.ToBase64String(bytes);
        adapter.Overrides = new()
        {
            ["contacts"] = new() { new() { ["source_id"] = "c1", ["email"] = "ada@customer.test" } },
            ["custom_fields"] = new()
            {
                new() { ["source_id"] = "f1", ["name"] = "Plan", ["type"] = "select", ["options"] = new List<object?> { "free", "pro" } },
                new() { ["source_id"] = "f2", ["name"] = "Seats", ["type"] = "number" },
                new() { ["source_id"] = "f8", ["name"] = "Hologram", ["type"] = "hologram" },
                new() { ["source_id"] = "f9", ["name"] = "Broken", ["type"] = "select" },
            },
            ["tickets"] = new()
            {
                new()
                {
                    ["source_id"] = "k1", ["title"] = "One", ["requester_source_id"] = "c1",
                    ["custom_fields"] = new Dictionary<string, object?> { ["plan_code"] = "pro", ["f2"] = "many", ["skipped_src"] = "x", ["nope"] = "y" },
                },
                new() { ["source_id"] = "k2", ["title"] = "Two" },
            },
            ["replies"] = new() { new() { ["source_id"] = "r1", ["ticket_source_id"] = "k1", ["body"] = "Hi" } },
            ["attachments"] = new()
            {
                new() { ["source_id"] = "a1", ["parent_type"] = "ticket", ["parent_source_id"] = "k1", ["filename"] = "x.pdf", ["path"] = "/etc/passwd" },
                new() { ["source_id"] = "a2", ["parent_type"] = "ticket", ["parent_source_id"] = "k1", ["filename"] = "big.pdf", ["content"] = B64(Pdf.Concat(new byte[2000]).ToArray()) },
                new() { ["source_id"] = "a3", ["parent_type"] = "ticket", ["parent_source_id"] = "k1", ["filename"] = "run.exe", ["content"] = B64(new byte[] { 0x4D, 0x5A }) },
                new() { ["source_id"] = "a5", ["parent_type"] = "ticket", ["parent_source_id"] = "zz", ["filename"] = "y.pdf", ["content"] = B64(Pdf) },
                new() { ["source_id"] = "a6", ["parent_type"] = "ticket", ["parent_source_id"] = "k1", ["filename"] = "../../evil.pdf", ["content"] = B64(Pdf) },
                new() { ["source_id"] = "a7", ["parent_type"] = "ticket", ["parent_source_id"] = "k1", ["filename"] = "z.pdf", ["content"] = "!!not base64!!" },
            },
            ["satisfaction_ratings"] = new()
            {
                new() { ["source_id"] = "s1", ["ticket_source_id"] = "k1", ["rating"] = 9 },
                new() { ["source_id"] = "s2", ["ticket_source_id"] = "k1", ["rating"] = "x" },
                new() { ["source_id"] = "s3", ["ticket_source_id"] = "zz", ["rating"] = 3 },
                new() { ["source_id"] = "s4", ["ticket_source_id"] = "k2", ["score"] = 5 },
                new() { ["source_id"] = "s5", ["ticket_source_id"] = "k2", ["rating"] = 3 },
                new() { ["source_id"] = "s6", ["ticket_source_id"] = "k1", ["rating"] = 2.5 },
            },
        };

        var job = await StartImportAsync(host, browser, new { tickets = new Dictionary<string, string> { ["plan_code"] = "custom_fields.plan", ["skipped_src"] = "" } });

        Assert.Equal("completed", job.Status);
        var errors = ErrorsByType(job);

        // Definitions: an unknown type and an option-less select fail.
        Assert.Equal("2 2", Progress(job, "custom_fields"));
        Assert.Contains("hologram", errors["custom_fields"]["f8"]);
        Assert.Contains("option", errors["custom_fields"]["f9"]);

        // Values: mapped, skipped, invalid and unknown keys; the ticket is kept.
        Assert.Equal("2 0", Progress(job, "tickets"));
        var k1 = int.Parse((await MappedAsync(host, job.Id, "tickets", "k1"))!);
        Assert.Equal(new Dictionary<string, string?> { ["plan"] = "pro" }, await FieldValuesAsync(host, k1));
        var valueErrors = errors["custom_field_values"]["k1"];
        Assert.Contains("custom_fields.seats", valueErrors);
        Assert.Contains("nope", valueErrors);
        Assert.DoesNotContain("skipped_src", valueErrors);

        // Attachments: only the well-formed one under the limit is stored, under its base name.
        Assert.Equal("1 5", Progress(job, "attachments"));
        Assert.Contains("content", errors["attachments"]["a1"]);
        Assert.Contains("larger than 1 KB", errors["attachments"]["a2"]);
        Assert.Contains("cannot be attached", errors["attachments"]["a3"]);
        Assert.Contains("Parent ticket not found", errors["attachments"]["a5"]);
        Assert.Contains("base64", errors["attachments"]["a7"]);
        var a6 = await host.QueryAsync(db => db.Attachments.AsNoTracking().SingleAsync(a => a.Id == int.Parse(MappedSync(db, job.Id, "a6"))));
        Assert.Equal("evil.pdf", a6.Filename);
        Assert.DoesNotContain("..", a6.Path);
        Assert.Single(adapter.Storage.Files);

        // Ratings: 1-5 integers on an imported ticket, one per ticket.
        Assert.Equal("1 5", Progress(job, "satisfaction_ratings"));
        Assert.All(new[] { "s1", "s2", "s3", "s5", "s6" }, sid => Assert.True(errors["satisfaction_ratings"].ContainsKey(sid), sid));
        var k2 = int.Parse((await MappedAsync(host, job.Id, "tickets", "k2"))!);
        Assert.Equal(5, await host.QueryAsync(db => db.SatisfactionRatings.Where(r => r.TicketId == k2).Select(r => r.Rating).SingleAsync()));
        Assert.Equal(0, await host.QueryAsync(db => db.SatisfactionRatings.CountAsync(r => r.TicketId == k1)));
    }
}
