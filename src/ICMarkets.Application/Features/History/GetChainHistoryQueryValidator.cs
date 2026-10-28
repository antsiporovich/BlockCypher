using FluentValidation;
using ICMarkets.Domain;

namespace ICMarkets.Application.Features.History;

public sealed class GetChainHistoryQueryValidator : AbstractValidator<GetChainHistoryQuery>
{
    public GetChainHistoryQueryValidator()
    {
        RuleFor(x => x.Network)
            .Must(n => NetworkKey.TryParse(n, out _))
            .WithMessage(x => $"Unknown network '{x.Network}'. Valid values: {string.Join(", ", NetworkKey.AllKeys)}.");

        RuleFor(x => x.Take).InclusiveBetween(1, 200);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
    }
}
