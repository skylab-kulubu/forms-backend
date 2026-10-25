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

var allowedOrigin = Environment.GetEnvironmentVariable("ALLOWED_ORIGIN")
    ?? builder.Configuration["Cors:AllowedOrigin"]
    ?? "http://localhost:3000";

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigin).AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddTrustedForwardedHeaders(builder.Configuration);
builder.Services.AddFormsJwtAuthentication(builder.Configuration);
builder.Services.AddFormsAuthorization();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOidcIdentity(builder.Configuration);
builder.Services.AddSkylabCore(builder.Configuration);
builder.Services.AddSkyMail(builder.Configuration);
builder.Services.AddTurnstile(builder.Configuration);

var app = builder.Build();

app.UseInternalEndpointGuard();
app.UseForwardedHeaders();

await app.Services.ApplyDatabaseMigrationsAsync();

app.UseCors("AllowFrontend");
app.UseStorageConflictResponses();
app.UseFormsJwtAuthentication();
app.UseAccountAccessGate();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();

app.MapAccountAccessHealthEndpoints();
app.MapFormAdminEndpoints();
app.MapWorkflowAdminEndpoints();
app.MapFormEndpoints();
app.MapInternalEndpoints();

app.Run();
