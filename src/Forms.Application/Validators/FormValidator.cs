using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts;
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
}
