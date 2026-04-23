namespace Backend.Services;

/// <summary>
/// Builds invoice data from charging sessions and energy prices (same logic as PDF invoices).
/// </summary>
public interface IInvoiceComputationService
{
    /// <summary>
    /// Returns null if there are no charging sessions for the given charge card and month.
    /// Does not create Mollie payments or persist anything.
    /// </summary>
    Task<InvoiceData?> BuildInvoiceDataReadOnlyAsync(int chargeCardId, int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// Distinct charge card ids that have at least one session in the given Brussels calendar month.
    /// </summary>
    Task<IReadOnlyList<int>> GetChargeCardIdsForMonthAsync(int year, int month, int? chargeCardIdFilter, CancellationToken cancellationToken = default);
}
