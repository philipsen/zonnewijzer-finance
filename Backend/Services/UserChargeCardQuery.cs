using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public static class UserChargeCardQuery
{
    public static async Task<IReadOnlyList<int>> GetChargeCardIdsForUserAsync(
        ApplicationDbContext db,
        int userId,
        CancellationToken cancellationToken = default) =>
        await db.UserChargeCards
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.ChargeCardId)
            .Select(x => x.ChargeCardId)
            .ToListAsync(cancellationToken);

    public static async Task<bool> UserIsLinkedToCardAsync(
        ApplicationDbContext db,
        int userId,
        int chargeCardId,
        CancellationToken cancellationToken = default) =>
        await db.UserChargeCards
            .AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.ChargeCardId == chargeCardId, cancellationToken);

    public static async Task<int?> GetUserIdLinkedToCardAsync(
        ApplicationDbContext db,
        int chargeCardId,
        CancellationToken cancellationToken = default) =>
        await db.UserChargeCards
            .AsNoTracking()
            .Where(x => x.ChargeCardId == chargeCardId)
            .Select(x => (int?)x.UserId)
            .FirstOrDefaultAsync(cancellationToken);
}
