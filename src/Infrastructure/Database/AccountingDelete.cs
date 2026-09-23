using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Npgsql;
using Subscrio.Core.Application.Errors;

namespace Subscrio.Core.Infrastructure.Database;

internal static class AccountingDelete
{
    internal static async Task Run(SubscrioDbContext db, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error) when (IsReferenceConflict(error))
        {
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Deleted))
                entry.State = EntityState.Unchanged;
            throw new ConflictException("Cannot delete an entity with retained accounting or related history. Archive it instead.");
        }
    }
    private static bool IsReferenceConflict(Exception? error) => error != null && (error is PostgresException { SqlState: "23503" } || error is SqlException { Number: 547 } || IsReferenceConflict(error.InnerException));
}
