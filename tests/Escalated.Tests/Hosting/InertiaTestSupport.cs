using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Escalated.Tests.Hosting;

/// <summary>
/// Makes requests the way the Inertia client does, and checks a page response
/// against the prop names the frontend's page manifest says the component reads.
/// </summary>
internal static class InertiaTestSupport
{
    /// <summary>An Inertia visit: <c>X-Inertia: true</c> plus <c>Accept</c> as the client sends it.</summary>
    public static HttpRequestMessage Visit(HttpMethod method, string url, object? body = null, string? referer = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Inertia", "true");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
        if (referer is not null) request.Headers.Referrer = new Uri(referer);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return request;
    }

    /// <summary>Gets a page with an Inertia visit and returns its page object.</summary>
    public static async Task<JsonElement> PageAsync(HttpClient client, string url, string? cookie = null)
    {
        var request = Visit(HttpMethod.Get, url);
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {url}: {(int)response.StatusCode} {text}");
        Assert.Equal("true", response.Headers.GetValues("X-Inertia").Single());
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>The flash cookie a redirect set, as a Cookie header value for the next request.</summary>
    public static string? FlashCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0]).FirstOrDefault(v => v.StartsWith("escalated_flash=", StringComparison.Ordinal))
            : null;

    private static readonly Lazy<JsonElement> Manifest = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Escalated.sln")))
        {
            directory = directory.Parent;
        }

        var path = Path.Combine(directory!.FullName, "tests", "Escalated.Tests", "Fixtures", "escalated-pages.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    });

    /// <summary>The page names the component and carries every prop the manifest lists for it.</summary>
    public static void AssertPage(JsonElement page, string component)
    {
        Assert.Equal(component, page.GetProperty("component").GetString());
        var props = page.GetProperty("props");

        Assert.True(Manifest.Value.GetProperty("props").TryGetProperty(component, out var contract),
            $"{component} is not in the page manifest");
        foreach (var name in contract.GetProperty("props").EnumerateArray().Select(p => p.GetString()!))
        {
            Assert.True(props.TryGetProperty(name, out _), $"{component} is missing the prop '{name}'");
        }
    }

    /// <summary>Asserts the object has every key, and returns it for chaining.</summary>
    public static JsonElement Keys(JsonElement element, params string[] keys)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        foreach (var key in keys)
        {
            Assert.True(element.TryGetProperty(key, out _), $"missing '{key}' in {element}");
        }

        return element;
    }
}
