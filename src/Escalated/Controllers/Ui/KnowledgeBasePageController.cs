using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Controllers.Widget;
using Escalated.Data;
using Escalated.Services;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The public knowledge base (<c>Escalated/Customer/KnowledgeBase/Index</c> and
/// <c>Article</c>) and its feedback post, with escalated-laravel's
/// <c>Customer/KnowledgeBaseController</c> routes and props. Only published
/// articles show. With <c>Escalated:KnowledgeBase:Enabled</c> off the pages are a
/// 404; with the <c>knowledge_base_public</c> setting off, a visitor who is not
/// signed in gets a 403.
/// </summary>
[Route("support/kb")]
[AllowAnonymous]
[EscalatedPage]
public class KnowledgeBasePageController : ControllerBase
{
    private readonly EscalatedDbContext _db;
    private readonly SettingsService _settings;
    private readonly KnowledgeBaseService _knowledgeBase;
    private readonly EscalatedOptions _options;

    public KnowledgeBasePageController(EscalatedDbContext db, SettingsService settings, KnowledgeBaseService knowledgeBase,
        IOptions<EscalatedOptions> options)
    {
        _db = db;
        _settings = settings;
        _knowledgeBase = knowledgeBase;
        _options = options.Value;
    }

    private bool FeedbackEnabled => _options.KnowledgeBase.AllowFeedback;

    /// <summary>Null when the visitor may read the knowledge base; otherwise the refusal.</summary>
    private async Task<IActionResult?> RefusalAsync(CancellationToken ct)
    {
        if (!_options.KnowledgeBase.Enabled) return NotFound();
        if (this.CurrentUserId() is null && !await GuestSettings.IsOnAsync(_settings, "knowledge_base_public", ct))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return null;
    }

    private IQueryable<Models.Article> Published() => _db.Articles.AsNoTracking().Where(a => a.Status == "published");

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (await RefusalAsync(ct) is { } refusal) return refusal;

        string? Get(string key) => string.IsNullOrWhiteSpace(Request.Query[key]) ? null : Request.Query[key].ToString().Trim();
        var search = Get("search");
        var category = Get("category");

        var categories = await _db.ArticleCategories.AsNoTracking().Where(c => c.ParentId == null)
            .OrderBy(c => c.Position).ThenBy(c => c.Name)
            .Select(c => new { Category = c, Articles = _db.Articles.Count(a => a.CategoryId == c.Id && a.Status == "published") })
            .ToListAsync(ct);

        var query = Published().Include(a => a.Category).AsQueryable();
        if (search is not null)
        {
            var needle = search.ToLowerInvariant();
            query = query.Where(a => a.Title.ToLower().Contains(needle) || (a.Body != null && a.Body.ToLower().Contains(needle)));
        }
        if (category is not null)
        {
            var categoryId = int.TryParse(category, out var id) ? id : -1;
            query = query.Where(a => a.CategoryId == categoryId);
        }

        const int perPage = 15;
        var page = Math.Max(1, int.TryParse(Get("page"), out var p) ? p : 1);
        var total = await query.CountAsync(ct);
        var articles = await query.OrderByDescending(a => a.PublishedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * perPage).Take(perPage).ToListAsync(ct);

        var filters = new Dictionary<string, string?>();
        if (search is not null) filters["search"] = search;
        if (category is not null) filters["category"] = category;

        return UiResults.Render("Escalated/Customer/KnowledgeBase/Index", new
        {
            categories = categories.Select(row =>
            {
                var item = AdminPageController.Category(row.Category);
                item["articles_count"] = row.Articles;
                return item;
            }).ToList(),
            articles = UiPresenter.Paginator(articles.Select(a => (object?)AdminPageController.Article(a)).ToList(), total, page, perPage, Request.Path, filters),
            filters,
            feedbackEnabled = FeedbackEnabled,
        });
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Show(string slug, CancellationToken ct)
    {
        if (await RefusalAsync(ct) is { } refusal) return refusal;

        var article = await Published().Include(a => a.Category).FirstOrDefaultAsync(a => a.Slug == slug, ct);
        if (article is null) return NotFound();

        await _knowledgeBase.RecordViewAsync(article.Id, ct);
        article.ViewCount++;

        var related = await Published().Where(a => a.CategoryId == article.CategoryId && a.Id != article.Id)
            .OrderBy(a => a.Id).Take(5)
            .Select(a => new { a.Id, a.Title, a.Slug })
            .ToListAsync(ct);

        return UiResults.Render("Escalated/Customer/KnowledgeBase/Article", new
        {
            article = AdminPageController.Article(article),
            related = related.Select(r => new Dictionary<string, object?> { ["id"] = r.Id, ["title"] = r.Title, ["slug"] = r.Slug }).ToList(),
            feedbackEnabled = FeedbackEnabled,
        });
    }

    [HttpPost("{slug}/feedback")]
    public async Task<IActionResult> Feedback(string slug, CancellationToken ct)
    {
        if (await RefusalAsync(ct) is { } refusal) return refusal;
        if (!FeedbackEnabled) return NotFound();

        var article = await Published().FirstOrDefaultAsync(a => a.Slug == slug, ct);
        if (article is null) return NotFound();

        var back = $"/{_options.RoutePrefix.Trim('/')}/kb/{Uri.EscapeDataString(article.Slug)}";
        var input = await UiInput.ReadAsync(Request, ct);
        if (input.String("helpful")?.ToLowerInvariant() is not ("1" or "0" or "true" or "false"))
        {
            return UiResults.Back(HttpContext, back, errors: new Dictionary<string, string> { ["helpful"] = "The helpful field must be true or false." });
        }

        await _knowledgeBase.RecordFeedbackAsync(article.Id, input.Bool("helpful"), ct);
        return UiResults.Back(HttpContext, back, success: "Thank you for your feedback!");
    }
}
