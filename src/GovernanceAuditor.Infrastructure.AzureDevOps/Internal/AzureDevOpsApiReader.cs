using System.Text.Json;
using GovernanceAuditor.Infrastructure.AzureDevOps.Dtos;

namespace GovernanceAuditor.Infrastructure.AzureDevOps.Internal;

/// <summary>
/// Effectue les lectures HTTP (GET) et agrège automatiquement les pages via
/// l'en-tête de continuation <c>x-ms-continuationtoken</c> d'Azure DevOps.
/// </summary>
internal sealed class AzureDevOpsApiReader
{
    private const string ContinuationHeader = "x-ms-continuationtoken";

    private readonly HttpClient _httpClient;

    public AzureDevOpsApiReader(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    /// <summary>
    /// Lit une liste paginée. Suit les pages tant qu'un token de continuation est renvoyé,
    /// et concatène les éléments <c>value[]</c>.
    /// </summary>
    public async Task<IReadOnlyList<T>> GetListAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUrl);

        var results = new List<T>();
        string? continuation = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = continuation is null
                ? relativeUrl
                : $"{relativeUrl}&continuationToken={Uri.EscapeDataString(continuation)}";

            using var response = await _httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var page = await JsonSerializer
                    .DeserializeAsync<ListResponse<T>>(stream, AzureDevOpsJson.Options, cancellationToken)
                    .ConfigureAwait(false);

                if (page is { Value.Count: > 0 })
                {
                    results.AddRange(page.Value);
                }
            }

            continuation = response.Headers.TryGetValues(ContinuationHeader, out var values)
                ? values.FirstOrDefault()
                : null;
        }
        while (!string.IsNullOrEmpty(continuation));

        return results;
    }
}
