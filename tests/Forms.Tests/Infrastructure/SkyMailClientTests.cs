using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Infrastructure.Mail;

namespace Skylab.Forms.Tests.Infrastructure;

public class SkyMailClientTests
{
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }

    private static (SkyMailClient Client, StubHandler Handler) Client(HttpStatusCode status)
    {
        var handler = new StubHandler(status);

        return (new SkyMailClient(new HttpClient(handler) { BaseAddress = new Uri("http://skymail.test/v1/") }, NullLogger<SkyMailClient>.Instance), handler);
    }

    [Fact]
    public async Task A_mail_goes_out_in_skymails_snake_case_body()
    {
        var (client, handler) = Client(HttpStatusCode.Accepted);

        var sent = await client.SendAsync(new EmailMessage("forms.form-copy", "ayse@example.com", "Ayşe Yılmaz", new() { ["formTitle"] = "Başvuru" }));

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.True(sent);
        Assert.Equal("forms.form-copy", body.RootElement.GetProperty("template_key").GetString());
        Assert.Equal("ayse@example.com", body.RootElement.GetProperty("recipient_email").GetString());
        Assert.Equal("Ayşe Yılmaz", body.RootElement.GetProperty("recipient_full_name").GetString());
        Assert.Equal("Başvuru", body.RootElement.GetProperty("body_variables").GetProperty("formTitle").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, MailAudienceOutcome.TooLarge)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, MailAudienceOutcome.TooLarge)]
    [InlineData(HttpStatusCode.InternalServerError, MailAudienceOutcome.Unavailable)]
    public async Task An_audience_export_reports_what_happened_without_wording_it(HttpStatusCode status, MailAudienceOutcome expected)
    {
        var (client, _) = Client(status);
        var audience = new FormMailRecipientsContract(Guid.NewGuid(), Guid.NewGuid(), "Başvuru", [new FormMailRecipientContract("Ayşe", "ayse@example.com")], 1, 0, 0);

        Assert.Equal(expected, (await client.ExportAsync(audience)).Outcome);
    }
}
