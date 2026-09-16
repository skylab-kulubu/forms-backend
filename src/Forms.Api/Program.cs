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
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

await app.Services.ApplyDatabaseMigrationsAsync();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowFrontend");

app.MapFormAdminEndpoints();
app.MapWorkflowAdminEndpoints();
app.MapFormEndpoints();

app.Run();
