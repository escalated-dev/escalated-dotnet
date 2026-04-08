using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

public class ImportSourceMap
{
    [Key]
    public int Id { get; set; }

    [Required]
    public Guid ImportJobId { get; set; }

    [Required]
    [MaxLength(50)]
    public string EntityType { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string SourceId { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string EscalatedId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ImportJobId))]
    public ImportJob? ImportJob { get; set; }
}
