using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models.Newsletter;

[Table("escalated_newsletter_lists")]
public class NewsletterList
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required, MaxLength(16)]
    public string Kind { get; set; } = "static"; // "static" | "dynamic"

    [Column(TypeName = "json")]
    public string? FilterJson { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<NewsletterListMember> Members { get; set; } = new List<NewsletterListMember>();
}
