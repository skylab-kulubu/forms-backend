using Microsoft.Extensions.Options;
using Skylab.Forms.Api.Auth;
using Skylab.Forms.Api.AccountAccess;
using Skylab.Forms.Api.Endpoints;
using Skylab.Forms.Api.Extensions;
using Skylab.Forms.Application;
using Skylab.Forms.Infrastructure;
using Skylab.Forms.Infrastructure.Integrations.Cloudflare;
using Skylab.Forms.Infrastructure.Integrations.Oidc;
using Skylab.Forms.Infrastructure.Integrations.SkyMail;
using Skylab.Forms.Infrastructure.Integrations.SkylabCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFrontendCors();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddTrustedForwardedHeaders();
builder.Services.AddGuestUploadRateLimit();
builder.Services.AddFormsJwtAuthentication();
builder.Services.AddFormsAuthorization();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructure();
builder.Services.AddOidcIdentity(builder.Configuration);
builder.Services.AddSkylabCore(builder.Configuration);
builder.Services.AddSkyMail(builder.Configuration);
builder.Services.AddTurnstile(builder.Configuration);

var app = builder.Build();

// Eksik ya da hatalı bir ayar, migration veritabanına dokunmadan açılışı durdurur.
app.Services.GetRequiredService<IStartupValidator>().Validate();
app.Services.LogIntegrationState(app.Logger);

app.UseInternalEndpointGuard();
app.UseForwardedHeaders();

await app.Services.ApplyDatabaseMigrationsAsync();

app.UseCors(FrontendCorsExtensions.PolicyName);
app.UseRateLimiter();
app.UseErrorResponses();
app.UseFormsJwtAuthentication();
app.UseAccountAccessGate();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapAccountAccessHealthEndpoints();
app.MapFormAdminEndpoints();
app.MapWorkflowAdminEndpoints();
app.MapFormEndpoints();
app.MapInternalEndpoints();

app.Run();

// Entegrasyon testleri uygulamayı WebApplicationFactory<Program> ile açar.
public partial class Program;
