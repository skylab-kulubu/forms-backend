using Skylab.Shared.Domain.Enums;

namespace Skylab.Shared.Application.Contracts;

public record ServiceResult<T>(
    ServiceStatus Status,
    T? Data = default,
    string? Message = null
);