using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Escalated.Tests.Services;

public class SkillRoutingServiceTests
{
    private sealed record SkillRow(int UserId, int? TagsProf, int? DeptsProf);

    private static async Task<(EscalatedDbContext Db, Ticket Ticket)> SeedAsync(string ticketRef, IList<SkillRow> abilities)
    {
        var db = TestHelpers.CreateInMemoryDb();
        var now = DateTime.UtcNow;
        var tag = new Tag { Name = $"{ticketRef}-t", Slug = $"{ticketRef}-t", CreatedAt = now, UpdatedAt = now };
        var dept = new Department { Name = $"{ticketRef}-d", Slug = $"{ticketRef}-d", IsActive = true, CreatedAt = now, UpdatedAt = now };
        db.Tags.Add(tag);
        db.Departments.Add(dept);

        var sTag = new Skill { Name = $"{ticketRef}-st", Slug = $"{ticketRef}-st", CreatedAt = now, UpdatedAt = now };
        var sDept = new Skill { Name = $"{ticketRef}-sd", Slug = $"{ticketRef}-sd", CreatedAt = now, UpdatedAt = now };
        db.Skills.AddRange(sTag, sDept);
        db.SaveChanges();

        db.SkillRoutingTags.Add(new SkillRoutingTag { SkillId = sTag.Id, TagId = tag.Id });
        db.SkillRoutingDepartments.Add(new SkillRoutingDepartment { SkillId = sDept.Id, DepartmentId = dept.Id });

        foreach (var a in abilities)
        {
            if (a.TagsProf is { } tp)
            {
                db.AgentSkills.Add(new AgentSkill
                {
                    UserId = a.UserId,
                    SkillId = sTag.Id,
                    Proficiency = tp,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            if (a.DeptsProf is { } dp)
            {
                db.AgentSkills.Add(new AgentSkill
                {
                    UserId = a.UserId,
                    SkillId = sDept.Id,
                    Proficiency = dp,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        var ticket = new Ticket
        {
            Reference = ticketRef,
            Subject = "routing",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            DepartmentId = dept.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };

        ticket.Tags.Add(tag);

        db.Tickets.Add(ticket);
        db.SaveChanges();

        var tracked = await db.Tickets.Include(t => t.Tags).SingleAsync(t => t.Reference == ticketRef);
        return (db, tracked);
    }

    [Fact]
    public async Task RequiresEveryMatchedSkillAcrossTagAndDepartmentSkills()
    {
        var (db, ticket) = await SeedAsync(
            "ESC-R1",
            new SkillRow[]
            {
                new(10, 5, 5),
                new(11, 5, null),
            });

        try
        {
            var ids = await new SkillRoutingService(db).FindMatchingAgentIdsAsync(ticket);
            Assert.Single(ids);
            Assert.Equal(10, ids[0]);
        }
        finally
        {
            await db.DisposeAsync();
        }
    }

    [Fact]
    public async Task OrdersByDescendingProficiencyThenAscOpenWorkload()
    {
        var (db, ticket) = await SeedAsync(
            "ESC-R2",
            new SkillRow[]
            {
                new(501, 5, 5),
                new(502, 2, 2),
            });

        try
        {
            var now = DateTime.UtcNow;
            for (var i = 0; i < 3; i++)
            {
                db.Tickets.Add(new Ticket
                {
                    Reference = $"WK501-{ticket.Reference}-{i}",
                    Subject = "load",
                    Status = TicketStatus.Open,
                    Priority = TicketPriority.Low,
                    AssignedTo = 501,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            for (var i = 0; i < 2; i++)
            {
                db.Tickets.Add(new Ticket
                {
                    Reference = $"WK502-{ticket.Reference}-{i}",
                    Subject = "load",
                    Status = TicketStatus.Open,
                    Priority = TicketPriority.Low,
                    AssignedTo = 502,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            await db.SaveChangesAsync();

            db.ChangeTracker.Clear();
            ticket = await db.Tickets.AsNoTracking().Include(t => t.Tags).SingleAsync(t => t.Reference == ticket.Reference);

            var service = new SkillRoutingService(db);
            var ids = await service.FindMatchingAgentIdsAsync(ticket);
            Assert.Equal(new[] { 501, 502 }, ids);

            var best = await service.FindBestAgentAsync(ticket);
            Assert.Equal(501, best);
        }
        finally
        {
            await db.DisposeAsync();
        }
    }

    [Fact]
    public async Task SameProficiencySum_PrefersLowerOpenWorkload()
    {
        var (db, ticket) = await SeedAsync(
            "ESC-R3",
            new SkillRow[]
            {
                new(702, 4, 4),
                new(703, 4, 4),
            });

        try
        {
            var now = DateTime.UtcNow;
            for (var i = 0; i < 5; i++)
            {
                db.Tickets.Add(new Ticket
                {
                    Reference = $"BLK-702-{ticket.Reference}-{i}",
                    Subject = "x",
                    Status = TicketStatus.Open,
                    AssignedTo = 702,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            await db.SaveChangesAsync();

            db.ChangeTracker.Clear();
            ticket = await db.Tickets.AsNoTracking().Include(t => t.Tags).SingleAsync(t => t.Reference == ticket.Reference);

            var ids = await new SkillRoutingService(db).FindMatchingAgentIdsAsync(ticket);
            Assert.Equal(new[] { 703, 702 }, ids.ToArray());
        }
        finally
        {
            await db.DisposeAsync();
        }
    }

    [Fact]
    public async Task NoRoutingRules_YieldsEmpty()
    {
        var db = TestHelpers.CreateInMemoryDb();
        try
        {
            var now = DateTime.UtcNow;
            var tag = new Tag { Name = "only", Slug = "only", CreatedAt = now, UpdatedAt = now };
            var dept = new Department { Name = "onlyd", Slug = "onlyd", IsActive = true, CreatedAt = now, UpdatedAt = now };
            db.Tags.Add(tag);
            db.Departments.Add(dept);
            db.SaveChanges();

            var ticket = new Ticket
            {
                Reference = "ESC-R4",
                Subject = "-",
                Status = TicketStatus.Open,
                Priority = TicketPriority.Medium,
                DepartmentId = dept.Id,
                CreatedAt = now,
                UpdatedAt = now,
            };

            ticket.Tags.Add(tag);

            db.Tickets.Add(ticket);
            db.SaveChanges();

            db.ChangeTracker.Clear();
            ticket = await db.Tickets.AsNoTracking().Include(t => t.Tags).SingleAsync();

            Assert.Empty(await new SkillRoutingService(db).FindMatchingAgentIdsAsync(ticket));
            Assert.Null(await new SkillRoutingService(db).FindBestAgentAsync(ticket));
        }
        finally
        {
            await db.DisposeAsync();
        }
    }
}
