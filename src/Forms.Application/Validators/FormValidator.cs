using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Validators;

public static class FormValidator
{
    public static ServiceResult<bool> ValidateUpsert(bool allowAnonymous, bool allowMultiple, List<FormSchemaItem> schema)
    {
        if (allowAnonymous && !allowMultiple)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Anonim formlarda çoklu yanıt özelliği açık olmalıdır.");

        bool hasFileField = schema.Any(x => x.Type.Equals("file"));

        if (hasFileField && allowAnonymous)
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
