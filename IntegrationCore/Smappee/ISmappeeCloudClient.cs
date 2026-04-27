namespace Backend.Services.Smappee;

public interface ISmappeeCloudClient
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmappeeRfidDto>> GetWhitelistedRfidsAsync(int serviceLocationId, CancellationToken cancellationToken = default);

    /// <param name="rangeStartMs">Inclusive start (epoch ms), Brussels wall-time interpretation per Smappee docs.</param>
    /// <param name="rangeEndMs">Exclusive end (epoch ms), or null for open-ended range.</param>
    /// <param name="active">Smappee <c>active</c> query: true = active sessions only, false = completed only, null = omit (API default for completed in range).</param>
    Task<IReadOnlyList<SmappeeSessionDto>> GetChargingSessionsAsync(
        int serviceLocationId,
        long rangeStartMs,
        long? rangeEndMs,
        bool? active = null,
        CancellationToken cancellationToken = default);

    /// <summary>Same token for both calls (active and completed) to avoid double OAuth.</summary>
    Task<(IReadOnlyList<SmappeeSessionDto> Active, IReadOnlyList<SmappeeSessionDto> Completed)>
        GetChargingSessionsActiveAndCompletedAsync(
            int serviceLocationId,
            long rangeStartMs,
            long? rangeEndMs,
            CancellationToken cancellationToken = default);

    /// <summary>
    /// One OAuth token: active + completed sessions plus charger <c>deviceSerialNumber</c> values for this park
    /// (from GET <c>servicelocation</c>, same <c>serviceLocationId</c> may appear multiple times — e.g. EV multi-charger).
    /// </summary>
    Task<(IReadOnlyList<SmappeeSessionDto> Active, IReadOnlyList<SmappeeSessionDto> Completed, IReadOnlyList<string> ChargerSerialsAtPark)>
        GetChargingLiveDataAsync(
            int serviceLocationId,
            long rangeStartMs,
            long? rangeEndMs,
            CancellationToken cancellationToken = default);

    /// <summary>GET <c>servicelocation</c> — all service locations for the account (ids, names, device serials).</summary>
    Task<(string? AppName, IReadOnlyList<SmappeeServiceLocationRowDto> ServiceLocations)> GetServiceLocationsListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>GET <c>servicelocation/{{id}}/info</c> — coordinates, tariff hints, appliances.</summary>
    Task<SmappeeServiceLocationInfoDto> GetServiceLocationInfoAsync(int serviceLocationId, CancellationToken cancellationToken = default);

    /// <summary>Same as <see cref="GetServiceLocationInfoAsync"/> but reuses an existing access token (one OAuth round-trip for many parks).</summary>
    Task<SmappeeServiceLocationInfoDto> GetServiceLocationInfoAsync(
        string accessToken,
        int serviceLocationId,
        CancellationToken cancellationToken = default);
}
