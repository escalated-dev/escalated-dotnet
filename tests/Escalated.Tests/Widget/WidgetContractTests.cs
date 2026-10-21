using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Migrations;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Escalated.Tests.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Escalated.Tests.Widget;

/// <summary>
/// The requests the shared widget (<c>EscalatedWidget.vue</c> in
/// <c>@escalated-dev/escalated</c>) sends, and the response fields it reads.
/// </summary>
public class WidgetContractTests
{
    private const string Shop = "https://shop.example.com";

    [Fact]
    public async Task Config_OffersTheVerifiedStatusTab()
    {
        await using var host = await StartAsync();

        using var json = await GetJsonAsync(host, "/support/widget/config");

        Assert.True(json.RootElement.GetProperty("status_lookup_enabled").GetBoolean());
        Assert.True(json.RootElement.GetProperty("guest_tickets_enabled").GetBoolean());
        Assert.True(json.RootElement.GetProperty("guest_verification_required").GetBoolean());
    }

    [Fact]
    public async Task Articles_SearchInTheWidgetShape()
    {
        await using var host = await StartAsync();
        await SeedArticlesAsync(host);

        using var json = await GetJsonAsync(host, "/support/widget/articles?q=password");

        var article = Assert.Single(json.RootElement.GetProperty("articles").EnumerateArray());
        Assert.Equal("Reset your password", article.GetProperty("title").GetString());
        Assert.Equal("reset-your-password", article.GetProperty("slug").GetString());
        Assert.Equal("Open settings and choose a new password.", article.GetProperty("excerpt").GetString());
    }

    [Fact]
    public async Task Articles_SearchIgnoresCase()
    {
        await using var host = await StartAsync();
        await SeedArticlesAsync(host);

        using var json = await GetJsonAsync(host, "/support/widget/articles?q=RESET");

        Assert.Single(json.RootElement.GetProperty("articles").EnumerateArray());
    }

    [Theory]
    [InlineData("/support/widget/articles?q=p")]
    [InlineData("/support/widget/articles")]
    public async Task Articles_IgnoreQueriesShorterThanTwoCharacters(string path)
    {
        await using var host = await StartAsync();
        await SeedArticlesAsync(host);

        using var json = await GetJsonAsync(host, path);

        Assert.Empty(json.RootElement.GetProperty("articles").EnumerateArray());
    }

    [Fact]
    public async Task Articles_LongBodiesAreCutInTheExcerpt()
    {
        await using var host = await StartAsync();
        await host.SeedAsync(async db =>
        {
            db.Articles.Add(new Article { Title = "Long read", Slug = "long-read", Body = new string('a', 400), Status = "published" });
            await db.SaveChangesAsync();
        });

        using var json = await GetJsonAsync(host, "/support/widget/articles?q=long");

        var excerpt = json.RootElement.GetProperty("articles")[0].GetProperty("excerpt").GetString()!;
        Assert.Equal(new string('a', 200) + "...", excerpt);
    }

    [Fact]
    public async Task Article_IsReturnedTopLevelBySlug()
    {
        await using var host = await StartAsync();
        await SeedArticlesAsync(host);

        using var json = await GetJsonAsync(host, "/support/widget/articles/reset-your-password");

        var root = json.RootElement;
        Assert.Equal("Reset your password", root.GetProperty("title").GetString());
        Assert.Equal("reset-your-password", root.GetProperty("slug").GetString());
        Assert.Equal("<p>Open settings and choose a new password.</p>", root.GetProperty("body").GetString());
        Assert.Equal("Accounts", root.GetProperty("category").GetString());
    }

