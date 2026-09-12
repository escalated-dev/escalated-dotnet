using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Escalated.Tests;

/// <summary>
/// Every page name this package renders resolves to a component in
/// <c>@escalated-dev/escalated</c>.
///
/// <para>Inertia resolving a name to nothing is not an error. The response is a
/// 200, the resolver returns undefined, Vue renders nothing, and the panel comes
/// up blank -- which reads as a permissions problem or an empty dataset. Screens
/// shipped that way across six of the backends in this portfolio before anyone
/// noticed, and the controller tests asserting a 200 said they were fine
/// throughout.</para>
///
/// <para>Neither repo's tests can see the failure alone: a controller test
/// asserts a status, and the frontend never hears the name. This is the
/// comparison, against the manifest the frontend package publishes and this repo
/// vendors at tests/Escalated.Tests/Fixtures/escalated-pages.json.</para>
///
/// <para>Adding a screen goes: component into the frontend, frontend release,
/// refresh the fixture, then render the name here. In that order, or it ships
/// blank.</para>
/// </summary>
public class PageNameParityTests
{
    private static readonly Regex PageName = new("\"(Escalated/[A-Za-z0-9/_]+)\"", RegexOptions.Compiled);

    /// <summary>
    /// The repository root, found by walking up from the test binary until the
    /// solution file turns up. Tests run from bin/, so nothing else locates it.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Escalated.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return directory!.FullName;
    }

    private static List<string> ShippedPages()
    {
        var path = Path.Combine(RepositoryRoot(), "tests", "Escalated.Tests", "Fixtures", "escalated-pages.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.GetProperty("pages")
            .EnumerateArray()
            .Select(page => page.GetString()!)
            .ToList();
    }

    /// <summary>
    /// Page names rendered anywhere under src/, mapped to the files that render
    /// them, so a failure can name the file and not only the string.
    /// </summary>
    private static Dictionary<string, SortedSet<string>> RenderedPages()
    {
        var root = RepositoryRoot();
        var source = Path.Combine(root, "src");
        var found = new Dictionary<string, SortedSet<string>>();

        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            foreach (Match match in PageName.Matches(File.ReadAllText(file)))
            {
                var name = match.Groups[1].Value;

                if (!found.TryGetValue(name, out var files))
                {
                    found[name] = files = new SortedSet<string>();
                }

                files.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
            }
        }

        return found;
    }

    private static string Explain(List<string> missing, Dictionary<string, SortedSet<string>> rendered)
    {
        var lines = new List<string>
        {
            "these page names have no component in @escalated-dev/escalated, so they render a blank panel:",
        };

        lines.AddRange(missing.Select(name => $"  {name}  ({string.Join(", ", rendered[name])})"));
        lines.Add("");
        lines.Add("Either the name is wrong, or the component has not been released yet.");
        lines.Add("If it has been: refresh tests/Escalated.Tests/Fixtures/escalated-pages.json from the package.");

        return string.Join("\n", lines);
    }

    [Fact]
    public void RendersOnlyPageNamesTheFrontendShips()
    {
        var rendered = RenderedPages();
        var shipped = ShippedPages().ToHashSet();

        Assert.True(rendered.Count > 0, "found no page names at all, which means this test is not looking where it should");

        var missing = rendered.Keys.Where(name => !shipped.Contains(name)).OrderBy(name => name).ToList();

        Assert.True(missing.Count == 0, Explain(missing, rendered));
    }

    [Fact]
    public void ThePageManifestIsPresentAndLooksLikeOne()
    {
        // A fixture gone missing or empty would make the test above pass by
        // comparing against nothing.
        var shipped = ShippedPages();

        Assert.True(shipped.Count > 50, $"the manifest has only {shipped.Count} pages, which does not look like the real one");
        Assert.All(shipped, name => Assert.StartsWith("Escalated/", name));
    }
}
