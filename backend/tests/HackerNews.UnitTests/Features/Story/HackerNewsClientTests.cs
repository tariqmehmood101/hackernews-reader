using HackerNews.Api.Features.Story.Services;
using HackerNews.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace HackerNews.UnitTests.Features.Story;

public sealed class HackerNewsClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HackerNewsClient CreateClient(StubHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://hn.test/v0/") },
            NullLogger<HackerNewsClient>.Instance);

    [Fact]
    public async Task Parses_a_complete_story()
    {
        var client = CreateClient(StubHttpMessageHandler.Returning(
            """
            {"id":8863,"title":"My YC app","url":"http://www.ycombinator.com","by":"dhouston",
             "time":1175714200,"score":104,"descendants":71,"type":"story"}
            """));

        var story = await client.GetStoryAsync(8863, Ct);

        story.ShouldNotBeNull();
        story.Id.ShouldBe(8863);
        story.Title.ShouldBe("My YC app");
        story.Url.ShouldBe("http://www.ycombinator.com");
        story.By.ShouldBe("dhouston");
        story.Score.ShouldBe(104);
        story.Descendants.ShouldBe(71);
        story.Time.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1175714200));
    }

    [Fact]
    public async Task Keeps_url_null_for_a_story_that_has_no_hyperlink()
    {
        // Ask HN posts carry no url at all. This is the edge case the brief calls out.
        var client = CreateClient(StubHttpMessageHandler.Returning(
            """{"id":121003,"title":"Ask HN: The Arc Effect","by":"tel","time":1203647620,"type":"story"}"""));

        var story = await client.GetStoryAsync(121003, Ct);

        story.ShouldNotBeNull();
        story.Title.ShouldBe("Ask HN: The Arc Effect");
        story.Url.ShouldBeNull();
    }

    [Fact]
    public async Task Normalises_a_blank_url_to_null_rather_than_an_empty_string()
    {
        var client = CreateClient(StubHttpMessageHandler.Returning(
            """{"id":1,"title":"Blank","url":"   ","type":"story"}"""));

        (await client.GetStoryAsync(1, Ct))!.Url.ShouldBeNull();
    }

    [Fact]
    public async Task Returns_null_when_upstream_answers_with_a_json_null()
    {
        var client = CreateClient(StubHttpMessageHandler.Returning("null"));

        (await client.GetStoryAsync(999, Ct)).ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"id":1,"title":"Gone","deleted":true}""")]
    [InlineData("""{"id":1,"title":"Flagged","dead":true}""")]
    [InlineData("""{"id":1,"type":"story"}""")]
    [InlineData("""{"id":1,"title":"   ","type":"story"}""")]
    public async Task Skips_tombstoned_and_untitled_items(string json)
    {
        var client = CreateClient(StubHttpMessageHandler.Returning(json));

        (await client.GetStoryAsync(1, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Tolerates_unexpected_extra_fields()
    {
        // The HN docs explicitly tell clients to ignore fields they do not expect.
        var client = CreateClient(StubHttpMessageHandler.Returning(
            """{"id":1,"title":"Fine","type":"story","kids":[1,2],"somethingNew":{"a":1}}"""));

        (await client.GetStoryAsync(1, Ct))!.Title.ShouldBe("Fine");
    }

    [Fact]
    public async Task Parses_the_newest_story_id_list()
    {
        var client = CreateClient(StubHttpMessageHandler.Returning("[3,2,1]"));

        var ids = await client.GetNewestStoryIdsAsync(Ct);

        ids.ShouldBe([3, 2, 1]);
    }

    [Fact]
    public async Task Returns_an_empty_id_list_when_upstream_answers_with_a_json_null()
    {
        var client = CreateClient(StubHttpMessageHandler.Returning("null"));

        (await client.GetNewestStoryIdsAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Requests_the_documented_upstream_paths()
    {
        var handler = StubHttpMessageHandler.Returning("null");
        var client = CreateClient(handler);

        await client.GetNewestStoryIdsAsync(Ct);
        await client.GetStoryAsync(42, Ct);

        handler.Requests[0].ShouldBe("https://hn.test/v0/newstories.json");
        handler.Requests[1].ShouldBe("https://hn.test/v0/item/42.json");
    }
}
