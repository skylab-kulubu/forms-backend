namespace Skylab.Forms.Application.Abstractions;

public interface ICurrentUser
{
    Guid? Id { get; }

    bool IsPlatformAdmin { get; }
}
