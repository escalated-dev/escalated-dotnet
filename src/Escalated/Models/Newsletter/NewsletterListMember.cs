using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Models.Newsletter;

[Table("escalated_newsletter_list_members")]
[Index(nameof(ListId), nameof(ContactId), IsUnique = true)]
public class NewsletterListMember
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ListId { get; set; }

    [Required]
    public int ContactId { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public string? AddedBy { get; set; }

    [ForeignKey(nameof(ListId))]
    public NewsletterList? List { get; set; }

    [ForeignKey(nameof(ContactId))]
    public Contact? Contact { get; set; }
}
