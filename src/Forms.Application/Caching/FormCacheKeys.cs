namespace Skylab.Forms.Application.Caching;

public static class FormCacheKeys
{
    public static string Analytics(Guid formId) => $"form:analytics:{formId}";
    public static string LinkStats(Guid formId) => $"form:link-stats:{formId}";

    /// <summary>
    /// Bir kullanıcının bütün formlardaki yanıt ve düzenleme taslakları (SCAN kalıbı).
    /// Anahtarlar FormDraftService'teki forms:draft:response|form:{formId}:{userId} biçimidir.
    /// </summary>
    public static string[] UserDraftPatterns(Guid userId) =>
    [
        $"forms:draft:response:*:{userId}",
        $"forms:draft:form:*:{userId}"
    ];

    public const string ResponseShareTokenPrefix = "response:share:token:";
    public const string ResponseShareResponsePrefix = "response:share:response:";
}
