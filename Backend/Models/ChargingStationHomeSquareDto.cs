namespace Backend.Models;

/// <summary>Home dashboard tile for a charging station appliance (name + optional live state from Smappee).</summary>
public class ChargingStationHomeSquareDto
{
    public const string LiveStateFree = "free";
    public const string LiveStateIdle = "idle";
    public const string LiveStateActive = "active";

    public string Name { get; set; } = "";

    /// <summary>
    /// <see cref="LiveStateFree"/> = no session (available / finished); <see cref="LiveStateIdle"/> = preparing or suspended;
    /// <see cref="LiveStateActive"/> = charging. <see langword="null"/> when live data is missing or the point cannot be matched.
    /// </summary>
    public string? LiveState { get; set; }
}
