using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Services.Mail;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

/// <summary>Core ayarlanmamışken Forms'un yetenekleri kapalıdır, ama hiçbiri arızaymış gibi davranmaz.</summary>
[Collection(ApiCollection.Name)]
public class WithoutCoreTests(FormsApiFactory api)
{
    [Fact]
    public async Task A_form_without_core_simply_has_no_short_link()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var form = await owner.CreateFormAsync(Api.NewForm("Kulüp başvurusu"));

        var link = await owner.GetAsync($"/api/admin/forms/{form.Id}/short-link");

        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
    }

    [Fact]
    public async Task Guest_uploads_stay_off_without_a_file_store_even_when_asked_for()
    {
        await using var host = api.WithWebHostBuilder(builder => builder
            .UseSetting("GuestUploads:Mode", "on")
            .UseSetting("Integrations:Turnstile:SecretKey", "1x0000000000000000000000000000000AA")
            .UseSetting("Integrations:Turnstile:AllowTestSecret", "true"));

        var capability = await (await host.CreateClient().GetAsync("/api/forms/guest-uploads")).Content.ReadFromJsonAsync<ServiceResult<object>>();

        Assert.Equal(ServiceStatus.Success, capability?.Status);
        Assert.Null(capability?.Data);
    }

    [Fact]
    public async Task A_reminder_that_reached_nobody_is_not_marked_as_sent()
    {
        // Kişi dizininde olmayan sahip: core kapalıyken herkes böyledir.
        var form = await api.ClientFor(Guid.NewGuid()).CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")) with { RequiresManualReview = true });
        var responseId = (await (await api.ClientFor(api.Users.Add("Mehmet Kaya").Id).SubmitAsync(form.Id, ("name", "Mehmet"))).DataAsync<ResponseSubmitResult>()).ResponseId!.Value;

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        await db.Responses.Where(r => r.Id == responseId).ExecuteUpdateAsync(s => s.SetProperty(r => r.SubmittedAt, DateTime.UtcNow.AddDays(-3)));

        await scope.ServiceProvider.GetRequiredService<IPendingResponseReminder>().RemindAsync();

        Assert.Null(await db.Responses.Where(r => r.Id == responseId).Select(r => r.PendingReminderSentAt).SingleAsync());
    }
}
