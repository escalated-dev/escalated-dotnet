using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Xunit;

namespace Escalated.Tests.Services;

/// <summary>
/// Which business schedule a business-hours SLA policy counts against, as
/// escalated-laravel's <c>SlaService::scheduleFor</c>: the policy's schedule, else
/// the ticket department's, else the default schedule, else the configured hours.
/// </summary>
public class SlaBusinessScheduleTests
{
    // Monday 2 November 2026, 10:00 UTC.
    private static readonly DateTime Created = new(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);

    private static BusinessSchedule Monday(string name, string start, bool isDefault = false) => new()
    {
        Name = name,
        Timezone = "UTC",
        IsDefault = isDefault,
        Schedule = $$$"""{"monday":{"start":"{{{start}}}","end":"20:00"}}""",
    };

    private static async Task<(EscalatedDbContext Db, SlaService Service)> SetupAsync()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var service = new SlaService(db, TestHelpers.MockEventDispatcher().Object, TestHelpers.DefaultOptions());
        await Task.CompletedTask;
        return (db, service);
    }

    // Monday 2 November 2026, 20:00 UTC: after the configured hours, so calendar time and
    // the configured fallback give different answers.
    private static readonly DateTime Evening = new(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc);

    private static async Task<DateTime?> DueAsync(EscalatedDbContext db, SlaService service, SlaPolicy policy, int? departmentId, DateTime? created = null)
    {
        db.SlaPolicies.Add(policy);
        var ticket = new Ticket { Reference = "ESC-1", Subject = "Hours", Priority = TicketPriority.High, DepartmentId = departmentId, CreatedAt = created ?? Created };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        await service.AttachPolicyAsync(ticket, policy);
        return ticket.FirstResponseDueAt;
    }

    private static SlaPolicy Policy(bool businessHoursOnly = true, int? scheduleId = null) => new()
    {
        Name = "Hours",
        FirstResponseHours = """{"high":1}""",
        BusinessHoursOnly = businessHoursOnly,
        BusinessScheduleId = scheduleId,
        IsActive = true,
    };

    [Fact]
    public async Task ThePolicysScheduleComesFirst()
    {
        var (db, service) = await SetupAsync();
        var policySchedule = Monday("Policy", "12:00");
        var departmentSchedule = Monday("Department", "14:00");
        db.AddRange(policySchedule, departmentSchedule, Monday("Default", "16:00", isDefault: true));
        await db.SaveChangesAsync();
        var department = new Department { Name = "Billing", Slug = "billing", BusinessScheduleId = departmentSchedule.Id };
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        var due = await DueAsync(db, service, Policy(scheduleId: policySchedule.Id), department.Id);

        Assert.Equal(new DateTime(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc), due);
    }

    [Fact]
    public async Task ThenTheTicketDepartmentsSchedule()
    {
        var (db, service) = await SetupAsync();
        var departmentSchedule = Monday("Department", "14:00");
        db.AddRange(departmentSchedule, Monday("Default", "16:00", isDefault: true));
        await db.SaveChangesAsync();
        var department = new Department { Name = "Billing", Slug = "billing", BusinessScheduleId = departmentSchedule.Id };
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        var due = await DueAsync(db, service, Policy(), department.Id);

        Assert.Equal(new DateTime(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc), due);
    }

    [Fact]
    public async Task ThenTheDefaultSchedule()
    {
        var (db, service) = await SetupAsync();
        db.AddRange(Monday("Other", "12:00"), Monday("Default", "16:00", isDefault: true));
        await db.SaveChangesAsync();
        var department = new Department { Name = "Billing", Slug = "billing" };
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        var due = await DueAsync(db, service, Policy(), department.Id);

        Assert.Equal(new DateTime(2026, 11, 2, 17, 0, 0, DateTimeKind.Utc), due);
    }

    [Fact]
    public async Task ThenTheConfiguredBusinessHours()
    {
        var (db, service) = await SetupAsync();
        db.Add(Monday("Not default", "16:00"));
        await db.SaveChangesAsync();

        // Configured hours: weekdays 09:00-17:00 UTC.
        var due = await DueAsync(db, service, Policy(), null);

        Assert.Equal(new DateTime(2026, 11, 2, 11, 0, 0, DateTimeKind.Utc), due);
    }

    [Fact]
    public async Task ADeletedPolicyScheduleFallsThroughToTheDepartment()
    {
        var (db, service) = await SetupAsync();
        var departmentSchedule = Monday("Department", "14:00");
        db.Add(departmentSchedule);
        await db.SaveChangesAsync();
        var department = new Department { Name = "Billing", Slug = "billing", BusinessScheduleId = departmentSchedule.Id };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        var policy = Policy();
        db.SlaPolicies.Add(policy);
        await db.SaveChangesAsync();

        // A stale id, as where foreign keys are not enforced.
        policy.BusinessScheduleId = 9999;
        var schedule = await service.ScheduleForAsync(policy, new Ticket { DepartmentId = department.Id });

        Assert.Equal(departmentSchedule.Id, schedule?.Id);
    }

    [Fact]
    public async Task HolidaysOnTheChosenScheduleAreSkipped()
    {
        var (db, service) = await SetupAsync();
        var schedule = new BusinessSchedule
        {
            Name = "Weekdays",
            Timezone = "UTC",
            Schedule = """{"monday":{"start":"09:00","end":"17:00"},"tuesday":{"start":"09:00","end":"17:00"}}""",
        };
        schedule.Holidays.Add(new Holiday { Name = "Closed", Date = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc) });
        db.Add(schedule);
        await db.SaveChangesAsync();

        var due = await DueAsync(db, service, Policy(scheduleId: schedule.Id), null);

        Assert.Equal(new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc), due);
    }

    [Fact]
    public async Task CalendarTimePoliciesIgnoreSchedules()
    {
        var (db, service) = await SetupAsync();
        var schedule = Monday("Policy", "16:00", isDefault: true);
        db.Add(schedule);
        await db.SaveChangesAsync();

        var due = await DueAsync(db, service, Policy(businessHoursOnly: false, scheduleId: schedule.Id), null, Evening);

        Assert.Equal(Evening.AddHours(1), due);
    }

    [Fact]
    public async Task AScheduleWithNoOpenDayCountsCalendarTime()
    {
        var (db, service) = await SetupAsync();
        var schedule = new BusinessSchedule { Name = "Closed", Timezone = "UTC", Schedule = "{}" };
        db.Add(schedule);
        await db.SaveChangesAsync();

        var due = await DueAsync(db, service, Policy(scheduleId: schedule.Id), null, Evening);

        Assert.Equal(Evening.AddHours(1), due);
    }

    [Fact]
    public async Task AnUnknownScheduleZoneCountsInUtc()
    {
        var (db, service) = await SetupAsync();
        var schedule = Monday("Nowhere", "12:00");
        schedule.Timezone = "Not/AZone";
        db.Add(schedule);
        await db.SaveChangesAsync();

        var due = await DueAsync(db, service, Policy(scheduleId: schedule.Id), null);

        Assert.Equal(new DateTime(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc), due);
    }
}
