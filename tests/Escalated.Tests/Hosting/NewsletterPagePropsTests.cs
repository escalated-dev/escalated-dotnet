using System.Net;
using System.Text.Json;
using Escalated.Models.Newsletter;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The newsletter screens' nested props read as <c>Admin/Newsletters/Show.vue</c>
/// reads them (<c>from_name</c>, <c>summary_total</c>, <c>target_list.name</c>),
/// with the UI on and off. They used to serialize camelCase, and the stat tiles
/// and sender line rendered blank.
/// </summary>
public class NewsletterPagePropsTests
{
    private const string Admin = "admin-1";

    private static async Task<(EscalatedTestHost Host, HttpClient Client, int NewsletterId)> StartAsync(bool ui, string status = "sent")
    {
        var host = await EscalatedTestHost.StartAsync(options =>
        {
            options.EnableNewsletters = true;
            options.Ui.Enabled = ui;
        });
        await host.GrantRoleAsync(Admin, "escalated-admin");

        var id = 0;
        await host.SeedAsync(async db =>
        {
            var list = new NewsletterList { Name = "Customers", Kind = "static" };
            db.NewsletterLists.Add(list);
            await db.SaveChangesAsync();
            var newsletter = new Newsletter
            {
                Subject = "October news",
                FromEmail = "news@example.com",
                FromName = "Example News",
                TargetListId = list.Id,
                Status = status,
                SummaryTotal = 120,
                SummarySent = 118,
            };
            db.Newsletters.Add(newsletter);
            await db.SaveChangesAsync();
            id = newsletter.Id;
        });

        return (host, host.ClientFor(Admin), id);
    }

    private static void AssertNested(JsonElement props)
    {
        var newsletter = Keys(props.GetProperty("newsletter"), "from_name", "from_email", "summary_total", "summary_sent", "target_list");
        Assert.Equal("Example News", newsletter.GetProperty("from_name").GetString());
        Assert.Equal(120, newsletter.GetProperty("summary_total").GetInt32());
        Assert.Equal("Customers", newsletter.GetProperty("target_list").GetProperty("name").GetString());
        Assert.False(newsletter.TryGetProperty("fromName", out _));

        // Top-level names keep the page's own spelling.
        Assert.True(props.TryGetProperty("topClicks", out _));
    }

    [Fact]
    public async Task WithTheUiOff_ShowAnswersComponentAndSnakeCaseProps()
    {
        var (host, client, id) = await StartAsync(ui: false);
        await using var hostScope = host;

        var response = await client.GetAsync($"/admin/newsletters/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Escalated/Admin/Newsletters/Show", json.RootElement.GetProperty("component").GetString());
        AssertNested(json.RootElement.GetProperty("props"));
    }

    [Fact]
    public async Task WithTheUiOn_ShowIsAnInertiaPage()
    {
        var (host, client, id) = await StartAsync(ui: true);
        await using var hostScope = host;

        var page = await PageAsync(client, $"/admin/newsletters/{id}");

        AssertPage(page, "Escalated/Admin/Newsletters/Show");
        AssertNested(page.GetProperty("props"));
        Assert.True(page.GetProperty("props").GetProperty("escalated").GetProperty("features").GetProperty("newsletters").GetBoolean());
    }

    [Fact]
    public async Task WithTheUiOn_AnInertiaFormPostRedirects303()
    {
        var (host, client, id) = await StartAsync(ui: true, status: "draft");
        await using var hostScope = host;

        var response = await client.SendAsync(Visit(HttpMethod.Delete, $"/admin/newsletters/{id}"));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/admin/newsletters", response.Headers.Location!.ToString());
    }
}
