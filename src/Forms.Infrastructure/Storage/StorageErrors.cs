using Npgsql;

namespace Skylab.Forms.Infrastructure.Storage;

public static class StorageErrors
{
    public static string? CodeOf(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres.SqlState;

        return null;
    }
}
