using Escalated.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<EscalatedDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? "Host=db;Port=5432;Database=escalated;Username=escalated;Password=escalated"));

builder.Services.AddControllers()
    .AddApplicationPart(typeof(EscalatedDbContext).Assembly);

var app = builder.Build();

// Auto-migrate on boot
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
    ctx.Database.EnsureCreated();
}

app.MapGet("/", () => "Escalated .NET demo host. Set APP_ENV=demo for /demo routes.");
app.MapGet("/demo", () => Results.Content(
    "<html><body><h1>Escalated .NET Demo</h1><p>Host project bootstrapped. The /demo picker, click-to-login, and seed script still need to be wired — see PR body for the punch list.</p></body></html>",
    "text/html"));

app.MapControllers();

app.Run();
