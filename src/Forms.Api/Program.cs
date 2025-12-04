using Forms.Infrastructure.Storage;
using Forms.Application.Services;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

Env.Load();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(Environment.GetEnvironmentVariable("CONNECTION_STRING"));
});

builder.Services.AddScoped<IFormService, FormService>();

var app = builder.Build();

app.Run();