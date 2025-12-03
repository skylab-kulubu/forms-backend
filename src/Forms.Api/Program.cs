using Forms.Infrastructure.Storage;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

Env.Load();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(Environment.GetEnvironmentVariable("CONNECTION_STRING"));
});

var app = builder.Build();

app.Run();