using System.Net;
using System.Text.Json.Nodes;
using Escalated.Events;
using Escalated.Models;
using Escalated.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Plugins;

/// <summary>The plugin contract's port test checklist, "Hooks and context", through the HTTP pipeline.</summary>
public class PluginHooksAndContextTests
{
    private static async Task<HttpResponseMessage> CreateTicketAsync(PluginHost plugins, string subject)
    {
        var response = await plugins.CustomerClient.SendAsync(Visit(HttpMethod.Post, "/support", new { subject, description = "Please help." }));
        Assert.True(response.StatusCode is HttpStatusCode.SeeOther or HttpStatusCode.Found, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        await plugins.IdleAsync();
        return response;
    }

    [Fact]
    public async Task ATicketCreatedOverHttpReachesActivePluginsWithTheFlatTicketAndTicket()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug);

        await CreateTicketAsync(plugins, "Printer on fire");

        var payload = plugins.Recorder.Actions(CrmPlugin.Name, "ticket.created").Single()!;
        Assert.Equal("Printer on fire", payload["subject"]!.GetValue<string>());
        Assert.Equal("Printer on fire", payload["title"]!.GetValue<string>());
        foreach (var key in new[] { "id", "reference", "status", "priority", "department_id", "assigned_to", "requester_id", "metadata", "created_at", "updated_at" })
        {
            Assert.True(payload.AsObject().ContainsKey(key), $"missing {key}");
        }

        Assert.Equal(PluginHost.Customer, payload["requester_id"]!.GetValue<string>());
        Assert.Equal("open", payload["status"]!.GetValue<string>());
        Assert.StartsWith("ESC-", payload["reference"]!.GetValue<string>());
        Assert.Equal(payload["id"]!.GetValue<int>(), payload["ticket"]!["id"]!.GetValue<int>());
        Assert.Equal("Printer on fire", payload["ticket"]!["title"]!.GetValue<string>());

