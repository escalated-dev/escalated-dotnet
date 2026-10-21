using Escalated.Models;
using Escalated.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The knowledge base admin pages, with escalated-laravel's routes and props:
/// <c>KnowledgeBase/Articles/Index</c> and <c>Form</c> (<c>ArticleController</c>,
/// <c>admin/kb-articles</c>) and <c>KnowledgeBase/Categories/Index</c>
/// (<c>ArticleCategoryController</c>, <c>admin/kb-categories</c>).
/// </summary>
public partial class AdminPageController
{
    private static readonly HashSet<string> ArticleStatuses = new() { "draft", "published" };

    // --- Articles ---

    [HttpGet("kb-articles")]
    public async Task<IActionResult> KnowledgeBaseArticles(CancellationToken ct)
    {
        string? Get(string key) => string.IsNullOrWhiteSpace(Request.Query[key]) ? null : Request.Query[key].ToString().Trim();
        var search = Get("search");
        var status = Get("status");
        var categoryId = int.TryParse(Get("category_id"), out var c) ? c : (int?)null;

        var query = _db.Articles.AsNoTracking().Include(a => a.Category).AsQueryable();
        if (search is not null)
        {
            var needle = search.ToLowerInvariant();
            query = query.Where(a => a.Title.ToLower().Contains(needle) || (a.Body != null && a.Body.ToLower().Contains(needle)));
        }
        if (status is not null) query = query.Where(a => a.Status == status);
        if (categoryId is not null) query = query.Where(a => a.CategoryId == categoryId);

        const int perPage = 20;
        var page = Math.Max(1, int.TryParse(Get("page"), out var p) ? p : 1);
        var total = await query.CountAsync(ct);
        var articles = await query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * perPage).Take(perPage).ToListAsync(ct);

        var rows = new List<object?>();
        foreach (var article in articles)
        {
            var row = Article(article);
            row["author"] = await _present.UserAsync(article.AuthorId, ct);
            rows.Add(row);
        }

        var filters = new Dictionary<string, string?>();
        if (search is not null) filters["search"] = search;
        if (status is not null) filters["status"] = status;
        if (Get("category_id") is { } category) filters["category_id"] = category;

