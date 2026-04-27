using System.Net;
using System.Text;
using Escalated.Models;
using Escalated.Services.Email.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Escalated.Tests.Services.Email.Inbound;

/// <summary>
/// Tests for <see cref="AttachmentDownloader"/>. Uses a stub
/// <see cref="HttpMessageHandler"/> to control the provider response
/// without needing a live HTTP server.
/// </summary>
public class AttachmentDownloaderTests
{
    private class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            var response = Respond?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK);
            return Task.FromResult(response);
        }
    }

    private class RecordingStorage : IAttachmentStorage
    {
        public string Name => "memory";
        public List<(string Filename, string ContentType, byte[] Content)> Puts { get; } = new();
        public string ReturnPath { get; set; } = "/tmp/fake-path";

        public async Task<string> PutAsync(string filename, Stream content, string contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            Puts.Add((filename, contentType, ms.ToArray()));
            return ReturnPath;
        }
    }

    private static AttachmentDownloader CreateDownloader(
        StubHandler handler,
        RecordingStorage storage,
        Data.EscalatedDbContext db,
        AttachmentDownloaderOptions? options = null)
    {
        var http = new HttpClient(handler);
        return new AttachmentDownloader(
            http, storage, db,
            NullLogger<AttachmentDownloader>.Instance,
            options);
    }

    private static PendingAttachment Pending(
        string url,
        string name = "report.pdf",
        string contentType = "application/pdf")
        => new()
        {
            Name = name,
            ContentType = contentType,
            SizeBytes = null,
            DownloadUrl = url,
        };

    [Fact]
    public async Task DownloadAsync_HappyPath_PersistsAttachment()
    {
        var handler = new StubHandler
        {
            Respond = _ =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("hello pdf")),
                };
                resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
                return resp;
            },
        };
        var storage = new RecordingStorage { ReturnPath = "/store/report.pdf" };
        var db = TestHelpers.CreateInMemoryDb();
        var downloader = CreateDownloader(handler, storage, db);

        var a = await downloader.DownloadAsync(Pending("https://provider/att/1"), ticketId: 42, replyId: null);

        Assert.Equal("report.pdf", a.Filename);
        Assert.Equal("application/pdf", a.MimeType);
        Assert.Equal(9, a.Size);
        Assert.Equal("/store/report.pdf", a.Path);
        Assert.Equal("memory", a.Disk);
        Assert.Equal("ticket", a.AttachableType);
        Assert.Equal(42, a.AttachableId);
        Assert.Single(storage.Puts);
        Assert.Equal("hello pdf", Encoding.UTF8.GetString(storage.Puts[0].Content));
        Assert.Equal(1, await db.Attachments.CountAsync());
    }

    [Fact]
    public async Task DownloadAsync_SetsAttachableToReplyWhenReplyIdProvided()
    {
        var handler = new StubHandler();
        var storage = new RecordingStorage();
        var db = TestHelpers.CreateInMemoryDb();
        var downloader = CreateDownloader(handler, storage, db);

        var a = await downloader.DownloadAsync(Pending("https://x/y"), ticketId: 42, replyId: 7);

        Assert.Equal("reply", a.AttachableType);
        Assert.Equal(7, a.AttachableId);
    }

    [Fact]
    public async Task DownloadAsync_404_ThrowsAndDoesNotPersist()
    {
        var handler = new StubHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        var storage = new RecordingStorage();
        var db = TestHelpers.CreateInMemoryDb();
        var downloader = CreateDownloader(handler, storage, db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadAsync(Pending("https://x/missing"), 1, null));

        Assert.Empty(storage.Puts);
        Assert.Equal(0, await db.Attachments.CountAsync());
    }

    [Fact]
    public async Task DownloadAsync_OverSizeLimit_ThrowsTooLarge()
    {
        var handler = new StubHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[100]),
            },
        };
        var storage = new RecordingStorage();
        var db = TestHelpers.CreateInMemoryDb();
        var downloader = CreateDownloader(handler, storage, db,
            new AttachmentDownloaderOptions { MaxBytes = 10 });

        await Assert.ThrowsAsync<AttachmentTooLargeException>(() =>
            downloader.DownloadAsync(Pending("https://x/big"), 1, null));

        Assert.Empty(storage.Puts);
    }

    [Fact]
    public async Task DownloadAsync_SendsBasicAuthHeader()
    {
        var handler = new StubHandler();
        var storage = new RecordingStorage();
        var db = TestHelpers.CreateInMemoryDb();
        var downloader = CreateDownloader(handler, storage, db, new AttachmentDownloaderOptions
        {
            BasicAuth = new BasicAuth("api", "key-secret"),
        });

        await downloader.DownloadAsync(Pending("https://x/y"), 1, null);

        var auth = handler.LastRequest?.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Basic", auth!.Scheme);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter!));
        Assert.Equal("api:key-secret", decoded);
    }

    [Fact]
    public async Task DownloadAsync_MissingUrl_Throws()
    {
        var downloader = CreateDownloader(new StubHandler(), new RecordingStorage(), TestHelpers.CreateInMemoryDb());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadAsync(Pending(url: ""), 1, null));
    }

    [Fact]
    public async Task DownloadAsync_FallsBackToResponseContentType()
    {
        var handler = new StubHandler
        {
            Respond = _ =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3 }),
                };
                resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                return resp;
            },
        };
        var storage = new RecordingStorage();
        var db = TestHelpers.CreateInMemoryDb();
        var downloader = CreateDownloader(handler, storage, db);

        // ContentType on PendingAttachment is empty — should pick up from response.
        var pending = new PendingAttachment
        {
            Name = "img",
            ContentType = "",
            SizeBytes = null,
            DownloadUrl = "https://x/y",
        };
        var a = await downloader.DownloadAsync(pending, 1, null);

        Assert.Equal("image/png", a.MimeType);
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("/tmp/evil.txt", "evil.txt")]
    [InlineData("", "attachment")]
    [InlineData(null, "attachment")]
    [InlineData("..", "attachment")]
    public void SafeFilename_StripsPathSeparators(string? input, string expected)
    {
        Assert.Equal(expected, AttachmentDownloader.SafeFilename(input));
    }

    [Fact]
    public async Task DownloadAllAsync_ContinuesPastFailures()
    {
        var callCount = 0;
        var handler = new StubHandler
        {
            Respond = _ =>
            {
                callCount++;
                if (callCount == 2)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) };
            },
        };
        var storage = new RecordingStorage();
        var db = TestHelpers.CreateInMemoryDb();
        var downloader = CreateDownloader(handler, storage, db);

        var results = await downloader.DownloadAllAsync(
            new List<PendingAttachment>
            {
                Pending("https://x/1", name: "a"),
                Pending("https://x/2", name: "b"),
                Pending("https://x/3", name: "c"),
            },
            ticketId: 1,
            replyId: null);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].Succeeded);
        Assert.False(results[1].Succeeded);
        Assert.NotNull(results[1].Error);
        Assert.True(results[2].Succeeded);
        Assert.Equal(2, await db.Attachments.CountAsync());
    }

    [Fact]
    public async Task LocalFileAttachmentStorage_PutAsync_WritesFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "esc-tests-" + Guid.NewGuid());
        var storage = new LocalFileAttachmentStorage(root);

        using var content = new MemoryStream(Encoding.UTF8.GetBytes("payload"));
        var path = await storage.PutAsync("hello.txt", content, "text/plain");

        Assert.StartsWith(root, path);
        Assert.EndsWith("hello.txt", path);
        Assert.Equal("payload", await File.ReadAllTextAsync(path));

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void LocalFileAttachmentStorage_RejectsEmptyRoot()
    {
        Assert.Throws<ArgumentException>(() => new LocalFileAttachmentStorage(""));
    }

    [Fact]
    public async Task LocalFileAttachmentStorage_DifferentCallsProduceDifferentPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "esc-tests-" + Guid.NewGuid());
        var storage = new LocalFileAttachmentStorage(root);

        using var c1 = new MemoryStream(new byte[] { 1 });
        using var c2 = new MemoryStream(new byte[] { 2 });
        var p1 = await storage.PutAsync("x.txt", c1, "text/plain");
        // Nudge the clock so the timestamp prefix differs. Ticks resolution
        // is 100ns on Windows — a sub-millisecond delay is plenty.
        await Task.Delay(1);
        var p2 = await storage.PutAsync("x.txt", c2, "text/plain");

        Assert.NotEqual(p1, p2);

        Directory.Delete(root, recursive: true);
    }
}
