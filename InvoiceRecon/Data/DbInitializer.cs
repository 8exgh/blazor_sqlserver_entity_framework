using InvoiceRecon.Models;
using Microsoft.EntityFrameworkCore;

namespace InvoiceRecon.Data;

/// <summary>
/// Creates the schema and loads demo data on first run. A PoC shortcut - a real app would use
/// EF migrations rather than EnsureCreated.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, ILogger logger)
    {
        var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        // The SQL Server container needs 20-40s to accept connections under emulation.
        const int maxAttempts = 20;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.EnsureCreatedAsync();
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning("SQL Server not ready (attempt {Attempt}/{Max}): {Message}",
                    attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        if (await db.Invoices.AnyAsync())
        {
            return;
        }

        logger.LogInformation("Seeding demo invoices and payments.");
        db.Payments.AddRange(SeedPayments());
        db.Invoices.AddRange(SeedInvoices());
        await db.SaveChangesAsync();
    }

    // The seed is arranged so a single auto-match run demonstrates every outcome:
    // clean matches, amount discrepancies, an amount-only match, an ambiguous pair the
    // engine declines, an invoice nobody paid, and payments with no invoice.
    public static IEnumerable<Payment> SeedPayments() =>
    [
        new() { BankReference = "ACME-1001", PayerName = "Acme Corp",        Amount = 1250.00m, ReceivedDate = new(2026, 6, 4) },
        new() { BankReference = "GBX-1002",  PayerName = "Globex Ltd",       Amount = 3400.50m, ReceivedDate = new(2026, 6, 6) },
        new() { BankReference = "INI-1003",  PayerName = "Initech",          Amount = 875.25m,  ReceivedDate = new(2026, 6, 9) },
        new() { BankReference = "UMB-1004",  PayerName = "Umbrella Inc",     Amount = 1950.00m, ReceivedDate = new(2026, 6, 11) }, // short paid
        new() { BankReference = "REF 8829911", PayerName = "Hooli",          Amount = 640.00m,  ReceivedDate = new(2026, 6, 14) }, // ref useless, amount unique
        new() { BankReference = "TRF-99001", PayerName = "Unknown Payer",    Amount = 5000.00m, ReceivedDate = new(2026, 6, 16) }, // ambiguous
        new() { BankReference = "cyb 1009",  PayerName = "Cyberdyne",        Amount = 990.00m,  ReceivedDate = new(2026, 6, 19) }, // ref needs normalizing
        new() { BankReference = "VEH-1010",  PayerName = "Vehement Capital", Amount = 2800.00m, ReceivedDate = new(2026, 6, 21) }, // over paid
        new() { BankReference = "MISC-4471", PayerName = "Unknown Payer",    Amount = 415.00m,  ReceivedDate = new(2026, 6, 22) }, // orphan
        new() { BankReference = "TRF-55120", PayerName = "Duplicate Sender", Amount = 1250.00m, ReceivedDate = new(2026, 6, 23) }, // orphan
    ];

    public static IEnumerable<Invoice> SeedInvoices() =>
    [
        new() { InvoiceNumber = "INV-1001", CustomerName = "Acme Corp",         Reference = "ACME-1001", Amount = 1250.00m, IssueDate = new(2026, 6, 1) },
        new() { InvoiceNumber = "INV-1002", CustomerName = "Globex Ltd",        Reference = "GBX-1002",  Amount = 3400.50m, IssueDate = new(2026, 6, 3) },
        new() { InvoiceNumber = "INV-1003", CustomerName = "Initech",           Reference = "INI-1003",  Amount = 875.25m,  IssueDate = new(2026, 6, 5) },
        new() { InvoiceNumber = "INV-1004", CustomerName = "Umbrella Inc",      Reference = "UMB-1004",  Amount = 2000.00m, IssueDate = new(2026, 6, 8) },
        new() { InvoiceNumber = "INV-1005", CustomerName = "Hooli",             Reference = "HOO-1005",  Amount = 640.00m,  IssueDate = new(2026, 6, 10) },
        new() { InvoiceNumber = "INV-1006", CustomerName = "Stark Industries",  Reference = "STK-1006",  Amount = 5000.00m, IssueDate = new(2026, 6, 12) },
        new() { InvoiceNumber = "INV-1007", CustomerName = "Wayne Enterprises", Reference = "WYN-1007",  Amount = 5000.00m, IssueDate = new(2026, 6, 13) },
        new() { InvoiceNumber = "INV-1008", CustomerName = "Soylent Corp",      Reference = "SOY-1008",  Amount = 1180.75m, IssueDate = new(2026, 6, 15) },
        new() { InvoiceNumber = "INV-1009", CustomerName = "Cyberdyne",         Reference = "CYB-1009",  Amount = 990.00m,  IssueDate = new(2026, 6, 18) },
        new() { InvoiceNumber = "INV-1010", CustomerName = "Vehement Capital",  Reference = "VEH-1010",  Amount = 2750.00m, IssueDate = new(2026, 6, 20) },
    ];
}
