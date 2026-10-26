using System.Net;
using System.Net.Http.Json;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.GuestUploads;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.GuestUploads;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class GuestUploadGateTests(FormsApiFactory api)
{
    [Fact]
    public async Task A_signed_in_answer_to_a_file_question_must_be_a_file_id()
    {
        var form = await api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id).CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("cv", "file")));

        var sent = await api.ClientFor(api.Users.Add("Mehmet Kaya").Id).SubmitAsync(form.Id, ("cv", "https://evil.example/cv.pdf"));
        var body = await sent.Content.ReadFromJsonAsync<ServiceResult<ResponseSubmitResult>>();

        // Misafir yolundaki geçersiz dosya cevabıyla aynı cevap: dosyayı yeniden yükle.
        Assert.Equal(HttpStatusCode.Gone, sent.StatusCode);
        Assert.Equal(GuestUploadReason.FileExpired, body?.Data?.Reason);
    }

    [Fact]
    public async Task Guest_uploads_past_the_per_address_limit_are_refused_before_their_body_is_read()
    {
        await using var host = api.WithWebHostBuilder(_ => { });
        var client = host.CreateClient();
        var formId = Guid.NewGuid();

        HttpResponseMessage last = null!;
        for (var upload = 0; upload <= GuestRateLimits.IpFilesPerMinute; upload++)
            last = await client.PostAsync($"/api/forms/{formId}/guest-uploads", new MultipartFormDataContent { { new StringContent("cv"), "questionId" } });

        var body = await last.Content.ReadFromJsonAsync<ServiceResult<GuestUploadContract>>();

        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.Equal(GuestUploadReason.TooManyUploads, body?.Data?.Reason);
        Assert.True(last.Headers.RetryAfter is not null);
    }
}
