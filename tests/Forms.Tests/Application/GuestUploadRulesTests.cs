using Skylab.Forms.Application.GuestUploads;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Tests.Application;

public class GuestUploadRulesTests
{
    [Theory]
    [InlineData("CV.PDF", GuestUploadRules.Pdf)]
    [InlineData("foto.jfif", GuestUploadRules.Jpeg)]
    [InlineData("belge.docx", null)]
    [InlineData(null, null)]
    public void A_guest_file_type_comes_from_known_extensions(string? fileName, string? expected) =>
        Assert.Equal(expected, GuestUploadRules.TypeOfFileName(fileName));

    [Theory]
    [InlineData("image/jpg; charset=binary", GuestUploadRules.Jpeg)]
    [InlineData(" IMAGE/PNG ", GuestUploadRules.Png)]
    [InlineData("application/zip", null)]
    public void Declared_content_types_are_normalized(string contentType, string? expected) =>
        Assert.Equal(expected, GuestUploadRules.TypeOfContentType(contentType));

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\Users\\ali\\cv.pdf", "cv.pdf")]
    [InlineData("fatura\u202Efdp.exe", "faturafdp.exe")]
    [InlineData("\u202E\u2066", "dosya")]
    public void File_names_lose_their_path_and_hidden_direction_marks(string fileName, string expected) =>
        Assert.Equal(expected, GuestUploadRules.SanitizeFileName(fileName));

    [Fact]
    public void Accepted_files_narrow_the_supported_types()
    {
        var question = new FormSchemaItem { Type = GuestUploadRules.FileQuestionType, Props = { ["acceptedFiles"] = "image/*, .docx" } };

        Assert.Equal(new[] { GuestUploadRules.Jpeg, GuestUploadRules.Png }, GuestUploadRules.EffectiveTypes(question));
        Assert.Equal(new[] { GuestUploadRules.Jpeg, GuestUploadRules.Png, GuestUploadRules.Docx }, GuestUploadRules.EffectiveAccountTypes(question));
    }

    [Theory]
    [InlineData(null, GuestUploadRules.MaxBytes)]
    [InlineData(10.0, 10L * 1024 * 1024)]
    [InlineData(0.5, 512L * 1024)]
    [InlineData(500.0, GuestUploadRules.MaxBytes)]
    [InlineData(-1.0, GuestUploadRules.MaxBytes)]
    public void The_size_limit_is_read_in_megabytes_and_capped(double? megabytes, long expected)
    {
        var question = new FormSchemaItem { Type = GuestUploadRules.FileQuestionType };
        if (megabytes is { } size) question.Props["maxSize"] = size;

        Assert.Equal(expected, GuestUploadRules.EffectiveMaxBytes(question));
    }

    [Fact]
    public void A_new_session_id_passes_the_session_id_check() =>
        Assert.True(GuestUploadKeys.IsSessionId(GuestUploadKeys.NewSessionId()));
}
