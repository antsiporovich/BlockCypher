using FluentValidation;
using ICMarkets.Domain;

namespace ICMarkets.Application.Features.History;

public sealed class GetLatestSnapshotQueryValidator : AbstractValidator<GetLatestSnapshotQuery>
{
    public GetLatestSnapshotQueryValidator()
    {
        RuleFor(x => x.Network)
            .Must(n => NetworkKey.TryParse(n, out _))
            .WithMessage(x => $"Unknown network '{x.Network}'. Valid values: {string.Join(", ", NetworkKey.AllKeys)}.");
    }
}
