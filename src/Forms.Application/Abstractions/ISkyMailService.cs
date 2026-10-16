using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Forms.Application.Abstractions;

public interface ISkyMailService
{
    Task<bool> SendSingleAsync(SingleMailRequest request, CancellationToken ct = default);
    Task<ServiceResult<FormMailSourceContract>> PrepareFormMailSourceAsync(FormMailRecipientsContract request, CancellationToken ct = default);
}
