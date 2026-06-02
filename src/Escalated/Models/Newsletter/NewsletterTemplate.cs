using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models.Newsletter;

[Table("escalated_newsletter_templates")]
public class NewsletterTemplate
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Theme { get; set; } = "default";

    [MaxLength(998)]
    public string? SubjectTemplate { get; set; }

    [Required]
    public string BodyMarkdown { get; set; } = string.Empty;

    [Column(TypeName = "json")]
    public string? MergeFieldsSchema { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
