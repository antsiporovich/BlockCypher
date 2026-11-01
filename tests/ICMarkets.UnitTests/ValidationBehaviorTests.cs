using AwesomeAssertions;
using FluentValidation;
using FluentValidation.Results;
using ICMarkets.Application.Behaviors;
using MediatR;

namespace ICMarkets.UnitTests.Behaviors;

// With several validators for one request, each must get its OWN ValidationContext: a shared one
// accumulates failures (so every result surfaces the shared list and the errors are counted more
// than once) and is mutated concurrently when validators run in parallel. No endpoint has two
// validators today; these tests guard the day one does.
public sealed class ValidationBehaviorTests
{
    private sealed record TestRequest(string Value);

    private sealed class FailingValidator : AbstractValidator<TestRequest>
    {
        public FailingValidator(string message) =>
            RuleFor(x => x.Value).Must(_ => false).WithMessage(message);
    }

    private sealed class PassingValidator : AbstractValidator<TestRequest>;

    private sealed class ContextCapturingValidator(List<object> seen) : AbstractValidator<TestRequest>
    {
        public override Task<ValidationResult> ValidateAsync(ValidationContext<TestRequest> context, CancellationToken cancellation = default)
        {
            seen.Add(context);
            return Task.FromResult(new ValidationResult());
        }
    }

    private static ValidationBehavior<TestRequest, Unit> Behavior(params IValidator<TestRequest>[] validators) => new(validators);

    [Fact]
    public async Task TwoValidatorsEachAddingOneError_YieldExactlyTwoErrors_NoDoubleCounting()
    {
        var behavior = Behavior(new FailingValidator("first"), new FailingValidator("second"));

        var act = () => behavior.Handle(new TestRequest("x"), _ => Task.FromResult(Unit.Value), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        ex.Errors.Should().HaveCount(2);
        ex.Errors.Select(e => e.ErrorMessage).Should().BeEquivalentTo(new[] { "first", "second" });
    }

    [Fact]
    public async Task EachValidatorGetsItsOwnContext()
    {
        var seen = new List<object>();
        var behavior = Behavior(new ContextCapturingValidator(seen), new ContextCapturingValidator(seen));

        await behavior.Handle(new TestRequest("x"), _ => Task.FromResult(Unit.Value), CancellationToken.None);

        seen.Should().HaveCount(2);
        seen[0].Should().NotBeSameAs(seen[1]);
    }

    [Fact]
    public async Task AllValidatorsPass_NextIsInvoked_AndNothingIsThrown()
    {
        var called = false;
        var behavior = Behavior(new PassingValidator(), new PassingValidator());

        await behavior.Handle(new TestRequest("x"), _ =>
        {
            called = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task NoValidators_NextIsInvoked()
    {
        var called = false;

        await Behavior().Handle(new TestRequest("x"), _ =>
        {
            called = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        called.Should().BeTrue();
    }
}
