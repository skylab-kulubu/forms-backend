using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Skylab.Forms.Infrastructure.Auth;
using Xunit;

namespace Skylab.Forms.Api.Tests;

public sealed class JwtCurrentUserServiceTests
{
    private static readonly Guid Subject = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Reads_subject_and_roles_from_authenticated_principal()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", Subject.ToString()),
                new Claim("realm_access", "{\"roles\":[\"ADMIN\"]}"),
                new Claim(
                    "resource_access",
                    "{\"forms\":{\"roles\":[\"skyforms:*\"]},\"dotnet\":{\"roles\":[\"legacy-role\"]}}")
            ], "validated-jwt"))
        };
        var service = CreateService(context);

        Assert.Equal(Subject, await service.GetUserIdAsync());
        Assert.True(await service.HasRoleAsync("admin"));
        Assert.True(await service.HasRoleAsync("SKYFORMS:*", "forms"));
        Assert.True(await service.HasRoleAsync("LEGACY-ROLE", "forms"));
    }

    [Fact]
    public async Task Ignores_unverified_claims_even_when_request_contains_bearer_header()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", Subject.ToString()),
                new Claim("realm_access", "{\"roles\":[\"ADMIN\"]}"),
                new Claim("resource_access", "{\"forms\":{\"roles\":[\"skyforms:*\"]}}")
            ]))
        };
        context.Request.Headers.Authorization = "Bearer forged.jwt.value";
        var service = CreateService(context);

        Assert.Null(await service.GetUserIdAsync());
        Assert.False(await service.HasRoleAsync("ADMIN"));
        Assert.False(await service.HasRoleAsync("skyforms:*", "forms"));
    }

    [Fact]
    public async Task Does_not_mix_claims_from_an_unauthenticated_identity_into_validated_identity()
    {
        var validatedIdentity = new ClaimsIdentity(authenticationType: "validated-jwt");
        var unverifiedIdentity = new ClaimsIdentity(
        [
            new Claim("sub", Subject.ToString()),
            new Claim("realm_access", "{\"roles\":[\"ADMIN\"]}"),
            new Claim("resource_access", "{\"forms\":{\"roles\":[\"skyforms:*\"]}}")
        ]);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal([validatedIdentity, unverifiedIdentity])
        };
        var service = CreateService(context);

        Assert.Null(await service.GetUserIdAsync());
        Assert.False(await service.HasRoleAsync("ADMIN"));
        Assert.False(await service.HasRoleAsync("skyforms:*", "forms"));
    }

    [Fact]
    public async Task Malformed_validated_role_claims_do_not_grant_roles()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", Subject.ToString()),
                new Claim("realm_access", "not-json"),
                new Claim("resource_access", "not-json")
            ], "validated-jwt"))
        };
        var service = CreateService(context);

        Assert.Equal(Subject, await service.GetUserIdAsync());
        Assert.False(await service.HasRoleAsync("ADMIN"));
        Assert.False(await service.HasRoleAsync("skyforms:*", "forms"));
    }

    private static JwtCurrentUserService CreateService(HttpContext context)
    {
        return new JwtCurrentUserService(new HttpContextAccessor { HttpContext = context });
    }
}
