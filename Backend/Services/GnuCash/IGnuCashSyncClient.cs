using Backend.Contracts;

namespace Backend.Services.GnuCash;

public interface IGnuCashSyncClient
{
    Task<GnuCashInsertResponse?> InsertInvoicesAsync(GnuCashInsertRequest request, string? correlationId, bool dryRun, CancellationToken cancellationToken = default);

    Task<GnuCashInvoicesExistResponse?> GetExistingInvoiceIdsAsync(GnuCashInvoicesExistRequest request, CancellationToken cancellationToken = default);

    Task<GnuCashEnsureCustomerResponse?> EnsureCustomerAsync(GnuCashEnsureCustomerRequest request, string? correlationId, bool dryRun, CancellationToken cancellationToken = default);

    Task<GnuCashWipeAppResponse?> WipeAppBusinessAsync(string? correlationId, bool dryRun, CancellationToken cancellationToken = default);
}
