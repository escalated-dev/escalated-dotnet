using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Escalated.Services;

public class MentionService
{
    private static readonly Regex MentionRegex = new(@"@(\w+(?:\.\w+)*)", RegexOptions.Compiled);

    public List<string> ExtractMentions(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        var matches = MentionRegex.Matches(text);
        return matches.Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    public string ExtractUsernameFromEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return "";
        var parts = email.Split('@');
        return parts.Length > 0 ? parts[0] : email;
    }
}
