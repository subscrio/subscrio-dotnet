using Microsoft.EntityFrameworkCore;

namespace Subscrio.Core.Infrastructure.Repositories;

/// <summary>
/// Shared insert-or-update SaveChanges path for tracked EF records.
/// </summary>
public static class EfSaveHelper
{
    /// <summary>
    /// Inserts when <paramref name="getId"/> is 0; otherwise persists tracked changes.
    /// </summary>
    public static async Task<T> SaveAsync<T>(
        DbContext db,
        DbSet<T> set,
        T record,
        Func<T, long> getId) where T : class
    {
        if (getId(record) == 0)
        {
            set.Add(record);
        }

        await db.SaveChangesAsync();
        return record;
    }
}
