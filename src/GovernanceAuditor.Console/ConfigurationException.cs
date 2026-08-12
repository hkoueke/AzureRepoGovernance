namespace GovernanceAuditor.Console;

/// <summary>
/// Configuration invalide détectée en dehors du mécanisme de validation des options
/// (transport non sécurisé, format de rapport inconnu…). Conduit au code de sortie 2.
/// </summary>
public sealed class ConfigurationException : Exception
{
    /// <summary>Crée une exception avec le motif de rejet.</summary>
    public ConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Crée une exception sans message spécifique.</summary>
    public ConfigurationException()
    {
    }

    /// <summary>Crée une exception avec le motif de rejet et la cause sous-jacente.</summary>
    public ConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
