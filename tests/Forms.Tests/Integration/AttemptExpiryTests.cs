using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AttemptExpiryTests(FormsApiFactory api)
{
    [Fact]
    public async Task Opening_the_form_inside_the_grace_window_keeps_the_attempt_open_for_the_answer_that_follows()
    {
        var exam = await api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id).CreateFormAsync(Api.NewForm("Sınav", Api.Question("name")) with { TimeLimitMinutes = 30 });
        var candidate = api.Users.Add("Mehmet Kaya");
        var client = api.ClientFor(candidate.Id);
        (await client.PostAsync($"/api/forms/{exam.Id}/attempt", null)).EnsureSuccessStatusCode();

        using (var scope = api.Services.CreateScope())
        {
            // Süre yarım dakika önce doldu: ek süre içindeyiz, aday sayfayı yeniler.
            await scope.ServiceProvider.GetRequiredService<FormsDbContext>().Attempts
                .Where(a => a.FormId == exam.Id && a.UserId == candidate.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeadlineAt, DateTime.UtcNow.AddSeconds(-30)));
        }

        (await client.GetAsync($"/api/forms/{exam.Id}")).EnsureSuccessStatusCode();
        var submitted = await client.SubmitAsync(exam.Id, ("name", "Mehmet"));

        using var check = api.Services.CreateScope();
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        Assert.Equal(FormAttemptStatus.Submitted, await check.ServiceProvider.GetRequiredService<FormsDbContext>().Attempts
            .Where(a => a.FormId == exam.Id && a.UserId == candidate.Id).Select(a => a.Status).SingleAsync());
    }

    [Fact]
    public async Task Opening_the_form_after_the_grace_window_closes_the_attempt()
    {
        var exam = await api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id).CreateFormAsync(Api.NewForm("Sınav", Api.Question("name")) with { TimeLimitMinutes = 30 });
        var candidate = api.Users.Add("Mehmet Kaya");
        var client = api.ClientFor(candidate.Id);
        (await client.PostAsync($"/api/forms/{exam.Id}/attempt", null)).EnsureSuccessStatusCode();

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        await db.Attempts.Where(a => a.FormId == exam.Id && a.UserId == candidate.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeadlineAt, DateTime.UtcNow.AddMinutes(-5)));

        (await client.GetAsync($"/api/forms/{exam.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(FormAttemptStatus.NoSubmission, await db.Attempts.Where(a => a.FormId == exam.Id && a.UserId == candidate.Id).Select(a => a.Status).SingleAsync());
    }
}
