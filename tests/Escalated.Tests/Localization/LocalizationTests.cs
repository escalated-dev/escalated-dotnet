using System.Globalization;
using Escalated.Configuration;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace Escalated.Tests.Localization;

public class LocalizationTests
{
    [Theory]
    [InlineData("ar", "الإدارة")]
    [InlineData("de", "Verwaltung")]
    [InlineData("en", "Admin")]
    [InlineData("en-GB", "Admin")]
    [InlineData("es", "Administración")]
    [InlineData("fr", "Administration")]
    [InlineData("fr-CA", "Administration")]
    [InlineData("it", "Admin")]
    [InlineData("ja", "管理")]
    [InlineData("ko", "관리")]
    [InlineData("nl", "Beheer")]
    [InlineData("pl", "Administracja")]
    [InlineData("pt-BR", "Admin")]
    [InlineData("ru", "Администрирование")]
    [InlineData("tr", "Yonetim")]
    [InlineData("zh-CN", "管理")]
    [InlineData("cs-CZ", "Admin")]
    [InlineData("", "Admin")]
    public void RegisteredLocalizerReadsTheEmbeddedCatalog(string culture, string expected)
    {
        using var cultures = new CultureScope(culture);
        using var services = CreateServices();
        var localizer = services.GetRequiredService<IStringLocalizer<Ticket>>();

        var value = localizer["nav.admin"];

        Assert.Equal(expected, value.Value);
        Assert.False(value.ResourceNotFound);
    }

    [Fact]
    public void FactoryStringOverloadUsesTheSameCatalog()
    {
        using var cultures = new CultureScope("fr");
        using var services = CreateServices();
        var factory = services.GetRequiredService<IStringLocalizerFactory>();
        var localizer = factory.Create(nameof(Ticket), typeof(Ticket).Assembly.GetName().Name!);

        Assert.Equal("Administration", localizer["nav.admin"].Value);
    }

    [Fact]
    public void MissingKeysRemainMissingAndObjectsAreNotReturnedAsStrings()
    {
        using var cultures = new CultureScope("fr");
        using var services = CreateServices();
        var localizer = services.GetRequiredService<IStringLocalizer<Ticket>>();

        foreach (var key in new[] { "not.a.catalog.key", "nav" })
        {
            var value = localizer[key];
            Assert.Equal(key, value.Value);
            Assert.True(value.ResourceNotFound);
        }
    }

