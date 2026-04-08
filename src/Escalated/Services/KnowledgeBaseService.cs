using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

public class KnowledgeBaseService
{
    private readonly EscalatedDbContext _db;

    public KnowledgeBaseService(EscalatedDbContext db)
    {
        _db = db;
    }

    public async Task<Article> CreateArticleAsync(string title, string body, int? categoryId = null,
        int? authorId = null, CancellationToken ct = default)
    {
        var slug = GenerateSlug(title);
        var article = new Article
        {
            Title = title,
            Slug = slug,
            Body = body,
            CategoryId = categoryId,
            AuthorId = authorId,
            Status = "draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Articles.Add(article);
        await _db.SaveChangesAsync(ct);
        return article;
    }

    public async Task<Article?> PublishArticleAsync(int articleId, CancellationToken ct = default)
    {
        var article = await _db.Articles.FindAsync(new object[] { articleId }, ct);
        if (article == null) return null;

        article.Status = "published";
        article.PublishedAt = DateTime.UtcNow;
        article.UpdatedAt = DateTime.UtcNow;
        _db.Articles.Update(article);
        await _db.SaveChangesAsync(ct);
        return article;
    }

    public async Task<List<Article>> SearchAsync(string query, CancellationToken ct = default)
    {
        return await _db.Articles
            .Where(a => a.Status == "published")
            .Where(a => a.Title.Contains(query) || (a.Body != null && a.Body.Contains(query)))
            .OrderByDescending(a => a.ViewCount)
            .Take(20)
            .ToListAsync(ct);
    }

    public async Task RecordViewAsync(int articleId, CancellationToken ct = default)
    {
        var article = await _db.Articles.FindAsync(new object[] { articleId }, ct);
        if (article != null)
        {
            article.ViewCount++;
            _db.Articles.Update(article);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task RecordFeedbackAsync(int articleId, bool helpful, CancellationToken ct = default)
    {
        var article = await _db.Articles.FindAsync(new object[] { articleId }, ct);
        if (article != null)
        {
            if (helpful) article.HelpfulCount++;
            else article.NotHelpfulCount++;
            _db.Articles.Update(article);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<ArticleCategory> CreateCategoryAsync(string name, int? parentId = null,
        CancellationToken ct = default)
    {
        var category = new ArticleCategory
        {
            Name = name,
            Slug = GenerateSlug(name),
            ParentId = parentId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ArticleCategories.Add(category);
        await _db.SaveChangesAsync(ct);
        return category;
    }

    public async Task<List<ArticleCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        return await _db.ArticleCategories
            .Include(c => c.Children)
            .Where(c => c.ParentId == null)
            .OrderBy(c => c.Position)
            .ToListAsync(ct);
    }

    private static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        return slug.Trim('-');
    }
}
