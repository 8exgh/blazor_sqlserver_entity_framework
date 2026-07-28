using InvoiceRecon.Data;
using InvoiceRecon.Models;
using Microsoft.EntityFrameworkCore;

namespace InvoiceRecon.Services;

/// <summary>Result of one auto-match run, broken down by the pass that made each match.</summary>
public record MatchResult(int ExactMatches, int ReferenceOnlyMatches, int AmountOnlyMatches)
{
    public int Total => ExactMatches + ReferenceOnlyMatches + AmountOnlyMatches;
}

/// <summary>An invoice paired with its payment, plus the derived reconciliation status.</summary>
public record ReconciliationRow(Invoice Invoice, Payment? Payment)
{
    public bool IsMatched => Payment is not null;
    public decimal Delta => Payment is null ? 0m : Payment.Amount - Invoice.Amount;
    public bool HasDiscrepancy => IsMatched && Delta != 0m;

    public string Status => (IsMatched, HasDiscrepancy) switch
    {
        (false, _) => "Unmatched",
        (true, true) => "Discrepancy",
        (true, false) => "Matched"
    };
}

public class ReconciliationService(IDbContextFactory<AppDbContext> factory)
{
    public async Task<List<ReconciliationRow>> GetInvoicesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var invoices = await db.Invoices
            .Include(i => i.MatchedPayment)
            .OrderBy(i => i.InvoiceNumber)
            .AsNoTracking()
            .ToListAsync();

        return [.. invoices.Select(i => new ReconciliationRow(i, i.MatchedPayment))];
    }

    /// <summary>Payments that have not been applied to any invoice.</summary>
    public async Task<List<Payment>> GetUnappliedPaymentsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await UnappliedPaymentsQuery(db)
            .OrderBy(p => p.ReceivedDate)
            .AsNoTracking()
            .ToListAsync();
    }

    /// <summary>
    /// Matches open invoices to unapplied payments in three passes of decreasing confidence.
    /// Anything an earlier pass consumed is off the table for later ones.
    /// </summary>
    public async Task<MatchResult> RunAutoMatchAsync()
    {
        await using var db = await factory.CreateDbContextAsync();

        var invoices = await db.Invoices.Where(i => i.MatchedPaymentId == null).ToListAsync();
        var payments = await UnappliedPaymentsQuery(db).ToListAsync();

        // The working set is small in a PoC, so match in memory rather than in SQL.
        var openInvoices = invoices.ToList();
        var openPayments = payments.ToList();
        var exact = 0;
        var referenceOnly = 0;
        var amountOnly = 0;

        void Apply(Invoice invoice, Payment payment)
        {
            invoice.MatchedPaymentId = payment.Id;
            invoice.MatchKind = MatchKind.Auto;
            openInvoices.Remove(invoice);
            openPayments.Remove(payment);
        }

        // Pass 1 - reference and amount both agree.
        foreach (var invoice in openInvoices.ToList())
        {
            var payment = openPayments.FirstOrDefault(p =>
                Normalize(p.BankReference) == Normalize(invoice.Reference) && p.Amount == invoice.Amount);

            if (payment is not null)
            {
                Apply(invoice, payment);
                exact++;
            }
        }

        // Pass 2 - reference agrees but the amount does not. Still the right payment; the
        // difference is surfaced as a discrepancy for someone to chase.
        foreach (var invoice in openInvoices.ToList())
        {
            var payment = openPayments.FirstOrDefault(p =>
                Normalize(p.BankReference) == Normalize(invoice.Reference));

            if (payment is not null)
            {
                Apply(invoice, payment);
                referenceOnly++;
            }
        }

        // Pass 3 - no usable reference, so fall back to the amount. Only safe when exactly one
        // invoice and one payment share that amount; anything ambiguous is left for a human.
        foreach (var amount in openInvoices.Select(i => i.Amount).Distinct().ToList())
        {
            var candidateInvoices = openInvoices.Where(i => i.Amount == amount).ToList();
            var candidatePayments = openPayments.Where(p => p.Amount == amount).ToList();

            if (candidateInvoices.Count == 1 && candidatePayments.Count == 1)
            {
                Apply(candidateInvoices[0], candidatePayments[0]);
                amountOnly++;
            }
        }

        await db.SaveChangesAsync();
        return new MatchResult(exact, referenceOnly, amountOnly);
    }

    public async Task MatchAsync(int invoiceId, int paymentId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
        if (invoice is null)
        {
            return;
        }

        // Guard against two browser sessions claiming the same payment.
        var alreadyApplied = await db.Invoices.AnyAsync(i => i.MatchedPaymentId == paymentId);
        if (alreadyApplied)
        {
            return;
        }

        invoice.MatchedPaymentId = paymentId;
        invoice.MatchKind = MatchKind.Manual;
        await db.SaveChangesAsync();
    }

    public async Task UnmatchAsync(int invoiceId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
        if (invoice is null)
        {
            return;
        }

        invoice.MatchedPaymentId = null;
        invoice.MatchKind = MatchKind.None;
        await db.SaveChangesAsync();
    }

    /// <summary>Clears every match so the demo can be run again from scratch.</summary>
    public async Task ResetAllAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Invoices
            .Where(i => i.MatchedPaymentId != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.MatchedPaymentId, (int?)null)
                .SetProperty(i => i.MatchKind, MatchKind.None));
    }

    private static IQueryable<Payment> UnappliedPaymentsQuery(AppDbContext db) =>
        db.Payments.Where(p => !db.Invoices.Any(i => i.MatchedPaymentId == p.Id));

    /// <summary>Bank references arrive with inconsistent casing, spacing and punctuation.</summary>
    private static string Normalize(string reference) =>
        new([.. reference.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
}
