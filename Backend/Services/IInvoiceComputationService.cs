namespace Backend.Services;

/// <summary>
/// Builds invoice data from charging sessions and energy prices (same logic as PDF invoices).
/// </summary>
public interface IInvoiceComputationService
{
    /// <summary>
    /// Returns null if there are no charging sessions for the given user and month.
    /// Does not create Mollie payments or persist anything.
    /// </summary>
    Task<InvoiceData?> BuildInvoiceDataReadOnlyAsync(int chargingUserId, int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// Distinct charging user ids that have at least one session in the given calendar month.
    /// </summary>
    Task<IReadOnlyList<int>> GetChargingUserIdsForMonthAsync(int year, int month, int? chargingUserIdFilter, CancellationToken cancellationToken = default);
}