        return UiResults.Render("Escalated/Admin/KnowledgeBase/Articles/Index", new
        {
            articles = UiPresenter.Paginator(rows, total, page, perPage, Request.Path, filters),
            categories = await CategoryOptionsAsync(ct),
            filters,
        });
    }

    [HttpGet("kb-articles/create")]
    public async Task<IActionResult> CreateKnowledgeBaseArticle(CancellationToken ct) =>
        UiResults.Render("Escalated/Admin/KnowledgeBase/Articles/Form", new Dictionary<string, object?>
        {
            ["article"] = null,
            ["categories"] = await CategoryOptionsAsync(ct),
        });

    [HttpGet("kb-articles/{id:int}/edit")]
    public async Task<IActionResult> EditKnowledgeBaseArticle(int id, CancellationToken ct)
    {
        var article = await _db.Articles.AsNoTracking().Include(a => a.Category).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (article is null) return NotFound();
        return UiResults.Render("Escalated/Admin/KnowledgeBase/Articles/Form", new { article = Article(article), categories = await CategoryOptionsAsync(ct) });
    }

    /// <summary>An article as the knowledge base pages read it, with its category's id, name and slug.</summary>
    internal static Dictionary<string, object?> Article(Article article) => new()
    {
        ["id"] = article.Id,
        ["title"] = article.Title,
        ["slug"] = article.Slug,
        ["body"] = article.Body,
        ["status"] = article.Status,
        ["category_id"] = article.CategoryId,
        ["category"] = article.Category is { } category
            ? new Dictionary<string, object?> { ["id"] = category.Id, ["name"] = category.Name, ["slug"] = category.Slug }
            : null,
        ["author_id"] = article.AuthorId,
        ["view_count"] = article.ViewCount,
        ["helpful_count"] = article.HelpfulCount,
        ["not_helpful_count"] = article.NotHelpfulCount,
        ["published_at"] = article.PublishedAt,
        ["created_at"] = article.CreatedAt,
        ["updated_at"] = article.UpdatedAt,
    };

    /// <summary>Categories as <c>{id, name}</c> in position order, as Laravel's <c>ordered()</c> scope.</summary>
    private async Task<List<Dictionary<string, object?>>> CategoryOptionsAsync(CancellationToken ct) =>
        (await _db.ArticleCategories.AsNoTracking().OrderBy(c => c.Position).ThenBy(c => c.Name).ToListAsync(ct))
            .Select(c => new Dictionary<string, object?> { ["id"] = c.Id, ["name"] = c.Name }).ToList();

    [HttpPost("kb-articles")]
    public Task<IActionResult> StoreKnowledgeBaseArticle(CancellationToken ct) => SaveArticleAsync(null, ct);

    [HttpPut("kb-articles/{id:int}")]
    public Task<IActionResult> UpdateKnowledgeBaseArticle(int id, CancellationToken ct) => SaveArticleAsync(id, ct);

    private async Task<IActionResult> SaveArticleAsync(int? id, CancellationToken ct)
    {
        var article = id is null ? new Article { CreatedAt = DateTime.UtcNow, AuthorId = CurrentUser() } : await _db.Articles.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (article is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();
        var title = input.String("title");
        var status = input.String("status");
        if (title is null) errors["title"] = "The title field is required.";
        else if (title.Length > 255) errors["title"] = "The title may not be greater than 255 characters.";
        if (status is null || !ArticleStatuses.Contains(status)) errors["status"] = "The selected status is invalid.";
        var categoryId = input.Int("category_id");
        if (input.String("category_id") is not null
            && (categoryId is null || !await _db.ArticleCategories.AnyAsync(c => c.Id == categoryId, ct)))
        {
            errors["category_id"] = "The selected category is invalid.";
        }

        var slug = input.String("slug") is { } given ? Department.GenerateSlug(given) : title is null ? null : Department.GenerateSlug(title);
        if (slug is { Length: > 255 }) errors["slug"] = "The slug may not be greater than 255 characters.";
        else if (slug is not null && await _db.Articles.AnyAsync(a => a.Slug == slug && a.Id != article.Id, ct))
        {
            errors["slug"] = "The slug has already been taken.";
        }

        var formUrl = id is null ? $"{Prefix}/admin/kb-articles/create" : $"{Prefix}/admin/kb-articles/{id}/edit";
        if (errors.Count > 0) return UiResults.Back(HttpContext, formUrl, errors: errors);

        article.Title = title!;
        article.Slug = slug!;
        article.Body = input.String("body");
        article.Status = status!;
        article.CategoryId = categoryId;
        if (status == "published" && article.PublishedAt is null) article.PublishedAt = DateTime.UtcNow;
        article.UpdatedAt = DateTime.UtcNow;
        if (id is null) _db.Articles.Add(article);
        await _db.SaveChangesAsync(ct);

        return new UiRedirectResult($"{Prefix}/admin/kb-articles") { Success = id is null ? "Article created." : "Article updated." };
    }

    [HttpDelete("kb-articles/{id:int}")]
    public async Task<IActionResult> DestroyKnowledgeBaseArticle(int id, CancellationToken ct)
    {
        var article = await _db.Articles.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (article is null) return NotFound();

        _db.Articles.Remove(article);
        await _db.SaveChangesAsync(ct);
        return new UiRedirectResult($"{Prefix}/admin/kb-articles") { Success = "Article deleted." };
    }

    // --- Categories ---

    [HttpGet("kb-categories")]
    public async Task<IActionResult> KnowledgeBaseCategories(CancellationToken ct)
    {
        var categories = await _db.ArticleCategories.AsNoTracking().OrderBy(c => c.Position).ThenBy(c => c.Name)
            .Select(c => new { Category = c, Articles = _db.Articles.Count(a => a.CategoryId == c.Id) })
            .ToListAsync(ct);

        return UiResults.Render("Escalated/Admin/KnowledgeBase/Categories/Index", new
        {
            categories = categories.Select(row =>
            {
                var item = Category(row.Category);
                item["articles_count"] = row.Articles;
                return item;
            }).ToList(),
        });
    }

    internal static Dictionary<string, object?> Category(ArticleCategory category) => new()
    {
        ["id"] = category.Id,
        ["name"] = category.Name,
        ["slug"] = category.Slug,
        ["description"] = category.Description,
        ["parent_id"] = category.ParentId,
        ["position"] = category.Position,
        ["created_at"] = category.CreatedAt,
        ["updated_at"] = category.UpdatedAt,
    };

    [HttpPost("kb-categories")]
    public Task<IActionResult> StoreKnowledgeBaseCategory(CancellationToken ct) => SaveCategoryAsync(null, ct);

    [HttpPut("kb-categories/{id:int}")]
    public Task<IActionResult> UpdateKnowledgeBaseCategory(int id, CancellationToken ct) => SaveCategoryAsync(id, ct);

    private async Task<IActionResult> SaveCategoryAsync(int? id, CancellationToken ct)
    {
        var category = id is null ? new ArticleCategory { CreatedAt = DateTime.UtcNow } : await _db.ArticleCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();
        var name = input.String("name");
        var description = input.String("description");
        if (name is null) errors["name"] = "The name field is required.";
        else if (name.Length > 255) errors["name"] = "The name may not be greater than 255 characters.";
        if (description is { Length: > 1000 }) errors["description"] = "The description may not be greater than 1000 characters.";

        var parentId = input.Int("parent_id");
        if (input.String("parent_id") is not null)
        {
            if (parentId is null || !await _db.ArticleCategories.AnyAsync(c => c.Id == parentId, ct)) errors["parent_id"] = "The selected parent is invalid.";
            else if (id is not null && await IsSelfOrDescendantAsync(parentId.Value, id.Value, ct)) errors["parent_id"] = "A category cannot be placed under itself.";
        }

        var position = input.Int("position");
        if (input.String("position") is not null && position is not >= 0) errors["position"] = "The position must be at least 0.";

        var slug = input.String("slug") is { } given ? Department.GenerateSlug(given) : name is null ? null : Department.GenerateSlug(name);
        if (slug is { Length: > 255 }) errors["slug"] = "The slug may not be greater than 255 characters.";
        else if (slug is not null && await _db.ArticleCategories.AnyAsync(c => c.Slug == slug && c.Id != category.Id, ct))
        {
            errors["slug"] = "The slug has already been taken.";
        }

        var index = $"{Prefix}/admin/kb-categories";
        if (errors.Count > 0) return UiResults.Back(HttpContext, index, errors: errors);

        category.Name = name!;
        category.Slug = slug!;
        category.Description = description;
        category.ParentId = parentId;
        category.Position = position ?? 0;
        category.UpdatedAt = DateTime.UtcNow;
        if (id is null) _db.ArticleCategories.Add(category);
        await _db.SaveChangesAsync(ct);

        return new UiRedirectResult(index) { Success = id is null ? "Category created." : "Category updated." };
    }

    /// <summary>Whether <paramref name="candidate"/> is <paramref name="categoryId"/> or sits below it.</summary>
    private async Task<bool> IsSelfOrDescendantAsync(int candidate, int categoryId, CancellationToken ct)
    {
        var parents = await _db.ArticleCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ParentId, ct);
        var seen = new HashSet<int>();
        for (int? current = candidate; current is { } node && seen.Add(node); current = parents.GetValueOrDefault(node))
        {
            if (node == categoryId) return true;
        }

        return false;
    }

    /// <summary>Deletes the category; its articles and subcategories stay, with no category or parent.</summary>
    [HttpDelete("kb-categories/{id:int}")]
    public async Task<IActionResult> DestroyKnowledgeBaseCategory(int id, CancellationToken ct)
    {
        var category = await _db.ArticleCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return NotFound();

        await _db.Articles.Where(a => a.CategoryId == id).ExecuteUpdateAsync(set => set.SetProperty(a => a.CategoryId, (int?)null), ct);
        await _db.ArticleCategories.Where(c => c.ParentId == id).ExecuteUpdateAsync(set => set.SetProperty(c => c.ParentId, (int?)null), ct);
        _db.ArticleCategories.Remove(category);
        await _db.SaveChangesAsync(ct);
        return new UiRedirectResult($"{Prefix}/admin/kb-categories") { Success = "Category deleted." };
    }
}
