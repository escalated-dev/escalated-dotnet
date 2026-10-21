using System.Net;
using System.Security.Claims;
using Escalated.Authorization;
using Escalated.Data;
using Escalated.Hubs;
using Escalated.Models;
using Escalated.Services;
using Escalated.Tests.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Escalated.Tests.Schema;

/// <summary>Regression tests for the 2026-10-20 audit fixes outside the migrations.</summary>
public class AuditFixTests
{
    [Theory]
    [InlineData("http://localhost/hook")]
    [InlineData("http://api.localhost/hook")]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://10.1.2.3/hook")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::1]/hook")]
    [InlineData("http://metadata.google.internal/")]
    [InlineData("ftp://example.com/hook")]
    [InlineData("not a url")]
    public void UrlGuard_RejectsLocalAndNonHttpTargetsAtSaveTime(string url)
    {
        Assert.False(OutboundUrlGuard.TryValidateUri(url, out _));
    }

    [Fact]
    public void UrlGuard_AcceptsAPublicHttpsUrl()
    {
        Assert.True(OutboundUrlGuard.TryValidateUri("https://hooks.example.com/escalated", out var uri));
        Assert.Equal("hooks.example.com", uri.Host);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.20")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    public async Task UrlGuard_RefusesAHostnameThatResolvesToAPrivateAddress(string resolved)
    {
        var handler = OutboundUrlGuard.CreateHandler((_, _) => Task.FromResult(new[] { IPAddress.Parse(resolved) }));
        using var client = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://hooks.example.test/"));
        Assert.Contains("Refusing to connect", error.ToString());
    }

    [Fact]
    public void UrlGuard_NeverFollowsRedirectsOrUsesTheSystemProxy()
    {
        var handler = OutboundUrlGuard.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
    }

    [Theory]
    [InlineData("Escalated Admin", "escalated-admin")]
    [InlineData("A/B", "a-b")]
    [InlineData("  Tier 2 -- Support  ", "tier-2-support")]
    [InlineData("Équipe Réseau", "equipe-reseau")]
    [InlineData("!!!", "role")]
    public void RoleSlug_IsLowercaseAsciiJoinedByHyphens(string name, string slug)
    {
        Assert.Equal(slug, RoleSlug.From(name));
    }

    [Fact]
    public async Task LoadAttachments_FillsTicketAndReplyAttachments()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var ticket = new Ticket { Reference = "ATT-1", Subject = "Files" };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        var reply = new Reply { TicketId = ticket.Id, Body = "See attached" };
        db.Replies.Add(reply);
        await db.SaveChangesAsync();
        db.Attachments.AddRange(
            new Attachment { AttachableType = "ticket", AttachableId = ticket.Id, Filename = "on-ticket.txt", Path = "a", MimeType = "text/plain", Size = 1 },
            new Attachment { AttachableType = "Reply", AttachableId = reply.Id, Filename = "on-reply.txt", Path = "b", MimeType = "text/plain", Size = 1 });
        await db.SaveChangesAsync();

        var loaded = await db.Tickets.Include(t => t.Replies).SingleAsync(t => t.Id == ticket.Id);
        await db.LoadAttachmentsAsync(loaded);

        Assert.Equal("on-ticket.txt", Assert.Single(loaded.Attachments).Filename);
        Assert.Equal("on-reply.txt", Assert.Single(loaded.Replies.Single().Attachments).Filename);
    }

    [Fact]
    public async Task Hub_WithoutEscalatedServices_RefusesEveryJoin()
    {
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns("anonymous");
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity()));
        var hub = new EscalatedHub { Context = context.Object, Groups = new Mock<IGroupManager>().Object };

        await Assert.ThrowsAsync<HubException>(() => hub.JoinTicketList());
        await Assert.ThrowsAsync<HubException>(() => hub.JoinTicket(1));
    }

    [Fact]
    public async Task Hub_InDefaultMode_RefusesAnonymousStaffRoomsAndTicketsWithoutAToken()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var ticket = new Ticket { Reference = "HUB-1", Subject = "Private", GuestToken = "secret-guest-token" };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns("anonymous");
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity()));
        context.SetupGet(c => c.Features).Returns(new Microsoft.AspNetCore.Http.Features.FeatureCollection());
        var groups = new Mock<IGroupManager>();
        var hub = new EscalatedHub(
            scope.ServiceProvider.GetRequiredService<TicketAccess>(), db,
            scope.ServiceProvider.GetRequiredService<TicketRealtimeAudience>(),
            scope.ServiceProvider.GetRequiredService<TicketBroadcastService>())
        { Context = context.Object, Groups = groups.Object };

        await Assert.ThrowsAsync<HubException>(() => hub.JoinTicketList());
        await Assert.ThrowsAsync<HubException>(() => hub.JoinTicket(ticket.Id));
        groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
