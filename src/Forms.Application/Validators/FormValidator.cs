using Forms.Application.Contracts;
using Forms.Domain.Models;

namespace Forms.Application.Validators;

public static class FormValidator
{
    public static ServiceResult<bool> ValidateUpsert(bool allowAnonymous, bool allowMultiple, List<FormSchemaItem> schema)
    {
        if (allowAnonymous && !allowMultiple)
        {
            return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Anonim formlarda çoklu yanıt özelliği açık olmalıdır.");
        }

        bool hasFileField = schema.Any(x => x.Type.Equals("file"));

        if (hasFileField && allowAnonymous)
        {
            return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Anonim formlarda dosya yüklenemez.");
        }

        return new ServiceResult<bool>(FormAccessStatus.Available, Data: true);
    }
}