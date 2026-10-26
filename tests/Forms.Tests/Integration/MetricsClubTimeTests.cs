using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Metrics;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class MetricsClubTimeTests(FormsApiFactory api)
{
    private async Task<FormMetricsContract> MetricsOfOneAnswerAtAsync(DateTime submittedAt)
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var form = await owner.CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));
        (await api.ClientFor(api.Users.Add("Mehmet Kaya").Id).SubmitAsync(form.Id, ("name", "Mehmet"))).EnsureSuccessStatusCode();

        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<FormsDbContext>().Responses
                .Where(r => r.FormId == form.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.SubmittedAt, submittedAt));

        return await (await owner.GetAsync($"/api/admin/forms/{form.Id}/metrics")).DataAsync<FormMetricsContract>();
    }

    [Fact]
    public async Task An_answer_just_after_midnight_in_Istanbul_counts_on_that_day()
    {
        // İstanbul'da dün 00:30; UTC'de bir önceki gün 21:30.
        var yesterday = ClubTime.ToLocal(DateTime.UtcNow).Date.AddDays(-1);

        var metrics = await MetricsOfOneAnswerAtAsync(ClubTime.ToUtc(yesterday.AddMinutes(30)));

        Assert.Equal(yesterday.ToString("ddd", ClubTime.Culture), metrics.DailyTrend.Single(day => day.Count == 1).Label);
    }

    [Fact]
    public async Task An_hourly_bucket_is_labelled_with_the_club_hour()
    {
        var twoHoursAgo = DateTime.UtcNow.AddHours(-2);

        var metrics = await MetricsOfOneAnswerAtAsync(twoHoursAgo);

        Assert.Equal($"{ClubTime.ToLocal(twoHoursAgo):HH}:00", metrics.HourlyTrend.Single(hour => hour.Count == 1).Label);
    }
}
