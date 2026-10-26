using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Contracts.Draft;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

/// <summary>"Form şu an cevap alıyor mu?" sorusunun tek cevabı her uçta aynı olmalı.</summary>
[Collection(ApiCollection.Name)]
public class FormAvailabilityTests(FormsApiFactory api)
{
    [Fact]
    public async Task A_form_past_its_closing_time_takes_no_draft_and_shows_no_share_card()
    {
        var form = await api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id).CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));
        await CloseAsync(form.Id);
        var applicant = api.ClientFor(api.Users.Add("Mehmet Kaya").Id);

        var draft = await applicant.PostAsJsonAsync("/api/forms/responses/draft", new ResponseDraftRequest(form.Id, [new FormResponseSchemaItem { Id = "name", Answer = "Mehmet" }], 10));
        var card = await api.CreateClient().GetAsync($"/api/forms/{form.Id}/meta");

        Assert.Equal((HttpStatusCode.Gone, HttpStatusCode.Gone), (draft.StatusCode, card.StatusCode));
    }

    [Fact]
    public async Task A_timed_form_past_its_closing_time_still_takes_the_answer_of_a_running_attempt()
    {
        var exam = await api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id).CreateFormAsync(Api.NewForm("Sınav", Api.Question("name")) with { TimeLimitMinutes = 30 });
        var candidate = api.ClientFor(api.Users.Add("Mehmet Kaya").Id);
        (await candidate.PostAsync($"/api/forms/{exam.Id}/attempt", null)).EnsureSuccessStatusCode();
        await CloseAsync(exam.Id);

        Assert.Equal(HttpStatusCode.Created, (await candidate.SubmitAsync(exam.Id, ("name", "Mehmet"))).StatusCode);
    }

    private async Task CloseAsync(Guid formId)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<FormsDbContext>().Forms
            .Where(form => form.Id == formId)
            .ExecuteUpdateAsync(s => s.SetProperty(form => form.ClosesAt, DateTime.UtcNow.AddMinutes(-1)));
    }
}
