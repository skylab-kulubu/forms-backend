using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Skylab.Forms.Infrastructure.Auth;

namespace Skylab.Forms.Tests.Infrastructure;

public class ProviderRoleClaimsTests
{
    private static readonly ProviderRoleOptions Roles = new()
    {
        Clients = ["forms", "dotnet"],
        PlatformAdmin = "skyforms:*",
        AccountErasure = "skyforms:account:erase"
    };

    private static async Task<ClaimsPrincipal> SignInAsync(ProviderRoleOptions roles, params Claim[] claims)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));

        return await new ProviderRoleClaims(Options.Create(roles)).TransformAsync(principal);
    }

    private static Claim ClientRoles(string client, params string[] roles) =>
        new("resource_access", JsonSerializer.Serialize(new Dictionary<string, object> { [client] = new { roles } }), "JSON");

    [Theory]
    [InlineData("forms")]
    [InlineData("dotnet")]
    public async Task A_configured_client_role_becomes_the_platform_admin_permission(string client)
    {
        var principal = await SignInAsync(Roles, ClientRoles(client, "SKYFORMS:*"));

        Assert.True(FormsPermissions.Has(principal, FormsPermissions.PlatformAdmin));
        Assert.False(FormsPermissions.Has(principal, FormsPermissions.AccountErasure));
    }

    [Fact]
    public async Task A_role_of_another_client_grants_nothing() =>
        Assert.False(FormsPermissions.Has(await SignInAsync(Roles, ClientRoles("core", "skyforms:*")), FormsPermissions.PlatformAdmin));

    [Fact]
    public async Task A_permission_claim_carried_by_the_token_itself_is_ignored()
    {
        var principal = await SignInAsync(Roles, new Claim(FormsPermissions.ClaimType, FormsPermissions.PlatformAdmin, ClaimValueTypes.String, "https://auth.example"));

        Assert.False(FormsPermissions.Has(principal, FormsPermissions.PlatformAdmin));
    }

    [Fact]
    public async Task An_unconfigured_role_is_granted_to_nobody() =>
        Assert.False(FormsPermissions.Has(await SignInAsync(new ProviderRoleOptions { Clients = ["forms"] }, ClientRoles("forms", "")), FormsPermissions.PlatformAdmin));

    [Fact]
    public async Task Running_twice_adds_the_permission_once()
    {
        var transformation = new ProviderRoleClaims(Options.Create(Roles));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([ClientRoles("forms", "skyforms:*")], "Bearer"));

        await transformation.TransformAsync(await transformation.TransformAsync(principal));

        Assert.Single(principal.FindAll(FormsPermissions.ClaimType));
    }
}
