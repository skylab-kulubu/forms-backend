using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Infrastructure.Integrations.SkyMail;

namespace Skylab.Forms.Tests.Infrastructure;

public class SkyMailClientTests
{
    /// <param name="reply">İsteğin gövdesinden cevabın gövdesini üretir.</param>
    private sealed class StubHandler(HttpStatusCode status, Func<string, string>? reply) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(status);
            if (reply is not null) response.Content = new StringContent(reply(Body ?? ""), Encoding.UTF8, "application/json");

            return response;
        }
    }

    private static (SkyMailClient Client, StubHandler Handler) Client(HttpStatusCode status, Func<string, string>? reply = null)
    {
        var handler = new StubHandler(status, reply);

        return (new SkyMailClient(new HttpClient(handler) { BaseAddress = new Uri("http://skymail.test/v1/") }, NullLogger<SkyMailClient>.Instance), handler);
    }

    private static FormMailRecipientsContract Audience() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Başvuru", [new FormMailRecipientContract("Ayşe", "ayse@example.com")], 1, 0, 0);

    /// <summary>SkyMail'in kaydettiği listeyi geri yazması; <paramref name="recipientCount"/> verilirse sayı bozulur.</summary>
    private static string Echo(string request, int? recipientCount = null)
    {
        using var sent = JsonDocument.Parse(request);
        var root = sent.RootElement;

        return JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(),
            form_id = root.GetProperty("form_id").GetGuid(),
            owner_sub = root.GetProperty("owner_sub").GetGuid(),
            form_title = root.GetProperty("form_title").GetString(),
            recipient_count = recipientCount ?? root.GetProperty("recipients").GetArrayLength(),
            response_count = root.GetProperty("response_count").GetInt32(),
            skipped_count = root.GetProperty("skipped_count").GetInt32(),
            duplicate_count = root.GetProperty("duplicate_count").GetInt32(),
            prepared_at = DateTimeOffset.UtcNow,
            expires_at = DateTimeOffset.UtcNow.AddDays(7)
        });
    }

    [Fact]
    public async Task A_mail_goes_out_in_skymails_snake_case_body()
    {
        var (client, handler) = Client(HttpStatusCode.Accepted);

        var sent = await client.SendAsync(new EmailMessage("forms.form-copy", "ayse@example.com", "Ayşe Yılmaz", new() { ["formTitle"] = "Başvuru" }));

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(MailDelivery.Sent, sent);
        Assert.Equal("forms.form-copy", body.RootElement.GetProperty("template_key").GetString());
        Assert.Equal("ayse@example.com", body.RootElement.GetProperty("recipient_email").GetString());
        Assert.Equal("Ayşe Yılmaz", body.RootElement.GetProperty("recipient_full_name").GetString());
        Assert.Equal("Başvuru", body.RootElement.GetProperty("body_variables").GetProperty("formTitle").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, MailDelivery.Refused)]
    [InlineData(HttpStatusCode.UnprocessableEntity, MailDelivery.Refused)]
    [InlineData(HttpStatusCode.Unauthorized, MailDelivery.RetryLater)]
    [InlineData(HttpStatusCode.NotFound, MailDelivery.RetryLater)]
    [InlineData(HttpStatusCode.TooManyRequests, MailDelivery.RetryLater)]
    [InlineData(HttpStatusCode.RequestTimeout, MailDelivery.RetryLater)]
    [InlineData(HttpStatusCode.BadGateway, MailDelivery.RetryLater)]
    public async Task A_mail_skymail_refuses_for_good_is_not_tried_again(HttpStatusCode status, MailDelivery expected)
    {
        var (client, _) = Client(status);

        Assert.Equal(expected, await client.SendAsync(new EmailMessage("forms.form-copy", "ayse@example.com", "Ayşe Yılmaz", [])));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, MailAudienceOutcome.TooLarge)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, MailAudienceOutcome.TooLarge)]
    [InlineData(HttpStatusCode.InternalServerError, MailAudienceOutcome.Unavailable)]
    public async Task An_audience_export_reports_what_happened_without_wording_it(HttpStatusCode status, MailAudienceOutcome expected)
    {
        var (client, _) = Client(status);

        Assert.Equal(expected, (await client.ExportAsync(Audience())).Outcome);
    }

    [Fact]
    public async Task An_audience_skymail_echoes_back_is_exported()
    {
        var audience = Audience();
        var (client, _) = Client(HttpStatusCode.Created, request => Echo(request));

        var export = await client.ExportAsync(audience);

        Assert.Equal(MailAudienceOutcome.Exported, export.Outcome);
        Assert.Equal((audience.FormId, audience.PreparedByUserId, 1), (export.Source?.FormId, export.Source?.PreparedByUserId, export.Source?.RecipientCount));
    }

    [Fact]
    public async Task An_echo_that_disagrees_with_the_request_is_not_trusted()
    {
        var (client, _) = Client(HttpStatusCode.Created, request => Echo(request, recipientCount: 2));

        Assert.Equal(MailAudienceOutcome.Unavailable, (await client.ExportAsync(Audience())).Outcome);
    }
}
