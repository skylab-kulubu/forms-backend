namespace Skylab.Forms.Application.Caching;

public static class FormCacheKeys
{
    public const string AnalyticsPrefix = "form:analytics:";
    public static string Analytics(Guid formId) => $"{AnalyticsPrefix}{formId}";
    public static string LinkStats(Guid formId) => $"form:link-stats:{formId}";

    public const string ResponseShareTokenPrefix = "response:share:token:";
    public const string ResponseShareResponsePrefix = "response:share:response:";
}
