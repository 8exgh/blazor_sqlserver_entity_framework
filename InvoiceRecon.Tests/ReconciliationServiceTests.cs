using FluentAssertions;
using InvoiceRecon.Data;
using InvoiceRecon.Models;
using InvoiceRecon.Services;

namespace InvoiceRecon.Tests;

public class ReconciliationServiceTests : IAsyncDisposable
{
    private readonly TestDatabase _db = new();
    private readonly ReconciliationService _service;

    public ReconciliationServiceTests() => _service = new ReconciliationService(_db);

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    // ---------- pass 1: reference and amount both agree ----------

    [Fact]
    public async Task RunAutoMatchAsync_WhenReferenceAndAmountAgree_MatchesOnTheFirstPass()
    {
        // Arrange
        await _db.SeedAsync(
            [Build.Invoice("INV-1", 1250.00m, "ACME-1001")],
            [Build.Payment("ACME-1001", 1250.00m)]);

        // Act
        var result = await _service.RunAutoMatchAsync();

        // Assert
        result.ExactMatches.Should().Be(1);
        result.ReferenceOnlyMatches.Should().Be(0);
        result.AmountOnlyMatches.Should().Be(0);
        result.Total.Should().Be(1);

        var invoice = await _db.GetInvoiceAsync("INV-1");
        invoice.MatchedPaymentId.Should().NotBeNull();
        invoice.MatchKind.Should().Be(MatchKind.Auto);
    }

    [Theory]
    [InlineData("cyb 1009")]      // lower case and a space instead of the dash
    [InlineData("CYB/1009")]      // different punctuation
    [InlineData("  cyb-1009  ")]  // surrounding whitespace
    public async Task RunAutoMatchAsync_WhenReferenceDiffersOnlyInCaseOrPunctuation_StillMatches(string bankReference)
    {
        // Arrange
        await _db.SeedAsync(
            [Build.Invoice("INV-1", 990.00m, "CYB-1009")],
            [Build.Payment(bankReference, 990.00m)]);

        // Act
        var result = await _service.RunAutoMatchAsync();

        // Assert
        result.ExactMatches.Should().Be(1);
    }

    [Fact]
    public async Task RunAutoMatchAsync_WhenReferencesAreUnrelated_DoesNotMatchOnReference()
    {
        // Arrange
        await _db.SeedAsync(
            [Build.Invoice("INV-1", 100.00m, "AAA-111")],
            [Build.Payment("BBB-222", 250.00m)]);

        // Act
        var result = await _service.RunAutoMatchAsync();

        // Assert
        result.Total.Should().Be(0);
        (await _db.GetInvoiceAsync("INV-1")).MatchedPaymentId.Should().BeNull();
    }

    // ---------- pass 2: reference agrees, amount does not ----------

    [Fact]
    public async Task RunAutoMatchAsync_WhenReferenceAgreesButAmountDiffers_MatchesAndSurfacesTheDiscrepancy()
    {
        // Arrange - the customer short paid by 50.
        await _db.SeedAsync(
            [Build.Invoice("INV-1", 2000.00m, "UMB-1004")],
            [Build.Payment("UMB-1004", 1950.00m)]);

        // Act
        var result = await _service.RunAutoMatchAsync();

        // Assert
        result.ReferenceOnlyMatches.Should().Be(1);
        result.ExactMatches.Should().Be(0);

        var row = (await _service.GetInvoicesAsync()).Single();
        row.Status.Should().Be("Discrepancy");
        row.Delta.Should().Be(-50.00m);
    }

    // ---------- pass 3: amount only, and only when unambiguous ----------

