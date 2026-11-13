# Newsletters

## Newsletters (optional, partial port)

Models + renderer for the admin-only newsletter broadcast feature. EF Core migration not bundled — host integrators generate one with `dotnet ef migrations add NewsletterSystem` after referencing this package.

```csharp
using Escalated.Services.Newsletter;

var renderer = new NewsletterRenderer(new NewsletterRendererOptions
{
    BaseUrl = "https://support.example.com",
    DefaultTheme = "default",
    TrackingEnabled = true,
    ThemesDir = Path.Combine(env.ContentRootPath, "Views/NewsletterThemes"),
    MarkdownToHtml = md => Markdig.Markdown.ToHtml(md),
    Brand = new NewsletterBrand
    {
        Name = "Acme",
        Accent = "#2563eb",
        PhysicalAddress = "Acme Inc. · 123 Main St",
    },
});

var html = renderer.Render(delivery, newsletter, contact, template);
```

Ships: `Models/Newsletter/*.cs` (5 EF Core entities), `Models/Contact.cs` (gains `MarketingOptOutAt`), `Services/Newsletter/NewsletterRenderer.cs` (full renderer), `Views/NewsletterThemes/{default,branded}.html` (starter themes).

Follow-up PR: EF Core migration in-package, planner/dispatcher/tracker services using `EscalatedDbContext`, ASP.NET controllers.
