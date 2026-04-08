using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Escalated.Models;

public class Role
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

    public bool IsSystem { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
    public ICollection<RoleUser> Users { get; set; } = new List<RoleUser>();
}

public class Permission
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Group { get; set; }

    public ICollection<Role> Roles { get; set; } = new List<Role>();
}

public class RolePermission
{
    public int RoleId { get; set; }
    public int PermissionId { get; set; }
    public Role? Role { get; set; }
    public Permission? Permission { get; set; }
}

public class RoleUser
{
    public int RoleId { get; set; }
    public int UserId { get; set; }
    public Role? Role { get; set; }
}
