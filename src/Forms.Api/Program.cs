using Skylab.Forms.Api.Auth;
using Skylab.Forms.Api.AccountAccess;
using Skylab.Forms.Api.Endpoints;
using Skylab.Forms.Application;
using Skylab.Forms.Infrastructure;

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
builder.Services.AddFormsJwtAuthentication(builder.Configuration);
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

await app.Services.ApplyDatabaseMigrationsAsync();

app.UseFormsJwtAuthentication("AllowFrontend");
app.UseCors("AllowFrontend");
app.UseAccountAccessGate();
app.UseSwagger();
app.UseSwaggerUI();

app.MapAccountAccessHealthEndpoints();
app.MapFormAdminEndpoints();
app.MapWorkflowAdminEndpoints();
app.MapFormEndpoints();

app.Run();
