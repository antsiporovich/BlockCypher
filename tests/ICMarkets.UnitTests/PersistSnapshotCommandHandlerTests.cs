using AwesomeAssertions;
using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;
using ICMarkets.Application.Features.Ingestion;
using ICMarkets.Application.Mapping;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace ICMarkets.UnitTests.Features.Ingestion;

public sealed class PersistSnapshotCommandHandlerTests
{
    private readonly IBlockchainRepository _repo = Substitute.For<IBlockchainRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly SnapshotMapper _mapper = new();

    [Fact]
    public async Task Handle_BtcChainInfo_PersistsFeeRates_AndLeavesGasPricesNull()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var handler = new PersistSnapshotCommandHandler(_repo, _unitOfWork, clock, _mapper);
        var info = new BlockCypherChainInfoDto
        {
            Name = "BTC.main",
            Height = 100,
            Hash = "hash",
            Time = DateTimeOffset.UtcNow,
            PeerCount = 1,
            UnconfirmedCount = 2,
            HighFeePerKb = 10,
            MediumFeePerKb = 5,
            LowFeePerKb = 1,
        };

        BlockchainSnapshot? captured = null;
        _repo.AddAsync(Arg.Do<BlockchainSnapshot>(s => captured = s), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var dto = await handler.Handle(new PersistSnapshotCommand(Network.BtcMain, new ChainInfoResult(info, "{}")), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.FeeRates.Should().NotBeNull();
        captured.GasPrices.Should().BeNull();
        captured.CreatedAt.Should().Be(clock.GetUtcNow());
        dto.CreatedAt.Should().Be(clock.GetUtcNow());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EthChainInfo_PersistsGasPrices_AndLeavesFeeRatesNull()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var handler = new PersistSnapshotCommandHandler(_repo, _unitOfWork, clock, _mapper);
        var info = new BlockCypherChainInfoDto
        {
            Name = "ETH.main",
            Height = 200,
            Hash = "hash",
            Time = DateTimeOffset.UtcNow,
            HighGasPrice = 30,
            MediumGasPrice = 20,
            LowGasPrice = 10,
            BaseFee = 5,
        };

        BlockchainSnapshot? captured = null;
        _repo.AddAsync(Arg.Do<BlockchainSnapshot>(s => captured = s), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await handler.Handle(new PersistSnapshotCommand(Network.EthMain, new ChainInfoResult(info, "{}")), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.GasPrices.Should().NotBeNull();
        captured.FeeRates.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoFeeOrGasFieldsPresent_LeavesBothNull()
    {
        // e.g. an upstream response missing both families entirely - neither
        // value object should be fabricated as all-null.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var handler = new PersistSnapshotCommandHandler(_repo, _unitOfWork, clock, _mapper);
        var info = new BlockCypherChainInfoDto { Name = "X", Height = 1, Hash = "h", Time = DateTimeOffset.UtcNow };

        BlockchainSnapshot? captured = null;
        _repo.AddAsync(Arg.Do<BlockchainSnapshot>(s => captured = s), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await handler.Handle(new PersistSnapshotCommand(Network.DashMain, new ChainInfoResult(info, "{}")), CancellationToken.None);

        captured!.FeeRates.Should().BeNull();
        captured.GasPrices.Should().BeNull();
    }
}
