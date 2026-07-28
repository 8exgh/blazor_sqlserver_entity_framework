using InvoiceRecon.Models;
using Microsoft.EntityFrameworkCore;

namespace InvoiceRecon.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>(e =>
        {
            e.Property(i => i.Amount).HasPrecision(18, 2);
            e.HasOne(i => i.MatchedPayment)
                .WithOne()
                .HasForeignKey<Invoice>(i => i.MatchedPaymentId)
                .OnDelete(DeleteBehavior.SetNull);
            // One-to-one: a payment can only ever be applied to a single invoice.
            e.HasIndex(i => i.MatchedPaymentId).IsUnique().HasFilter("[MatchedPaymentId] IS NOT NULL");
        });

        modelBuilder.Entity<Payment>()
            .Property(p => p.Amount).HasPrecision(18, 2);
    }
}
