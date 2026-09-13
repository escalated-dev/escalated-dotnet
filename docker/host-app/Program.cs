using System.Security.Claims;
using Escalated.Controllers.Admin;
using Escalated.Data;
using Escalated.Extensions;
using Escalated.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<EscalatedDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? "Host=db;Port=5432;Database=escalated;Username=escalated;Password=escalated"));

builder.Services.AddEscalated();

// Escalated owns no authentication: it authorizes whoever the host signed in.
// This demo signs you in with a cookie when you pick a seeded user below. A real
// host uses ASP.NET Core Identity, OpenID Connect, JWT bearer tokens, and so on.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/demo";
        options.AccessDeniedPath = "/demo";
    });

builder.Services.AddControllers()
    .AddApplicationPart(typeof(EscalatedDbContext).Assembly);

var app = builder.Build();

// Auto-migrate on boot + seed demo agents on empty DB.
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
    ctx.Database.EnsureCreated();
    var now = DateTime.UtcNow;

    if (!ctx.AgentProfiles.Any())
    {
        ctx.Departments.AddRange(
            new Department { Name = "Support", Slug = "support", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new Department { Name = "Billing", Slug = "billing", IsActive = true, CreatedAt = now, UpdatedAt = now }
        );
        ctx.AgentProfiles.AddRange(
            new AgentProfile { UserId = "1", AgentType = "full", MaxTickets = 50, Signature = "Alice (Admin)", CreatedAt = now, UpdatedAt = now },
            new AgentProfile { UserId = "2", AgentType = "full", MaxTickets = 50, Signature = "Bob (Agent)", CreatedAt = now, UpdatedAt = now },
            new AgentProfile { UserId = "3", AgentType = "full", MaxTickets = 50, Signature = "Carol (Agent)", CreatedAt = now, UpdatedAt = now }
        );
        ctx.SaveChanges();
    }

    // The escalated-admin and escalated-agent roles are what the default
    // authorization policies check. Seeded separately so an existing demo
    // database gets them too.
    if (!ctx.Roles.Any(r => r.Slug == AdminUsersController.AdminRoleSlug))
    {
        var adminRole = new Role { Name = "Escalated Admin", Slug = AdminUsersController.AdminRoleSlug, IsSystem = true, CreatedAt = now, UpdatedAt = now };
        var agentRole = new Role { Name = "Escalated Agent", Slug = AdminUsersController.AgentRoleSlug, IsSystem = true, CreatedAt = now, UpdatedAt = now };
        ctx.Roles.AddRange(adminRole, agentRole);
        ctx.SaveChanges();

        ctx.RoleUsers.AddRange(
            new RoleUser { RoleId = adminRole.Id, UserId = "1" },
            new RoleUser { RoleId = agentRole.Id, UserId = "1" },
            new RoleUser { RoleId = agentRole.Id, UserId = "2" },
            new RoleUser { RoleId = agentRole.Id, UserId = "3" }
        );
        ctx.SaveChanges();
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/demo"));

app.MapGet("/demo", (EscalatedDbContext db) =>
{
    var agents = db.AgentProfiles.OrderBy(a => a.Id).ToList();
    var rows = string.Join("", agents.Select(a => $@"
        <form method='POST' action='/demo/login/{a.Id}'>
            <button type='submit' class='user'>
                <span>{System.Net.WebUtility.HtmlEncode(a.Signature)}</span>
                <span class='meta'>UserId {a.UserId} · {a.AgentType}</span>
            </button>
        </form>"));
    var html = $@"<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'>
        <title>Escalated · .NET Demo</title>
        <style>
            body{{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;background:#0f172a;color:#e2e8f0;margin:0;padding:2rem}}
            .wrap{{max-width:720px;margin:0 auto}}
            h1{{font-size:1.5rem;margin:0 0 .25rem}}
            p.lede{{color:#94a3b8;margin:0 0 2rem}}
            form{{display:block;margin:0}}
            button.user{{display:flex;width:100%;align-items:center;justify-content:space-between;padding:.75rem 1rem;background:#1e293b;border:1px solid #334155;border-radius:8px;color:#f1f5f9;font-size:.95rem;cursor:pointer;margin-bottom:.5rem;text-align:left}}
            button.user:hover{{background:#273549;border-color:#475569}}
            .meta{{color:#94a3b8;font-size:.8rem}}
        </style></head><body><div class='wrap'>
        <h1>Escalated .NET Demo</h1>
        <p class='lede'>Click an agent to sign in and load their dashboard. Database seeds on first boot.</p>
        {rows}
        </div></body></html>";
    return Results.Content(html, "text/html");
});

// Sign in as the seeded agent. Escalated reads the user id from the NameIdentifier
// claim; nothing in the URL says who you are.
app.MapPost("/demo/login/{id:int}", async (int id, HttpContext http, EscalatedDbContext db) =>
{
    var agent = await db.AgentProfiles.FirstOrDefaultAsync(a => a.Id == id);
    if (agent is null)
    {
        return Results.NotFound();
    }

    var identity = new ClaimsIdentity(
        new[]
        {
            new Claim(ClaimTypes.NameIdentifier, agent.UserId),
            new Claim(ClaimTypes.Name, agent.Signature ?? agent.UserId),
        },
        CookieAuthenticationDefaults.AuthenticationScheme);

    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    return Results.Redirect("/support/agent/tickets/dashboard");
});

app.MapControllers();

app.Run();
