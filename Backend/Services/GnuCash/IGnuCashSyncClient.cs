using Backend.Contracts;

namespace Backend.Services.GnuCash;

public interface IGnuCashSyncClient
{
    Task<GnuCashInsertResponse?> InsertInvoicesAsync(GnuCashInsertRequest request, string? correlationId, CancellationToken cancellationToken = default);
}
