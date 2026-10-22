using System.Net;
using System.Text;
using System.Text.Json;
using Escalated.Middleware;
using Escalated.Models;
using Escalated.Services;
using Escalated.Services.Email.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// Stored secrets never reach the browser or an API client (escalated-laravel
/// <c>SecretsNeverSerializedTest</c>). Import source credentials, webhook signing
/// secrets, API token hashes and the host's inbound and guest-access keys are kept
/// server-side so the app can use them. Every page and JSON response that carries
/// one of those records is checked here, through the real pipeline, for the raw
/// secret anywhere in the body.
/// </summary>
public class SecretsNeverSerializedTests
{
    private const string Admin = "admin-1";
    private const string ImportApiKey = "imp-key-7f3a9c2e5b1d";
    private const string ImportPassword = "imp-pass-q8w7e6r5t4";
    private const string WebhookSecret = "whsec-4c8e1a7b9d3f";
    private const string TokenHash = "tokhash-9e8d7c6b5a4f3e2d1c0b";
    private const string InboundSecret = "inbound-5d4c3b2a1f0e";
    private const string GuestSecret = "guest-key-0a1b2c3d4e5f";
    private const string SmtpPassword = "smtp-pass-6f5e4d3c2b1a";
    private const string GuestToken = "guest-tok-1q2w3e4r5t6y";

    private static readonly string[] HostSecrets = { InboundSecret, GuestSecret, SmtpPassword };

    private sealed class Directory : IUserDirectory
    {
        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Array.Empty<UserDirectoryEntry>(), 0, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<UserDirectoryEntry?>(id == Admin ? new UserDirectoryEntry(Admin, "Ada Admin", "ada@example.com") : null);
    }

    private sealed record Seeded(int WebhookId, Guid JobId, string SealedCredentials);

    private static async Task<(EscalatedTestHost Host, HttpClient Client, Seeded Seed)> StartAsync()
    {
        var adapter = new ImportPagesTests.FakeImportAdapter();
        var host = await EscalatedTestHost.StartAsync(
            options =>
            {
                options.Ui.Enabled = true;
                options.Email.InboundSecret = InboundSecret;
                options.Email.Smtp.Password = SmtpPassword;
                options.GuestAccess.Secret = GuestSecret;
            },
            services =>
            {
                services.AddSingleton<IUserDirectory, Directory>();
                services.AddSingleton<IImportAdapter>(adapter);
                services.AddSingleton<IAttachmentStorage>(adapter.Storage);
            });
        await host.GrantRoleAsync(Admin, "escalated-admin");

        var sealedCredentials = host.Services.GetRequiredService<ImportCredentials>()
            .Seal(new Dictionary<string, string> { ["api_key"] = ImportApiKey, ["password"] = ImportPassword });

        Seeded? seeded = null;
        await host.SeedAsync(async db =>
        {
            var webhook = new Webhook { Url = "https://hooks.example.com/escalated", Secret = WebhookSecret, Events = """["ticket.created"]""" };
            db.Webhooks.Add(webhook);

            var errors = Enumerable.Range(1, 60)
                .Select(i => new Dictionary<string, object?> { ["entity_type"] = "tickets", ["source_id"] = $"k{i}", ["error"] = $"failure {i}" })
                .ToList();
            var job = new ImportJob
            {
                UserId = Admin,
                Platform = "fakedesk",
                Status = "importing",
                Credentials = sealedCredentials,
                FieldMappings = """{"tickets":{"subject":"title"}}""",
                Progress = """{"tickets":{"total":10,"processed":4,"skipped":0,"failed":0}}""",
                Errors = JsonSerializer.Serialize(errors),
                StartedAt = DateTime.UtcNow,
            };
            db.ImportJobs.Add(job);

            db.ApiTokens.Add(new ApiToken { Name = "CI", TokenHash = TokenHash, TokenableType = "User", TokenableId = 1, Abilities = """["agent"]""" });
            db.Tickets.Add(new Ticket
            {
                Reference = "ESC-00001",
                Subject = "Guest question",
                Description = "Hello",
                GuestName = "Gus",
                GuestEmail = "gus@example.com",
                GuestToken = GuestToken,
            });
            await db.SaveChangesAsync();

            seeded = new Seeded(webhook.Id, job.Id, sealedCredentials);
        });

        return (host, host.ClientFor(Admin), seeded!);
    }

