namespace Forms.Application.Contracts;

public record ServiceResult<T>(
    FormAccessStatus Status,
    T? Data = default,
    string? Message = null
);