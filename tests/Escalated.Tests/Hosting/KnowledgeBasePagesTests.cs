using System.Net;
using System.Text.Json;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The knowledge base pages: <c>Admin/KnowledgeBase/Articles/Index</c> and
/// <c>Form</c>, <c>Categories/Index</c> (escalated-laravel's <c>ArticleController</c>
/// and <c>ArticleCategoryController</c>), and <c>Customer/KnowledgeBase/Index</c> and
/// <c>Article</c> (<c>Customer/KnowledgeBaseController</c>).
/// </summary>
public class KnowledgeBasePagesTests
{
    private const string Admin = "admin-1";
    private const string Customer = "customer-1";

    private sealed class Directory : IUserDirectory
    {
        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Array.Empty<UserDirectoryEntry>(), 0, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<UserDirectoryEntry?>(id == Admin ? new UserDirectoryEntry(Admin, "Ada Admin", "ada@example.com") : null);
    }

    private sealed record Seeded(int CategoryId, int ChildCategoryId, int PublishedId, int DraftId);

    private static async Task<(EscalatedTestHost Host, HttpClient Client, Seeded Seed)> StartAsync(Action<Escalated.Configuration.EscalatedOptions>? configure = null)
    {
        var host = await EscalatedTestHost.StartAsync(
            options =>
            {
                options.Ui.Enabled = true;
                configure?.Invoke(options);
            },
            services => services.AddSingleton<IUserDirectory, Directory>());
        await host.GrantRoleAsync(Admin, "escalated-admin");

        Seeded? seeded = null;
        await host.SeedAsync(async db =>
        {
            var billing = new ArticleCategory { Name = "Billing", Slug = "billing", Description = "Money things", Position = 1 };
            db.ArticleCategories.Add(billing);
            await db.SaveChangesAsync();
            var refunds = new ArticleCategory { Name = "Refunds", Slug = "refunds", ParentId = billing.Id };
            db.ArticleCategories.Add(refunds);
            var published = new Article
            {
                Title = "Update your card",
                Slug = "update-your-card",
                Body = "<p>Go to settings.</p>",
                Status = "published",
                CategoryId = billing.Id,
                AuthorId = Admin,
                ViewCount = 3,
                HelpfulCount = 2,
                PublishedAt = DateTime.UtcNow.AddDays(-1),
            };
            var sibling = new Article { Title = "Read an invoice", Slug = "read-an-invoice", Status = "published", CategoryId = billing.Id, PublishedAt = DateTime.UtcNow.AddDays(-2) };
            var draft = new Article { Title = "Upcoming pricing", Slug = "upcoming-pricing", Status = "draft", CategoryId = billing.Id };
            db.Articles.AddRange(published, sibling, draft);
            await db.SaveChangesAsync();
            seeded = new Seeded(billing.Id, refunds.Id, published.Id, draft.Id);
        });

        return (host, host.ClientFor(Admin), seeded!);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        var response = await client.SendAsync(Visit(method, url, body ?? new { }));
        Assert.True(response.StatusCode == HttpStatusCode.SeeOther, $"{method} {url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return response;
    }

    // --- Admin articles ---

    [Fact]
    public async Task AdminArticles_IndexFiltersAndPaginates()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/kb-articles");
        AssertPage(page, "Escalated/Admin/KnowledgeBase/Articles/Index");
        var props = page.GetProperty("props");
        var articles = Keys(props.GetProperty("articles"), "data", "current_page", "last_page", "total", "links");
        Assert.Equal(3, articles.GetProperty("total").GetInt32());
        var row = articles.GetProperty("data").EnumerateArray().Single(a => a.GetProperty("id").GetInt32() == seed.PublishedId);
        Keys(row, "id", "title", "slug", "status", "category", "category_id", "view_count", "helpful_count", "not_helpful_count", "author", "created_at", "published_at");
        Assert.Equal("Billing", row.GetProperty("category").GetProperty("name").GetString());
        Assert.Equal("Ada Admin", row.GetProperty("author").GetProperty("name").GetString());
        Keys(props.GetProperty("categories")[0], "id", "name");

        var drafts = await PageAsync(client, "/support/admin/kb-articles?status=draft");
        Assert.Equal(1, drafts.GetProperty("props").GetProperty("articles").GetProperty("total").GetInt32());
        Assert.Equal("draft", drafts.GetProperty("props").GetProperty("filters").GetProperty("status").GetString());
        var searched = await PageAsync(client, "/support/admin/kb-articles?search=invoice");
        Assert.Equal(1, searched.GetProperty("props").GetProperty("articles").GetProperty("total").GetInt32());
        var inCategory = await PageAsync(client, $"/support/admin/kb-articles?category_id={seed.ChildCategoryId}");
        Assert.Equal(0, inCategory.GetProperty("props").GetProperty("articles").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AdminArticles_FormStoreUpdateAndDestroy()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        var create = await PageAsync(client, "/support/admin/kb-articles/create");
        AssertPage(create, "Escalated/Admin/KnowledgeBase/Articles/Form");
        Assert.Equal(JsonValueKind.Null, create.GetProperty("props").GetProperty("article").ValueKind);
        var edit = await PageAsync(client, $"/support/admin/kb-articles/{seed.DraftId}/edit");
        Keys(edit.GetProperty("props").GetProperty("article"), "id", "title", "slug", "body", "status", "category_id");

        await SendAsync(client, HttpMethod.Post, "/support/admin/kb-articles",
            new { title = "Change your plan", slug = "", body = "Steps.", status = "published", category_id = seed.CategoryId });
        var created = await host.QueryAsync(db => db.Articles.AsNoTracking().SingleAsync(a => a.Title == "Change your plan"));
        Assert.Equal("change-your-plan", created.Slug);
        Assert.Equal(Admin, created.AuthorId);
        Assert.NotNull(created.PublishedAt);

        await SendAsync(client, HttpMethod.Put, $"/support/admin/kb-articles/{seed.DraftId}",
            new { title = "New pricing", slug = "new-pricing", body = "Soon.", status = "published", category_id = "" });
        var updated = await host.QueryAsync(db => db.Articles.AsNoTracking().SingleAsync(a => a.Id == seed.DraftId));
        Assert.Equal("new-pricing", updated.Slug);
        Assert.Equal("published", updated.Status);
        Assert.NotNull(updated.PublishedAt);
        Assert.Null(updated.CategoryId);

        var invalid = await SendAsync(client, HttpMethod.Post, "/support/admin/kb-articles", new { title = "", status = "archived", category_id = 999 });
        Assert.Equal("/support/admin/kb-articles/create", invalid.Headers.Location!.ToString());
        Keys((await PageAsync(client, "/support/admin/kb-articles/create", FlashCookie(invalid))).GetProperty("props").GetProperty("errors"),
            "title", "status", "category_id");
        var taken = await SendAsync(client, HttpMethod.Post, "/support/admin/kb-articles", new { title = "Dup", slug = "update-your-card", status = "draft" });
        Keys((await PageAsync(client, "/support/admin/kb-articles/create", FlashCookie(taken))).GetProperty("props").GetProperty("errors"), "slug");

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/kb-articles/{seed.PublishedId}");
        Assert.False(await host.QueryAsync(db => db.Articles.AnyAsync(a => a.Id == seed.PublishedId)));
    }

    // --- Admin categories ---

    [Fact]
    public async Task AdminCategories_IndexStoreUpdateAndDestroy()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/kb-categories");
        AssertPage(page, "Escalated/Admin/KnowledgeBase/Categories/Index");
        var billing = page.GetProperty("props").GetProperty("categories").EnumerateArray().Single(c => c.GetProperty("id").GetInt32() == seed.CategoryId);
        Keys(billing, "id", "name", "slug", "parent_id", "position", "description", "articles_count");
        Assert.Equal(3, billing.GetProperty("articles_count").GetInt32());

        await SendAsync(client, HttpMethod.Post, "/support/admin/kb-categories", new { name = "Getting started", slug = "", parent_id = "", position = 0, description = "" });
        var created = await host.QueryAsync(db => db.ArticleCategories.AsNoTracking().SingleAsync(c => c.Name == "Getting started"));
        Assert.Equal("getting-started", created.Slug);

        await SendAsync(client, HttpMethod.Put, $"/support/admin/kb-categories/{created.Id}", new { name = "Start here", slug = "start-here", parent_id = seed.CategoryId, position = 4 });
        var updated = await host.QueryAsync(db => db.ArticleCategories.AsNoTracking().SingleAsync(c => c.Id == created.Id));
        Assert.Equal(seed.CategoryId, updated.ParentId);
        Assert.Equal(4, updated.Position);

        var invalid = await SendAsync(client, HttpMethod.Post, "/support/admin/kb-categories", new { name = "", parent_id = 999, position = -1 });
        Keys((await PageAsync(client, "/support/admin/kb-categories", FlashCookie(invalid))).GetProperty("props").GetProperty("errors"), "name", "parent_id", "position");
        var own = await SendAsync(client, HttpMethod.Put, $"/support/admin/kb-categories/{created.Id}", new { name = "Loop", parent_id = created.Id });
        Keys((await PageAsync(client, "/support/admin/kb-categories", FlashCookie(own))).GetProperty("props").GetProperty("errors"), "parent_id");

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/kb-categories/{seed.CategoryId}");
        Assert.False(await host.QueryAsync(db => db.ArticleCategories.AnyAsync(c => c.Id == seed.CategoryId)));
        Assert.All(await host.QueryAsync(db => db.Articles.Select(a => a.CategoryId).ToListAsync()), id => Assert.Null(id));
        Assert.Null(await host.QueryAsync(db => db.ArticleCategories.Where(c => c.Id == seed.ChildCategoryId).Select(c => c.ParentId).SingleAsync()));
    }

