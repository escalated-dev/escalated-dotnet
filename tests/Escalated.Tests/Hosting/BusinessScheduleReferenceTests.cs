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
/// The schedule picker on the SLA policy and department forms, as escalated-laravel's
/// <c>StoreSlaPolicyRequest</c> / <c>StoreDepartmentRequest</c>: <c>business_schedule_id</c>
/// is nullable, must name a schedule, and only changes when sent. Deleting a schedule
/// clears it from policies and departments.
/// </summary>
public class BusinessScheduleReferenceTests
{
    private const string Admin = "admin-1";

    private sealed class Directory : IUserDirectory
    {
        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Array.Empty<UserDirectoryEntry>(), 0, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<UserDirectoryEntry?>(id == Admin ? new UserDirectoryEntry(Admin, "Ada Admin", "ada@example.com") : null);
    }

    private static async Task<(EscalatedTestHost Host, HttpClient Client, int ScheduleId)> StartAsync()
    {
        var host = await EscalatedTestHost.StartAsync(
            options => options.Ui.Enabled = true,
            services => services.AddSingleton<IUserDirectory, Directory>());
        await host.GrantRoleAsync(Admin, "escalated-admin");

        var scheduleId = 0;
        await host.SeedAsync(async db =>
        {
            var schedule = new BusinessSchedule { Name = "Office", Timezone = "UTC", Schedule = """{"monday":{"start":"09:00","end":"17:00"}}""" };
            db.BusinessSchedules.Add(schedule);
            await db.SaveChangesAsync();
            scheduleId = schedule.Id;
        });

        return (host, host.ClientFor(Admin), scheduleId);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object body)
    {
        var response = await client.SendAsync(Visit(method, url, body));
        Assert.True(response.StatusCode == HttpStatusCode.SeeOther, $"{method} {url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return response;
    }

    private static object PolicyBody(string name, object? scheduleId, bool send = true) => send
        ? new Dictionary<string, object?>
        {
            ["name"] = name,
            ["first_response_hours"] = new { high = 4 },
            ["resolution_hours"] = new { high = 8 },
            ["business_hours_only"] = true,
            ["business_schedule_id"] = scheduleId,
        }
        : new Dictionary<string, object?>
        {
            ["name"] = name,
            ["first_response_hours"] = new { high = 4 },
            ["resolution_hours"] = new { high = 8 },
            ["business_hours_only"] = true,
        };

    [Fact]
    public async Task SlaPolicies_StoreUpdateKeepAndClearTheSchedule()
    {
        var (host, client, scheduleId) = await StartAsync();
        await using var __ = host;

        await SendAsync(client, HttpMethod.Post, "/support/admin/sla-policies", PolicyBody("Office hours", scheduleId));
        var policy = await host.QueryAsync(db => db.SlaPolicies.AsNoTracking().SingleAsync(p => p.Name == "Office hours"));
        Assert.Equal(scheduleId, policy.BusinessScheduleId);

        var edit = await PageAsync(client, $"/support/admin/sla-policies/{policy.Id}/edit");
        Assert.Equal(scheduleId, edit.GetProperty("props").GetProperty("policy").GetProperty("business_schedule_id").GetInt32());

        // Not sent: left alone.
        await SendAsync(client, HttpMethod.Put, $"/support/admin/sla-policies/{policy.Id}", PolicyBody("Renamed", null, send: false));
        Assert.Equal(scheduleId, await host.QueryAsync(db => db.SlaPolicies.Where(p => p.Id == policy.Id).Select(p => p.BusinessScheduleId).SingleAsync()));

        // Sent as null: cleared.
        await SendAsync(client, HttpMethod.Put, $"/support/admin/sla-policies/{policy.Id}", PolicyBody("Renamed", null));
        Assert.Null(await host.QueryAsync(db => db.SlaPolicies.Where(p => p.Id == policy.Id).Select(p => p.BusinessScheduleId).SingleAsync()));
    }

    [Fact]
    public async Task SlaPolicies_RefuseAnUnknownSchedule()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var response = await SendAsync(client, HttpMethod.Post, "/support/admin/sla-policies", PolicyBody("Office hours", 9999));

        Assert.Equal("/support/admin/sla-policies/create", response.Headers.Location!.ToString());
        var form = await PageAsync(client, "/support/admin/sla-policies/create", FlashCookie(response));
        Assert.True(form.GetProperty("props").GetProperty("errors").TryGetProperty("business_schedule_id", out _));
        Assert.False(await host.QueryAsync(db => db.SlaPolicies.AnyAsync(p => p.Name == "Office hours")));
    }

