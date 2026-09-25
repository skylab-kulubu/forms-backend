using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Services;
using Skylab.Forms.Application.Services.ShortLinks;
using Skylab.Forms.Application.Services.Workflows;

namespace Skylab.Forms.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IFormService, FormService>();
        services.AddScoped<IFormResponseService, FormResponseService>();
        services.AddScoped<IFormMetricService, FormMetricService>();
        services.AddScoped<IFormDraftService, FormDraftService>();
        services.AddScoped<IComponentGroupService, ComponentGroupService>();
        services.AddScoped<IFormWorkflowRuntime, FormWorkflowRuntime>();
        services.AddScoped<IFormWorkflowService, FormWorkflowService>();
        services.AddScoped<IFormMailNotifier, FormMailNotifier>();
        services.AddScoped<IFormShortLinkService, FormShortLinkService>();

        return services;
    }
}