        // AuditPlugin is inactive and never called.
        Assert.Empty(plugins.Recorder.CallsOf(AuditPlugin.Name));
    }

    [Fact]
    public async Task AReplyDeliversReplyCreatedAndNoLegacyNameIsEverDelivered()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug);
        await CreateTicketAsync(plugins, "Needs a reply");
        var reference = await plugins.Host.QueryAsync(db => db.Tickets.Select(t => t.Reference).SingleAsync());

        var reply = await plugins.CustomerClient.SendAsync(Visit(HttpMethod.Post, $"/support/{reference}/reply", new { body = "Any news?" }));
        Assert.Equal(HttpStatusCode.SeeOther, reply.StatusCode);
        await plugins.IdleAsync();

        var payload = plugins.Recorder.Actions(CrmPlugin.Name, "reply.created").Single()!;
        Assert.Equal("Any news?", payload["body"]!.GetValue<string>());
        Assert.Equal("Any news?", payload["reply"]!["body"]!.GetValue<string>());
        Assert.False(payload["is_internal"]!.GetValue<bool>());
        Assert.Equal(reference, payload["ticket"]!["reference"]!.GetValue<string>());
        Assert.Equal(payload["ticket_id"]!.GetValue<int>(), payload["ticket"]!["id"]!.GetValue<int>());

        var hooks = plugins.Recorder.CallsOf(CrmPlugin.Name).Where(c => c.StartsWith("action:", StringComparison.Ordinal)).Select(c => c["action:".Length..]).ToList();
        Assert.All(hooks, hook => Assert.True(EventNames.Canonical.Contains(hook) || PluginRegistry.LifecycleActions.Contains(hook), hook));
        Assert.DoesNotContain(hooks, hook => EventNames.Legacy.ContainsKey(hook));
    }

    [Fact]
    public async Task APluginActionThatThrowsDoesNotFailTheRequest()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(AuditPlugin.Slug);
        await CreateTicketAsync(plugins, "Still works");
        var reference = await plugins.Host.QueryAsync(db => db.Tickets.Select(t => t.Reference).SingleAsync());

        var reply = await plugins.CustomerClient.SendAsync(Visit(HttpMethod.Post, $"/support/{reference}/reply", new { body = "Throw please" }));
        await plugins.IdleAsync();

        Assert.Equal(HttpStatusCode.SeeOther, reply.StatusCode);
        Assert.Single(plugins.Recorder.Actions(AuditPlugin.Name, "reply.created"));
        Assert.Equal(1, await plugins.Host.QueryAsync(db => db.Replies.CountAsync()));
    }

    [Fact]
    public async Task AnActionHandlerWritesThroughCtxAndThoseWritesAreAnnouncedToo()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug);

        await CreateTicketAsync(plugins, "Write back");

        var note = await plugins.Host.QueryAsync(db => db.Replies.SingleAsync());
        Assert.Equal("Seen by CRM", note.Body);
        Assert.True(note.IsInternalNote);
        Assert.Null(note.AuthorId); // outside a request a plugin acts as nobody
        Assert.Single(plugins.Recorder.Actions(CrmPlugin.Name, "note.created"));
    }

    [Fact]
    public async Task FiltersRunInPriorityOrderForActivePluginsAndSkipOneThatThrows()
    {
        await using var plugins = await PluginHost.StartAsync();

        Assert.Equal("x", (await plugins.WithRegistryAsync(r => r.ApplyFilterAsync("demo.value", JsonValue.Create("x"))))!.GetValue<string>());

        await plugins.ActivateAsync(CrmPlugin.Slug);
        Assert.Equal("x|crm", (await plugins.WithRegistryAsync(r => r.ApplyFilterAsync("demo.value", JsonValue.Create("x"))))!.GetValue<string>());

        await plugins.ActivateAsync(AuditPlugin.Slug);
        Assert.Equal("x|audit|crm", (await plugins.WithRegistryAsync(r => r.ApplyFilterAsync("demo.value", JsonValue.Create("x"))))!.GetValue<string>());
        Assert.Equal("y", (await plugins.WithRegistryAsync(r => r.ApplyFilterAsync("demo.throws", JsonValue.Create("y"))))!.GetValue<string>());
    }

    [Fact]
    public async Task CtxConfigSetMerges()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug); // onActivate sets {activated: true}

        await WithContextAsync(plugins, CrmPlugin.Name, ctx => ctx.ConfigSetAsync(new JsonObject { ["api_key"] = "k1" }));
        var config = await plugins.AdminClient.SendAsync(PluginHost.Script(HttpMethod.Get, $"/support/api/plugins/{CrmPlugin.Slug}/ctx"));

        var body = await PluginHost.JsonAsync(config);
        Assert.True(body.GetProperty("activated").GetBoolean());
        Assert.Equal("k1", body.GetProperty("api_key").GetString());
    }

    [Fact]
    public async Task CtxStoreQueryHandlesTheOperatorsAndRejectsSqlInAFieldName()
    {
        await using var plugins = await PluginHost.StartAsync();

        var results = await WithContextAsync(plugins, CrmPlugin.Name, async ctx =>
        {
            await ctx.StoreInsertAsync("deals", new JsonObject { ["key"] = "a", ["amount"] = 10, ["stage"] = "won", ["tags"] = new JsonArray("vip"), ["owner"] = new JsonObject { ["id"] = 1 } });
            await ctx.StoreInsertAsync("deals", new JsonObject { ["key"] = "b", ["amount"] = 25, ["stage"] = "open", ["owner"] = new JsonObject { ["id"] = 2 } });
            await ctx.StoreInsertAsync("deals", new JsonObject { ["key"] = "c", ["amount"] = 40, ["stage"] = "lost" });
            await ctx.StoreSetAsync("other", "x", new JsonObject { ["amount"] = 99 });

            async Task<List<string>> Keys(JsonObject filter, JsonObject? options = null) =>
                (await ctx.StoreQueryAsync("deals", filter, options)).Select(d => d!["key"]!.GetValue<string>()).ToList();

            return new Dictionary<string, List<string>>
            {
                ["eq"] = await Keys(new JsonObject { ["stage"] = "won" }),
                ["array"] = await Keys(new JsonObject { ["tags"] = "vip" }),
                ["gt"] = await Keys(new JsonObject { ["amount"] = new JsonObject { ["$gt"] = 10 } }),
                ["gte_lte"] = await Keys(new JsonObject { ["amount"] = new JsonObject { ["$gte"] = 10, ["$lte"] = 25 } }),
                ["lt"] = await Keys(new JsonObject { ["amount"] = new JsonObject { ["$lt"] = 25 } }),
                ["ne"] = await Keys(new JsonObject { ["stage"] = new JsonObject { ["$ne"] = "open" } }),
                ["in"] = await Keys(new JsonObject { ["stage"] = new JsonObject { ["$in"] = new JsonArray("open", "lost") } }),
                ["nin"] = await Keys(new JsonObject { ["stage"] = new JsonObject { ["$nin"] = new JsonArray("open", "lost") } }),
                ["dotted"] = await Keys(new JsonObject { ["owner.id"] = 2 }),
                ["missing"] = await Keys(new JsonObject { ["owner"] = null }),
                ["sorted"] = await Keys(new JsonObject(), new JsonObject { ["orderBy"] = "amount", ["order"] = "desc", ["limit"] = 2 }),
            };
        });

        Assert.Equal(new[] { "a" }, results["eq"]);
        Assert.Equal(new[] { "a" }, results["array"]);
        Assert.Equal(new[] { "b", "c" }, results["gt"]);
        Assert.Equal(new[] { "a", "b" }, results["gte_lte"]);
        Assert.Equal(new[] { "a" }, results["lt"]);
        Assert.Equal(new[] { "a", "c" }, results["ne"]);
        Assert.Equal(new[] { "b", "c" }, results["in"]);
        Assert.Equal(new[] { "a" }, results["nin"]);
        Assert.Equal(new[] { "b" }, results["dotted"]);
        Assert.Equal(new[] { "c" }, results["missing"]);
        Assert.Equal(new[] { "c", "b" }, results["sorted"]);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => WithContextAsync(plugins, CrmPlugin.Name, ctx =>
            ctx.StoreQueryAsync("deals", new JsonObject { ["amount) OR 1=1 --"] = 1 })));
        Assert.Contains("Invalid store field name", error.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => WithContextAsync(plugins, CrmPlugin.Name, ctx =>
            ctx.StoreQueryAsync("deals", new JsonObject(), new JsonObject { ["orderBy"] = "amount; DROP TABLE x" })));
        await Assert.ThrowsAsync<ArgumentException>(() => WithContextAsync(plugins, CrmPlugin.Name, ctx =>
            ctx.StoreQueryAsync("deals", new JsonObject { ["amount"] = new JsonObject { ["$where"] = "1" } })));
    }

    [Fact]
    public async Task OnePluginCannotReadAnothersRowsThroughCtx()
    {
        await using var plugins = await PluginHost.StartAsync();
        await WithContextAsync(plugins, CrmPlugin.Name, async ctx =>
        {
            await ctx.StoreSetAsync("notes", "k", JsonValue.Create("crm-only"));
            await ctx.ConfigSetAsync(new JsonObject { ["secret"] = "s" });
            return true;
        });

        var (note, all, config) = await WithContextAsync(plugins, AuditPlugin.Name, async ctx =>
            (await ctx.StoreGetAsync("notes", "k"), await ctx.StoreQueryAsync("notes"), await ctx.ConfigAllAsync()));

        Assert.Null(note);
        Assert.Empty(all);
        Assert.Empty(config);
    }

    [Fact]
    public async Task CtxTicketsAndRepliesGoThroughTheTicketServiceWithAllowListedKeys()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug);
        var actor = new PluginActor(PluginHost.Agent, null);

        var created = await WithContextAsync(plugins, CrmPlugin.Name, ctx => ctx.CallAsync("tickets.create", new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["title"] = "From a plugin",
                ["body"] = "Created by ctx.",
                ["priority"] = "high",
                ["requester_email"] = "pat@example.com",
                ["requester_name"] = "Pat",
                ["metadata"] = new JsonObject { ["source"] = "crm" },
                ["status"] = "in_progress",
            },
        }), actor);
        await plugins.IdleAsync();

        Assert.Equal("From a plugin", created!["subject"]!.GetValue<string>());
        Assert.Equal("high", created["priority"]!.GetValue<string>());
        Assert.Equal("in_progress", created["status"]!.GetValue<string>());
        Assert.Equal("crm", created["metadata"]!["source"]!.GetValue<string>());
        Assert.NotNull(created["contact_id"]);
        Assert.Single(plugins.Recorder.Actions(CrmPlugin.Name, "ticket.created"));
        Assert.Single(plugins.Recorder.Actions(CrmPlugin.Name, "ticket.status_changed"));
        var ticketId = created["id"]!.GetValue<int>();

        var found = await WithContextAsync(plugins, CrmPlugin.Name, ctx => ctx.CallAsync("tickets.query", new JsonObject
        {
            ["filter"] = new JsonObject { ["priority"] = "high", ["status"] = new JsonArray("in_progress", "open") },
        }));
        Assert.Equal(ticketId, found!.AsArray().Single()!["id"]!.GetValue<int>());

        var badKey = await Assert.ThrowsAsync<ArgumentException>(() => WithContextAsync(plugins, CrmPlugin.Name, ctx =>
            ctx.CallAsync("tickets.query", new JsonObject { ["filter"] = new JsonObject { ["Subject; DROP TABLE escalated_tickets"] = "x" } })));
        Assert.Contains("cannot filter", badKey.Message);

        var updated = await WithContextAsync(plugins, CrmPlugin.Name, ctx => ctx.CallAsync("tickets.update", new JsonObject
        {
            ["id"] = ticketId,
            ["data"] = new JsonObject { ["subject"] = "Renamed", ["priority"] = "low", ["requester_id"] = "someone-else" },
        }), actor);
        Assert.Equal("Renamed", updated!["subject"]!.GetValue<string>());
        Assert.Equal("low", updated["priority"]!.GetValue<string>());
        Assert.Null(updated["requester_id"]);

        var reply = await WithContextAsync(plugins, CrmPlugin.Name, ctx => ctx.CallAsync("replies.create", new JsonObject
        {
            ["data"] = new JsonObject { ["ticket_id"] = ticketId, ["body"] = "Noted.", ["is_internal"] = true, ["author_id"] = "forged" },
        }), actor);
        Assert.True(reply!["is_internal"]!.GetValue<bool>());
        Assert.Equal(PluginHost.Agent, reply["user_id"]!.GetValue<string>());

        var replies = await WithContextAsync(plugins, CrmPlugin.Name, ctx => ctx.CallAsync("replies.query", new JsonObject
        {
            ["filter"] = new JsonObject { ["ticket_id"] = ticketId, ["is_internal"] = true },
        }));
        Assert.Single(replies!.AsArray());
        await Assert.ThrowsAsync<ArgumentException>(() => WithContextAsync(plugins, CrmPlugin.Name, ctx =>
            ctx.CallAsync("replies.query", new JsonObject { ["filter"] = new JsonObject { ["body"] = "x" } })));

        var contact = await WithContextAsync(plugins, CrmPlugin.Name, ctx => ctx.CallAsync("contacts.findByEmail", new JsonObject { ["email"] = "PAT@example.com" }));
        Assert.Equal("Pat", contact!["name"]!.GetValue<string>());
        var tag = await WithContextAsync(plugins, CrmPlugin.Name, ctx => ctx.CallAsync("tags.create", new JsonObject { ["data"] = new JsonObject { ["name"] = "VIP Client" } }));
        Assert.Equal("vip-client", tag!["slug"]!.GetValue<string>());
        var me = await WithContextAsync(plugins, CrmPlugin.Name, ctx => ctx.CurrentUserAsync(), actor);
        Assert.Equal("Avery Agent", me!["name"]!.GetValue<string>());
        var agents = await WithContextAsync(plugins, CrmPlugin.Name, ctx => ctx.CallAsync("agents.all"));
        Assert.Contains(agents!.AsArray(), a => a!["id"]!.GetValue<string>() == PluginHost.Agent);
    }

    [Fact]
    public async Task EmittedActionsReachActivePluginsButNeverImpersonateTheHost()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(AuditPlugin.Slug);

        await WithContextAsync(plugins, CrmPlugin.Name, async ctx =>
        {
            await ctx.EmitAsync("audit.ping", new JsonObject { ["n"] = 1 });
            await ctx.EmitAsync("ticket.created", new JsonObject { ["id"] = 999 });
            await ctx.EmitAsync("plugin.activated", new JsonObject());
            await ctx.EmitAsync("ticket.replied", new JsonObject());
            return true;
        });
        await plugins.IdleAsync();

        Assert.Equal(1, plugins.Recorder.Actions(AuditPlugin.Name, "audit.ping").Single()!["n"]!.GetValue<int>());
        Assert.Empty(plugins.Recorder.Actions(AuditPlugin.Name, "ticket.created"));
        Assert.Single(plugins.Recorder.Actions(AuditPlugin.Name, "plugin.activated")); // its own activation only
    }

    private static async Task<T> WithContextAsync<T>(PluginHost plugins, string plugin, Func<PluginContext, Task<T>> work, PluginActor? actor = null)
    {
        using var scope = plugins.Host.Services.CreateScope();
        var ctx = new PluginContext(scope.ServiceProvider.GetRequiredService<PluginContextHandler>(), plugin, actor ?? PluginActor.None);
        return await work(ctx);
    }

    private static Task WithContextAsync(PluginHost plugins, string plugin, Func<PluginContext, Task> work) =>
        WithContextAsync(plugins, plugin, async ctx =>
        {
            await work(ctx);
            return true;
        });
}
