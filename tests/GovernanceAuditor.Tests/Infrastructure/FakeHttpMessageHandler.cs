using System.Net;
using System.Text;

namespace GovernanceAuditor.Tests.Infrastructure;

/// <summary>
/// Handler HTTP factice : route les requêtes via une fonction fournie par le test
/// et enregistre les chemins appelés (utile pour vérifier la pagination).
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    /// <summary>Chemins (PathAndQuery) des requêtes reçues, dans l'ordre.</summary>
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.PathAndQuery);
        return Task.FromResult(_responder(request));
    }

    /// <summary>Construit une réponse 200 JSON, avec éventuellement un token de continuation.</summary>
    public static HttpResponseMessage Json(string body, string? continuationToken = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (continuationToken is not null)
        {
            response.Headers.TryAddWithoutValidation("x-ms-continuationtoken", continuationToken);
        }

        return response;
    }
}
