using Skylab.Forms.Infrastructure.Storage;
using Skylab.Forms.Application.Services;
using Skylab.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using Steeltoe.Discovery.Eureka;
using Steeltoe.Discovery.HttpClients;

var builder = WebApplication.CreateBuilder(args);

var allowedOrigin = Environment.GetEnvironmentVariable("ALLOWED_ORIGIN") ?? "http://localhost:3000";

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policyBuilder =>
    {
        policyBuilder.WithOrigins(allowedOrigin).AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddDbContext<FormsDbContext>(options =>
{
    options.UseNpgsql(Environment.GetEnvironmentVariable("CONNECTION_STRING"), 
        npgsqlOptionsAction: sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: new[] { "28P01" }); 
        });
});

builder.Services.AddEurekaDiscoveryClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IFormService, FormService>();
builder.Services.AddScoped<IFormResponseService, FormResponseService>();
builder.Services.AddScoped<IFormMetricService, FormMetricService>();
builder.Services.AddScoped<IComponentGroupService, ComponentGroupService>();

builder.Services.AddHttpClient<ICurrentUserService, RemoteCurrentUserService>(client => { client.BaseAddress = new Uri("http://super-skylab"); }).AddServiceDiscovery();
builder.Services.AddHttpClient<IExternalUserService, ExternalUserService>(client => { client.BaseAddress = new Uri("http://super-skylab"); }).AddServiceDiscovery();


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<FormsDbContext>();
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Startup] Database migration failed: {ex.Message}");
        Console.ResetColor();
    }
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowFrontend");

app.MapFormAdminEndpoints();
app.MapFormEndpoints();

app.Run();