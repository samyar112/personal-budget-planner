namespace backend.Models;

/// <summary>Which producer wrote a ledger row. Stored as a string so a reordered enum cannot corrupt data.</summary>
public enum TransactionSource
{
    Plaid,
    PdfUpload,
    Seed,
}
