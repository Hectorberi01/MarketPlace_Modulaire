using FluentAssertions;
using Marketplace.Modules.Settlement.Domain.Batches;
using Marketplace.Modules.Settlement.Domain.Earnings;
using Xunit;

namespace Settlement.UnitTests;

public class SettlementTests
{
    [Fact]
    public void Earning_net_is_gross_minus_commission()
    {
        // Scénario dossier : brut 5600 (base 7000 - réduction vendeur 1400), commission 560.
        var earning = SellerEarning.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5600m, 560m, "XOF").Value;

        earning.NetAmount.Should().Be(5040m);
        earning.Status.Should().Be(EarningStatus.Accrued);
    }

    [Fact]
    public void Batch_aggregates_payout_and_completes_when_paid()
    {
        var batch = SettlementBatch.Create(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, "XOF").Value;
        var seller = Guid.NewGuid();

        var payoutId = batch.AddPayout(seller, grossAmount: 5600m, commissionAmount: 560m, netAmount: 5040m);
        batch.TotalNet.Should().Be(5040m);
        batch.Status.Should().Be(SettlementStatus.Pending);

        batch.MarkPayoutPaid(payoutId, "MOMO-REF-1").IsSuccess.Should().BeTrue();
        batch.Status.Should().Be(SettlementStatus.Completed);
        batch.Payouts.Single().Status.Should().Be(PayoutStatus.Paid);
    }

    [Fact]
    public void Batch_with_two_sellers_sums_net_correctly()
    {
        var batch = SettlementBatch.Create(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, "XOF").Value;

        batch.AddPayout(Guid.NewGuid(), 5600m, 560m, 5040m);
        batch.AddPayout(Guid.NewGuid(), 10000m, 1000m, 9000m);

        batch.Payouts.Should().HaveCount(2);
        batch.TotalNet.Should().Be(14040m);
    }

    [Fact]
    public void Release_passe_le_gain_de_accrued_a_released_puis_idempotent()
    {
        var earning = SellerEarning.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1000m, 100m, "XOF").Value;

        earning.Release();
        earning.Status.Should().Be(EarningStatus.Released);
        earning.ReleasedAtUtc.Should().NotBeNull();

        var firstReleasedAt = earning.ReleasedAtUtc;
        earning.Release(); // idempotent
        earning.ReleasedAtUtc.Should().Be(firstReleasedAt);
    }

    [Fact]
    public void MarkSettled_links_earning_to_batch()
    {
        var earning = SellerEarning.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1000m, 100m, "XOF").Value;
        var batchId = Guid.NewGuid();

        earning.MarkSettled(batchId);

        earning.Status.Should().Be(EarningStatus.Settled);
        earning.SettlementBatchId.Should().Be(batchId);
    }
}
