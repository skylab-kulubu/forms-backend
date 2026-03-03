using Skylab.Shared.Application.Contracts;
using Skylab.Shared.Domain.Enums;
using Skylab.Forms.Application.Contracts;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Validators;

public static class FormValidator
{
    public static ServiceResult<bool> ValidateUpsert(bool allowAnonymous, bool allowMultiple, List<FormSchemaItem> schema, Guid? linkedFormId)
    {
        if (allowAnonymous && !allowMultiple)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Anonim formlarda çoklu yanıt özelliği açık olmalıdır.");

        bool hasFileField = schema.Any(x => x.Type.Equals("file"));

        if (hasFileField && allowAnonymous)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Anonim formlarda dosya yüklenemez.");

        if (allowAnonymous && linkedFormId.HasValue)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Anonim yanıtlara izin veren formlar başka bir forma bağlanamaz."); ;

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true);
    }
}