using System.Net;
using System.Text;

namespace HackerNews.UnitTests.TestSupport;

/// <summary>
/// Serves canned responses keyed by request path so client tests never touch the network.
/// </summary>
internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    private readonly List<string> _requests = [];

    public IReadOnlyList<string> Requests => _requests;

    /// <summary>Responds with the given JSON body for every request.</summary>
    public static StubHttpMessageHandler Returning(string json) =>
        new(_ => Json(json));

    /// <summary>Matches on a substring of the request path; unmatched paths return 404.</summary>
    public static StubHttpMessageHandler Routing(IReadOnlyDictionary<string, string> routes) =>
        new(request =>
        {
            var path = request.RequestUri!.AbsoluteUri;
            var match = routes.FirstOrDefault(route => path.Contains(route.Key, StringComparison.Ordinal));
            return match.Value is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(match.Value);
        });

    public static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add(request.RequestUri!.AbsoluteUri);
        }

        return Task.FromResult(responder(request));
    }
}
