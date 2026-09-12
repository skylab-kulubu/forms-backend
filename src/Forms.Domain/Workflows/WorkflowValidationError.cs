namespace Skylab.Forms.Domain.Workflows;

/// <summary>
/// Publish doğrulamasının tek bir bulgusu. Code makine tarafında sabit kalır,
/// Message doğrudan akış editörüne gösterilir.
/// </summary>
public sealed record WorkflowValidationError(string Code, string Message, string? NodeKey = null);
