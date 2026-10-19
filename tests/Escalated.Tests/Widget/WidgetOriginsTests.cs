using Escalated.Configuration;
using Escalated.Services;
using Escalated.Widget;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Escalated.Tests.Widget;

/// <summary>
/// Which other sites may embed the widget. Mirrors escalated-laravel's
/// WidgetOriginsTest.
/// </summary>
public class WidgetOriginsTests
{
    [Theory]
    [InlineData("https://Shop.Example.com", "https://shop.example.com")]
    [InlineData("https://shop.example.com/", "https://shop.example.com")]
    [InlineData("https://shop.example.com:443", "https://shop.example.com")]
    [InlineData("http://localhost:5173", "http://localhost:5173")]
    [InlineData("https://*.example.com", "https://*.example.com")]
    [InlineData("https://*.example.com:8443", "https://*.example.com:8443")]
    [InlineData("  https://shop.example.com  ", "https://shop.example.com")]
    [InlineData("https://shop.example.com/checkout", null)]
    [InlineData("https://shop.example.com?x=1", null)]
    [InlineData("https://shop.example.com#top", null)]
    [InlineData("shop.example.com", null)]
    [InlineData("*", null)]
    [InlineData("https://*", null)]
    [InlineData("https://a.*.example.com", null)]
    [InlineData("ftp://shop.example.com", null)]
    [InlineData("https://user@shop.example.com", null)]
    [InlineData("", null)]
    public void NormalizePattern_KeepsOnlyBareOrigins(string entry, string? expected)
    {
        Assert.Equal(expected, WidgetOrigins.NormalizePattern(entry));
    }

    [Theory]
    [InlineData("https://shop.example.com", true)]
    [InlineData("https://SHOP.example.com:443", true)]
    [InlineData("http://shop.example.com", false)]
    [InlineData("https://shop.example.com:8443", false)]
    [InlineData("https://help.brand.test", true)]
    [InlineData("https://a.b.brand.test", true)]
    [InlineData("https://brand.test", false)]
    [InlineData("http://help.brand.test", false)]
    [InlineData("https://notbrand.test", false)]
    [InlineData("https://help.brand.test.evil.com", false)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("http://localhost", false)]
    [InlineData("null", false)]
    public void Allows_MatchesExactOriginsPortsAndSubdomainWildcards(string origin, bool allowed)
    {
        var entries = new[] { "https://shop.example.com", "https://*.brand.test", "http://localhost:5173" };

        Assert.Equal(allowed, WidgetOrigins.Allows(entries, origin));
    }

    [Fact]
    public void Allows_NeverMatchesABareStar()
    {
        Assert.False(WidgetOrigins.Allows(new[] { "*" }, "https://shop.example.com"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("null", null)]
    [InlineData("https://help.example.com", null)]
    [InlineData("https://help.example.com:443", null)]
    [InlineData("https://HELP.example.com", null)]
    [InlineData("https://shop.example.com", "https://shop.example.com")]
    [InlineData("http://help.example.com", "http://help.example.com")]
    [InlineData("https://help.example.com:8443", "https://help.example.com:8443")]
    public void CrossOrigin_OnlyReportsAnOriginThatNamesAnotherSite(string? origin, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("help.example.com");
        context.Request.Path = "/support/widget/config";
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        Assert.Equal(expected, WidgetOrigins.CrossOrigin(context.Request));
    }

    [Fact]
    public async Task Policy_MergesTheConfiguredListWithTheAdminSetting()
    {
        using var db = TestHelpers.CreateDb();
        var settings = new SettingsService(db);
        await settings.SetAsync("widget_allowed_origins", "https://blog.example.com, https://bad.example.com/path\n");
        var options = new EscalatedOptions();
        options.Widget.AllowedOrigins.Add("https://shop.example.com");
        var policy = new WidgetOriginPolicy(Options.Create(options), settings);

        Assert.Equal(new[] { "https://shop.example.com", "https://blog.example.com" }, await policy.PatternsAsync());
        Assert.True(await policy.AllowsAsync("https://blog.example.com"));
        Assert.True(await policy.AllowsAsync("https://shop.example.com"));
        Assert.False(await policy.AllowsAsync("https://bad.example.com"));
        Assert.False(policy.AllowsConfigured("https://blog.example.com"));
    }

    [Fact]
    public void AllowedOrigins_DefaultsToEmpty()
    {
        Assert.Empty(new EscalatedOptions().Widget.AllowedOrigins);
        Assert.Equal(600, new EscalatedOptions().Widget.PreflightMaxAge);
    }

    [Fact]
    public void AllowedOrigins_BindFromConfigurationAndTheEnvironmentVariable()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Escalated:Widget:AllowedOrigins:0"] = "https://shop.example.com",
                ["ESCALATED_WIDGET_ALLOWED_ORIGINS"] = "https://blog.example.com, https://*.example.org",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddEscalated(configuration, _ => { });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<EscalatedOptions>>().Value;

        Assert.Equal(
            new[] { "https://shop.example.com", "https://blog.example.com", "https://*.example.org" },
            options.Widget.AllowedOrigins);
    }
}
