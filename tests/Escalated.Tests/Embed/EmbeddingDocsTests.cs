using System.Text;
using System.Text.RegularExpressions;
using Escalated.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Escalated.Tests.Embed;

/// <summary>docs/embedding-admin.md shows what the tests run: its settings block binds, and its Razor view is the fixture.</summary>
public class EmbeddingDocsTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Escalated.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    private static List<string> Blocks(string language)
    {
        var docs = File.ReadAllText(Path.Combine(Root(), "docs", "embedding-admin.md")).ReplaceLineEndings("\n");
        return Regex.Matches(docs, "```" + language + "\n(.*?)```", RegexOptions.Singleline).Select(m => m.Groups[1].Value).ToList();
    }

    [Fact]
    public void TheDocumentedSettingsBind()
    {
        var json = Blocks("json").First(b => b.Contains("\"Embed\"", StringComparison.Ordinal));
        var configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection();
        services.AddEscalated(configuration, _ => { });
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<EscalatedOptions>>().Value;

        Assert.True(options.Ui.Enabled);
        Assert.True(options.Ui.Embed.Enabled);
        Assert.Equal("layout", options.Ui.Embed.Mode);
        Assert.Equal("admin/support", options.Ui.Embed.Prefix);
        Assert.Equal(new[] { "BackOfficeAdmin" }, options.Ui.Embed.Policies);
        Assert.False(options.Ui.Embed.ReplaceAdminGate);
        Assert.Equal("Layouts/AdminLayout", options.Ui.Embed.Layout);
        // JSON null binds as an empty string, which reads as no root view.
        Assert.Null(EscalatedEmbed.RootView(options));
        Assert.Empty(options.Ui.Embed.FrameAncestors);
    }

    [Fact]
    public void TheDocumentedRazorViewIsTheOneTheTestsRender()
    {
        var view = Blocks("cshtml").First(b => b.Contains("EscalatedEmbedRootViewModel", StringComparison.Ordinal));
        var fixture = File.ReadAllText(Path.Combine(Root(), "tests", "Escalated.Tests", "Views", "Shared", "SupportAdmin.cshtml")).ReplaceLineEndings("\n");

        Assert.Equal(fixture, view);
    }
}
