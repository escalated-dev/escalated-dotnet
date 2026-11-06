using System.Net;
using Escalated.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Embed.EmbedTestSupport;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Embed;

/// <summary>
/// A server-rendered host (ADR 2026-11-05-embeddable-admin, host type B): the Razor view
/// <c>Views/Shared/SupportAdmin.cshtml</c> (the template docs/embedding-admin.md shows) is the
/// root view of embedded admin pages, and of nothing else.
/// </summary>
public class EmbeddedAdminRootViewTests
{
    private static Task<Hosting.EscalatedTestHost> StartAsync() => EmbedTestSupport.StartAsync(
        o => o.EmbedAdmin(prefix: Prefix, rootView: "~/Views/Shared/SupportAdmin.cshtml"),
        services: s => s.AddControllersWithViews().AddApplicationPart(typeof(EmbeddedAdminRootViewTests).Assembly));

    [Fact]
    public async Task RendersEmbeddedAdminPagesInTheHostTemplate()
    {
        await using var host = await StartAsync();

        var response = await host.ClientFor(Admin).SendAsync(Browse("/backoffice/helpdesk/tags"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("<header class=\"topbar\">Back office</header>", html);
        Assert.Contains("<title>Support · Back office</title>", html);
        Assert.Contains("<li><a href=\"/backoffice/helpdesk/tags\" class=\"active\" aria-current=\"page\">Tags</a></li>", html);
        Assert.Contains("<li><a href=\"/backoffice/helpdesk/reports\">Reports</a></li>", html);
        Assert.Contains("<script data-page=\"app\" type=\"application/json\">", html);
        Assert.Contains("Escalated/Admin/Tags/Index", html);
        Assert.Contains("\"escalated.admin.tags.index\":\"/backoffice/helpdesk/tags\"", html);
        Assert.Contains("<main><script data-page=", html);
    }

    [Fact]
    public async Task InertiaVisitsStillGetThePageObject()
    {
        await using var host = await StartAsync();

        var page = await PageAsync(host.ClientFor(Admin), "/backoffice/helpdesk/tags");

        Assert.Equal("Escalated/Admin/Tags/Index", page.GetProperty("component").GetString());
    }

    [Fact]
    public async Task OtherPagesKeepTheNormalRootView()
    {
        await using var host = await StartAsync();
        await host.GrantRoleAsync(Admin, "escalated-agent");

        var response = await host.ClientFor(Admin).SendAsync(Browse("/support/agent"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("topbar", html);
        Assert.StartsWith("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">", html);
    }
}
