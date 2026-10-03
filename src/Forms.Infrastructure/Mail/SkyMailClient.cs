using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Mail;

public class SkyMailClient : ISkyMailService
{
    private static readonly JsonSerializerOptions JsonOptions = new(){ PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly HttpClient _httpClient;
    private readonly ILogger<SkyMailClient> _logger;

    public SkyMailClient(HttpClient httpClient, ILogger<SkyMailClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> SendSingleAsync(SingleMailRequest request, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("mail_tasks/single", request, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("skymail /mail_tasks/single başarısız: {Status} {Body}", response.StatusCode, body);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "skymail /mail_tasks/single isteğinde hata (template {TemplateId})", request.TemplateId);
            return false;
        }
    }
}
