using System.Text;

namespace Escalated.Models;

/// <summary>Turns a role name into its URL-safe slug: lowercase ASCII letters and digits joined by single hyphens.</summary>
public static class RoleSlug
{
    public static string From(string name)
    {
        var builder = new StringBuilder(name.Length);
        var pendingHyphen = false;
        foreach (var ch in name.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingHyphen && builder.Length > 0) builder.Append('-');
                builder.Append(ch);
                pendingHyphen = false;
            }
            else if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                pendingHyphen = true;
            }
        }
        var slug = builder.ToString();
        if (slug.Length > 90) slug = slug[..90].TrimEnd('-');
        return slug.Length == 0 ? "role" : slug;
    }
}