    [Fact]
    public async Task Departments_StoreUpdateKeepAndClearTheSchedule()
    {
        var (host, client, scheduleId) = await StartAsync();
        await using var __ = host;

        await SendAsync(client, HttpMethod.Post, "/support/admin/departments", new { name = "Billing", business_schedule_id = scheduleId });
        var department = await host.QueryAsync(db => db.Departments.AsNoTracking().SingleAsync(d => d.Name == "Billing"));
        Assert.Equal(scheduleId, department.BusinessScheduleId);

        var edit = await PageAsync(client, $"/support/admin/departments/{department.Id}/edit");
        Assert.Equal(scheduleId, edit.GetProperty("props").GetProperty("department").GetProperty("business_schedule_id").GetInt32());

        await SendAsync(client, HttpMethod.Put, $"/support/admin/departments/{department.Id}", new { name = "Billing" });
        Assert.Equal(scheduleId, await host.QueryAsync(db => db.Departments.Where(d => d.Id == department.Id).Select(d => d.BusinessScheduleId).SingleAsync()));

        await SendAsync(client, HttpMethod.Put, $"/support/admin/departments/{department.Id}", new Dictionary<string, object?> { ["name"] = "Billing", ["business_schedule_id"] = null });
        Assert.Null(await host.QueryAsync(db => db.Departments.Where(d => d.Id == department.Id).Select(d => d.BusinessScheduleId).SingleAsync()));
    }

    [Fact]
    public async Task Departments_RefuseAnUnknownSchedule()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var response = await SendAsync(client, HttpMethod.Post, "/support/admin/departments", new { name = "Billing", business_schedule_id = 9999 });

        Assert.Equal("/support/admin/departments/create", response.Headers.Location!.ToString());
        var form = await PageAsync(client, "/support/admin/departments/create", FlashCookie(response));
        Assert.True(form.GetProperty("props").GetProperty("errors").TryGetProperty("business_schedule_id", out _));
        Assert.False(await host.QueryAsync(db => db.Departments.AnyAsync(d => d.Name == "Billing")));
    }

    [Fact]
    public async Task DeletingAScheduleClearsItFromPoliciesAndDepartments()
    {
        var (host, client, scheduleId) = await StartAsync();
        await using var __ = host;
        await host.SeedAsync(async db =>
        {
            db.Departments.Add(new Department { Name = "Billing", Slug = "billing", BusinessScheduleId = scheduleId });
            db.SlaPolicies.Add(new SlaPolicy { Name = "Office hours", BusinessScheduleId = scheduleId });
            await db.SaveChangesAsync();
        });

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/business-hours/{scheduleId}", new { });

        Assert.False(await host.QueryAsync(db => db.BusinessSchedules.AnyAsync()));
        Assert.Null(await host.QueryAsync(db => db.Departments.Select(d => d.BusinessScheduleId).SingleAsync()));
        Assert.Null(await host.QueryAsync(db => db.SlaPolicies.Select(p => p.BusinessScheduleId).SingleAsync()));
    }

    [Fact]
    public async Task TheJsonApi_RefusesAnUnknownSchedule()
    {
        var (host, client, scheduleId) = await StartAsync();
        await using var __ = host;

        var policy = await client.PostAsJsonAsync("/support/admin/sla-policies", new { name = "Api", businessScheduleId = 9999 });
        var department = await client.PostAsJsonAsync("/support/admin/departments", new { name = "Api", slug = "api", businessScheduleId = 9999 });
        var known = await client.PostAsJsonAsync("/support/admin/sla-policies", new { name = "Known", businessScheduleId = scheduleId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, policy.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, department.StatusCode);
        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(scheduleId, await host.QueryAsync(db => db.SlaPolicies.Where(p => p.Name == "Known").Select(p => p.BusinessScheduleId).SingleAsync()));
    }
}
