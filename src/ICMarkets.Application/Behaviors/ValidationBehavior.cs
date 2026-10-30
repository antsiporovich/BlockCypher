using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace ICMarkets.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!validators.Any())
            return await next();

        // Fresh ValidationContext per validator: FluentValidation accumulates failures into the
        // context, so a single shared one both double-counts (every result surfaces the shared
        // list) and is mutated concurrently under Task.WhenAll. Run sequentially - validation is
        // cheap, and a validator may resolve the scoped DbContext (not thread-safe) for an async
        // rule, which parallel execution would corrupt.
        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(new ValidationContext<TRequest>(request), ct);
            if (!result.IsValid)
                failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
