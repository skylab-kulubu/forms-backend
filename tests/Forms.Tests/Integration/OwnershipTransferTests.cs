using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Ownership;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class OwnershipTransferTests(FormsApiFactory api)
{
    private sealed class UnreachableDirectory : IUserDirectory
    {
        public Task<UserContract?> GetUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<UserContract?>(null);

        public Task<List<UserContract>> GetUsersAsync(IEnumerable<Guid> userIds, CancellationToken ct = default) => Task.FromResult(new List<UserContract>());

        public Task<UserLookup> FindUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(new UserLookup(UserLookupOutcome.Unavailable));
    }

    [Fact]
    public async Task A_form_moves_only_to_a_person_the_directory_knows()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var form = await owner.CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));

        var toStranger = await owner.PostAsJsonAsync($"/api/admin/forms/{form.Id}/transfer", new OwnershipTransferRequest(Guid.NewGuid()));
        var toMember = await owner.PostAsJsonAsync($"/api/admin/forms/{form.Id}/transfer", new OwnershipTransferRequest(api.Users.Add("Mehmet Kaya").Id));

        Assert.Equal(HttpStatusCode.BadRequest, toStranger.StatusCode);
        Assert.Equal(HttpStatusCode.OK, toMember.StatusCode);
    }

    [Fact]
    public async Task An_unreachable_directory_is_not_reported_as_a_missing_person()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var form = await owner.CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));

        await using var directoryDown = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUserDirectory>();
            services.AddSingleton<IUserDirectory, UnreachableDirectory>();
        }));
        var client = directoryDown.CreateClient();
        client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;

        var transfer = await client.PostAsJsonAsync($"/api/admin/forms/{form.Id}/transfer", new OwnershipTransferRequest(api.Users.Add("Mehmet Kaya").Id));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, transfer.StatusCode);
    }
}
