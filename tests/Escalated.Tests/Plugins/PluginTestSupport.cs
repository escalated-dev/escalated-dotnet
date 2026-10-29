using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Models;
using Escalated.Plugins;
using Escalated.Services;
using Escalated.Tests.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Escalated.Tests.Plugins;

/// <summary>A test host with the UI on, the fixture plugins registered, and an admin, an agent and a customer.</summary>
internal sealed class PluginHost : IAsyncDisposable
{
    public const string Admin = "admin-1";
    public const string Agent = "agent-1";
    public const string Customer = "customer-1";

    private PluginHost(EscalatedTestHost host, PluginRecorder recorder)
    {
        Host = host;
        Recorder = recorder;
    }

    public EscalatedTestHost Host { get; }
    public PluginRecorder Recorder { get; }

    public HttpClient AdminClient => Host.ClientFor(Admin);
    public HttpClient AgentClient => Host.ClientFor(Agent);
    public HttpClient CustomerClient => Host.ClientFor(Customer);

    private sealed class Directory : IUserDirectory
    {
        private static readonly Dictionary<string, UserDirectoryEntry> Users = new()
        {
            [Admin] = new(Admin, "Ada Admin", "ada@example.com"),
            [Agent] = new(Agent, "Avery Agent", "avery@example.com"),
            [Customer] = new(Customer, "Casey Customer", "casey@example.com"),
        };

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Users.Values.ToList(), Users.Count, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Users.GetValueOrDefault(id));
    }

    public static async Task<PluginHost> StartAsync(bool natives = true, Action<EscalatedOptions>? options = null,
        Action<IServiceCollection>? services = null)
    {
        var recorder = new PluginRecorder();
        var host = await EscalatedTestHost.StartAsync(
            o =>
            {
                o.Ui.Enabled = true;
                options?.Invoke(o);
            },
            s =>
            {
                s.AddSingleton(recorder);
                s.AddSingleton<IUserDirectory, Directory>();
                if (natives)
                {
                    s.AddEscalatedPlugin<CrmPlugin>();
                    s.AddEscalatedPlugin<AuditPlugin>();
                }

                services?.Invoke(s);
            });
        await host.GrantRoleAsync(Admin, "escalated-admin");
        await host.GrantRoleAsync(Agent, "escalated-agent");
        return new PluginHost(host, recorder);
    }

    /// <summary>Activates a plugin as the admin would, and waits for what that delivered.</summary>
    public async Task ActivateAsync(string slug)
    {
        var response = await AdminClient.SendAsync(InertiaTestSupport.Visit(HttpMethod.Post, $"/support/admin/plugins/{slug}/activate"));
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        await IdleAsync();
    }

    /// <summary>Waits until every queued plugin action has been delivered.</summary>
    public Task IdleAsync() => Host.Services.GetRequiredService<PluginEventRelay>().WhenIdleAsync();

    public Task<Plugin?> RowAsync(string slug) =>
        Host.QueryAsync(db => db.Plugins.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug));

    public async Task<T> WithRegistryAsync<T>(Func<PluginRegistry, Task<T>> work)
    {
        using var scope = Host.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<PluginRegistry>());
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>A JSON request as a page script sends it.</summary>
    public static HttpRequestMessage Script(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}
