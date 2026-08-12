using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;

namespace GovernanceAuditor.Core.Analyzers;

/// <summary>
/// Vérifie, pour chaque branche protégée présente dans le dépôt, la présence des
/// policies de gouvernance attendues.
/// </summary>
/// <remarks>
/// Note importante : Azure DevOps n'a PAS de type de policy « protection contre le push direct ».
/// Ce blocage est un effet de la présence d'au moins une policy de branche activée et bloquante.
/// L'analyseur considère donc la protection présente dès qu'une telle policy existe sur la branche.
/// </remarks>
public sealed class BranchPolicyAnalyzer : IRepositoryAnalyzer
{
    private const string BuildValidationTypeId = "0609b952-1397-4640-95ec-e00a01b2c241";
    private const string MinimumReviewersTypeId = "fa4e907d-c16b-4a4c-9dfa-4906e5d171dd";
    private const string CommentRequirementsTypeId = "c6a1889d-b943-4856-b76f-9e46bb6b0df2";

    /// <inheritdoc />
    public Task<IReadOnlyCollection<AuditFinding>> AnalyzeAsync(RepositoryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var findings = new List<AuditFinding>();

        foreach (var branch in context.Branches)
        {
            if (!BranchRules.IsProtected(branch, context.Rules))
            {
                continue;
            }

            var branchPolicies = PoliciesFor(context, branch);
            findings.AddRange(EvaluateBranch(context, branch.Name, branchPolicies));
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }

    private static List<PolicyInfo> PoliciesFor(RepositoryContext context, BranchInfo branch)
    {
        return context.Policies
            .Where(p => p.Enabled)
            .Where(p => p.ScopeRepositoryId is null ||
                        string.Equals(p.ScopeRepositoryId, context.Repository.Id, StringComparison.OrdinalIgnoreCase))
            .Where(p => p.ScopeRefName is null ||
                        string.Equals(BranchRules.ShortName(p.ScopeRefName), branch.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static IEnumerable<AuditFinding> EvaluateBranch(RepositoryContext context, string branchName, List<PolicyInfo> policies)
    {
        var repository = context.Repository.Name;

        if (!HasType(policies, BuildValidationTypeId))
        {
            yield return Critical(repository, branchName, "MissingBuildValidation",
                "Aucune policy de validation par build sur la branche protégée.",
                "Créer une policy de build validation obligatoire sur cette branche.");
        }

        var minReviewers = Find(policies, MinimumReviewersTypeId);
        if (minReviewers is null)
        {
            yield return Critical(repository, branchName, "MissingMinimumReviewers",
                "Aucune policy de nombre minimal de relecteurs sur la branche protégée.",
                "Créer une policy exigeant un nombre minimal de relecteurs.");
        }
        else if (minReviewers.MinimumApproverCount is { } count && count < context.Rules.RequiredReviewers)
        {
            yield return new AuditFinding
            {
                Severity = Severity.Warning,
                Category = "InsufficientReviewers",
                Repository = repository,
                Branch = branchName,
                Message = $"Nombre minimal de relecteurs ({count}) inférieur au seuil attendu ({context.Rules.RequiredReviewers}).",
                Recommendation = $"Porter le nombre minimal de relecteurs à au moins {context.Rules.RequiredReviewers}.",
            };
        }

        if (!policies.Any(p => p.Blocking))
        {
            yield return Critical(repository, branchName, "MissingDirectPushProtection",
                "Aucune policy bloquante : les poussées directes ne sont pas empêchées.",
                "Ajouter au moins une policy de branche bloquante pour imposer le passage par pull request.");
        }

        if (!HasType(policies, CommentRequirementsTypeId))
        {
            yield return new AuditFinding
            {
                Severity = Severity.Warning,
                Category = "MissingCommentResolution",
                Repository = repository,
                Branch = branchName,
                Message = "Aucune policy de résolution obligatoire des commentaires.",
                Recommendation = "Activer la policy exigeant la résolution des commentaires avant complétion.",
            };
        }
    }

    private static bool HasType(IEnumerable<PolicyInfo> policies, string typeId) =>
        policies.Any(p => string.Equals(p.PolicyTypeId, typeId, StringComparison.OrdinalIgnoreCase));

    private static PolicyInfo? Find(IEnumerable<PolicyInfo> policies, string typeId) =>
        policies.FirstOrDefault(p => string.Equals(p.PolicyTypeId, typeId, StringComparison.OrdinalIgnoreCase));

    private static AuditFinding Critical(string repository, string branch, string category, string message, string recommendation) =>
        new()
        {
            Severity = Severity.Critical,
            Category = category,
            Repository = repository,
            Branch = branch,
            Message = message,
            Recommendation = recommendation,
        };
}
