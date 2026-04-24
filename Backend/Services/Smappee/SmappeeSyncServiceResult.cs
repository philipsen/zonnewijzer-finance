namespace Backend.Services.Smappee;

public sealed class SmappeeSyncServiceResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int CardsAdded { get; init; }
    public int CardsUpdated { get; init; }
    public int SessionsAdded { get; init; }
    public int SessionsUpdated { get; init; }
    public int SessionsSkipped { get; init; }
    public int SessionsErrors { get; init; }

    public static SmappeeSyncServiceResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };

    public static SmappeeSyncServiceResult Ok(
        int cardsAdded,
        int cardsUpdated,
        int sessAdded,
        int sessUpdated,
        int sessSkipped,
        int sessErrors) =>
        new()
        {
            Success = true,
            CardsAdded = cardsAdded,
            CardsUpdated = cardsUpdated,
            SessionsAdded = sessAdded,
            SessionsUpdated = sessUpdated,
            SessionsSkipped = sessSkipped,
            SessionsErrors = sessErrors
        };
}
