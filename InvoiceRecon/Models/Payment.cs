namespace InvoiceRecon.Models;

/// <summary>An incoming credit on the bank statement.</summary>
public class Payment
{
    public int Id { get; set; }
    public string BankReference { get; set; } = "";
    public string PayerName { get; set; } = "";
    public decimal Amount { get; set; }
    public DateOnly ReceivedDate { get; set; }
}
