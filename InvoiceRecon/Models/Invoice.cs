namespace InvoiceRecon.Models;

/// <summary>How an invoice came to be linked to a payment.</summary>
public enum MatchKind
{
    None = 0,
    Auto = 1,
    Manual = 2
}

public class Invoice
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";

    /// <summary>Remittance reference the payer is expected to quote on the bank transfer.</summary>
    public string Reference { get; set; } = "";

    public decimal Amount { get; set; }
    public DateOnly IssueDate { get; set; }

    /// <summary>Null means the invoice is still unreconciled.</summary>
    public int? MatchedPaymentId { get; set; }
    public Payment? MatchedPayment { get; set; }
    public MatchKind MatchKind { get; set; }
}
