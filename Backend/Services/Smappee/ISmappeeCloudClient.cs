namespace Backend.Services.Smappee;

public interface ISmappeeCloudClient
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmappeeRfidDto>> GetWhitelistedRfidsAsync(int serviceLocationId, CancellationToken cancellationToken = default);

    /// <param name="rangeStartMs">Inclusive start (epoch ms), Brussels wall-time interpretation per Smappee docs.</param>
    /// <param name="rangeEndMs">Exclusive end (epoch ms), or null for open-ended range.</param>
    Task<IReadOnlyList<SmappeeSessionDto>> GetChargingSessionsAsync(
        int serviceLocationId,
        long rangeStartMs,
        long? rangeEndMs,
        CancellationToken cancellationToken = default);
}