    [Fact]
    public void EnumerationWorksWithoutAnOverrideResource()
    {
        using var cultures = new CultureScope("fr-CA");
        using var services = CreateServices();
        var localizer = services.GetRequiredService<IStringLocalizer<Ticket>>();

        var entries = localizer.GetAllStrings(includeParentCultures: true).ToList();

        Assert.Contains(entries, entry => entry.Name == "nav.admin" && entry.Value == "Administration");
        Assert.Equal(entries.Count, entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(entries, entry => Assert.False(entry.ResourceNotFound));
        Assert.Empty(localizer.GetAllStrings(includeParentCultures: false));
    }

    [Fact]
    public void ExactCultureEnumerationReturnsItsOwnCatalog()
    {
        using var cultures = new CultureScope("en");
        using var services = CreateServices();
        var localizer = services.GetRequiredService<IStringLocalizer<Ticket>>();

        Assert.Contains(localizer.GetAllStrings(false), entry => entry.Name == "nav.admin" && entry.Value == "Admin");
    }

    [Fact]
    public void RealResxOverridesWinWhileOtherKeysUseTheCatalog()
    {
        using var cultures = new CultureScope("fr");
        using var services = CreateServices();
        var localizer = services.GetRequiredService<IStringLocalizer<LocalizationMessages>>();

        Assert.Equal("Panneau personnalisé", localizer["nav.admin"].Value);
        Assert.Equal("Panneau agent", localizer["nav.agentPanel"].Value);
        Assert.Equal(string.Empty, localizer["nav.myTickets"].Value);
        Assert.False(localizer["nav.myTickets"].ResourceNotFound);
        Assert.Equal("Montant 12,50", localizer["example.amount", 12.5m].Value);

        var entries = localizer.GetAllStrings(true).ToList();
        Assert.Single(entries, entry => entry.Name == "nav.admin");
        Assert.Contains(entries, entry => entry.Name == "nav.admin" && entry.Value == "Panneau personnalisé");
        Assert.Contains(entries, entry => entry.Name == "nav.agentPanel" && entry.Value == "Panneau agent");
    }

    [Fact]
    public void CatalogMessagesAcceptPositionalAndNamedArguments()
    {
        using var cultures = new CultureScope("fr");
        using var services = CreateServices();
        var localizer = services.GetRequiredService<IStringLocalizer<Ticket>>();

        Assert.Equal("3 ticket(s) mis à jour.", localizer["bulk.updated", 3].Value);
        Assert.Equal("3 ticket(s) mis à jour.", localizer["bulk.updated", new Dictionary<string, object?> { ["count"] = 3 }].Value);
    }

    [Fact]
    public void RawMessagesKeepTheirNamedPlaceholders()
    {
        using var cultures = new CultureScope("en");
        using var services = CreateServices();
        var localizer = services.GetRequiredService<IStringLocalizer<Ticket>>();

        Assert.Equal("{count} ticket(s) updated.", localizer["bulk.updated"].Value);
    }

    [Fact]
    public void MultiplePlaceholdersUseTheirNamesOrCanonicalPositions()
    {
        using var cultures = new CultureScope("fr");
        using var services = CreateServices();
        var localizer = services.GetRequiredService<IStringLocalizer<Ticket>>();

        Assert.Equal("[ESC-123] Nouveau ticket : Connexion", localizer["notifications.newTicket.subject", "ESC-123", "Connexion"].Value);
        Assert.Equal("[ESC-123] Nouveau ticket : Connexion", localizer["notifications.newTicket.subject", new Dictionary<string, object?> { ["subject"] = "Connexion", ["reference"] = "ESC-123" }].Value);
    }

    [Fact]
    public void NamedArgumentValuesAreInsertedOnce()
    {
        using var cultures = new CultureScope("en");
        using var services = CreateServices();
        var localizer = services.GetRequiredService<IStringLocalizer<Ticket>>();

        var value = localizer["ticket.macroApplied", new Dictionary<string, object?> { ["name"] = "{count}", ["count"] = 99 }];

        Assert.Equal("Macro \"{count}\" applied.", value.Value);
    }

    [Fact]
    public void LiteralFormatFallbackUsesTheFormattingCulture()
    {
        using var cultures = new CultureScope("en", "fr-FR");
        using var services = CreateServices();
        var localizer = services.GetRequiredService<IStringLocalizer<Ticket>>();

        var value = localizer["Amount {0:N2}", 12.5m];

        Assert.Equal("Amount 12,50", value.Value);
        Assert.True(value.ResourceNotFound);
    }

    [Fact]
    public void ASharedFactoryDoesNotLeakCultureBetweenConcurrentRequests()
    {
        using var services = CreateServices();
        var localizer = services.GetRequiredService<IStringLocalizer<Ticket>>();

        Parallel.For(0, 64, index =>
        {
            var french = index % 2 == 0;
            using var cultures = new CultureScope(french ? "fr" : "en");
            Assert.Equal(french ? "Administration" : "Admin", localizer["nav.admin"].Value);
        });
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEscalated(new ConfigurationBuilder().Build(), options => options.UseSqlite("Data Source=:memory:"));
        return services.BuildServiceProvider();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _ui = CultureInfo.CurrentUICulture;
        private readonly CultureInfo _format = CultureInfo.CurrentCulture;

        public CultureScope(string ui, string? format = null)
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(ui);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(format ?? ui);
        }

        public void Dispose()
        {
            CultureInfo.CurrentUICulture = _ui;
            CultureInfo.CurrentCulture = _format;
        }
    }
}

public sealed class LocalizationMessages
{
}
