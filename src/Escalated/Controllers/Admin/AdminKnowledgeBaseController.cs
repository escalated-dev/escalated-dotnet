using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Admin;

[ApiController]
[Route("support/admin/knowledge-base")]
public class AdminKnowledgeBaseController : ControllerBase
{
    private readonly KnowledgeBaseService _kbService;
    private readonly EscalatedDbContext _db;

    public AdminKnowledgeBaseController(KnowledgeBaseService kbService, EscalatedDbContext db)
    {
        _kbService = kbService;
        _db = db;
    }

    [HttpGet("articles")]
    public async Task<IActionResult> Articles([FromQuery] string? status)
    {
        var query = _db.Articles.Include(a => a.Category).AsQueryable();
        if (!string.IsNullOrEmpty(status))
            query = query.Where(a => a.Status == status);

        return Ok(await query.OrderByDescending(a => a.UpdatedAt).ToListAsync());
    }

    [HttpPost("articles")]
    public async Task<IActionResult> CreateArticle([FromBody] CreateArticleRequest request)
    {
        var article = await _kbService.CreateArticleAsync(
            request.Title, request.Body, request.CategoryId, request.AuthorId);
        return Ok(article);
    }

    [HttpPut("articles/{id:int}")]
    public async Task<IActionResult> UpdateArticle(int id, [FromBody] UpdateArticleRequest request)
    {
        var article = await _db.Articles.FindAsync(id);
        if (article == null) return NotFound();

        if (request.Title != null) article.Title = request.Title;
        if (request.Body != null) article.Body = request.Body;
        if (request.CategoryId.HasValue) article.CategoryId = request.CategoryId;
        article.UpdatedAt = DateTime.UtcNow;

        _db.Articles.Update(article);
        await _db.SaveChangesAsync();
        return Ok(article);
    }

    [HttpPost("articles/{id:int}/publish")]
    public async Task<IActionResult> Publish(int id)
    {
        var article = await _kbService.PublishArticleAsync(id);
        if (article == null) return NotFound();
        return Ok(article);
    }

    [HttpDelete("articles/{id:int}")]
    public async Task<IActionResult> DeleteArticle(int id)
    {
        var article = await _db.Articles.FindAsync(id);
        if (article == null) return NotFound();
        _db.Articles.Remove(article);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("categories")]
    public async Task<IActionResult> Categories()
    {
        return Ok(await _kbService.GetCategoriesAsync());
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        var category = await _kbService.CreateCategoryAsync(request.Name, request.ParentId);
        return Ok(category);
    }
}

public record CreateArticleRequest(string Title, string Body, int? CategoryId = null, int? AuthorId = null);
public record UpdateArticleRequest(string? Title = null, string? Body = null, int? CategoryId = null);
public record CreateCategoryRequest(string Name, int? ParentId = null);
