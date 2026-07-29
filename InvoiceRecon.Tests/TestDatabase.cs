using InvoiceRecon.Data;
using InvoiceRecon.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InvoiceRecon.Tests;

/// <summary>
/// A throwaway SQLite database, one per test, so the suite runs without Docker or SQL Server.
/// The connection is held open because an in-memory SQLite database only lives as long as its
/// connection; every context handed out shares it and therefore sees the same data.
/// </summary>
public sealed class TestDatabase : IDbContextFactory<AppDbContext>, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDatabase()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        // SQLite has no decimal type, but every amount comparison in the service happens in
        // memory rather than in SQL, so the substitution does not affect what is under test.
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext() => new(_options);

    /// <summary>Writes entities directly, bypassing the service under test.</summary>
    public async Task SeedAsync(IEnumerable<Invoice> invoices, IEnumerable<Payment> payments)
    {
        await using var db = CreateDbContext();
        // Payments first so their ids exist before an invoice points at one.
        db.Payments.AddRange(payments);
        await db.SaveChangesAsync();
        db.Invoices.AddRange(invoices);
        await db.SaveChangesAsync();
    }

    public async Task<Invoice> GetInvoiceAsync(string invoiceNumber)
    {
        await using var db = CreateDbContext();
        return await db.Invoices.AsNoTracking().SingleAsync(i => i.InvoiceNumber == invoiceNumber);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}

/// <summary>Terse builders so each test reads as data, not as object construction.</summary>
public static class Build
{
    public static Invoice Invoice(string number, decimal amount, string reference = "", int? matchedPaymentId = null) =>
        new()
        {
            InvoiceNumber = number,
            CustomerName = $"Customer {number}",
            Reference = reference.Length == 0 ? $"REF-{number}" : reference,
            Amount = amount,
            IssueDate = new DateOnly(2026, 6, 1),
            MatchedPaymentId = matchedPaymentId,
            MatchKind = matchedPaymentId is null ? MatchKind.None : MatchKind.Auto
        };

    public static Payment Payment(string bankReference, decimal amount) =>
        new()
        {
            BankReference = bankReference,
            PayerName = $"Payer {bankReference}",
            Amount = amount,
            ReceivedDate = new DateOnly(2026, 6, 15)
        };
}
