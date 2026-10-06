namespace Skylab.Forms.Application.Caching;

public static class FormCacheKeys
{
    public const string AnalyticsPrefix = "form:analytics:";
    public static string Analytics(Guid formId) => $"{AnalyticsPrefix}{formId}";
    public static string LinkStats(Guid formId) => $"form:link-stats:{formId}";

    public static string ResponseDraft(Guid formId, Guid userId) => $"forms:draft:response:{formId}:{userId}";
    public static string ResponseDraftPrefix(Guid formId) => $"forms:draft:response:{formId}:";
    public static string FormDraft(Guid formId, Guid userId) => $"forms:draft:form:{formId}:{userId}";
    public static string FormDraftPrefix(Guid formId) => $"forms:draft:form:{formId}:";

    /// <summary>Kişinin bütün formlardaki yanıt ve form taslaklarını bulan SCAN kalıpları.</summary>
    public static string[] UserDraftPatterns(Guid userId) =>
        [$"forms:draft:response:*:{userId}", $"forms:draft:form:*:{userId}"];

    public const string ResponseShareTokenPrefix = "response:share:token:";
    public static string ResponseShareToken(string token) => $"{ResponseShareTokenPrefix}{token}";
    public static string ResponseShareResponse(Guid responseId) => $"response:share:response:{responseId}";
}
