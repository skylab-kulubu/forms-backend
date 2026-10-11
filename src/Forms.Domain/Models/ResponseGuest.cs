using System.Text.RegularExpressions;

namespace Skylab.Forms.Domain.Models;

/// <summary>
/// Formu giriş yapmadan dolduran misafirin, formun kimlik alanlarına (props.identity) yazdıkları;
/// ad ve soyad sorulmayan formda yalnız form sahibinin işaretlediği e-posta sorusuna yazdığı adres.
/// Kayıtlı kullanıcının yanıtında hiç kayıt olmaz; onun kimliği profilinden gelir.
/// </summary>
public class ResponseGuest
{
    /// <summary>Form sayfasındaki notla aynı kural: forms-frontend FormDisplayer.jsx EMAIL_PATTERN.</summary>
    private const string EmailPattern = @"^[^\s@]+@[^\s@]+\.[^\s@]+$";

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Yalnız işaretli e-posta sorusundan gelen misafirin adı ve soyadı yoktur.</summary>
    public bool HasName => !string.IsNullOrEmpty(FirstName) && !string.IsNullOrEmpty(LastName);

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
    /// Ad ve soyad sorulmayan formda form sahibinin props.identity "email" ile işaretlediği soruya yazılan adres; ad ve soyad boş kalır.
    /// İşaretli soru yoksa ya da cevabı adres değilse null: cevap yine kabul edilir, yalnız misafire mail gitmez.
    /// </summary>
    public static ResponseGuest? FromEmailQuestion(IEnumerable<FormSchemaItem> schema, IReadOnlyList<FormResponseSchemaItem> answers)
    {
        var field = schema.FirstOrDefault(field => IdentityOf(field) == "email");
        var email = field is null ? null : answers.FirstOrDefault(answer => answer.Id == field.Id)?.Answer?.Trim();

        return email is not null && Regex.IsMatch(email, EmailPattern) ? new ResponseGuest { Email = email.ToLowerInvariant() } : null;
    }

    private static string? IdentityOf(FormSchemaItem field) =>
        field.Props is not null && field.Props.TryGetValue("identity", out var value) ? value?.ToString() : null;
}
