using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;

namespace GovernanceAuditor.Console;

/// <summary>
/// Remplace les données personnelles (nom, adresse e-mail) par des pseudonymes
/// stables au sein d'une exécution, lorsque <c>Privacy:RedactActors</c> est activé.
/// </summary>
/// <remarks>
/// Le sel est tiré aléatoirement à chaque exécution. Sans lui, un simple condensat
/// serait réversible : l'espace des adresses e-mail d'une entreprise est petit et
/// se force par recherche exhaustive. Conséquence assumée : les pseudonymes ne sont
/// pas comparables d'un rapport à l'autre — c'est précisément le but.
/// </remarks>
internal sealed class ActorRedactor
{
    private readonly byte[] _salt = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<string, string> _pseudonyms = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Applique la pseudonymisation à l'ensemble des anomalies d'un résultat.</summary>
    public AuditRunResult Redact(AuditRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var redacted = result.Findings
            .Select(finding => finding with
            {
                Actor = Pseudonymise(finding.Actor),
                ActorEmail = finding.ActorEmail is null ? null : "(masquée)",
            })
            .ToList();

        return result with { Findings = redacted };
    }

    private string? Pseudonymise(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            return actor;
        }

        if (_pseudonyms.TryGetValue(actor, out var existing))
        {
            return existing;
        }

        var pseudonym = "Acteur-" + Fingerprint(actor);
        _pseudonyms[actor] = pseudonym;
        return pseudonym;
    }

    private string Fingerprint(string value)
    {
        var payload = Encoding.UTF8.GetBytes(value.ToUpperInvariant());
        var digest = HMACSHA256.HashData(_salt, payload);

        var builder = new StringBuilder(8);
        for (var i = 0; i < 4; i++)
        {
            builder.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
