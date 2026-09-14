using FluentValidation;
using FluentValidation.Results;
using HackerNews.Api.Shared.Behaviors;
using MediatR;

namespace HackerNews.UnitTests.Shared;

public sealed class ValidationBehaviorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Request(string Name);

    private sealed class NameRequired : AbstractValidator<Request>
    {
        public NameRequired() => RuleFor(r => r.Name).NotEmpty().WithMessage("Name is required.");
    }

    private sealed class NameShort : AbstractValidator<Request>
    {
        public NameShort() => RuleFor(r => r.Name).MaximumLength(3).WithMessage("Name is too long.");
    }

    private sealed class NameMinimumLength : AbstractValidator<Request>
    {
        public NameMinimumLength() =>
            RuleFor(r => r.Name).MinimumLength(3).WithMessage("Name is too short.");
    }

    private static (RequestHandlerDelegate<string> Next, Func<int> CallCount) Next(string result = "ok")
    {
        var calls = 0;
        return (_ =>
        {
            calls++;
            return Task.FromResult(result);
        }, () => calls);
    }

    [Fact]
    public async Task Calls_the_handler_when_there_are_no_validators()
    {
        var behavior = new ValidationBehavior<Request, string>([]);
        var (next, callCount) = Next();

        var result = await behavior.Handle(new Request("anything"), next, Ct);

        result.ShouldBe("ok");
        callCount().ShouldBe(1);
    }

    [Fact]
    public async Task Calls_the_handler_exactly_once_when_the_request_is_valid()
    {
        var behavior = new ValidationBehavior<Request, string>([new NameRequired()]);
        var (next, callCount) = Next();

        await behavior.Handle(new Request("abc"), next, Ct);

        callCount().ShouldBe(1);
    }

    [Fact]
    public async Task Throws_and_never_reaches_the_handler_when_the_request_is_invalid()
    {
        var behavior = new ValidationBehavior<Request, string>([new NameRequired()]);
        var (next, callCount) = Next();

        var exception = await Should.ThrowAsync<ValidationException>(
            () => behavior.Handle(new Request(string.Empty), next, Ct));

        exception.Errors.ShouldContain(e => e.ErrorMessage == "Name is required.");
        callCount().ShouldBe(0);
    }

    [Fact]
    public async Task Aggregates_failures_across_every_registered_validator()
    {
        var behavior = new ValidationBehavior<Request, string>([new NameRequired(), new NameShort()]);
        var (next, _) = Next();

        var exception = await Should.ThrowAsync<ValidationException>(
            () => behavior.Handle(new Request("far too long"), next, Ct));

        exception.Errors.Select(e => e.ErrorMessage).ShouldContain("Name is too long.");
    }

    [Fact]
    public async Task Reports_every_validators_failure_at_once_rather_than_only_the_first()
    {
        // An empty name violates both rules, so the caller should see both messages in one
        // response instead of having to fix one problem to discover the next.
        var behavior = new ValidationBehavior<Request, string>(
            [new NameRequired(), new NameMinimumLength()]);
        var (next, _) = Next();

        var exception = await Should.ThrowAsync<ValidationException>(
            () => behavior.Handle(new Request(string.Empty), next, Ct));

        exception.Errors.ShouldAllBe(failure => failure is ValidationFailure);
        exception.Errors.Select(e => e.ErrorMessage)
            .ShouldBe(["Name is required.", "Name is too short."], ignoreOrder: true);
    }
}