    // --- Customer ---

    [Fact]
    public async Task CustomerIndex_ListsPublishedArticlesAndRootCategories()
    {
        var (host, _, seed) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Customer);

        var page = await PageAsync(client, "/support/kb");
        AssertPage(page, "Escalated/Customer/KnowledgeBase/Index");
        var props = page.GetProperty("props");
        var category = Assert.Single(props.GetProperty("categories").EnumerateArray());
        Keys(category, "id", "name", "slug", "description", "articles_count");
        Assert.Equal(2, category.GetProperty("articles_count").GetInt32());
        var articles = Keys(props.GetProperty("articles"), "data", "total", "last_page", "links");
        Assert.Equal(new[] { "Update your card", "Read an invoice" }, articles.GetProperty("data").EnumerateArray().Select(a => a.GetProperty("title").GetString()));
        Keys(articles.GetProperty("data")[0], "id", "title", "slug", "category", "view_count", "published_at");
        Assert.True(props.GetProperty("feedbackEnabled").GetBoolean());

        var searched = await PageAsync(client, "/support/kb?search=invoice");
        Assert.Equal(1, searched.GetProperty("props").GetProperty("articles").GetProperty("total").GetInt32());
        Assert.Equal("invoice", searched.GetProperty("props").GetProperty("filters").GetProperty("search").GetString());
        var byCategory = await PageAsync(client, $"/support/kb?category={seed.ChildCategoryId}");
        Assert.Equal(0, byCategory.GetProperty("props").GetProperty("articles").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task CustomerArticle_ShowsAPublishedArticleCountsTheViewAndListsRelated()
    {
        var (host, _, seed) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Customer);

        var page = await PageAsync(client, "/support/kb/update-your-card");
        AssertPage(page, "Escalated/Customer/KnowledgeBase/Article");
        var props = page.GetProperty("props");
        var article = Keys(props.GetProperty("article"), "id", "title", "slug", "body", "category", "view_count", "published_at");
        Assert.Equal("Billing", article.GetProperty("category").GetProperty("name").GetString());
        Assert.Equal(4, article.GetProperty("view_count").GetInt32());
        var related = Assert.Single(props.GetProperty("related").EnumerateArray());
        Keys(related, "id", "title", "slug");
        Assert.Equal("read-an-invoice", related.GetProperty("slug").GetString());

        var draft = await client.SendAsync(Visit(HttpMethod.Get, "/support/kb/upcoming-pricing"));
        Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);
    }

    [Fact]
    public async Task CustomerFeedback_CountsHelpfulAndNotHelpful()
    {
        var (host, _, seed) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Customer);

        var response = await SendAsync(client, HttpMethod.Post, "/support/kb/update-your-card/feedback", new { helpful = true });
        await SendAsync(client, HttpMethod.Post, "/support/kb/update-your-card/feedback", new { helpful = false });

        var article = await host.QueryAsync(db => db.Articles.AsNoTracking().SingleAsync(a => a.Id == seed.PublishedId));
        Assert.Equal(3, article.HelpfulCount);
        Assert.Equal(1, article.NotHelpfulCount);
        var page = await PageAsync(client, "/support/kb/update-your-card", FlashCookie(response));
        Assert.Equal("Thank you for your feedback!", page.GetProperty("props").GetProperty("flash").GetProperty("success").GetString());
    }

    [Fact]
    public async Task CustomerPages_FollowTheKnowledgeBaseSwitches()
    {
        var (host, _, _) = await StartAsync(o => o.KnowledgeBase.AllowFeedback = false);
        await using var __ = host;
        var anonymous = host.ClientFor(string.Empty);

        // Public by default: no sign-in needed.
        var page = await PageAsync(anonymous, "/support/kb");
        Assert.False(page.GetProperty("props").GetProperty("feedbackEnabled").GetBoolean());
        var feedback = await anonymous.SendAsync(Visit(HttpMethod.Post, "/support/kb/update-your-card/feedback", new { helpful = true }));
        Assert.Equal(HttpStatusCode.NotFound, feedback.StatusCode);

        await host.SeedAsync(async db =>
        {
            db.Settings.Add(new EscalatedSettings { Key = "knowledge_base_public", Value = "0" });
            await db.SaveChangesAsync();
        });
        var closed = await anonymous.SendAsync(Visit(HttpMethod.Get, "/support/kb"));
        Assert.Equal(HttpStatusCode.Forbidden, closed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.ClientFor(Customer).SendAsync(Visit(HttpMethod.Get, "/support/kb"))).StatusCode);

        var (offHost, _, _) = await StartAsync(o => o.KnowledgeBase.Enabled = false);
        await using var ___ = offHost;
        var off = await offHost.ClientFor(Customer).SendAsync(Visit(HttpMethod.Get, "/support/kb"));
        Assert.Equal(HttpStatusCode.NotFound, off.StatusCode);
    }
}
