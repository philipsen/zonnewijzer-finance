namespace Backend.Services;

/// <summary>
/// Builds invoice data from charging sessions and energy prices (same logic as PDF invoices).
/// Invoices are per <strong>app user</strong>: all linked charge cards’ sessions in the month are combined.
/// </summary>
public interface IInvoiceComputationService
{
    /// <summary>
    /// Returns null if there are no charging sessions for the user and month (on any linked card).
    /// Does not create Mollie payments or persist anything.
    /// </summary>
    Task<InvoiceData?> BuildInvoiceDataForAppUserAsync(int appUserId, int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// Distinct app user ids that have at least one session in the month on a card linked to that user.
    /// </summary>
    Task<IReadOnlyList<int>> GetAppUserIdsForMonthAsync(int year, int month, int? appUserIdFilter, CancellationToken cancellationToken = default);
}
