using HackerNews.Api.Features.Story;

namespace HackerNews.UnitTests.Features.Story;

public sealed class GetNewestStoriesValidatorTests
{
    private readonly GetNewestStoriesHandler.Validator _validator = new();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_page_below_one(int page)
    {
        var result = _validator.Validate(new GetNewestStoriesHandler.Query { Page = page });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(GetNewestStoriesHandler.Query.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(200)]
    public void Rejects_a_page_size_outside_the_allowed_range(int pageSize)
    {
        var result = _validator.Validate(new GetNewestStoriesHandler.Query { PageSize = pageSize });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(GetNewestStoriesHandler.Query.PageSize));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(100)]
    public void Accepts_page_sizes_at_and_inside_the_boundary(int pageSize)
    {
        _validator.Validate(new GetNewestStoriesHandler.Query { PageSize = pageSize })
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Rejects_an_over_long_search_term()
    {
        var query = new GetNewestStoriesHandler.Query
        {
            Search = new string('x', GetNewestStoriesHandler.Validator.MaxSearchLength + 1)
        };

        _validator.Validate(query).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Accepts_a_search_term_exactly_at_the_limit()
    {
        var query = new GetNewestStoriesHandler.Query
        {
            Search = new string('x', GetNewestStoriesHandler.Validator.MaxSearchLength)
        };

        _validator.Validate(query).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Accepts_the_defaults()
    {
        _validator.Validate(new GetNewestStoriesHandler.Query()).IsValid.ShouldBeTrue();
    }
}
