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

        // A supplied cursor must decode; a malformed/forged one is a 400, never a silent "first page".
        // The cursor is only ever trusted as far as TryParse - its contents are not used for authz.
        RuleFor(x => x.Cursor)
            .Must(c => HistoryCursor.TryParse(c, out _))
            .When(x => x.Cursor is not null)
            .WithMessage("The cursor is not a valid pagination token.");
    }
}
