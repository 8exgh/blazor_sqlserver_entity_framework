using FluentAssertions;
using InvoiceRecon.Services;

namespace InvoiceRecon.Tests;

/// <summary>The status shown in the UI is derived, not stored, so it is worth pinning down.</summary>
public class ReconciliationRowTests
{
    [Fact]
    public void Status_WhenNoPaymentIsAttached_IsUnmatched()
    {
        // Arrange
        var row = new ReconciliationRow(Build.Invoice("INV-1", 100.00m), Payment: null);

        // Act
        var status = row.Status;

        // Assert
        status.Should().Be("Unmatched");
        row.IsMatched.Should().BeFalse();
        row.HasDiscrepancy.Should().BeFalse();
        row.Delta.Should().Be(0m);
    }

    [Fact]
    public void Status_WhenTheAmountsAgree_IsMatchedWithNoDelta()
    {
        // Arrange
        var row = new ReconciliationRow(Build.Invoice("INV-1", 100.00m), Build.Payment("AAA-111", 100.00m));

        // Act
        var status = row.Status;

        // Assert
        status.Should().Be("Matched");
        row.HasDiscrepancy.Should().BeFalse();
        row.Delta.Should().Be(0m);
    }

    [Theory]
    [InlineData(2000.00, 1950.00, -50.00)]  // short paid
    [InlineData(2750.00, 2800.00, 50.00)]   // over paid
    public void Status_WhenTheAmountsDiffer_IsDiscrepancyWithASignedDelta(
        decimal invoiced, decimal paid, decimal expectedDelta)
    {
        // Arrange
        var row = new ReconciliationRow(Build.Invoice("INV-1", invoiced), Build.Payment("AAA-111", paid));

        // Act
        var status = row.Status;

        // Assert
        status.Should().Be("Discrepancy");
        row.IsMatched.Should().BeTrue();
        row.HasDiscrepancy.Should().BeTrue();
        row.Delta.Should().Be(expectedDelta);
    }
}
