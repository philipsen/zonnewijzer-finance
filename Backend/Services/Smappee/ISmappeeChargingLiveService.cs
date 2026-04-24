using Backend.Models;

namespace Backend.Services.Smappee;

public interface ISmappeeChargingLiveService
{
    Task<SmappeeChargingLiveResponse> GetLiveAsync(CancellationToken cancellationToken = default);
}