    [Theory]
    [InlineData("/support/widget/articles/nope")]
    [InlineData("/support/widget/articles/password-draft")]
    public async Task Article_UnknownOrUnpublishedIs404(string path)
    {
        await using var host = await StartAsync();
        await SeedArticlesAsync(host);

        var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Articles_AreHiddenWhenTheKnowledgeBaseIsOff()
    {
        await using var host = await StartAsync(o => o.KnowledgeBase.Enabled = false);
        await SeedArticlesAsync(host);

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/support/widget/articles?q=password")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/support/widget/articles/reset-your-password")).StatusCode);
    }

    [Fact]
    public async Task KbSearch_KeepsItsOldShape()
    {
        await using var host = await StartAsync();
        await SeedArticlesAsync(host);

        using var json = await GetJsonAsync(host, "/support/widget/kb/search?q=password");

        var article = Assert.Single(json.RootElement.EnumerateArray());
        Assert.True(article.TryGetProperty("snippet", out _));
    }

    [Fact]
    public async Task TicketCreate_BindsTheWidgetsDepartmentId()
    {
        await using var host = await StartAsync();
        var sales = await SeedDepartmentAsync(host, "Sales", active: true);

        var response = await PostAsync(host, "/support/widget/tickets", TicketBody(sales));

        Assert.True(response.IsSuccessStatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var reference = json.RootElement.GetProperty("reference").GetString();
        Assert.False(string.IsNullOrEmpty(reference));
        Assert.Equal(sales, await host.QueryAsync(db => db.Tickets.Select(t => t.DepartmentId).SingleAsync()));
    }

    [Fact]
    public async Task TicketCreate_AcceptsTheDepartmentIdAsAString()
    {
        await using var host = await StartAsync();
        var sales = await SeedDepartmentAsync(host, "Sales", active: true);

        var response = await PostAsync(host, "/support/widget/tickets", TicketBody(sales.ToString()));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(sales, await host.QueryAsync(db => db.Tickets.Select(t => t.DepartmentId).SingleAsync()));
    }

    [Fact]
    public async Task TicketCreate_KeepsTheCamelCaseDepartmentId()
    {
        await using var host = await StartAsync();
        var sales = await SeedDepartmentAsync(host, "Sales", active: true);

        var response = await PostAsync(host, "/support/widget/tickets", new
        {
            subject = "Help",
            description = "d",
            name = "Guest",
            email = "guest@example.com",
            departmentId = sales,
        });

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(sales, await host.QueryAsync(db => db.Tickets.Select(t => t.DepartmentId).SingleAsync()));
    }

    [Fact]
    public async Task TicketCreate_NullDepartmentIdMeansNone()
    {
        await using var host = await StartAsync();

        var response = await PostAsync(host, "/support/widget/tickets", TicketBody(null));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Null(await host.QueryAsync(db => db.Tickets.Select(t => t.DepartmentId).SingleAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TicketCreate_RejectsInactiveOrUnknownDepartments(bool unknown)
    {
        await using var host = await StartAsync();
        var archived = await SeedDepartmentAsync(host, "Archived", active: false);

        var response = await PostAsync(host, "/support/widget/tickets", TicketBody(unknown ? archived + 100 : archived));

        AssertFieldError(response, "department_id");
        Assert.Equal(0, await host.QueryAsync(db => db.Tickets.CountAsync()));
    }

    [Fact]
    public async Task ChatStart_ReturnsAnUnguessableToken()
    {
        await using var host = await StartAsync();

        var first = await StartChatAsync(host);
        var second = await StartChatAsync(host);

        Assert.True(first.Length >= 43, first);
        Assert.False(int.TryParse(first, out _));
        Assert.NotEqual(first, second);
        var hashes = await host.QueryAsync(db => db.ChatSessions.Select(s => s.VisitorTokenHash).ToListAsync());
        Assert.All(hashes, h => Assert.Equal(64, h!.Length));
        // Only a hash of the token is stored.
        Assert.DoesNotContain(first, hashes);
    }

    [Fact]
    public async Task ChatStart_BindsTheWidgetsDepartmentId()
    {
        await using var host = await StartAsync();
        var sales = await SeedDepartmentAsync(host, "Sales", active: true);

        await StartChatAsync(host, departmentId: sales);

        Assert.Equal(sales, await host.QueryAsync(db => db.ChatSessions.Select(s => s.DepartmentId).SingleAsync()));
    }

    [Fact]
    public async Task ChatStart_RejectsAnInactiveDepartment()
    {
        await using var host = await StartAsync();
        var archived = await SeedDepartmentAsync(host, "Archived", active: false);

        var response = await PostAsync(host, "/support/widget/chat/start", new { name = "Vee", email = "vee@example.com", department_id = archived });

        AssertFieldError(response, "department_id");
        Assert.Equal(0, await host.QueryAsync(db => db.ChatSessions.CountAsync()));
    }

    [Fact]
    public async Task ChatAvailability_AcceptsTheSnakeCaseDepartment()
    {
        await using var host = await StartAsync();

        using var json = await GetJsonAsync(host, "/support/widget/chat/availability?department_id=1");

        Assert.Contains(json.RootElement.GetProperty("available").ValueKind, new[] { JsonValueKind.True, JsonValueKind.False });
    }

    [Fact]
    public async Task ChatMessages_ReturnTheTranscriptInTheWidgetShape()
    {
        await using var host = await StartAsync(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IUserDirectory>(new OneUserDirectory("7", "Dana Agent"))));
        var token = await StartChatAsync(host, message: "Hello there");
        await host.SeedAsync(async db =>
        {
            var session = await db.ChatSessions.SingleAsync();
            session.AgentId = "7";
            session.Status = "active";
            db.Replies.Add(new Reply { TicketId = session.TicketId, Body = "Hi, how can I help?", AuthorType = "agent", AuthorId = "7", Type = "reply" });
            db.Replies.Add(new Reply { TicketId = session.TicketId, Body = "secret", AuthorType = "agent", AuthorId = "7", Type = "note", IsInternalNote = true });
            await db.SaveChangesAsync();
        });

        using var json = await GetJsonAsync(host, $"/support/widget/chat/{token}/messages");

        var root = json.RootElement;
        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal("Hello there", messages[0].GetProperty("body").GetString());
        Assert.False(messages[0].GetProperty("is_agent").GetBoolean());
        Assert.Equal("Vee", messages[0].GetProperty("author").GetProperty("name").GetString());
        Assert.True(messages[0].TryGetProperty("id", out _));
        Assert.True(messages[0].TryGetProperty("created_at", out _));
        Assert.Equal("Hi, how can I help?", messages[1].GetProperty("body").GetString());
        Assert.True(messages[1].GetProperty("is_agent").GetBoolean());
        Assert.Equal("Dana Agent", messages[1].GetProperty("author").GetProperty("name").GetString());
        Assert.Equal("Dana Agent", root.GetProperty("agent").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("typing").ValueKind);
        Assert.False(root.GetProperty("ended").GetBoolean());
    }

    [Fact]
    public async Task ChatMessages_AgentWithoutADirectoryNameIsCalledAgent()
    {
        await using var host = await StartAsync();
        var token = await StartChatAsync(host);
        await host.SeedAsync(async db =>
        {
            var session = await db.ChatSessions.SingleAsync();
            session.AgentId = "9";
            db.Replies.Add(new Reply { TicketId = session.TicketId, Body = "Hi", AuthorType = "agent", AuthorId = "9", Type = "reply" });
            await db.SaveChangesAsync();
        });

        using var json = await GetJsonAsync(host, $"/support/widget/chat/{token}/messages");

        Assert.Equal("Agent", json.RootElement.GetProperty("messages")[0].GetProperty("author").GetProperty("name").GetString());
        Assert.Equal("Agent", json.RootElement.GetProperty("agent").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ChatMessages_SendStoresTheVisitorsMessage()
    {
        await using var host = await StartAsync();
        var token = await StartChatAsync(host);

        var response = await PostAsync(host, $"/support/widget/chat/{token}/messages", new { body = "Where is my order?" });

        Assert.True(response.IsSuccessStatusCode);
        using var json = await GetJsonAsync(host, $"/support/widget/chat/{token}/messages");
        var message = Assert.Single(json.RootElement.GetProperty("messages").EnumerateArray());
        Assert.Equal("Where is my order?", message.GetProperty("body").GetString());
        Assert.False(message.GetProperty("is_agent").GetBoolean());
    }

    [Fact]
    public async Task ChatMessages_SendRequiresABody()
    {
        await using var host = await StartAsync();
        var token = await StartChatAsync(host);

        var response = await PostAsync(host, $"/support/widget/chat/{token}/messages", new { body = "  " });

        AssertFieldError(response, "body");
    }

    [Fact]
    public async Task ChatTyping_Succeeds()
    {
        await using var host = await StartAsync();
        var token = await StartChatAsync(host);

        var response = await host.Client.PostAsync($"/support/widget/chat/{token}/typing", null);

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ChatEnd_EndsTheChatAndStopsMessages()
    {
        await using var host = await StartAsync();
        var token = await StartChatAsync(host);

        var end = await host.Client.PostAsync($"/support/widget/chat/{token}/end", null);
        using var json = await GetJsonAsync(host, $"/support/widget/chat/{token}/messages");
        var send = await PostAsync(host, $"/support/widget/chat/{token}/messages", new { body = "late" });

        Assert.True(end.IsSuccessStatusCode);
        Assert.True(json.RootElement.GetProperty("ended").GetBoolean());
        Assert.False(send.IsSuccessStatusCode);
        Assert.Equal("ended", await host.QueryAsync(db => db.ChatSessions.Select(s => s.Status).SingleAsync()));
    }

    [Fact]
    public async Task ChatRate_StoresOneRatingAfterTheChatEnds()
    {
        await using var host = await StartAsync();
        var token = await StartChatAsync(host);
        await host.Client.PostAsync($"/support/widget/chat/{token}/end", null);

        var first = await PostAsync(host, $"/support/widget/chat/{token}/rate", new { rating = 5, comment = "Great" });
        var second = await PostAsync(host, $"/support/widget/chat/{token}/rate", new { rating = 1 });

        Assert.True(first.IsSuccessStatusCode);
        AssertFieldError(second, "rating");
        var rating = await host.QueryAsync(db => db.SatisfactionRatings.SingleAsync());
        Assert.Equal(5, rating.Rating);
        Assert.Equal("Great", rating.Comment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task ChatRate_RejectsRatingsOutsideOneToFive(int value)
    {
        await using var host = await StartAsync();
        var token = await StartChatAsync(host);
        await host.Client.PostAsync($"/support/widget/chat/{token}/end", null);

        var response = await PostAsync(host, $"/support/widget/chat/{token}/rate", new { rating = value });

        AssertFieldError(response, "rating");
        Assert.Equal(0, await host.QueryAsync(db => db.SatisfactionRatings.CountAsync()));
    }

    [Fact]
    public async Task ChatRate_WaitsForTheChatToEnd()
    {
        await using var host = await StartAsync();
        var token = await StartChatAsync(host);

        var response = await PostAsync(host, $"/support/widget/chat/{token}/rate", new { rating = 4 });

        AssertFieldError(response, "rating");
    }

    [Fact]
    public async Task ChatRoutes_RefuseTheNumericSessionId()
    {
        await using var host = await StartAsync();
        await StartChatAsync(host);
        var id = await host.QueryAsync(db => db.ChatSessions.Select(s => s.Id).SingleAsync());

        var send = await PostAsync(host, $"/support/widget/chat/{id}/messages", new { body = "hijack" });
        var read = await host.Client.GetAsync($"/support/widget/chat/{id}/messages");
        var typing = await host.Client.PostAsync($"/support/widget/chat/{id}/typing", null);
        var end = await host.Client.PostAsync($"/support/widget/chat/{id}/end", null);
        var rate = await PostAsync(host, $"/support/widget/chat/{id}/rate", new { rating = 1 });

        Assert.Equal(HttpStatusCode.NotFound, send.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, typing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, end.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rate.StatusCode);
        Assert.Equal("waiting", await host.QueryAsync(db => db.ChatSessions.Select(s => s.Status).SingleAsync()));
        Assert.Equal(0, await host.QueryAsync(db => db.Replies.CountAsync()));
    }

    [Fact]
    public async Task ChatRoutes_RefuseUnknownTokens()
    {
        await using var host = await StartAsync();
        await StartChatAsync(host);

        var response = await host.Client.GetAsync("/support/widget/chat/not-a-real-token-at-all/messages");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NewRoutes_CarryCorsForListedOrigins()
    {
        await using var host = await StartAsync();
        await SeedArticlesAsync(host);
        var token = await StartChatAsync(host);

        foreach (var path in new[] { "/support/widget/articles?q=password", $"/support/widget/chat/{token}/messages" })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("Origin", Shop);
            var response = await host.Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(Shop, string.Join(", ", response.Headers.GetValues("Access-Control-Allow-Origin")));
        }
    }

    private static Task<EscalatedTestHost> StartAsync(
        Action<EscalatedOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null) =>
        EscalatedTestHost.StartAsync(o =>
        {
            o.Widget.AllowedOrigins.Add(Shop);
            configure?.Invoke(o);
        }, configureServices);

    private static object TicketBody(object? departmentId) => new
    {
        name = "Guest",
        email = "guest@example.com",
        subject = "Help",
        description = "Something broke",
        department_id = departmentId,
    };

    private static async Task<string> StartChatAsync(EscalatedTestHost host, int? departmentId = null, string? message = null)
    {
        var response = await PostAsync(host, "/support/widget/chat/start", new
        {
            name = "Vee",
            email = "vee@example.com",
            department_id = departmentId,
            message,
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("id").ValueKind);
        Assert.True(json.RootElement.TryGetProperty("status", out _));
        return json.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Posts <paramref name="body"/>, with a fresh email proof where the route needs one.</summary>
    private static async Task<HttpResponseMessage> PostAsync(EscalatedTestHost host, string path, object body) =>
        await host.Client.PostAsync(path, JsonContent.Create(await GuestProof.ProvedAsync(host, path, body)));

    private static async Task<JsonDocument> GetJsonAsync(EscalatedTestHost host, string path)
    {
        var response = await host.Client.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static void AssertFieldError(HttpResponseMessage response, string field)
    {
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity, response.StatusCode.ToString());
        using var json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty(field, out var messages), json.RootElement.ToString());
        Assert.NotEmpty(messages.EnumerateArray());
    }

    private static async Task<int> SeedDepartmentAsync(EscalatedTestHost host, string name, bool active)
    {
        var id = 0;
        await host.SeedAsync(async db =>
        {
            var department = new Department { Name = name, Slug = name.ToLowerInvariant(), IsActive = active };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
            id = department.Id;
        });
        return id;
    }

    private static Task SeedArticlesAsync(EscalatedTestHost host) => host.SeedAsync(async db =>
    {
        var category = new ArticleCategory { Name = "Accounts", Slug = "accounts" };
        db.ArticleCategories.Add(category);
        await db.SaveChangesAsync();
        db.Articles.Add(new Article
        {
            Title = "Reset your password",
            Slug = "reset-your-password",
            Body = "<p>Open settings and choose a new password.</p>",
            CategoryId = category.Id,
            Status = "published",
        });
        db.Articles.Add(new Article { Title = "Password draft", Slug = "password-draft", Body = "draft", Status = "draft" });
        await db.SaveChangesAsync();
    });

    private sealed class OneUserDirectory : IUserDirectory
    {
        private readonly UserDirectoryEntry entry;
        public OneUserDirectory(string id, string name) => entry = new UserDirectoryEntry(id, name, null);

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(new[] { entry }, 1, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(id == entry.Id ? entry : null);
    }
}
