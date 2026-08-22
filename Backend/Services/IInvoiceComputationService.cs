namespace Backend.Services;

/// <summary>
/// Builds invoice data from charging sessions and energy prices (same logic as PDF invoices).
/// Invoices are per <strong>app user</strong>: all linked charge cards’ sessions that <strong>ended</strong> in the month are combined.
/// </summary>
public interface IInvoiceComputationService
{
    /// <summary>
    /// Returns null if there are no completed charging sessions for the user that ended in the month (on any linked card).
    /// Does not create Mollie payments or persist anything.
    /// </summary>
    Task<InvoiceData?> BuildInvoiceDataForAppUserAsync(int appUserId, int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// Distinct app user ids that have at least one session that ended in the month on a card linked to that user.
    /// </summary>
    Task<IReadOnlyList<int>> GetAppUserIdsForMonthAsync(int year, int month, int? appUserIdFilter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Distinct app user ids with at least one session in the inclusive Brussels calendar month range.
    /// </summary>
    Task<IReadOnlyList<int>> GetAppUserIdsForRangeAsync(
        int startYear, int startMonth, int endYear, int endMonth,
        int? appUserIdFilter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a combined invoice spanning multiple Brussels calendar months (per-session monthly rates).
    /// Sessions are loaded from Smappee, not the local database.
    /// Returns null when there are no sessions in the range.
    /// </summary>
    Task<InvoiceData?> BuildCombinedInvoiceDataForAppUserAsync(
        int appUserId,
        int startYear, int startMonth, int endYear, int endMonth,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches and groups Smappee sessions for a combined invoice period.</summary>
    Task<CombinedInvoiceSmappeeFetchResult> FetchCombinedSessionsFromSmappeeAsync(
        int startYear, int startMonth, int endYear, int endMonth,
        CancellationToken cancellationToken = default);

    /// <summary>Build combined invoice data from pre-fetched Smappee sessions for one user.</summary>
    Task<InvoiceData?> BuildCombinedInvoiceDataFromSessionsAsync(
        int appUserId,
        IReadOnlyList<CombinedInvoiceSourceSession> userSessions,
        int startYear, int startMonth, int endYear, int endMonth,
        CancellationToken cancellationToken = default);
}
