using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The admin operations pages (automations, business hours, skills, agent
/// capacity, audit log) against escalated-laravel's <c>AutomationController</c>,
/// <c>BusinessHoursController</c>, <c>SkillController</c>,
/// <c>CapacityController</c> and <c>AuditLogController</c>, plus the JSON admin
/// API on the shared paths staying as it was.
/// </summary>
public class AdminOperationsPagesTests
{
    private const string Admin = "1";
    private const string Agent = "2";
    private const string Customer = "3";

    private sealed class Directory : IUserDirectory
    {
        private static readonly List<UserDirectoryEntry> Users = new()
        {
            new(Admin, "Ada Admin", "ada@example.com"),
            new(Agent, "Avery Agent", "avery@example.com"),
            new(Customer, "Casey Customer", "casey@example.com"),
        };

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Users, Users.Count, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Users.FirstOrDefault(u => u.Id == id));
    }

    private static async Task<(EscalatedTestHost Host, HttpClient Client)> StartAsync()
    {
        var host = await EscalatedTestHost.StartAsync(
            options => options.Ui.Enabled = true,
            services => services.AddSingleton<IUserDirectory, Directory>());
        await host.GrantRoleAsync(Admin, "escalated-admin");
        await host.GrantRoleAsync(Agent, "escalated-agent");
        return (host, host.ClientFor(Admin));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object? body = null, string? expectLocation = null)
    {
        var response = await client.SendAsync(Visit(method, url, body ?? new { }));
        Assert.True(response.StatusCode == HttpStatusCode.SeeOther, $"{method} {url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        if (expectLocation is not null) Assert.Equal(expectLocation, response.Headers.Location!.ToString());
        return response;
    }

    private static async Task<JsonElement> ErrorsAfterAsync(HttpClient client, HttpResponseMessage response, string page)
    {
        Assert.Equal(page, response.Headers.Location!.ToString());
        var next = await PageAsync(client, page, FlashCookie(response));
        return next.GetProperty("props").GetProperty("errors");
    }

    private static async Task<string?> SuccessAfterAsync(HttpClient client, HttpResponseMessage response)
    {
        var next = await PageAsync(client, response.Headers.Location!.ToString(), FlashCookie(response));
        return next.GetProperty("props").GetProperty("flash").GetProperty("success").GetString();
    }

    private static List<string> Names(JsonElement errors) =>
        errors.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    [Fact]
    public async Task Pages_AreForAdminsOnly()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;
        var agent = host.ClientFor(Agent);

        foreach (var path in new[]
                 {
                     "/support/admin/automations", "/support/admin/automations/create", "/support/admin/business-hours",
                     "/support/admin/business-hours/create", "/support/admin/skills", "/support/admin/skills/create",
                     "/support/admin/capacity", "/support/admin/audit-log",
                 })
        {
            var response = await agent.SendAsync(Visit(HttpMethod.Get, path));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    // --- Automations ---

    [Fact]
    public async Task Automations_ListInPositionOrderAsTheModelSerializes()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        int stale = 0, nudge = 0;
        await host.SeedAsync(async db =>
        {
            var a = new Automation
            {
                Name = "Close stale",
                Conditions = """[{"field":"hours_since_updated","operator":">","value":72}]""",
                Actions = """[{"type":"change_status","value":"closed"}]""",
                Active = false,
                Position = 2,
            };
            var b = new Automation { Name = "Nudge", Conditions = """[{"field":"status","value":"open"}]""", Actions = """[{"type":"add_note","value":"Ping"}]""", Position = 1 };
            db.AddRange(a, b);
            await db.SaveChangesAsync();
            stale = a.Id;
            nudge = b.Id;
        });

        var page = await PageAsync(client, "/support/admin/automations");
        AssertPage(page, "Escalated/Admin/Automations/Index");
        var rows = page.GetProperty("props").GetProperty("automations").EnumerateArray().ToList();
        Assert.Equal(new[] { nudge, stale }, rows.Select(r => r.GetProperty("id").GetInt32()));
        var row = Keys(rows[1], "id", "name", "description", "conditions", "actions", "active", "position", "last_run_at", "created_at", "updated_at");
        Assert.False(row.GetProperty("active").GetBoolean());
        Assert.Equal(72, row.GetProperty("conditions")[0].GetProperty("value").GetInt32());
        Assert.Equal("change_status", row.GetProperty("actions")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Automations_CreateEditToggleAndDeleteThroughTheForm()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var form = await PageAsync(client, "/support/admin/automations/create");
        AssertPage(form, "Escalated/Admin/Automations/Form");
        Assert.Equal(JsonValueKind.Null, form.GetProperty("props").GetProperty("automation").ValueKind);

        var created = await SendAsync(client, HttpMethod.Post, "/support/admin/automations", new
        {
            name = "Escalate idle",
            conditions = new[] { new { field = "hours_since_created", @operator = ">=", value = 48 } },
            actions = new[] { new { type = "change_priority", value = "high" } },
            active = true,
        }, "/support/admin/automations");
        Assert.Equal("Automation created.", await SuccessAfterAsync(client, created));

        var row = await host.QueryAsync(db => db.Automations.SingleAsync(a => a.Name == "Escalate idle"));
        Assert.Contains("hours_since_created", row.Conditions);
        Assert.True(row.Active);

        var edit = await PageAsync(client, $"/support/admin/automations/{row.Id}/edit");
        AssertPage(edit, "Escalated/Admin/Automations/Form");
        Assert.Equal("Escalate idle", edit.GetProperty("props").GetProperty("automation").GetProperty("name").GetString());

        // The Index page's pause toggle sends the whole automation back.
        var toggled = await SendAsync(client, HttpMethod.Put, $"/support/admin/automations/{row.Id}", new
        {
            name = "Escalate idle",
            conditions = new[] { new { field = "hours_since_created", @operator = ">=", value = 48 } },
            actions = new[] { new { type = "change_priority", value = "high" } },
            active = false,
        }, "/support/admin/automations");
        Assert.Equal("Automation updated.", await SuccessAfterAsync(client, toggled));
        Assert.False((await host.QueryAsync(db => db.Automations.SingleAsync(a => a.Id == row.Id))).Active);

        var deleted = await SendAsync(client, HttpMethod.Delete, $"/support/admin/automations/{row.Id}", null, "/support/admin/automations");
        Assert.Equal("Automation deleted.", await SuccessAfterAsync(client, deleted));
        Assert.False(await host.QueryAsync(db => db.Automations.AnyAsync(a => a.Id == row.Id)));
    }

    [Fact]
    public async Task Automations_IncompleteFormComesBackWithFieldErrors()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var response = await SendAsync(client, HttpMethod.Post, "/support/admin/automations",
            new { name = "", conditions = Array.Empty<object>(), actions = Array.Empty<object>() });
        var errors = await ErrorsAfterAsync(client, response, "/support/admin/automations/create");
        Assert.Equal(new[] { "actions", "conditions", "name" }, Names(errors));
    }

    [Fact]
    public async Task Automations_JsonApiIsUnchanged()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        await host.SeedAsync(async db =>
        {
            db.Automations.Add(new Automation { Name = "Nudge" });
            await db.SaveChangesAsync();
        });

        var list = await client.GetFromJsonAsync<JsonElement>("/support/admin/automations");
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.Equal("Nudge", list[0].GetProperty("name").GetString());
    }

    // --- Business hours ---

    [Fact]
    public async Task BusinessHours_ListSchedulesWithTheirHolidays()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var id = 0;
        await host.SeedAsync(async db =>
        {
            var schedule = new BusinessSchedule
            {
                Name = "Office",
                Timezone = "America/Toronto",
                IsDefault = true,
                Schedule = """{"monday":{"start":"09:00","end":"17:00"}}""",
            };
            schedule.Holidays.Add(new Holiday { Name = "Canada Day", Date = new DateTime(2026, 7, 1), Recurring = true });
            db.BusinessSchedules.Add(schedule);
            await db.SaveChangesAsync();
            id = schedule.Id;
        });

        var page = await PageAsync(client, "/support/admin/business-hours");
        AssertPage(page, "Escalated/Admin/BusinessHours/Index");
        var row = Keys(page.GetProperty("props").GetProperty("schedules")[0], "id", "name", "timezone", "is_default", "schedule", "holidays", "created_at", "updated_at");
        Assert.Equal(id, row.GetProperty("id").GetInt32());
        Assert.True(row.GetProperty("is_default").GetBoolean());
        Assert.Equal("09:00", row.GetProperty("schedule").GetProperty("monday").GetProperty("start").GetString());
        var holiday = Keys(row.GetProperty("holidays")[0], "id", "schedule_id", "name", "date", "recurring", "created_at");
        Assert.Equal("2026-07-01", holiday.GetProperty("date").GetString());
        Assert.True(holiday.GetProperty("recurring").GetBoolean());
    }

    [Fact]
    public async Task BusinessHours_CreateAndEditWithHolidaysKeepingOneDefault()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        await host.SeedAsync(async db =>
        {
            db.BusinessSchedules.Add(new BusinessSchedule { Name = "Old default", IsDefault = true, Schedule = "{}" });
            await db.SaveChangesAsync();
        });

        var form = await PageAsync(client, "/support/admin/business-hours/create");
        AssertPage(form, "Escalated/Admin/BusinessHours/Form");
        var props = form.GetProperty("props");
        Assert.Equal(JsonValueKind.Null, props.GetProperty("schedule").ValueKind);
        var zones = props.GetProperty("timezones").EnumerateArray().Select(z => z.GetString()).ToList();
        Assert.Contains("UTC", zones);
        Assert.Contains("Europe/London", zones);

        var week = new Dictionary<string, object>
        {
            ["monday"] = new { start = "08:00", end = "16:00" },
            ["tuesday"] = new { start = "08:00", end = "16:00" },
            ["saturday"] = new { start = "", end = "" },
        };
        var created = await SendAsync(client, HttpMethod.Post, "/support/admin/business-hours", new
        {
            name = "Support desk",
            timezone = "Europe/London",
            is_default = true,
            schedule = week,
            holidays = new[] { new { name = "Boxing Day", date = "2026-12-26", recurring = true } },
        }, "/support/admin/business-hours");
        Assert.Equal("Business schedule created.", await SuccessAfterAsync(client, created));

        var row = await host.QueryAsync(db => db.BusinessSchedules.Include(s => s.Holidays).SingleAsync(s => s.Name == "Support desk"));
        Assert.True(row.IsDefault);
        Assert.Equal("Europe/London", row.Timezone);
        Assert.Contains("\"monday\"", row.Schedule);
        Assert.Equal(new DateTime(2026, 12, 26), Assert.Single(row.Holidays).Date.Date);
        Assert.Equal(1, await host.QueryAsync(db => db.BusinessSchedules.CountAsync(s => s.IsDefault)));

        var edit = await PageAsync(client, $"/support/admin/business-hours/{row.Id}/edit");
        AssertPage(edit, "Escalated/Admin/BusinessHours/Form");
        Assert.Equal("Support desk", edit.GetProperty("props").GetProperty("schedule").GetProperty("name").GetString());

        var updated = await SendAsync(client, HttpMethod.Put, $"/support/admin/business-hours/{row.Id}", new
        {
            name = "Support desk (UK)",
            timezone = "Europe/London",
            is_default = true,
            schedule = week,
            holidays = new[] { new { name = "New Year", date = "2027-01-01", recurring = false } },
        }, "/support/admin/business-hours");
        Assert.Equal("Business schedule updated.", await SuccessAfterAsync(client, updated));
        var after = await host.QueryAsync(db => db.BusinessSchedules.Include(s => s.Holidays).SingleAsync(s => s.Id == row.Id));
        Assert.Equal("Support desk (UK)", after.Name);
        Assert.Equal("New Year", Assert.Single(after.Holidays).Name);

        var deleted = await SendAsync(client, HttpMethod.Delete, $"/support/admin/business-hours/{row.Id}", null, "/support/admin/business-hours");
        Assert.Equal("Business schedule deleted.", await SuccessAfterAsync(client, deleted));
        Assert.False(await host.QueryAsync(db => db.BusinessSchedules.AnyAsync(s => s.Id == row.Id)));
    }

    [Fact]
    public async Task BusinessHours_RefusesAnUnknownTimezoneAndAHolidayWithoutADate()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var response = await SendAsync(client, HttpMethod.Post, "/support/admin/business-hours", new
        {
            name = "Bad",
            timezone = "Mars/Olympus",
            schedule = new { },
            holidays = new[] { new { name = "X", date = "" } },
        });
        var errors = await ErrorsAfterAsync(client, response, "/support/admin/business-hours/create");
        Assert.Equal(new[] { "holidays.0.date", "timezone" }, Names(errors));
    }

    // --- Skills ---

    [Fact]
    public async Task Skills_CreateListEditAndDeleteThroughTheForm()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        int tagId = 0, departmentId = 0;
        await host.SeedAsync(async db =>
        {
            var tag = new Tag { Name = "billing", Slug = "billing", Color = "#ff0000" };
            var department = new Department { Name = "Sales", Slug = "sales", IsActive = true };
            db.AddRange(tag, department);
            await db.SaveChangesAsync();
            tagId = tag.Id;
            departmentId = department.Id;
        });

        var form = await PageAsync(client, "/support/admin/skills/create");
        AssertPage(form, "Escalated/Admin/Skills/Form");
        var props = form.GetProperty("props");
        Assert.Equal(JsonValueKind.Null, props.GetProperty("skill").ValueKind);
        var agents = props.GetProperty("availableAgents").EnumerateArray().ToList();
        Assert.Equal(new[] { Admin, Agent }, agents.Select(a => a.GetProperty("id").GetString()));
        Keys(agents[1], "id", "name", "email");
        Assert.Equal("Avery Agent", agents[1].GetProperty("name").GetString());
        var tag = Keys(props.GetProperty("availableTags")[0], "id", "name", "color");
        Assert.Equal("#ff0000", tag.GetProperty("color").GetString());
        Keys(props.GetProperty("availableDepartments")[0], "id", "name");

        var created = await SendAsync(client, HttpMethod.Post, "/support/admin/skills", new
        {
            name = "Billing disputes",
            routing_tag_ids = new[] { tagId },
            routing_department_ids = new[] { departmentId },
            agents = new[] { new { user_id = Agent, proficiency = 4 } },
        }, "/support/admin/skills");
        Assert.Equal("Skill created.", await SuccessAfterAsync(client, created));

        var index = await PageAsync(client, "/support/admin/skills");
        AssertPage(index, "Escalated/Admin/Skills/Index");
        var row = Keys(index.GetProperty("props").GetProperty("skills")[0], "id", "name", "slug", "agents_count", "routing_tags_count", "routing_departments_count", "updated_at");
        Assert.Equal("billing-disputes", row.GetProperty("slug").GetString());
        Assert.Equal(1, row.GetProperty("agents_count").GetInt32());
        Assert.Equal(1, row.GetProperty("routing_tags_count").GetInt32());
        Assert.Equal(1, row.GetProperty("routing_departments_count").GetInt32());
        var id = row.GetProperty("id").GetInt32();

        var edit = await PageAsync(client, $"/support/admin/skills/{id}/edit");
        AssertPage(edit, "Escalated/Admin/Skills/Form");
        var skill = Keys(edit.GetProperty("props").GetProperty("skill"), "id", "name", "slug", "routing_tag_ids", "routing_department_ids", "agents");
        Assert.Equal(tagId, skill.GetProperty("routing_tag_ids")[0].GetInt32());
        Assert.Equal(departmentId, skill.GetProperty("routing_department_ids")[0].GetInt32());
        var assigned = Keys(skill.GetProperty("agents")[0], "user_id", "proficiency");
        Assert.Equal(Agent, assigned.GetProperty("user_id").GetString());
        Assert.Equal(4, assigned.GetProperty("proficiency").GetInt32());

        var updated = await SendAsync(client, HttpMethod.Put, $"/support/admin/skills/{id}", new
        {
            name = "Billing",
            routing_tag_ids = Array.Empty<int>(),
            routing_department_ids = new[] { departmentId },
            agents = Array.Empty<object>(),
        }, "/support/admin/skills");
        Assert.Equal("Skill updated.", await SuccessAfterAsync(client, updated));
        Assert.Equal(0, await host.QueryAsync(db => db.AgentSkills.CountAsync(a => a.SkillId == id)));
        Assert.Equal("Billing", (await host.QueryAsync(db => db.Skills.SingleAsync(s => s.Id == id))).Name);

        var deleted = await SendAsync(client, HttpMethod.Delete, $"/support/admin/skills/{id}", null, "/support/admin/skills");
        Assert.Equal("Skill deleted.", await SuccessAfterAsync(client, deleted));
        Assert.False(await host.QueryAsync(db => db.Skills.AnyAsync(s => s.Id == id)));
    }

    [Fact]
    public async Task Skills_RefuseADuplicateNameANonAgentAndAProficiencyOutOfRange()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        await host.SeedAsync(async db =>
        {
            db.Skills.Add(new Skill { Name = "Taken", Slug = "taken" });
            await db.SaveChangesAsync();
        });

        var response = await SendAsync(client, HttpMethod.Post, "/support/admin/skills", new
        {
            name = "Taken",
            routing_tag_ids = new[] { 999 },
            agents = new object[] { new { user_id = Customer, proficiency = 3 }, new { user_id = Agent, proficiency = 9 } },
        });
        var errors = await ErrorsAfterAsync(client, response, "/support/admin/skills/create");
        Assert.Equal(new[] { "agents.0.user_id", "agents.1.proficiency", "name", "routing_tag_ids.0" }, Names(errors));
    }

    [Fact]
    public async Task Skills_JsonApiIsUnchanged()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var response = await client.GetAsync("/support/admin/skills/create");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("availableAgents", out _));
        Assert.False(body.TryGetProperty("component", out _));
    }

    // --- Capacity ---

    [Fact]
    public async Task Capacity_ListsEachAgentAndUpdatesTheLimit()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var id = 0;
        await host.SeedAsync(async db =>
        {
            var capacity = new AgentCapacity { UserId = Agent, Channel = "chat", MaxConcurrent = 8, CurrentCount = 2 };
            db.AgentCapacities.Add(capacity);
            await db.SaveChangesAsync();
            id = capacity.Id;
        });

        var page = await PageAsync(client, "/support/admin/capacity");
        AssertPage(page, "Escalated/Admin/Capacity/Index");
        var row = Keys(page.GetProperty("props").GetProperty("capacities")[0], "id", "user_id", "agent_name", "channel", "max_concurrent", "current_count", "load_percentage");
        Assert.Equal(id, row.GetProperty("id").GetInt32());
        Assert.Equal(Agent, row.GetProperty("user_id").GetString());
        Assert.Equal("Avery Agent", row.GetProperty("agent_name").GetString());
        Assert.Equal("chat", row.GetProperty("channel").GetString());
        Assert.Equal(25.0, row.GetProperty("load_percentage").GetDouble());

        var response = await client.SendAsync(Visit(HttpMethod.Put, $"/support/admin/capacity/{id}", new { max_concurrent = 12 }, "http://localhost/support/admin/capacity"));
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("Capacity updated.", await SuccessAfterAsync(client, response));
        Assert.Equal(12, (await host.QueryAsync(db => db.AgentCapacities.SingleAsync(c => c.Id == id))).MaxConcurrent);

        var invalid = await client.SendAsync(Visit(HttpMethod.Put, $"/support/admin/capacity/{id}", new { max_concurrent = 0 }, "http://localhost/support/admin/capacity"));
        var errors = await ErrorsAfterAsync(client, invalid, "/support/admin/capacity");
        Assert.Equal(new[] { "max_concurrent" }, Names(errors));
    }

    // --- Audit log ---

    [Fact]
    public async Task AuditLog_PagesEntriesNewestFirstWithFilters()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        int older = 0, newer = 0;
        await host.SeedAsync(async db =>
        {
            var a = new AuditLog
            {
                UserId = Admin,
                EntityType = "Workflow",
                EntityId = 7,
                Action = "workflow.created",
                NewValues = """{"name":"Nudge"}""",
                IpAddress = "10.0.0.1",
                CreatedAt = new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc),
            };
            var b = new AuditLog
            {
                UserId = Agent,
                EntityType = "Skill",
                EntityId = 3,
                Action = "updated",
                OldValues = """{"name":"A"}""",
                NewValues = """{"name":"B"}""",
                CreatedAt = DateTime.UtcNow,
            };
            db.AuditLogs.AddRange(a, b);
            await db.SaveChangesAsync();
            older = a.Id;
            newer = b.Id;
        });

        var page = await PageAsync(client, "/support/admin/audit-log");
        AssertPage(page, "Escalated/Admin/AuditLog/Index");
        var props = page.GetProperty("props");
        var logs = Keys(props.GetProperty("logs"), "data", "current_page", "last_page", "per_page", "total", "links");
        Assert.Equal(50, logs.GetProperty("per_page").GetInt32());
        var rows = logs.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(new[] { newer, older }, rows.Select(r => r.GetProperty("id").GetInt32()));
        var first = Keys(rows[1], "id", "user_id", "user", "action", "auditable_type", "auditable_id", "old_values", "new_values", "ip_address", "created_at");
        Assert.Equal("created", first.GetProperty("action").GetString());
        Assert.Equal("Workflow", first.GetProperty("auditable_type").GetString());
        Assert.Equal(7, first.GetProperty("auditable_id").GetInt32());
        Assert.Equal("Ada Admin", first.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal("Nudge", first.GetProperty("new_values").GetProperty("name").GetString());
        Assert.Equal("10.0.0.1", first.GetProperty("ip_address").GetString());

        Assert.Equal(new[] { "created", "updated", "deleted" }, props.GetProperty("actions").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(new[] { "Skill", "Workflow" }, props.GetProperty("resourceTypes").EnumerateArray().Select(t => t.GetString()).OrderBy(t => t, StringComparer.Ordinal));
        var users = props.GetProperty("users").EnumerateArray().ToList();
        Assert.Equal(new[] { "Ada Admin", "Avery Agent" }, users.Select(u => u.GetProperty("name").GetString()));
        Keys(users[0], "id", "name");
        Assert.Empty(props.GetProperty("filters").EnumerateObject());

        var filtered = await PageAsync(client, $"/support/admin/audit-log?action=created&auditable_type=Workflow&user_id={Admin}");
        Assert.Equal(new[] { older }, filtered.GetProperty("props").GetProperty("logs").GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()));
        Assert.Equal("created", filtered.GetProperty("props").GetProperty("filters").GetProperty("action").GetString());

        var dated = await PageAsync(client, "/support/admin/audit-log?date_from=2026-01-01&date_to=2026-01-02");
        Assert.Equal(new[] { older }, dated.GetProperty("props").GetProperty("logs").GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()));
    }
}
