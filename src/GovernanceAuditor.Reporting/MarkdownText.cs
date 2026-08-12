using System.Text;

namespace GovernanceAuditor.Reporting;

/// <summary>
/// Neutralise les textes provenant d'Azure DevOps (noms de branches, titres de PR,
/// messages d'erreur) avant leur insertion dans le rapport.
/// </summary>
/// <remarks>
/// Ces valeurs sont contrôlables par un utilisateur du serveur (un nom de branche
/// est du texte libre). Sans traitement, elles permettraient d'injecter de la
/// structure Markdown (titres, listes, liens, images, HTML) ou de casser la mise en
/// forme via des retours à la ligne : le rapport doit rester fidèle et lisible.
/// </remarks>
internal static class MarkdownText
{
    /// <summary>
    /// Caractères capables de créer de la structure ou un lien en Markdown.
    /// Les parenthèses sont volontairement exclues : sans crochet ouvrant (échappé ici),
    /// elles ne peuvent pas former de lien, et les échapper nuirait à la lisibilité.
    /// </summary>
    private const string Structural = @"\`*_[]<>#|~";

    /// <summary>
    /// Rend un texte sûr pour une insertion « en ligne » : les caractères de contrôle
    /// et retours à la ligne sont remplacés par des espaces, les caractères structurants
    /// sont échappés.
    /// </summary>
    public static string Inline(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        var previousWasSpace = false;

        foreach (var character in value)
        {
            // Retours à la ligne et caractères de contrôle : réduits à un espace unique.
            if (char.IsControl(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            previousWasSpace = character == ' ';

            if (Structural.Contains(character, StringComparison.Ordinal))
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString().Trim();
    }
}
