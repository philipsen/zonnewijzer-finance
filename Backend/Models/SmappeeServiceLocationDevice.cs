namespace Backend.Models;

/// <summary>One row from GET <c>servicelocation</c> (a device at a park; same park may appear multiple times).</summary>
public class SmappeeServiceLocationDevice
{
    public int Id { get; set; }

    public int ServiceLocationId { get; set; }

    /// <summary>Empty when Smappee omits serial for a row.</summary>
    public string DeviceSerialNumber { get; set; } = "";

    public string? Name { get; set; }
    public string? ServiceLocationUuid { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
