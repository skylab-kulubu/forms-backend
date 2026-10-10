using System.Net.Mail;

namespace Skylab.Forms.Domain.Models;

/// <summary>
/// Formu giriş yapmadan dolduran misafirin, formun kimlik alanlarına (props.identity) yazdıkları;
/// kimlik alanı olmayan formda yalnız e-posta sorusuna yazdığı adres.
/// Kayıtlı kullanıcının yanıtında hiç kayıt olmaz; onun kimliği profilinden gelir.
/// </summary>
public class ResponseGuest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Form ad, soyad ve e-postayı kimlik alanlarıyla soruyor mu.</summary>
    public static bool IsAskedBy(IEnumerable<FormSchemaItem> schema)
    {
        var keys = schema.Select(IdentityOf).ToHashSet();

        return keys.Contains("firstName") && keys.Contains("lastName") && keys.Contains("email");
    }

    /// <summary>Kimlik alanlarına yazılanlar; biri boşsa ya da e-posta geçersizse null.</summary>
    public static ResponseGuest? From(IEnumerable<FormSchemaItem> schema, IReadOnlyList<FormResponseSchemaItem> answers)
    {
        string? AnswerTo(string key) => schema
            .Where(field => IdentityOf(field) == key)
            .Select(field => answers.FirstOrDefault(answer => answer.Id == field.Id)?.Answer?.Trim())
            .FirstOrDefault(answer => !string.IsNullOrEmpty(answer));

        var firstName = AnswerTo("firstName");
        var lastName = AnswerTo("lastName");
        var email = AnswerTo("email");

        if (firstName is null || lastName is null || email is null || !email.Contains('@'))
            return null;

        return new ResponseGuest { FirstName = firstName, LastName = lastName, Email = email.ToLowerInvariant() };
    }

    /// <summary>
    /// Kimlik alanı olmayan formda e-posta tipli ilk kısa metin sorusuna yazılan geçerli adres; ad ve soyad boş kalır.
    /// Adres yoksa null: cevap yine kabul edilir, yalnız misafire mail gitmez.
    /// </summary>
    public static ResponseGuest? FromEmailQuestion(IEnumerable<FormSchemaItem> schema, IReadOnlyList<FormResponseSchemaItem> answers)
    {
        var email = schema
            .Where(field => field.Type == "short_text" && field.Props is not null && field.Props.TryGetValue("inputType", out var type) && type?.ToString() == "email")
            .Select(field => answers.FirstOrDefault(answer => answer.Id == field.Id)?.Answer?.Trim())
            .FirstOrDefault(answer => answer is { Length: <= 254 } && MailAddress.TryCreate(answer, out var address) && address.Address == answer
                && !answer.Contains('"') && address.Host.Contains('.') && !address.Host.StartsWith('['));

        return email is null ? null : new ResponseGuest { Email = email.ToLowerInvariant() };
    }

    private static string? IdentityOf(FormSchemaItem field) =>
        field.Props is not null && field.Props.TryGetValue("identity", out var value) ? value?.ToString() : null;
}
