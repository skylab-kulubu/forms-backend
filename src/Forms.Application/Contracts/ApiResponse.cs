namespace Forms.Application.Contracts;

public record ApiResponse<T>(
    bool Success,
    string Message,
    T? Data
);