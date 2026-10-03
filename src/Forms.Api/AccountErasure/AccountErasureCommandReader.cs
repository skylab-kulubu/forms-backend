using System.Text.Json;
using Microsoft.Net.Http.Headers;
using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Domain.Common;

namespace Skylab.Forms.Api.AccountErasure;

/// <summary>
/// Komut gövdesini sözleşmeye göre okur: tam olarak request_id, subject_id ve emails,
/// her biri bir kez, en çok 4 KB'lık bir JSON nesnesinde. Başka her şey reddedilir.
/// Değerler hiçbir hata mesajına yazılmaz.
/// </summary>
public static class AccountErasureCommandReader
{
    public const int MaxBodyBytes = 4096;
    private const int MaxEmails = 3;
    private const int MaxEmailLength = 254;

    /// <summary>Yoldaki komut kimliği: tireli 36 karakterlik UUID, büyük ya da küçük harf.</summary>
    public static bool TryParseRequestId(string? value, out Guid requestId)
    {
        requestId = Guid.Empty;
        return value is { Length: 36 } && Guid.TryParseExact(value, "D", out requestId);
    }

    public static async Task<AccountErasureCommand?> ReadAsync(HttpRequest request, CancellationToken ct)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType) ||
            !string.Equals(contentType.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (request.ContentLength > MaxBodyBytes)
            return null;

        var body = new byte[MaxBodyBytes + 1];
        var length = 0;
        int read;
        while (length < body.Length &&
               (read = await request.Body.ReadAsync(body.AsMemory(length, body.Length - length), ct)) > 0)
        {
            length += read;
        }

        if (length > MaxBodyBytes)
            return null;

        try
        {
            return Parse(body.AsSpan(0, length));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AccountErasureCommand? Parse(ReadOnlySpan<byte> body)
    {
        var reader = new Utf8JsonReader(body, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return null;

        string? requestId = null;
        string? subjectId = null;
        List<string>? emails = null;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            if (!reader.Read())
                return null;

            switch (name)
            {
                case "request_id" when requestId is null && reader.TokenType == JsonTokenType.String:
                    requestId = reader.GetString();
                    break;
                case "subject_id" when subjectId is null && reader.TokenType == JsonTokenType.String:
                    subjectId = reader.GetString();
                    break;
                case "emails" when emails is null && reader.TokenType == JsonTokenType.StartArray:
                    emails = [];
                    while (reader.Read() && reader.TokenType == JsonTokenType.String)
                    {
                        if (emails.Count == MaxEmails)
                            return null;
                        emails.Add(reader.GetString()!);
                    }
                    if (reader.TokenType != JsonTokenType.EndArray)
                        return null;
                    break;
                default:
                    return null;
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            return null;

        if (!TryParseRequestId(requestId, out var parsedRequestId))
            return null;

        // Keycloak sub'ları küçük harfli tireli UUID'dir ve Forms onları öyle tutar;
        // başka bir yazılış hiçbir satırla eşleşmez.
        if (subjectId is null ||
            !Guid.TryParseExact(subjectId, "D", out var parsedSubject) ||
            parsedSubject.ToString("D") != subjectId ||
            parsedSubject == DeletedUser.Id)
        {
            return null;
        }

        if (emails is null || !emails.All(IsPlainAddress))
            return null;

        var normalized = emails
            .Select(email => email.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new AccountErasureCommand(parsedRequestId, parsedSubject, normalized);
    }

    /// <summary>
    /// Core'un gönderdiği biçimde bir adres: boşluk ya da kontrol karakteri yok, son
    /// @'nin iki yanı dolu, en çok 254 karakter. Adresler metin içinde arandığı için
    /// açıkça adres olmayan bir değer eşleştirilmek yerine reddedilir.
    /// </summary>
    private static bool IsPlainAddress(string email)
    {
        if (email.Length == 0 || email.Length > MaxEmailLength)
            return false;
        if (email.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
            return false;

        var at = email.LastIndexOf('@');
        return at > 0 && at < email.Length - 1;
    }
}
