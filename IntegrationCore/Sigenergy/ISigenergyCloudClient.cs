namespace Backend.Services.Sigenergy;

public interface ISigenergyCloudClient
{
    /// <summary>
    /// Fetches <c>GET /openapi/systems/{systemId}/energyFlow</c> using a cached bearer token, logging in as needed.
    /// </summary>
    Task<SigenergyEnergyFlowData> GetEnergyFlowAsync(CancellationToken cancellationToken = default);
}
