using Npgsql;

namespace Skylab.Forms.Infrastructure.Storage;

public static class StorageErrors
{
    /// <summary>Hatanın zincirindeki Postgres hata kodu (SQLSTATE). Mesajın aksine istekten değer taşımaz; loga yazılabilir.</summary>
    public static string? CodeOf(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres.SqlState;

        return null;
    }
}
