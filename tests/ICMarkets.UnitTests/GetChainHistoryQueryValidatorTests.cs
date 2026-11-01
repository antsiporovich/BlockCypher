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
    public void Validate_MalformedCursor_HasError()
    {
        // a cursor that doesn't decode is a 400, never a silent "first page"
        var result = _validator.Validate(new GetChainHistoryQuery("btc-main", Cursor: "not-a-valid-cursor"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Cursor");
    }

    [Fact]
    public void Validate_NullCursor_IsValid()
    {
        // the first page has no cursor - absence must not be treated as a malformed token
        var result = _validator.Validate(new GetChainHistoryQuery("btc-main", Take: 50, Cursor: null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WellFormedCursor_IsValid()
    {
        var cursor = new HistoryCursor(DateTimeOffset.UtcNow, 7).Encode();

        var result = _validator.Validate(new GetChainHistoryQuery("btc-main", Take: 50, Cursor: cursor));

        result.IsValid.Should().BeTrue();
    }
}
