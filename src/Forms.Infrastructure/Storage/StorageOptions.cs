namespace Skylab.Forms.Infrastructure.Storage;

/// <summary>ConnectionStrings:Forms ve Redis:ConnectionString; ikisi de kurulumdan gelir.</summary>
internal sealed class StorageOptions
{
    public string Database { get; set; } = "";
    public string Redis { get; set; } = "";
}
