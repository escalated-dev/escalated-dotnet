using Escalated.Data;
using Escalated.Extensions;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<EscalatedDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? "Host=db;Port=5432;Database=escalated;Username=escalated;Password=escalated"));

builder.Services.AddEscalated();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(EscalatedDbContext).Assembly);

var app = builder.Build();

// Auto-migrate on boot + seed demo agents on empty DB.
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
    ctx.Database.EnsureCreated();

    if (!ctx.AgentProfiles.Any())
    {
        var now = DateTime.UtcNow;
        ctx.Departments.AddRange(
            new Department { Name = "Support", Slug = "support", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new Department { Name = "Billing", Slug = "billing", IsActive = true, CreatedAt = now, UpdatedAt = now }
        );
        ctx.AgentProfiles.AddRange(
            new AgentProfile { UserId = 1, AgentType = "full", MaxTickets = 50, Signature = "Alice (Admin)", CreatedAt = now, UpdatedAt = now },
            new AgentProfile { UserId = 2, AgentType = "full", MaxTickets = 50, Signature = "Bob (Agent)", CreatedAt = now, UpdatedAt = now },
            new AgentProfile { UserId = 3, AgentType = "full", MaxTickets = 50, Signature = "Carol (Agent)", CreatedAt = now, UpdatedAt = now }
        );
        ctx.SaveChanges();
    }
}

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
        <p class='lede'>Click an agent to load their dashboard. Database seeds on first boot.</p>
        {rows}
        </div></body></html>";
    return Results.Content(html, "text/html");
});

app.MapPost("/demo/login/{id:int}", (int id) => Results.Redirect($"/support/agent/tickets/dashboard?agentId={id}"));

app.MapControllers();

app.Run();
