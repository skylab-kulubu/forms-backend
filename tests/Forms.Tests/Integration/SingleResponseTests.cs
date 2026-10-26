using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Application.Services.AccountErasure;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class SingleResponseTests(FormsApiFactory api)
{
    [Fact]
    public async Task Sending_a_single_answer_form_several_times_at_once_keeps_one_answer()
    {
        var form = await api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id).CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));
        var applicant = api.Users.Add("Mehmet Kaya");
        var client = api.ClientFor(applicant.Id);

        var sent = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.SubmitAsync(form.Id, ("name", "Mehmet"))));

        using var scope = api.Services.CreateScope();
        Assert.Single(sent, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(sent.Where(response => response.StatusCode != HttpStatusCode.Created), response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<FormsDbContext>().Responses.CountAsync(r => r.FormId == form.Id && r.UserId == applicant.Id));
    }

    [Fact]
    public async Task Erasing_two_people_who_answered_the_same_single_answer_form_keeps_both_answers()
    {
        var form = await api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id).CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));
        var first = api.Users.Add("Mehmet Kaya");
        var second = api.Users.Add("Zeynep Ak");
        (await api.ClientFor(first.Id).SubmitAsync(form.Id, ("name", "Mehmet"))).EnsureSuccessStatusCode();
        (await api.ClientFor(second.Id).SubmitAsync(form.Id, ("name", "Zeynep"))).EnsureSuccessStatusCode();

        using var scope = api.Services.CreateScope();
        var erasure = scope.ServiceProvider.GetRequiredService<IAccountErasureService>();

        // İki cevap da aynı "Silinmiş kullanıcı"ya geçer; tek cevap kuralı buna takılmamalı.
        await erasure.EraseAsync(new AccountErasureCommand(Guid.NewGuid(), first.Id, []));
        await erasure.EraseAsync(new AccountErasureCommand(Guid.NewGuid(), second.Id, []));

        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<FormsDbContext>().Responses.CountAsync(r => r.FormId == form.Id));
    }
}
