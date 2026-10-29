using AwesomeAssertions;
using ICMarkets.Application.Features.History;

namespace ICMarkets.UnitTests.Features.History;

public sealed class GetLatestSnapshotQueryValidatorTests
{
    private readonly GetLatestSnapshotQueryValidator _validator = new();

    [Fact]
    public void Validate_UnknownNetwork_HasError()
    {
        var result = _validator.Validate(new GetLatestSnapshotQuery("doge-main"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Network");
    }

    [Theory]
    [InlineData("btc-main")]
    [InlineData("eth-main")]
    [InlineData("dash-main")]
    [InlineData("btc-test3")]
    [InlineData("ltc-main")]
    public void Validate_EachKnownNetwork_IsValid(string network)
    {
        var result = _validator.Validate(new GetLatestSnapshotQuery(network));

        result.IsValid.Should().BeTrue();
    }
}
