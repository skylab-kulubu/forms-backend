using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AttemptClockTests(FormsApiFactory api)
{
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(31, HttpStatusCode.Created)]
    [InlineData(33, HttpStatusCode.Gone)]
    public async Task A_timed_answer_is_taken_until_the_grace_after_the_deadline_runs_out(int minutesAfterStart, HttpStatusCode expected)
    {
        var clock = new TestClock();
        await using var host = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        var owner = host.CreateClient();
        owner.DefaultRequestHeaders.Authorization = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id).DefaultRequestHeaders.Authorization;
        var candidate = host.CreateClient();
        candidate.DefaultRequestHeaders.Authorization = api.ClientFor(api.Users.Add("Mehmet Kaya").Id).DefaultRequestHeaders.Authorization;

        var exam = await owner.CreateFormAsync(Api.NewForm("Sınav", Api.Question("name")) with { TimeLimitMinutes = 30 });
        (await candidate.PostAsync($"/api/forms/{exam.Id}/attempt", null)).EnsureSuccessStatusCode();

        // Süre 30 dakika, gönderim için 2 dakikalık pay var.
        clock.Now = clock.Now.AddMinutes(minutesAfterStart);
        var submitted = await candidate.SubmitAsync(exam.Id, ("name", "Mehmet"));

        Assert.Equal(expected, submitted.StatusCode);
    }
}
