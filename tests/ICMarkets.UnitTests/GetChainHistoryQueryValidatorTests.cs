using AwesomeAssertions;
using ICMarkets.Application.Features.History;

namespace ICMarkets.UnitTests.Features.History;

public sealed class GetChainHistoryQueryValidatorTests
{
    private readonly GetChainHistoryQueryValidator _validator = new();

    [Fact]
    public void Validate_UnknownNetwork_HasError()
    {
        var result = _validator.Validate(new GetChainHistoryQuery("doge-main"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Network");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Validate_TakeOutOfRange_HasError(int take)
    {
        var result = _validator.Validate(new GetChainHistoryQuery("btc-main", Take: take));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Take");
    }

    [Fact]
    public void Validate_NegativeSkip_HasError()
    {
        var result = _validator.Validate(new GetChainHistoryQuery("btc-main", Skip: -1));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Skip");
    }

    [Fact]
    public void Validate_KnownNetworkAndInRangeParameters_IsValid()
    {
        var result = _validator.Validate(new GetChainHistoryQuery("btc-main", Take: 50, Skip: 0));

        result.IsValid.Should().BeTrue();
    }
}