    private static void AssertHasNoSecrets(string body, params string[] secrets)
    {
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, body);
        }
    }

    private static async Task<string> PageBodyAsync(HttpClient client, string url, string? cookie = null)
    {
        var page = await PageAsync(client, url, cookie);
        return page.GetRawText();
    }

    private static async Task<string> JsonBodyAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {text}");
        Assert.False(response.Headers.Contains("X-Inertia"));
        return text;
    }

    private static HttpRequestMessage Json(HttpMethod method, string url, object body) => new(method, url)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    private static async Task SendPageAsync(HttpClient client, HttpMethod method, string url, object body)
    {
        var response = await client.SendAsync(Visit(method, url, body));
        Assert.True(response.StatusCode == HttpStatusCode.SeeOther, $"{method} {url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    // --- Webhooks ---------------------------------------------------------------

    [Fact]
    public async Task Webhook_pages_never_carry_the_signing_secret_and_say_has_secret_instead()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        var index = await PageAsync(client, "/support/admin/webhooks");
        AssertHasNoSecrets(index.GetRawText(), WebhookSecret);
        var listed = index.GetProperty("props").GetProperty("webhooks")[0];
        Assert.True(listed.GetProperty("has_secret").GetBoolean());
        Assert.False(listed.TryGetProperty("secret", out _));

        var edit = await PageAsync(client, $"/support/admin/webhooks/{seed.WebhookId}/edit");
        AssertHasNoSecrets(edit.GetRawText(), WebhookSecret);
        var form = edit.GetProperty("props").GetProperty("webhook");
        Assert.True(form.GetProperty("has_secret").GetBoolean());
        Assert.False(form.TryGetProperty("secret", out _));

        var log = await PageAsync(client, $"/support/admin/webhooks/{seed.WebhookId}/deliveries");
        AssertHasNoSecrets(log.GetRawText(), WebhookSecret);
        Assert.True(log.GetProperty("props").GetProperty("webhook").GetProperty("has_secret").GetBoolean());
    }

    [Fact]
    public async Task Webhook_json_api_never_carries_the_signing_secret()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var list = await JsonBodyAsync(await client.GetAsync("/support/admin/webhooks"));
        AssertHasNoSecrets(list, WebhookSecret);
        var webhook = JsonDocument.Parse(list).RootElement[0];
        Assert.True(webhook.GetProperty("has_secret").GetBoolean());
        Assert.False(webhook.TryGetProperty("secret", out _));

        // Create through the API still stores the secret it is sent, and does not echo it.
        const string created = "whsec-created-2b4d6f8h";
        var response = await JsonBodyAsync(await client.SendAsync(Json(HttpMethod.Post, "/support/admin/webhooks",
            new { url = "https://hooks.example.com/api", secret = created, events = """["ticket.created"]""", active = true })));
        AssertHasNoSecrets(response, created);
        Assert.True(JsonDocument.Parse(response).RootElement.GetProperty("has_secret").GetBoolean());
        Assert.Equal(created, await host.QueryAsync(db => db.Webhooks.Where(w => w.Url == "https://hooks.example.com/api").Select(w => w.Secret).SingleAsync()));
    }

    private static Task<string?> StoredSecretAsync(EscalatedTestHost host, int id) =>
        host.QueryAsync(db => db.Webhooks.AsNoTracking().Where(w => w.Id == id).Select(w => w.Secret).SingleAsync());

    [Fact]
    public async Task Webhook_update_keeps_a_blank_or_missing_secret_replaces_a_new_one_and_clear_secret_clears_it()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;
        var url = $"/support/admin/webhooks/{seed.WebhookId}";

        // The form posts `secret: ''` for "unchanged".
        await SendPageAsync(client, HttpMethod.Put, url,
            new { url = "https://hooks.example.com/escalated", events = new[] { "ticket.created" }, secret = "", clear_secret = false, active = true });
        Assert.Equal(WebhookSecret, await StoredSecretAsync(host, seed.WebhookId));

        await SendPageAsync(client, HttpMethod.Put, url,
            new { url = "https://hooks.example.com/escalated", events = new[] { "ticket.created" }, active = true });
        Assert.Equal(WebhookSecret, await StoredSecretAsync(host, seed.WebhookId));

        await SendPageAsync(client, HttpMethod.Put, url,
            new { url = "https://hooks.example.com/escalated", events = new[] { "ticket.created" }, secret = "whsec-rotated-1a2b", clear_secret = false, active = true });
        Assert.Equal("whsec-rotated-1a2b", await StoredSecretAsync(host, seed.WebhookId));

        // `clear_secret` wins over a secret sent with it.
        await SendPageAsync(client, HttpMethod.Put, url,
            new { url = "https://hooks.example.com/escalated", events = new[] { "ticket.created" }, secret = "ignored", clear_secret = true, active = true });
        Assert.Null(await StoredSecretAsync(host, seed.WebhookId));

        var edit = await PageAsync(client, $"/support/admin/webhooks/{seed.WebhookId}/edit");
        Assert.False(edit.GetProperty("props").GetProperty("webhook").GetProperty("has_secret").GetBoolean());
    }

    [Fact]
    public async Task Webhook_create_from_the_page_stores_a_secret_or_none()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        await SendPageAsync(client, HttpMethod.Post, "/support/admin/webhooks",
            new { url = "https://hooks.example.com/with", events = new[] { "ticket.created" }, secret = "whsec-new-9z8y", active = true });
        await SendPageAsync(client, HttpMethod.Post, "/support/admin/webhooks",
            new { url = "https://hooks.example.com/without", events = new[] { "ticket.created" }, secret = "", active = true });

        Assert.Equal("whsec-new-9z8y", await host.QueryAsync(db => db.Webhooks.Where(w => w.Url == "https://hooks.example.com/with").Select(w => w.Secret).SingleAsync()));
        Assert.Null(await host.QueryAsync(db => db.Webhooks.Where(w => w.Url == "https://hooks.example.com/without").Select(w => w.Secret).SingleAsync()));
    }

    // --- Import -----------------------------------------------------------------

    [Fact]
    public async Task Import_pages_and_json_never_carry_the_source_credentials()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;
        var secrets = new[] { ImportApiKey, ImportPassword, seed.SealedCredentials };

        var index = await PageAsync(client, "/support/admin/import");
        AssertHasNoSecrets(index.GetRawText(), secrets);
        var job = index.GetProperty("props").GetProperty("jobs")[0];
        Assert.Equal(
            new[] { "completed_at", "created_at", "error_log", "field_mappings", "id", "platform", "progress", "started_at", "status" },
            job.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(50, job.GetProperty("error_log").GetArrayLength());
        Assert.Equal("k60", job.GetProperty("error_log")[49].GetProperty("source_id").GetString());

        var progress = await PageAsync(client, $"/support/admin/import/progress/{seed.JobId}");
        AssertHasNoSecrets(progress.GetRawText(), secrets);
        Assert.Equal(
            new[] { "completed_at", "created_at", "error_log", "field_mappings", "id", "platform", "progress", "started_at", "status" },
            progress.GetProperty("props").GetProperty("job").EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));

        AssertHasNoSecrets(await JsonBodyAsync(await client.GetAsync($"/support/admin/import/status/{seed.JobId}")), secrets);
        AssertHasNoSecrets(await PageBodyAsync(client, "/support/admin/import/fakedesk/connect"), secrets);

        // The wizard's steps, with the credentials the connect step sealed into its cookie.
        var cookie = $"escalated_import_fakedesk={Uri.EscapeDataString(seed.SealedCredentials)}";
        AssertHasNoSecrets(await PageBodyAsync(client, "/support/admin/import/fakedesk/mapping", cookie), secrets);
        var review = await client.SendAsync(Visit(HttpMethod.Post, "/support/admin/import/fakedesk/review",
            new { field_mappings = new { tickets = new { subject = "title" } }, entity_types = new[] { "tickets" } }));
        AssertHasNoSecrets(await review.Content.ReadAsStringAsync(), secrets);

        var test = await client.SendAsync(Visit(HttpMethod.Post, "/support/admin/import/fakedesk/test-connection",
            new { credentials = new { subdomain = "acme", api_token = ImportApiKey } }));
        AssertHasNoSecrets(await test.Content.ReadAsStringAsync(), ImportApiKey);

        // The JSON admin API.
        AssertHasNoSecrets(await JsonBodyAsync(await client.GetAsync("/support/admin/import")), secrets);
        AssertHasNoSecrets(await JsonBodyAsync(await client.GetAsync($"/support/admin/import/{seed.JobId}")), secrets);

        // The run still reads them server-side.
        var stored = await host.QueryAsync(db => db.ImportJobs.AsNoTracking().SingleAsync(j => j.Id == seed.JobId));
        Assert.Equal(ImportApiKey, host.Services.GetRequiredService<ImportCredentials>().Open(stored.Credentials)!["api_key"]);
    }

    // --- API tokens and settings ------------------------------------------------

    [Fact]
    public async Task Api_token_page_and_json_never_carry_the_token_hash()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        AssertHasNoSecrets(await PageBodyAsync(client, "/support/admin/api-tokens"), TokenHash);
        AssertHasNoSecrets(await JsonBodyAsync(await client.GetAsync("/support/admin/api-tokens")), TokenHash);

        // Creation shows the plain token once, never the hash stored for it.
        var created = await JsonBodyAsync(await client.SendAsync(Json(HttpMethod.Post, "/support/admin/api-tokens", new { name = "Deploy" })));
        var plain = JsonDocument.Parse(created).RootElement.GetProperty("plainToken").GetString()!;
        AssertHasNoSecrets(created, ApiTokenAuthMiddleware.ComputeSha256(plain));
    }

    [Theory]
    [InlineData("/support/admin/settings")]
    [InlineData("/support/admin/settings/csat")]
    [InlineData("/support/admin/settings/data-retention")]
    [InlineData("/support/admin/settings/database")]
    [InlineData("/support/admin/settings/email")]
    [InlineData("/support/admin/settings/public-tickets")]
    public async Task Settings_pages_never_carry_the_hosts_inbound_smtp_or_guest_keys(string url)
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        AssertHasNoSecrets(await PageBodyAsync(client, url), HostSecrets);
    }

    [Fact]
    public async Task Settings_json_never_carries_the_hosts_keys()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        AssertHasNoSecrets(await JsonBodyAsync(await client.GetAsync("/support/admin/settings")), HostSecrets);
        AssertHasNoSecrets(await JsonBodyAsync(await client.GetAsync("/support/admin/settings/public-tickets")), HostSecrets);
    }

    [Fact]
    public async Task Ticket_json_never_carries_a_guest_token()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;
        var id = await host.QueryAsync(db => db.Tickets.Where(t => t.GuestToken == GuestToken).Select(t => t.Id).SingleAsync());

        AssertHasNoSecrets(await JsonBodyAsync(await client.GetAsync($"/support/admin/tickets/{id}")), GuestToken);
    }

    // --- Serializer level ---------------------------------------------------------

    [Fact]
    public void Models_leave_their_secrets_out_of_json()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var webhook = JsonSerializer.Serialize(new Webhook { Url = "https://x.test", Secret = WebhookSecret }, web);
        AssertHasNoSecrets(webhook, WebhookSecret);
        Assert.True(JsonDocument.Parse(webhook).RootElement.GetProperty("has_secret").GetBoolean());
        Assert.False(JsonDocument.Parse(JsonSerializer.Serialize(new Webhook { Secret = "" }, web)).RootElement.GetProperty("has_secret").GetBoolean());
        Assert.False(JsonDocument.Parse(JsonSerializer.Serialize(new Webhook(), web)).RootElement.GetProperty("has_secret").GetBoolean());

        AssertHasNoSecrets(JsonSerializer.Serialize(new ApiToken { Name = "CI", TokenHash = TokenHash }, web), TokenHash);
        AssertHasNoSecrets(JsonSerializer.Serialize(new ImportJob { Platform = "x", Credentials = ImportApiKey }, web), ImportApiKey);
        AssertHasNoSecrets(JsonSerializer.Serialize(new Ticket { GuestToken = GuestToken }, web), GuestToken);
    }
}
