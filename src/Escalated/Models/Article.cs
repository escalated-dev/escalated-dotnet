using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

public class Article
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Slug { get; set; } = string.Empty;

    public string? Body { get; set; }

    public int? CategoryId { get; set; }

    public int? AuthorId { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "draft"; // draft, published

    public int ViewCount { get; set; }
    public int HelpfulCount { get; set; }
    public int NotHelpfulCount { get; set; }

    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(CategoryId))]
    public ArticleCategory? Category { get; set; }
}

public class ArticleCategory
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int? ParentId { get; set; }

    public int Position { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ParentId))]
    public ArticleCategory? Parent { get; set; }
    public ICollection<ArticleCategory> Children { get; set; } = new List<ArticleCategory>();
    public ICollection<Article> Articles { get; set; } = new List<Article>();
}
