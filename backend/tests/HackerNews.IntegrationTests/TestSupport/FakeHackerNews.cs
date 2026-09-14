using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace HackerNews.IntegrationTests.TestSupport;

/// <summary>
/// A stand-in for the Hacker News API. Every integration test runs against this, so the suite is
/// deterministic and never touches the network.
/// </summary>
internal sealed class FakeHackerNews : HttpMessageHandler
{
    /// <summary>Ids are newest-first, matching the real feed.</summary>
    public const int StoryCount = 30;

    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly HttpStatusCode _idListStatus;
    private int _idListRequests;
    private int _itemRequests;

    private FakeHackerNews(HttpStatusCode idListStatus) => _idListStatus = idListStatus;

    public static FakeHackerNews Healthy() => new(HttpStatusCode.OK);

    /// <summary>Simulates the upstream being down: the id list always fails.</summary>
    public static FakeHackerNews Unreachable() => new(HttpStatusCode.InternalServerError);

    public int IdListRequests => Volatile.Read(ref _idListRequests);

    public int ItemRequests => Volatile.Read(ref _itemRequests);

    /// <summary>Every third story mentions Rust, so search has a predictable target.</summary>
    public static string TitleFor(int id) =>
        id % 3 == 0 ? $"Rust story {id}" : $"Other story {id}";

    /// <summary>Every fifth story is a text post with no hyperlink.</summary>
    public static string? UrlFor(int id) =>
        id % 5 == 0 ? null : $"https://example.com/{id}";

    public static int RustStoryCount =>
        Enumerable.Range(1, StoryCount).Count(id => TitleFor(id).Contains("Rust", StringComparison.Ordinal));

    public static int LinklessStoryCount =>
        Enumerable.Range(1, StoryCount).Count(id => UrlFor(id) is null);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("newstories.json", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _idListRequests);

            if (_idListStatus != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(_idListStatus));
            }

            // Newest first, exactly as the real endpoint orders them.
            var ids = Enumerable.Range(1, StoryCount).Reverse();
            return Task.FromResult(Json($"[{string.Join(',', ids)}]"));
        }

        var idSegment = path.Split('/').Last().Replace(".json", string.Empty, StringComparison.Ordinal);
        if (!int.TryParse(idSegment, CultureInfo.InvariantCulture, out var id))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        Interlocked.Increment(ref _itemRequests);

        if (id is < 1 or > StoryCount)
        {
            // The real API answers a bare "null" for ids it does not know.
            return Task.FromResult(Json("null"));
        }

        return Task.FromResult(Json(ItemJson(id)));
    }

    private static string ItemJson(int id)
    {
        var url = UrlFor(id);
        var fields = new List<string>
        {
            $"\"id\":{id}",
            $"\"title\":{JsonSerializer.Serialize(TitleFor(id))}",
            $"\"by\":\"author{id}\"",
            $"\"time\":{Base.AddMinutes(id).ToUnixTimeSeconds()}",
            $"\"score\":{id * 2}",
            $"\"descendants\":{id}",
            "\"type\":\"story\""
        };

        // Linkless stories omit the field entirely, just as Ask HN posts do upstream.
        if (url is not null)
        {
            fields.Add($"\"url\":{JsonSerializer.Serialize(url)}");
        }

        return $"{{{string.Join(',', fields)}}}";
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
}
