using GovernanceAuditor.Core.Model;
using GovernanceAuditor.Core.Options;

namespace GovernanceAuditor.Core.Analyzers;

/// <summary>
/// Fonctions utilitaires partagées par les analyseurs de branches.
/// </summary>
internal static class BranchRules
{
    private const string RefHeadsPrefix = "refs/heads/";

    /// <summary>Renvoie le nom court d'une branche (sans le préfixe « refs/heads/ »).</summary>
    public static string ShortName(string branchRefOrName)
    {
        ArgumentNullException.ThrowIfNull(branchRefOrName);
        return branchRefOrName.StartsWith(RefHeadsPrefix, StringComparison.OrdinalIgnoreCase)
            ? branchRefOrName[RefHeadsPrefix.Length..]
            : branchRefOrName;
    }

    /// <summary>Indique si la branche fait partie des branches protégées (comparaison insensible à la casse).</summary>
    public static bool IsProtected(BranchInfo branch, RulesOptions rules)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(rules);
        return rules.ProtectedBranches.Any(p => string.Equals(p, branch.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Indique si la branche est inactive depuis au moins <paramref name="inactiveDays"/> jours.</summary>
    public static bool IsInactive(BranchInfo branch, DateTimeOffset now, int inactiveDays)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return branch.LastCommitDate is { } last && last < now.AddDays(-inactiveDays);
    }

    /// <summary>
    /// Indique si une branche est « abandonnée » : non protégée, inactive depuis le seuil
    /// d'abandon, non mergée (AheadCount &gt; 0) et sans pull request active la prenant pour source.
    /// </summary>
    public static bool IsAbandoned(BranchInfo branch, RepositoryContext context, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(context);

        if (IsProtected(branch, context.Rules))
        {
            return false;
        }

        if (!IsInactive(branch, now, context.Rules.BranchAbandonedDays))
        {
            return false;
        }

        // AheadCount == 0 signifie que la branche est entièrement contenue dans la branche par défaut.
        if (branch.AheadCount == 0)
        {
            return false;
        }

        var hasActivePullRequest = context.PullRequests.Any(pr =>
            string.Equals(pr.Status, "active", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ShortName(pr.SourceBranch), branch.Name, StringComparison.OrdinalIgnoreCase));

        return !hasActivePullRequest;
    }

    /// <summary>Nombre de jours d'inactivité de la branche (0 si la date est inconnue).</summary>
    public static int InactiveDays(BranchInfo branch, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return branch.LastCommitDate is { } last ? Math.Max(0, (int)(now - last).TotalDays) : 0;
    }
}
