using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class MailRecipientsTests(FormsApiFactory api)
{
    private static FormSchemaItem Identity(string id, string identity)
    {
        var field = Api.Question(id);
        field.Props["identity"] = identity;

        return field;
    }

    [Fact]
    public async Task Members_are_reached_through_their_account_and_guests_through_their_identity_answers()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var form = await owner.CreateFormAsync(
            Api.NewForm("Tanışma etkinliği", Identity("ad", "firstName"), Identity("soyad", "lastName"), Identity("eposta", "email"))
                with { AllowAnonymousResponses = true, AllowMultipleResponses = true });
        var member = api.Users.Add("Mehmet Kaya");
        var guest = api.CreateClient();

        (await api.ClientFor(member.Id).SubmitAsync(form.Id, ("ad", "M"), ("soyad", "K"), ("eposta", "baska@ornek.com"))).EnsureSuccessStatusCode();
        (await guest.SubmitAsync(form.Id, ("ad", "Zeynep"), ("soyad", "Ak"), ("eposta", "zeynep@ornek.com"))).EnsureSuccessStatusCode();
        (await guest.SubmitAsync(form.Id, ("ad", "Zeynep"), ("soyad", "Ak"), ("eposta", "ZEYNEP@ornek.com"))).EnsureSuccessStatusCode();

        var recipients = await (await owner.GetAsync($"/api/admin/forms/{form.Id}/mail-recipients")).DataAsync<FormMailRecipientsContract>();

        Assert.Equal("Tanışma etkinliği", recipients.FormTitle);
        Assert.Equal(3, recipients.ResponseCount);
        Assert.Equal(1, recipients.DuplicateCount);
        Assert.Equal(0, recipients.SkippedCount);
        Assert.Equal([new("Mehmet Kaya", member.Email!), new("Zeynep Ak", "zeynep@ornek.com")], recipients.Recipients);
    }

    [Fact]
    public async Task A_guest_is_told_the_address_is_wrong_and_a_pasted_mailto_goes_through()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var form = await owner.CreateFormAsync(
            Api.NewForm("Tanışma etkinliği", Identity("ad", "firstName"), Identity("soyad", "lastName"), Identity("eposta", "email"))
                with { AllowAnonymousResponses = true, AllowMultipleResponses = true });
        var guest = api.CreateClient();

        var wrong = await guest.SubmitAsync(form.Id, ("ad", "Zeynep"), ("soyad", "Ak"), ("eposta", "zeynep@ornek"));
        var pasted = await guest.SubmitAsync(form.Id, ("ad", "Zeynep"), ("soyad", "Ak"), ("eposta", "mailto:Zeynep@Ornek.com"));

        using var scope = api.Services.CreateScope();
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("E-posta adresi geçersiz görünüyor; kontrol edip tekrar deneyin.", (await wrong.Content.ReadFromJsonAsync<ServiceResult<object>>())?.Message);
        Assert.Equal(HttpStatusCode.Created, pasted.StatusCode);
        Assert.Equal("zeynep@ornek.com", await scope.ServiceProvider.GetRequiredService<FormsDbContext>().Responses
            .Where(r => r.FormId == form.Id).Select(r => r.Guest!.Email).SingleAsync());
    }
}
