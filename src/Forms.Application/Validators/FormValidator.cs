using Forms.Application.Contracts;
using Forms.Domain.Models;

namespace Forms.Application.Validators;

public static class FormValidator
{
    public static ServiceResult<bool> ValidateUpsert(bool allowAnonymous, bool allowMultiple, List<FormSchemaItem>? schema)
    {
        if (allowAnonymous && !allowMultiple)
        {
            return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Anonim formlarda çoklu yanıt özelliği açık olmalıdır.");
        }

        // File upload anonimlik kontrolü gelicek

        return new ServiceResult<bool>(FormAccessStatus.Available, Data: true);
    }
}