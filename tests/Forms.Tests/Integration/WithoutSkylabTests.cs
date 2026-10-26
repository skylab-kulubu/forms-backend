using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

/// <summary>
/// Kabul testi "Skylab'siz açılış": core, SkyMail, Turnstile, hesap engeli ve misafir yüklemeleri kapalıyken Forms açılır
/// ve temel akışlar çalışır. Paylaşılan fabrikanın aksine sahte kişi dizini ya da mail kaydedici yoktur; kapalı her
/// sağlayıcı Forms'un kendi yedeğiyle çalışır.
/// </summary>
[Collection(ApiCollection.Name)]
public class WithoutSkylabTests(FormsApiFactory api)
{
    private sealed class SkylabFree(FormsApiFactory api) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            foreach (var (key, value) in new Dictionary<string, string>
            {
                ["ConnectionStrings:Forms"] = api.PostgresConnectionString,
                ["Redis:ConnectionString"] = api.RedisEndpoint,
                ["Authentication:Issuer"] = TestTokens.Issuer,
                ["Authentication:Roles:Clients:0"] = "forms",
                ["Authentication:Roles:PlatformAdmin"] = "skyforms:*",
                ["Authentication:Roles:AccountErasure"] = "skyforms:account:erase",
                ["AccountAccessGate:Mode"] = "off",
                ["Integrations:SkylabCore:BaseUrl"] = "",
                ["Integrations:SkyMail:BaseUrl"] = "",
                ["Integrations:Turnstile:SecretKey"] = "",
                ["GuestUploads:Mode"] = "off",
                ["Authentication:ServiceAccount:TokenUrl"] = "",
                ["Authentication:ServiceAccount:ClientId"] = "",
                ["Authentication:ServiceAccount:ClientSecret"] = "",
                ["FormMail:FormCopyTemplateKey"] = "",
                ["FormMail:StatusChangedTemplateKey"] = "",
                ["FormMail:PendingReminderTemplateKey"] = "",
                ["FormMail:AttemptUpdateTemplateKey"] = ""
            })
                builder.UseSetting(key, value);

            builder.ConfigureTestServices(services =>
            {
                FormsApiFactory.TrustTestTokens(services);
                FormsApiFactory.RemoveWorkers(services);
            });
        }
    }

    [Fact]
    public async Task Forms_opens_and_its_core_flows_work_with_every_Skylab_integration_switched_off()
    {
        await using var host = new SkylabFree(api);

        HttpClient As(Guid userId)
        {
            var client = host.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.For(userId));
            return client;
        }

        var owner = As(Guid.NewGuid());
        var member = As(Guid.NewGuid());

        // Form: oluşturulur, doldurulur, incelenir.
        var form = await owner.CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")) with { RequiresManualReview = true });
        var answered = await member.SubmitAsync(form.Id, ("name", "Mehmet"));
        Assert.Equal(HttpStatusCode.Created, answered.StatusCode);
        var responseId = (await answered.DataAsync<ResponseSubmitResult>()).ResponseId!.Value;

        var approved = await owner.PatchAsJsonAsync($"/api/admin/forms/responses/{responseId}/status", new { newStatus = FormResponseStatus.Approved });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(FormResponseStatus.Approved, (await (await owner.GetAsync($"/api/admin/forms/responses/{responseId}")).DataAsync<ResponseContract>()).Status);

        // Misafir giriş yapmadan doldurur.
        var open = await owner.CreateFormAsync(Api.NewForm("Etkinlik anketi", Api.Question("name")) with { AllowAnonymousResponses = true, AllowMultipleResponses = true });
        Assert.Equal(HttpStatusCode.Created, (await host.CreateClient().SubmitAsync(open.Id, ("name", "Zeynep"))).StatusCode);

        // Akış: başvuru incelenir, onaylanınca aday ikinci adımı doldurur ve başvuru tamamlanır.
        var apply = await owner.CreateFormAsync(Api.NewForm("Başvuru", Api.Question("name")));
        var interview = await owner.CreateFormAsync(Api.NewForm("Mülakat", Api.Question("name")));
        var workflowId = await owner.PublishWorkflowAsync("Başvuru akışı", new WorkflowDefinitionRequest(
            [
                new WorkflowNodeRequest("apply", apply.Id, IsStart: true, RequiresManualReview: true),
                new WorkflowNodeRequest("interview", interview.Id, IsStart: false, RequiresManualReview: false)
            ],
            [
                new WorkflowTransitionRequest("apply", "interview", WorkflowTransitionTrigger.ResponseApproved, null, Priority: 0),
                new WorkflowTransitionRequest("apply", null, WorkflowTransitionTrigger.ResponseDeclined, null, Priority: 0),
                new WorkflowTransitionRequest("interview", null, WorkflowTransitionTrigger.ResponseSubmitted, null, Priority: 0)
            ]));

        var applicantId = Guid.NewGuid();
        var applicant = As(applicantId);
        var applied = await (await applicant.SubmitAsync(apply.Id, ("name", "Aday"))).DataAsync<ResponseSubmitResult>();
        (await owner.PatchAsJsonAsync($"/api/admin/forms/responses/{applied.ResponseId}/status", new { newStatus = FormResponseStatus.Approved })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await applicant.GetAsync($"/api/forms/{interview.Id}")).StatusCode);
        (await applicant.SubmitAsync(interview.Id, ("name", "Aday"))).EnsureSuccessStatusCode();

        using (var scope = host.Services.CreateScope())
        {
            var run = await scope.ServiceProvider.GetRequiredService<FormsDbContext>().WorkflowInstances
                .SingleAsync(item => item.WorkflowId == workflowId && item.UserId == applicantId);
            Assert.Equal(WorkflowInstanceStatus.Completed, run.Status);
        }

        // Kapalı sağlayıcıların uçları arıza gibi değil, yetenek yokmuş gibi cevap verir.
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/admin/forms/{form.Id}/short-link")).StatusCode);
        Assert.Null((await (await host.CreateClient().GetAsync("/api/forms/guest-uploads")).Content.ReadFromJsonAsync<ServiceResult<object>>())?.Data);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/admin/forms/{form.Id}/mail-recipients")).StatusCode);
    }
}