    [Fact]
    public async Task RunAutoMatchAsync_WhenTheAmountIdentifiesExactlyOnePair_MatchesOnAmountAlone()
    {
        // Arrange - the bank reference is useless, but the amount is unique on both sides.
        await _db.SeedAsync(
            [Build.Invoice("INV-1", 640.00m, "HOO-1005")],
            [Build.Payment("REF 8829911", 640.00m)]);

        // Act
        var result = await _service.RunAutoMatchAsync();

        // Assert
        result.AmountOnlyMatches.Should().Be(1);
        (await _db.GetInvoiceAsync("INV-1")).MatchedPaymentId.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAutoMatchAsync_WhenTwoInvoicesShareTheAmount_LeavesBothForAHuman()
    {
        // Arrange - one payment of 5000 could settle either invoice; guessing would be wrong.
        await _db.SeedAsync(
            [Build.Invoice("INV-1", 5000.00m, "STK-1006"), Build.Invoice("INV-2", 5000.00m, "WYN-1007")],
            [Build.Payment("TRF-99001", 5000.00m)]);

        // Act
        var result = await _service.RunAutoMatchAsync();

        // Assert
        result.Total.Should().Be(0);
        (await _service.GetInvoicesAsync()).Should().OnlyContain(r => r.Status == "Unmatched");
        (await _service.GetUnappliedPaymentsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task RunAutoMatchAsync_WhenTwoPaymentsShareTheAmount_LeavesTheInvoiceForAHuman()
    {
        // Arrange - the mirror image: which of the two credits settled the invoice?
        await _db.SeedAsync(
            [Build.Invoice("INV-1", 500.00m, "ZZZ-999")],
            [Build.Payment("Q-1", 500.00m), Build.Payment("Q-2", 500.00m)]);

        // Act
        var result = await _service.RunAutoMatchAsync();

        // Assert
        result.Total.Should().Be(0);
        (await _db.GetInvoiceAsync("INV-1")).MatchedPaymentId.Should().BeNull();
    }

    // ---------- interaction between the passes ----------

    [Fact]
    public async Task RunAutoMatchAsync_DoesNotReuseAPaymentClaimedByAnEarlierPass()
    {
        // Arrange - INV-1 wins the payment on reference, so nothing is left for INV-2 to match
        // on amount even though the amounts agree.
        await _db.SeedAsync(
            [Build.Invoice("INV-1", 100.00m, "AAA-111"), Build.Invoice("INV-2", 100.00m, "BBB-222")],
            [Build.Payment("AAA-111", 100.00m)]);

        // Act
        var result = await _service.RunAutoMatchAsync();

        // Assert
        result.ExactMatches.Should().Be(1);
        result.AmountOnlyMatches.Should().Be(0);
        (await _db.GetInvoiceAsync("INV-2")).MatchedPaymentId.Should().BeNull();
    }

    [Fact]
    public async Task RunAutoMatchAsync_IgnoresInvoicesAndPaymentsThatAreAlreadyReconciled()
    {
        // Arrange - INV-1 is already settled. INV-2 is an identical open invoice that would
        // claim the same payment if the run considered payments that are already applied.
        await _db.SeedAsync([], [Build.Payment("AAA-111", 100.00m)]);
        var paymentId = (await _service.GetUnappliedPaymentsAsync()).Single().Id;
        await _db.SeedAsync(
            [
                Build.Invoice("INV-1", 100.00m, "AAA-111", matchedPaymentId: paymentId),
                Build.Invoice("INV-2", 100.00m, "AAA-111")
            ],
            []);

        // Act
        var result = await _service.RunAutoMatchAsync();

        // Assert
        result.Total.Should().Be(0);
        (await _db.GetInvoiceAsync("INV-1")).MatchedPaymentId.Should().Be(paymentId);
        (await _db.GetInvoiceAsync("INV-2")).MatchedPaymentId.Should().BeNull();
    }

    [Fact]
    public async Task RunAutoMatchAsync_WhenRunTwice_TheSecondRunFindsNothingNewToDo()
    {
        // Arrange
        await SeedDemoDataAsync();
        var first = await _service.RunAutoMatchAsync();

        // Act
        var second = await _service.RunAutoMatchAsync();

        // Assert
        first.Total.Should().Be(7);
        second.Total.Should().Be(0);
    }

    [Fact]
    public async Task RunAutoMatchAsync_OverTheDemoSeed_ProducesTheDocumentedOutcome()
    {
        // Arrange - the seed the app ships with, which is built to exercise every pass.
        await SeedDemoDataAsync();

        // Act
        var result = await _service.RunAutoMatchAsync();

        // Assert
        result.Should().Be(new MatchResult(ExactMatches: 4, ReferenceOnlyMatches: 2, AmountOnlyMatches: 1));

        var rows = await _service.GetInvoicesAsync();
        rows.Where(r => r.Status == "Matched").Should().HaveCount(5);
        rows.Where(r => r.Status == "Unmatched").Should().HaveCount(3);

        rows.Where(r => r.Status == "Discrepancy")
            .Select(r => (r.Invoice.InvoiceNumber, r.Delta))
            .Should().BeEquivalentTo(new[] { ("INV-1004", -50.00m), ("INV-1010", 50.00m) });

        // The two invoices that share an amount, and the one nobody paid.
        rows.Where(r => r.Status == "Unmatched").Select(r => r.Invoice.InvoiceNumber)
            .Should().BeEquivalentTo(new[] { "INV-1006", "INV-1007", "INV-1008" });

        (await _service.GetUnappliedPaymentsAsync()).Select(p => p.BankReference)
            .Should().BeEquivalentTo(new[] { "TRF-99001", "MISC-4471", "TRF-55120" });
    }

    // ---------- manual matching ----------

    [Fact]
    public async Task MatchAsync_AppliesThePaymentAndRecordsItAsAManualDecision()
    {
        // Arrange
        await _db.SeedAsync([Build.Invoice("INV-1", 5000.00m)], [Build.Payment("TRF-99001", 5000.00m)]);
        var invoiceId = (await _service.GetInvoicesAsync()).Single().Invoice.Id;
        var paymentId = (await _service.GetUnappliedPaymentsAsync()).Single().Id;

        // Act
        await _service.MatchAsync(invoiceId, paymentId);

        // Assert
        var invoice = await _db.GetInvoiceAsync("INV-1");
        invoice.MatchedPaymentId.Should().Be(paymentId);
        invoice.MatchKind.Should().Be(MatchKind.Manual);
        (await _service.GetUnappliedPaymentsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task MatchAsync_WhenThePaymentIsAlreadyApplied_LeavesTheSecondInvoiceAlone()
    {
        // Arrange - two users racing for the same credit.
        await _db.SeedAsync(
            [Build.Invoice("INV-1", 100.00m), Build.Invoice("INV-2", 100.00m)],
            [Build.Payment("AAA-111", 100.00m)]);
        var invoices = await _service.GetInvoicesAsync();
        var paymentId = (await _service.GetUnappliedPaymentsAsync()).Single().Id;
        await _service.MatchAsync(invoices[0].Invoice.Id, paymentId);

        // Act
        var act = async () => await _service.MatchAsync(invoices[1].Invoice.Id, paymentId);

        // Assert
        await act.Should().NotThrowAsync();
        (await _db.GetInvoiceAsync("INV-1")).MatchedPaymentId.Should().Be(paymentId);
        (await _db.GetInvoiceAsync("INV-2")).MatchedPaymentId.Should().BeNull();
    }

    [Fact]
    public async Task MatchAsync_WhenTheInvoiceDoesNotExist_DoesNothing()
    {
        // Arrange
        await _db.SeedAsync([], [Build.Payment("AAA-111", 100.00m)]);

        // Act
        var act = async () => await _service.MatchAsync(invoiceId: 999, paymentId: 1);

        // Assert
        await act.Should().NotThrowAsync();
        (await _service.GetUnappliedPaymentsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task UnmatchAsync_ClearsTheMatchAndReturnsThePaymentToThePool()
    {
        // Arrange
        await _db.SeedAsync([Build.Invoice("INV-1", 100.00m, "AAA-111")], [Build.Payment("AAA-111", 100.00m)]);
        await _service.RunAutoMatchAsync();
        var invoiceId = (await _service.GetInvoicesAsync()).Single().Invoice.Id;

        // Act
        await _service.UnmatchAsync(invoiceId);

        // Assert
        var invoice = await _db.GetInvoiceAsync("INV-1");
        invoice.MatchedPaymentId.Should().BeNull();
        invoice.MatchKind.Should().Be(MatchKind.None);
        (await _service.GetUnappliedPaymentsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ResetAllAsync_ClearsEveryMatchSoTheDemoCanBeRerun()
    {
        // Arrange
        await SeedDemoDataAsync();
        await _service.RunAutoMatchAsync();

        // Act
        await _service.ResetAllAsync();

        // Assert
        var rows = await _service.GetInvoicesAsync();
        rows.Should().OnlyContain(r => r.Status == "Unmatched");
        rows.Should().OnlyContain(r => r.Invoice.MatchKind == MatchKind.None);
        (await _service.GetUnappliedPaymentsAsync()).Should().HaveCount(10);
    }

    // ---------- queries ----------

    [Fact]
    public async Task GetUnappliedPaymentsAsync_ExcludesPaymentsThatAreAlreadyApplied()
    {
        // Arrange
        await _db.SeedAsync(
            [Build.Invoice("INV-1", 100.00m, "AAA-111")],
            [Build.Payment("AAA-111", 100.00m), Build.Payment("MISC-4471", 415.00m)]);
        await _service.RunAutoMatchAsync();

        // Act
        var unapplied = await _service.GetUnappliedPaymentsAsync();

        // Assert
        unapplied.Should().ContainSingle().Which.BankReference.Should().Be("MISC-4471");
    }

    [Fact]
    public async Task GetInvoicesAsync_ReturnsRowsInInvoiceNumberOrderWithTheirPaymentAttached()
    {
        // Arrange
        await _db.SeedAsync(
            [Build.Invoice("INV-2", 100.00m, "AAA-111"), Build.Invoice("INV-1", 200.00m, "BBB-222")],
            [Build.Payment("AAA-111", 100.00m)]);
        await _service.RunAutoMatchAsync();

        // Act
        var rows = await _service.GetInvoicesAsync();

        // Assert
        rows.Select(r => r.Invoice.InvoiceNumber).Should().ContainInOrder("INV-1", "INV-2");
        rows.Single(r => r.Invoice.InvoiceNumber == "INV-2").Payment!.BankReference.Should().Be("AAA-111");
        rows.Single(r => r.Invoice.InvoiceNumber == "INV-1").Payment.Should().BeNull();
    }

    private Task SeedDemoDataAsync() =>
        _db.SeedAsync(DbInitializer.SeedInvoices(), DbInitializer.SeedPayments());
}
