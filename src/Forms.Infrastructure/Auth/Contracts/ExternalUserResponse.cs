namespace Skylab.Forms.Infrastructure.Auth.Contracts;

internal sealed class ExternalUserResponse
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ProfilePictureUrl { get; set; }

    /// <summary>active, deletion_pending ya da deleted; eski core sürümlerinde boş.</summary>
    public string? Status { get; set; }
    public string? DisplayName { get; set; }
}
