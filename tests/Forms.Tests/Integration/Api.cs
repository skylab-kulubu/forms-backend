using System.Net.Http.Json;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Tests.Integration;

/// <summary>Senaryoların okunur kalması için uçların ince sarmalayıcıları.</summary>
internal static class Api
{
    public static FormSchemaItem Question(string id, string type = "short_text") => new() { Id = id, Type = type };

    public static FormUpsertRequest NewForm(string title, params FormSchemaItem[] schema) =>
        new(null, title, null, [.. schema], AllowAnonymousResponses: false, AllowMultipleResponses: false, RequiresManualReview: false, FormStatus.Open, Collaborators: null);

    public static async Task<FormContract> CreateFormAsync(this HttpClient client, FormUpsertRequest form)
    {
        var response = await client.PostAsJsonAsync("/api/admin/forms/", form);
        response.EnsureSuccessStatusCode();

        return await response.DataAsync<FormContract>();
    }

    public static async Task<Guid> CreateWorkflowAsync(this HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/admin/workflows/", new WorkflowUpsertRequest(name, null, AllowMultipleRuns: false));
        response.EnsureSuccessStatusCode();

        return (await response.DataAsync<WorkflowContract>()).Id;
    }

    public static Task<HttpResponseMessage> SubmitAsync(this HttpClient client, Guid formId, params (string QuestionId, string Answer)[] answers) =>
        client.PostAsJsonAsync("/api/forms/responses", new ResponseSubmitRequest(
            formId,
            [.. answers.Select(answer => new FormResponseSchemaItem { Id = answer.QuestionId, Answer = answer.Answer })],
            TimeSpent: 30));

    public static async Task<T> DataAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ServiceResult<T>>();

        return body is { Data: { } data } ? data : throw new InvalidOperationException($"{(int)response.StatusCode} yanıtında veri yok: {body?.Message}");
    }
}
