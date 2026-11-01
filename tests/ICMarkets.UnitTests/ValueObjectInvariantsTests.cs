using AwesomeAssertions;
using ICMarkets.Domain.ValueObjects;

namespace ICMarkets.UnitTests.Domain;

// Fees and gas prices cannot be negative, and the value objects enforce that themselves - so a
// negative tier is unrepresentable from any path, not just the adapter's validation.
public sealed class ValueObjectInvariantsTests
{
    [Fact]
    public void FeeRates_NegativeTier_Throws() =>
        ((Action)(() => _ = new FeeRates(-1, 0, 0))).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void FeeRates_ZeroNullAndPositiveTiers_AreAllowed()
    {
        _ = new FeeRates(0, 10, null);
        _ = new FeeRates(null, null, null);
    }

    [Fact]
    public void GasPrices_NegativeTier_Throws() =>
        ((Action)(() => _ = new GasPrices(0, 0, 0, -1, 0, 0, 0))).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void GasPrices_ZeroNullAndPositiveTiers_AreAllowed() =>
        _ = new GasPrices(1, 2, 3, null, null, null, null);
}
