namespace Escalated.Models;

/// <summary>
/// Maps legacy proficiency strings persisted before the canonical 1..5 numeric scale (#58).
/// </summary>
public static class LegacySkillProficiency
{
    public static int Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 3;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "beginner":
                return 1;
            case "intermediate":
                return 3;
            case "expert":
                return 5;
            default:
                return 3;
        }
    }
}
