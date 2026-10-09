namespace Skylab.Forms.Application.Caching;

public static class FormCacheKeys
{
    private const string ResponseDraftRoot = "forms:draft:response:";
    private const string FormDraftRoot = "forms:draft:form:";

    public const string AnalyticsPrefix = "form:analytics:";

    public static string Analytics(Guid formId) => $"{AnalyticsPrefix}{formId}";
    public static string LinkStats(Guid formId) => $"form:link-stats:{formId}";

    public static string ResponseDraft(Guid formId, Guid userId) => $"{ResponseDraftPrefix(formId)}{userId}";
    public static string ResponseDraftPrefix(Guid formId) => $"{ResponseDraftRoot}{formId}:";
    public static string FormDraft(Guid formId, Guid userId) => $"{FormDraftPrefix(formId)}{userId}";
    public static string FormDraftPrefix(Guid formId) => $"{FormDraftRoot}{formId}:";

    /// <summary>Kişinin bütün formlardaki yanıt ve form taslaklarını bulan SCAN kalıpları.</summary>
    public static string[] UserDraftPatterns(Guid userId) => [$"{ResponseDraftRoot}*:{userId}", $"{FormDraftRoot}*:{userId}"];
}
