namespace Skylab.Forms.Application.Caching;

public static class FormCacheKeys
{
    public static string Analytics(Guid formId) => $"form:analytics:{formId}";
    public static string LinkStats(Guid formId) => $"form:link-stats:{formId}";
}
