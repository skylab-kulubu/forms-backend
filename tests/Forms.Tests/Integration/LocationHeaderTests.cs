using System.Net;
using System.Net.Http.Json;
using Skylab.Forms.Application.Contracts.ComponentGroup;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Workflows;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class LocationHeaderTests(FormsApiFactory api)
{
    [Fact]
    public async Task Every_created_resource_points_at_a_route_that_answers()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);

        var form = await owner.PostAsJsonAsync("/api/admin/forms/", Api.NewForm("Kulüp başvurusu", Api.Question("name")));
        var formId = (await form.DataAsync<FormContract>()).Id;
        var answer = await api.ClientFor(api.Users.Add("Mehmet Kaya").Id).SubmitAsync(formId, ("name", "Mehmet"));
        var template = await owner.PostAsJsonAsync("/api/admin/forms/component-groups", new ComponentGroupUpsertRequest(null, "Kimlik alanları", null, []));
        var workflow = await owner.PostAsJsonAsync("/api/admin/workflows/", new WorkflowUpsertRequest("Başvuru akışı", null, false));

        foreach (var created in new[] { form, answer, template, workflow })
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(created.Headers.Location)).StatusCode);
        }
    }
}
