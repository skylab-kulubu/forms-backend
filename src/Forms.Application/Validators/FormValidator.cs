using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Validators;

public static class FormValidator
{
    public static ServiceResult<bool> ValidateUpsert(FormUpsertRequest contract, List<FormSchemaItem> schema, bool guestUploadsEnabled)
    {
        // Silmenin kendi komutu ve kuralları var: yalnız sahip, form yayındaki bir akışta değilken. Kaydetmek oraya ulaşamaz.
        if (contract.Status is not (FormStatus.Open or FormStatus.Closed))
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Form yalnız açık ya da kapalı olabilir.");

        if (contract.AllowAnonymousResponses && !contract.AllowMultipleResponses)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Anonim formlarda çoklu yanıt özelliği açık olmalıdır.");

        bool hasFileField = schema.Any(x => x.Type.Equals("file"));

        if (hasFileField && contract.AllowAnonymousResponses && !guestUploadsEnabled)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Anonim formlarda dosya yüklenemez.");

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true);
    }

    public static ServiceResult<bool> ValidateTiming(bool allowAnonymous, bool allowMultiple, FormTask? task, int? timeLimitMinutes)
    {
        if (task?.Content.Length > AttemptLimits.MaxTaskLength)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Görev metni en fazla 50.000 karakter olabilir.");

        if (timeLimitMinutes is not { } minutes) return new ServiceResult<bool>(ServiceStatus.Success, Data: true);

        if (minutes < 1 || minutes > AttemptLimits.MaxTimeLimitMinutes)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Kişisel süre 1 dakika ile 30 gün arasında olmalıdır.");

        if (allowAnonymous)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Anonim cevaba açık formda kişisel süre kullanılamaz.");

        if (allowMultiple)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Birden çok cevaba açık formda kişisel süre kullanılamaz.");

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true);
    }
}
