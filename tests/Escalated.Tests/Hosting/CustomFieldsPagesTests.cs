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
/// Custom fields and custom objects: the five admin pages with escalated-laravel's
/// props and routes, and the ticket <c>custom_fields</c> contract (values keyed by
/// slug on create and update, decoded on the show pages and JSON detail).
/// </summary>
public class CustomFieldsPagesTests
{
    private const string Admin = "admin-1";
    private const string Agent = "agent-1";
    private const string Customer = "customer-1";

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

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Users.GetValueOrDefault(id));
    }

    private sealed record Seeded(int TicketId, int OrderFieldId, int PlanFieldId, int ObjectId, int OtherObjectId, int RecordId);

    private static async Task<(EscalatedTestHost Host, Seeded Seed)> StartAsync(bool withFields = true)
    {
        var host = await EscalatedTestHost.StartAsync(
            options => options.Ui.Enabled = true,
            services => services.AddSingleton<IUserDirectory, Directory>());
        await host.GrantRoleAsync(Admin, "escalated-admin");
        await host.GrantRoleAsync(Agent, "escalated-agent");

        Seeded? seeded = null;
        await host.SeedAsync(async db =>
        {
            var order = new CustomField { Name = "Order number", Slug = "order_number", Type = "text", Required = true, Position = 1 };
            var plan = new CustomField { Name = "Plan", Slug = "plan", Type = "select", Options = """["basic","pro"]""", Position = 2 };
            if (withFields)
            {
                db.CustomFields.AddRange(
                    order,
                    plan,
                    new CustomField { Name = "Seats", Slug = "seats", Type = "number", Position = 3 },
                    new CustomField { Name = "Renewal", Slug = "renewal", Type = "date", Position = 4 },
                    new CustomField { Name = "Add-ons", Slug = "add_ons", Type = "multi_select", Options = """["sso","audit"]""", Position = 5 },
                    new CustomField { Name = "VIP", Slug = "vip", Type = "checkbox", Position = 6 },
                    new CustomField { Name = "Legacy", Slug = "legacy", Type = "text", Active = false, Position = 0 },
                    new CustomField { Name = "Company size", Slug = "company_size", Type = "number", Context = "organization", Position = 0 });
            }

            var ticket = new Ticket { Reference = "ESC-00001", Subject = "Card declined", RequesterId = Customer };
            var obj = new CustomObject
            {
                Name = "Vendors",
                Slug = "vendors",
                FieldsSchema = """[{"name":"company","type":"text","required":true},{"name":"employees","type":"number"},{"name":"since","type":"date"},{"name":"tier","type":"select","options":["gold","silver"]}]""",
            };
            var other = new CustomObject { Name = "Assets", Slug = "assets", FieldsSchema = """[{"name":"tag","type":"text"}]""" };
            db.AddRange(ticket, obj, other);
            await db.SaveChangesAsync();

            var record = new CustomObjectRecord { ObjectId = obj.Id, Data = """{"company":"Acme","employees":12}""", CreatedAt = DateTime.UtcNow.AddMinutes(-5) };
            db.CustomObjectRecords.AddRange(record, new CustomObjectRecord { ObjectId = obj.Id, Data = """{"company":"Globex"}""" });
            await db.SaveChangesAsync();

            seeded = new Seeded(ticket.Id, order.Id, plan.Id, obj.Id, other.Id, record.Id);
        });

        return (host, seeded!);
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

    private static Dictionary<string, object?> Values(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value);

    // --- Custom field admin ---

    [Fact]
    public async Task CustomFields_IndexAndFormRenderTheFields()
    {
        var (host, seed) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Admin);

        var index = await PageAsync(client, "/support/admin/custom-fields");
        AssertPage(index, "Escalated/Admin/CustomFields/Index");
        var fields = index.GetProperty("props").GetProperty("fields");
        Assert.Equal(8, fields.GetArrayLength());
        Assert.All(fields.EnumerateArray(), f => Keys(f, "id", "name", "slug", "type", "context", "options", "required",
            "placeholder", "description", "validation_rules", "conditions", "position", "active", "created_at", "updated_at"));
        var positions = fields.EnumerateArray().Select(f => f.GetProperty("position").GetInt32()).ToList();
        Assert.Equal(positions.OrderBy(p => p), positions);
        var plan = fields.EnumerateArray().Single(f => f.GetProperty("slug").GetString() == "plan");
        Assert.Equal(new[] { "basic", "pro" }, plan.GetProperty("options").EnumerateArray().Select(o => o.GetString()));
        Assert.Equal("ticket", plan.GetProperty("context").GetString());

        var create = await PageAsync(client, "/support/admin/custom-fields/create");
        AssertPage(create, "Escalated/Admin/CustomFields/Form");
        Assert.Equal(JsonValueKind.Null, create.GetProperty("props").GetProperty("field").ValueKind);
        Assert.Equal(new[] { "ticket", "user", "organization" },
            create.GetProperty("props").GetProperty("contexts").EnumerateArray().Select(c => c.GetString()));

        var edit = await PageAsync(client, $"/support/admin/custom-fields/{seed.PlanFieldId}/edit");
        AssertPage(edit, "Escalated/Admin/CustomFields/Form");
        Assert.Equal("Plan", edit.GetProperty("props").GetProperty("field").GetProperty("name").GetString());

        // The JSON admin API on the shared path is unchanged.
        var api = await client.GetAsync("/support/admin/custom-fields");
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        Assert.Equal(JsonValueKind.Array, (await api.Content.ReadFromJsonAsync<JsonElement>()).ValueKind);
    }

    [Fact]
    public async Task CustomFields_StoreGeneratesUniqueSlugsAndUpdateKeepsThem()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Admin);

        var body = new
        {
            name = "Order Number!",
            type = "textarea",
            context = "ticket",
            options = Array.Empty<string>(),
            required = false,
            placeholder = "e.g. 1234",
            description = "From the receipt",
            position = 9,
            active = true,
        };
        var response = await SendAsync(client, HttpMethod.Post, "/support/admin/custom-fields", body, "/support/admin/custom-fields");
        var flash = await PageAsync(client, "/support/admin/custom-fields", FlashCookie(response));
        Assert.Equal("Custom field created.", flash.GetProperty("props").GetProperty("flash").GetProperty("success").GetString());
        await SendAsync(client, HttpMethod.Post, "/support/admin/custom-fields", body);

        var created = await host.QueryAsync(db => db.CustomFields.Where(f => f.Name == "Order Number!").OrderBy(f => f.Id).ToListAsync());
        Assert.Equal(new[] { "order_number_2", "order_number_3" }, created.Select(f => f.Slug));
        Assert.Equal("e.g. 1234", created[0].Placeholder);
        Assert.Equal("From the receipt", created[0].Description);
        Assert.Equal(9, created[0].Position);

        await SendAsync(client, HttpMethod.Put, $"/support/admin/custom-fields/{created[0].Id}",
            new { name = "Renamed", type = "select", context = "user", options = new[] { "a", "b" }, required = true, active = false },
            "/support/admin/custom-fields");
        var updated = await host.QueryAsync(db => db.CustomFields.SingleAsync(f => f.Id == created[0].Id));
        Assert.Equal("order_number_2", updated.Slug);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("user", updated.Context);
        Assert.Equal("""["a","b"]""", updated.Options);
        Assert.True(updated.Required);
        Assert.False(updated.Active);
    }

    [Fact]
    public async Task CustomFields_StoreRejectsBadInput()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Admin);

        var response = await SendAsync(client, HttpMethod.Post, "/support/admin/custom-fields",
            new { name = "", type = "colour", context = "galaxy" });
        Keys(await ErrorsAfterAsync(client, response, "/support/admin/custom-fields/create"), "name", "type", "context");

        response = await SendAsync(client, HttpMethod.Post, "/support/admin/custom-fields",
            new { name = "Tier", type = "multi_select", context = "ticket", options = Array.Empty<string>() });
        Keys(await ErrorsAfterAsync(client, response, "/support/admin/custom-fields/create"), "options");
        Assert.False(await host.QueryAsync(db => db.CustomFields.AnyAsync(f => f.Name == "Tier")));
    }

    [Fact]
    public async Task CustomFields_DestroyRemovesTheFieldAndItsValues()
    {
        var (host, seed) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Admin);
        await host.SeedAsync(async db =>
        {
            db.CustomFieldValues.Add(new CustomFieldValue { CustomFieldId = seed.PlanFieldId, EntityType = "ticket", EntityId = seed.TicketId, Value = "pro" });
            await db.SaveChangesAsync();
        });

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/custom-fields/{seed.PlanFieldId}", null, "/support/admin/custom-fields");

        Assert.False(await host.QueryAsync(db => db.CustomFields.AnyAsync(f => f.Id == seed.PlanFieldId)));
        Assert.False(await host.QueryAsync(db => db.CustomFieldValues.AnyAsync(v => v.CustomFieldId == seed.PlanFieldId)));
    }

    [Fact]
    public async Task CustomFields_ReorderSetsPositions()
    {
        var (host, seed) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Admin);

        await SendAsync(client, HttpMethod.Post, "/support/admin/custom-fields/reorder", new
        {
            positions = new[] { new { id = seed.OrderFieldId, position = 7 }, new { id = seed.PlanFieldId, position = 3 } },
        });
        Assert.Equal(7, await host.QueryAsync(db => db.CustomFields.Where(f => f.Id == seed.OrderFieldId).Select(f => f.Position).SingleAsync()));
        Assert.Equal(3, await host.QueryAsync(db => db.CustomFields.Where(f => f.Id == seed.PlanFieldId).Select(f => f.Position).SingleAsync()));

        var bad = await SendAsync(client, HttpMethod.Post, "/support/admin/custom-fields/reorder", new
        {
            positions = new[] { new { id = 9999, position = 1 } },
        });
        Keys(await ErrorsAfterAsync(client, bad, "/support/admin/custom-fields"), "positions");
    }

    [Fact]
    public async Task CustomFields_AreAdminOnly()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;

        var response = await host.ClientFor(Agent).SendAsync(Visit(HttpMethod.Get, "/support/admin/custom-objects"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Custom objects ---

    [Fact]
    public async Task CustomObjects_IndexFormAndRecordsRenderLaravelsProps()
    {
        var (host, seed) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Admin);

        var index = await PageAsync(client, "/support/admin/custom-objects");
        AssertPage(index, "Escalated/Admin/CustomObjects/Index");
        var objects = index.GetProperty("props").GetProperty("objects");
        Assert.Equal(new[] { "Assets", "Vendors" }, objects.EnumerateArray().Select(o => o.GetProperty("name").GetString()));
        var vendors = Keys(objects[1], "id", "name", "slug", "fields_schema", "records_count", "created_at", "updated_at");
        Assert.Equal(2, vendors.GetProperty("records_count").GetInt32());
        Assert.Equal("company", vendors.GetProperty("fields_schema")[0].GetProperty("name").GetString());

        var create = await PageAsync(client, "/support/admin/custom-objects/create");
        AssertPage(create, "Escalated/Admin/CustomObjects/Form");
        Assert.Equal(JsonValueKind.Null, create.GetProperty("props").GetProperty("object").ValueKind);

        var edit = await PageAsync(client, $"/support/admin/custom-objects/{seed.ObjectId}/edit");
        AssertPage(edit, "Escalated/Admin/CustomObjects/Form");
        Assert.Equal("vendors", edit.GetProperty("props").GetProperty("object").GetProperty("slug").GetString());

        var records = await PageAsync(client, $"/support/admin/custom-objects/{seed.ObjectId}/records");
        AssertPage(records, "Escalated/Admin/CustomObjects/Records");
        Keys(records.GetProperty("props").GetProperty("object"), "id", "name", "fields_schema");
        var rows = records.GetProperty("props").GetProperty("records");
        Assert.Equal(2, rows.GetArrayLength());
        Keys(rows[0], "id", "object_id", "data", "created_at", "updated_at");
        Assert.True(rows[0].GetProperty("id").GetInt32() > rows[1].GetProperty("id").GetInt32(), "records are newest first");
        Assert.Equal("Acme", rows[1].GetProperty("data").GetProperty("company").GetString());
    }

    [Fact]
    public async Task CustomObjects_StoreUpdateAndValidate()
    {
        var (host, seed) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Admin);

        var schema = new object[] { new { name = "serial", type = "text", required = true }, new { name = "kind", type = "select", options = new[] { "a" } } };
        await SendAsync(client, HttpMethod.Post, "/support/admin/custom-objects",
            new { name = "Devices", slug = "devices", fields_schema = schema }, "/support/admin/custom-objects");
        var created = await host.QueryAsync(db => db.CustomObjects.SingleAsync(o => o.Slug == "devices"));
        Assert.Contains("\"serial\"", created.FieldsSchema);

        var taken = await SendAsync(client, HttpMethod.Post, "/support/admin/custom-objects",
            new { name = "Again", slug = "vendors", fields_schema = schema });
        Keys(await ErrorsAfterAsync(client, taken, "/support/admin/custom-objects/create"), "slug");

        var bad = await SendAsync(client, HttpMethod.Post, "/support/admin/custom-objects",
            new { name = "", slug = "has spaces", fields_schema = Array.Empty<object>() });
        Keys(await ErrorsAfterAsync(client, bad, "/support/admin/custom-objects/create"), "name", "slug", "fields_schema");

        var badType = await SendAsync(client, HttpMethod.Post, "/support/admin/custom-objects",
            new { name = "Things", slug = "things", fields_schema = new object[] { new { name = "", type = "colour" } } });
        Keys(await ErrorsAfterAsync(client, badType, "/support/admin/custom-objects/create"), "fields_schema.0.name", "fields_schema.0.type");

        // Updating may keep its own slug.
        await SendAsync(client, HttpMethod.Put, $"/support/admin/custom-objects/{seed.ObjectId}",
            new { name = "Suppliers", slug = "vendors", fields_schema = schema }, "/support/admin/custom-objects");
        Assert.Equal("Suppliers", await host.QueryAsync(db => db.CustomObjects.Where(o => o.Id == seed.ObjectId).Select(o => o.Name).SingleAsync()));
    }

    [Fact]
    public async Task CustomObjects_RecordDataIsValidatedAgainstTheSchema()
    {
        var (host, seed) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Admin);
        var recordsUrl = $"/support/admin/custom-objects/{seed.ObjectId}/records";

        var bad = await SendAsync(client, HttpMethod.Post, recordsUrl, new
        {
            data = Values(("company", ""), ("employees", "many"), ("since", "not a date"), ("tier", "bronze"), ("colour", "red")),
        });
        Keys(await ErrorsAfterAsync(client, bad, recordsUrl), "data.company", "data.employees", "data.since", "data.tier", "data.colour");

        var missing = await SendAsync(client, HttpMethod.Post, recordsUrl, new { });
        Keys(await ErrorsAfterAsync(client, missing, recordsUrl), "data");
        Assert.Equal(2, await host.QueryAsync(db => db.CustomObjectRecords.CountAsync(r => r.ObjectId == seed.ObjectId)));

        var ok = await SendAsync(client, HttpMethod.Post, recordsUrl, new
        {
            data = Values(("company", "Initech"), ("employees", 40), ("since", "2020-01-31"), ("tier", "gold")),
        }, recordsUrl);
        var flash = await PageAsync(client, recordsUrl, FlashCookie(ok));
        Assert.Equal("Record created.", flash.GetProperty("props").GetProperty("flash").GetProperty("success").GetString());
        Assert.Equal("Initech", flash.GetProperty("props").GetProperty("records")[0].GetProperty("data").GetProperty("company").GetString());

        await SendAsync(client, HttpMethod.Put, $"{recordsUrl}/{seed.RecordId}", new { data = Values(("company", "Acme Corp")) }, recordsUrl);
        Assert.Contains("Acme Corp", await host.QueryAsync(db => db.CustomObjectRecords.Where(r => r.Id == seed.RecordId).Select(r => r.Data).SingleAsync()));

        // A record addressed through another object is not found.
        var foreign = await client.SendAsync(Visit(HttpMethod.Put, $"/support/admin/custom-objects/{seed.OtherObjectId}/records/{seed.RecordId}", new { data = Values(("tag", "x")) }));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var foreignDelete = await client.SendAsync(Visit(HttpMethod.Delete, $"/support/admin/custom-objects/{seed.OtherObjectId}/records/{seed.RecordId}"));
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);

        await SendAsync(client, HttpMethod.Delete, $"{recordsUrl}/{seed.RecordId}", null, recordsUrl);
        Assert.False(await host.QueryAsync(db => db.CustomObjectRecords.AnyAsync(r => r.Id == seed.RecordId)));
    }

    [Fact]
    public async Task CustomObjects_DestroyRemovesTheRecords()
    {
        var (host, seed) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Admin);

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/custom-objects/{seed.ObjectId}", null, "/support/admin/custom-objects");

        Assert.False(await host.QueryAsync(db => db.CustomObjects.AnyAsync(o => o.Id == seed.ObjectId)));
        Assert.False(await host.QueryAsync(db => db.CustomObjectRecords.AnyAsync(r => r.ObjectId == seed.ObjectId)));
    }

    // --- Ticket values ---

    [Fact]
    public async Task CustomerCreate_ListsTheActiveTicketFieldsInOrder()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(host.ClientFor(Customer), "/support/create");
        var fields = page.GetProperty("props").GetProperty("custom_fields");
        Assert.Equal(new[] { "order_number", "plan", "seats", "renewal", "add_ons", "vip" },
            fields.EnumerateArray().Select(f => f.GetProperty("slug").GetString()));
        Keys(fields[1], "id", "name", "slug", "type", "options", "required", "placeholder", "description", "position");
        Assert.True(fields[0].GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task CustomerStore_RequiresRequiredFieldsAndCreatesNoTicket()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Customer);

        var response = await SendAsync(client, HttpMethod.Post, "/support", new { subject = "Help", description = "Please" });
        Keys(await ErrorsAfterAsync(client, response, "/support/create"), "custom_fields.order_number");

        response = await SendAsync(client, HttpMethod.Post, "/support",
            new { subject = "Help", description = "Please", custom_fields = Values(("order_number", "  ")) });
        Keys(await ErrorsAfterAsync(client, response, "/support/create"), "custom_fields.order_number");

        Assert.False(await host.QueryAsync(db => db.Tickets.AnyAsync(t => t.Subject == "Help")));
    }

    [Fact]
    public async Task CustomerStore_ValidatesEachType()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Customer);

        var response = await SendAsync(client, HttpMethod.Post, "/support", new
        {
            subject = "Help",
            description = "Please",
            custom_fields = Values(
                ("order_number", new string('x', 256)),
                ("plan", "enterprise"),
                ("seats", "lots"),
                ("renewal", "2026-02-30"),
                ("add_ons", new[] { "sso", "bogus" }),
                ("vip", "maybe"),
                ("legacy", "inactive"),
                ("company_size", 5),
                ("nope", "unknown")),
        });
        var errors = await ErrorsAfterAsync(client, response, "/support/create");
        Keys(errors, "custom_fields.order_number", "custom_fields.plan", "custom_fields.seats", "custom_fields.renewal",
            "custom_fields.add_ons", "custom_fields.vip", "custom_fields.legacy", "custom_fields.company_size", "custom_fields.nope");
        Assert.False(await host.QueryAsync(db => db.Tickets.AnyAsync(t => t.Subject == "Help")));
    }

    [Fact]
    public async Task CustomerStore_SavesValuesAndTheShowPageDecodesThem()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Customer);

        var response = await SendAsync(client, HttpMethod.Post, "/support", new
        {
            subject = "Help",
            description = "Please",
            custom_fields = Values(
                ("order_number", "A-100"),
                ("plan", "pro"),
                ("seats", "12"),
                ("renewal", "2026-03-01"),
                ("add_ons", new[] { "sso", "audit" }),
                ("vip", true)),
        });
        var ticket = await host.QueryAsync(db => db.Tickets.SingleAsync(t => t.Subject == "Help"));
        Assert.Equal($"/support/{ticket.Reference}", response.Headers.Location!.ToString());

        var stored = await host.QueryAsync(db => db.CustomFieldValues.Include(v => v.CustomField)
            .Where(v => v.EntityType == "ticket" && v.EntityId == ticket.Id).ToListAsync());
        var bySlug = stored.ToDictionary(v => v.CustomField!.Slug, v => v.Value);
        Assert.Equal("""["sso","audit"]""", bySlug["add_ons"]);
        Assert.Equal("1", bySlug["vip"]);
        Assert.Equal("12", bySlug["seats"]);
        Assert.Equal(ticket.Id, await host.QueryAsync(db => db.CustomFieldValues
            .Where(v => v.EntityId == ticket.Id).Select(v => EF.Property<int?>(v, "TicketId")).FirstAsync()));

        var page = await PageAsync(client, $"/support/{ticket.Reference}");
        var fields = page.GetProperty("props").GetProperty("custom_fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("slug").GetString()!, f => Keys(f, "id", "name", "type", "options", "required", "position", "value").GetProperty("value"));
        Assert.Equal("A-100", fields["order_number"].GetString());
        Assert.Equal(12m, fields["seats"].GetDecimal());
        Assert.True(fields["vip"].GetBoolean());
        Assert.Equal(new[] { "sso", "audit" }, fields["add_ons"].EnumerateArray().Select(a => a.GetString()));
        Assert.Equal("2026-03-01", fields["renewal"].GetString());
    }

    [Fact]
    public async Task CustomerStore_WithNoFieldsDefinedIsUnchanged()
    {
        var (host, _) = await StartAsync(withFields: false);
        await using var __ = host;
        var client = host.ClientFor(Customer);

        var page = await PageAsync(client, "/support/create");
        Assert.Equal(0, page.GetProperty("props").GetProperty("custom_fields").GetArrayLength());
        await SendAsync(client, HttpMethod.Post, "/support", new { subject = "Help", description = "Please" });
        Assert.True(await host.QueryAsync(db => db.Tickets.AnyAsync(t => t.Subject == "Help")));
    }

    [Fact]
    public async Task AgentUpdate_ChecksOnlyTheKeysSentAndClearsEmptyOptionalValues()
    {
        var (host, seed) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Agent);
        const string show = "/support/agent/tickets/ESC-00001";

        // The ticket predates the required field: an update that does not send it is fine.
        await SendAsync(client, HttpMethod.Put, show, new { custom_fields = Values(("plan", "pro"), ("seats", 3)) }, show);
        await SendAsync(client, HttpMethod.Put, show, new { custom_fields = Values(("seats", 4)) }, show);

        var page = await PageAsync(client, show);
        var fields = page.GetProperty("props").GetProperty("custom_fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("slug").GetString()!, f => f.GetProperty("value"));
        Assert.Equal("pro", fields["plan"].GetString());
        Assert.Equal(4m, fields["seats"].GetDecimal());
        Assert.Equal(JsonValueKind.Null, fields["order_number"].ValueKind);

        // Omitting custom_fields changes nothing; an empty optional value clears it.
        await SendAsync(client, HttpMethod.Put, show, new { }, show);
        await SendAsync(client, HttpMethod.Put, show, new { custom_fields = Values(("plan", null)) }, show);
        var remaining = await host.QueryAsync(db => db.CustomFieldValues.Where(v => v.EntityId == seed.TicketId).Select(v => v.CustomFieldId).ToListAsync());
        Assert.DoesNotContain(seed.PlanFieldId, remaining);
        Assert.Single(remaining);

        // A required field that is sent cannot be blank, and bad values are refused.
        var blank = await SendAsync(client, HttpMethod.Put, show, new { custom_fields = Values(("order_number", ""), ("seats", "x")) });
        Keys(await ErrorsAfterAsync(client, blank, show), "custom_fields.order_number", "custom_fields.seats");
        Assert.Equal(4m, decimal.Parse((await host.QueryAsync(db => db.CustomFieldValues
            .Where(v => v.EntityId == seed.TicketId).Select(v => v.Value).SingleAsync()))!));

        // The admin ticket page carries the same prop.
        var admin = await PageAsync(host.ClientFor(Admin), "/support/admin/tickets/ESC-00001");
        Assert.Equal(6, admin.GetProperty("props").GetProperty("custom_fields").GetArrayLength());
    }

    [Fact]
    public async Task JsonApi_CreateValidatesAndDetailCarriesTheValues()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(Customer);

        var invalid = await client.PostAsJsonAsync("/support/tickets", new { subject = "Api", custom_fields = Values(("seats", "x")) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Keys(errors, "custom_fields.seats", "custom_fields.order_number");
        Assert.False(await host.QueryAsync(db => db.Tickets.AnyAsync(t => t.Subject == "Api")));

        var created = await client.PostAsJsonAsync("/support/tickets", new { subject = "Api", custom_fields = Values(("order_number", "B-7"), ("vip", "0")) });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/support/tickets/{id}");
        var fields = detail.GetProperty("custom_fields").EnumerateArray().ToDictionary(f => f.GetProperty("slug").GetString()!, f => f.GetProperty("value"));
        Assert.Equal("B-7", fields["order_number"].GetString());
        Assert.False(fields["vip"].GetBoolean());

        var admin = await host.ClientFor(Admin).GetFromJsonAsync<JsonElement>($"/support/admin/tickets/{id}");
        Assert.Equal(6, admin.GetProperty("custom_fields").GetArrayLength());
    }
}
