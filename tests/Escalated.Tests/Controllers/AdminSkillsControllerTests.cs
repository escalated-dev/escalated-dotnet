using Escalated.Controllers.Admin;
using Escalated.Data;
using Escalated.Dtos.Admin;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Escalated.Tests.Controllers;

public class AdminSkillsControllerTests
{
    private sealed class StubUserDirectory : IUserDirectory
    {
        private readonly List<UserDirectoryEntry> _users = new();

        public UserDirectoryEntry Add(string id, string? name, string? email)
        {
            var e = new UserDirectoryEntry(id, name, email);
            _users.Add(e);
            return e;
        }

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default)
        {
            IEnumerable<UserDirectoryEntry> q = _users;
            return Task.FromResult(new UserDirectoryPage(q.ToList(), q.Count(), page, pageSize));
        }

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));
    }

    private static async Task<(AdminSkillsController Ctrl, EscalatedDbContext Db, StubUserDirectory Dir)> SeedControllerAsync()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var dir = new StubUserDirectory();
        var ctrl = new AdminSkillsController(db, dir);
        await SeedAgentsAsync(db, dir);
        await SeedRoutingLookups(db);
        return (ctrl, db, dir);
    }

    private static async Task SeedAgentsAsync(EscalatedDbContext db, StubUserDirectory dir)
    {
        var agentRole = new Role { Name = "Escalated Agent", Slug = AdminUsersController.AgentRoleSlug, IsSystem = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Roles.Add(agentRole);
        await db.SaveChangesAsync();

        dir.Add("10", "Agnes", "agnes@test");
        dir.Add("11", "Ben", "ben@test");
        db.RoleUsers.Add(new RoleUser { UserId = "10", RoleId = agentRole.Id });
        db.RoleUsers.Add(new RoleUser { UserId = "11", RoleId = agentRole.Id });
        await db.SaveChangesAsync();
    }

    private static async Task<(Tag T, Department D)> SeedRoutingLookups(EscalatedDbContext db)
    {
        var now = DateTime.UtcNow;
        var t = new Tag { Name = "priority", Slug = "priority", CreatedAt = now, UpdatedAt = now };
        var d = new Department { Name = "Support", Slug = "support", IsActive = true, CreatedAt = now, UpdatedAt = now };
        db.Tags.Add(t);
        db.Departments.Add(d);
        await db.SaveChangesAsync();
        return (t, d);
    }

    private static async Task GrantRoleSlugAsync(EscalatedDbContext db, string userId, string slug)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Slug == slug);
        if (role is null)
        {
            role = new Role
            {
                Name = slug,
                Slug = slug,
                IsSystem = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        db.RoleUsers.Add(new RoleUser { RoleId = role.Id, UserId = userId });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Index_IndexIsEmptyInitially()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var ctrl = new AdminSkillsController(db, new StubUserDirectory());

        var result = await ctrl.Index();

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AdminSkillsController.SkillsIndexEnvelope>(ok.Value);
        Assert.Empty(body.Skills);
    }

    [Fact]
    public async Task Create_ReturnsEnvelopeWithLookupsAndZeroSkill()
    {
        var (ctrl, _, _) = await SeedControllerAsync();

        var result = await ctrl.Create();

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AdminSkillsController.SkillEditEnvelope>(ok.Value);
        Assert.Equal(0, body.Skill.Id);
        Assert.Equal(2, body.AvailableAgents.Count);
        Assert.NotEmpty(body.AvailableTags);
        Assert.NotEmpty(body.AvailableDepartments);
    }

    [Fact]
    public async Task Store_And_Index_CountsRelationships()
    {
        var (ctrl, db, _) = await SeedControllerAsync();
        var (tag, dept) = (await db.Tags.FirstAsync(), await db.Departments.FirstAsync());

        var storeResult = await ctrl.Store(
            new CreateSkillDto
            {
                Name = "OAuth",
                Description = "auth",
                RoutingTagIds = new[] { tag.Id },
                RoutingDepartmentIds = new[] { dept.Id },
                Agents = new[]
                {
                    new AgentSkillEntryDto { UserId = "10", Proficiency = 5 },
                    new AgentSkillEntryDto { UserId = "11", Proficiency = 2 },
                },
            });

        var created = Assert.IsType<OkObjectResult>(storeResult);
        var envelope = Assert.IsType<AdminSkillsController.CreateSkillEnvelope>(created.Value);
        Assert.True(envelope.Id > 0);

        var ix = Assert.IsType<OkObjectResult>(await ctrl.Index()).Value;
        var list = Assert.IsType<AdminSkillsController.SkillsIndexEnvelope>(ix);
        Assert.Single(list.Skills);
        var row = list.Skills[0];
        Assert.Equal(envelope.Id, row.Id);
        Assert.Equal("OAuth", row.Name);
        Assert.Equal(2, row.AgentsCount);
        Assert.Equal(1, row.RoutingTagsCount);
        Assert.Equal(1, row.RoutingDepartmentsCount);

        var edit = Assert.IsType<OkObjectResult>(await ctrl.Edit(envelope.Id)).Value;
        var detail = Assert.IsType<AdminSkillsController.SkillEditEnvelope>(edit);
        Assert.Single(detail.Skill.RoutingTagIds);
        Assert.Equal(tag.Id, detail.Skill.RoutingTagIds[0]);
        Assert.Single(detail.Skill.RoutingDepartmentIds);
        Assert.Equal(dept.Id, detail.Skill.RoutingDepartmentIds[0]);
        Assert.Equal(2, detail.Skill.Agents.Length);
    }

    [Fact]
    public async Task Store_ReturnsBadRequest_WhenRoutingTagMissing()
    {
        var (ctrl, _, _) = await SeedControllerAsync();

        var res = await ctrl.Store(new CreateSkillDto
        {
            Name = "X",
            RoutingTagIds = new[] { int.MaxValue },
        });

        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task Update_AltersAssignments()
    {
        var (ctrl, db, _) = await SeedControllerAsync();
        await ctrl.Store(new CreateSkillDto { Name = "OnlyTag", RoutingTagIds = new[] { (await db.Tags.FirstAsync()).Id } });
        var id = db.Skills.Single().Id;

        var dept = await db.Departments.FirstAsync();
        await ctrl.Update(
            id,
            new UpdateSkillDto
            {
                Name = "Both",
                Description = null,
                RoutingDepartmentIds = new[] { dept.Id },
                RoutingTagIds = Array.Empty<int>(),
                Agents =
                    new[]
                    {
                        new AgentSkillEntryDto { UserId = "10", Proficiency = 4 },
                        new AgentSkillEntryDto { UserId = "10", Proficiency = 1 }, // last wins within payload
                    },
            });

        var refreshed = Assert.IsType<OkObjectResult>(await ctrl.Edit(id)).Value;
        var envelope = Assert.IsType<AdminSkillsController.SkillEditEnvelope>(refreshed);
        Assert.Equal("Both", envelope.Skill.Name);
        Assert.Single(envelope.Skill.Agents);
        Assert.Equal(1, envelope.Skill.Agents.First().Proficiency);
        Assert.Contains(dept.Id, envelope.Skill.RoutingDepartmentIds);
    }

    [Fact]
    public async Task Destroy_RemovesSkill()
    {
        var (ctrl, db, _) = await SeedControllerAsync();
        await ctrl.Store(new CreateSkillDto { Name = "rm", RoutingTagIds = Array.Empty<int>() });
        var id = db.Skills.Single().Id;

        Assert.IsType<OkObjectResult>(await ctrl.Destroy(id));

        Assert.False(await db.Skills.AnyAsync());
        Assert.False(await db.AgentSkills.AnyAsync());
    }

    [Fact]
    public async Task Edit_Returns404_WhenMissing()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var ctrl = new AdminSkillsController(db, new StubUserDirectory());

        Assert.IsType<NotFoundObjectResult>(await ctrl.Edit(int.MaxValue));
    }

    [Fact]
    public async Task UniqueSlug_AppendsNumericSuffix_OnCollision()
    {
        var (ctrl, db, _) = await SeedControllerAsync();
        await ctrl.Store(new CreateSkillDto { Name = "Same Name" });

        Assert.IsType<OkObjectResult>(await ctrl.Store(new CreateSkillDto { Name = "Same Name" }));

        var slugs = await db.Skills.Select(s => s.Slug).OrderBy(s => s).ToListAsync();
        Assert.Contains("same-name", slugs.First());
        Assert.Contains("same-name-", slugs.Last());
    }
}
