using Forms.Infrastructure.Storage;
using Forms.Application.Services;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Forms.API.Endpoints;

var builder = WebApplication.CreateBuilder(args);

Env.Load();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(Environment.GetEnvironmentVariable("CONNECTION_STRING"));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<IFormService, FormService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapFormEndpoints();

app.Run();