using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ResponseConcurrencyTests(FormsApiFactory api)
{
    [Fact]
    public async Task A_decision_loaded_before_another_reviewers_decision_cannot_overwrite_it()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var form = await owner.CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")) with { RequiresManualReview = true });
        var responseId = (await (await api.ClientFor(api.Users.Add("Mehmet Kaya").Id).SubmitAsync(form.Id, ("name", "Mehmet"))).DataAsync<ResponseSubmitResult>()).ResponseId!.Value;

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var stale = await db.Responses.SingleAsync(response => response.Id == responseId);

        (await owner.PatchAsJsonAsync($"/api/admin/forms/responses/{responseId}/status", new { newStatus = FormResponseStatus.Approved })).EnsureSuccessStatusCode();
        stale.ApplyReview(FormResponseStatus.Declined, Guid.NewGuid(), null, DateTime.UtcNow);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }
}
