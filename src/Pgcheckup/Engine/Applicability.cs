using Pgcheckup.Checks;

namespace Pgcheckup.Engine;

/// <summary>Decides whether a check can run on a server, before it runs.</summary>
public static class Applicability
{
    /// <summary>The privilege a check declares when it reads sequence counters.</summary>
    public const string SelectOnSequences = "select_on_sequences";

    /// <summary>Says why a check can't run here, if it can't.</summary>
    /// <param name="check">The check.</param>
    /// <param name="context">The server and role.</param>
    /// <returns>
    /// <see langword="null"/> when the check can run. Otherwise one reason, checked in this order:
    /// the Postgres version, the managed provider, then the role's privileges.
    /// </returns>
    public static string? SkipReason(CheckDefinition check, ServerContext context)
    {
        if (context.Major < check.MinVersion)
        {
            return $"needs Postgres {check.MinVersion} or later";
        }

        if (context.Provider is { } provider && check.SkipOn.Contains(provider.Id))
        {
            return $"managed by {provider.Name}";
        }

        var missing = check.Privileges.Where(p => !context.Privileges.Contains(p)).ToList();
        if (missing.Contains(SelectOnSequences))
        {
            return "can't read sequence counters; see pgcheckup grant";
        }

        return missing.Count > 0 ? $"needs {string.Join(" and ", missing)}" : null;
    }
}
