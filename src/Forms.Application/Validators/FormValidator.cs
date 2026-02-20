using Forms.Application.Contracts;
using Forms.Domain.Models;

namespace Forms.Application.Validators;

public static class FormValidator
{
    public static ServiceResult<bool> ValidateUpsert(bool allowAnonymous, bool allowMultiple, List<FormSchemaItem> schema, Guid? linkedFormId)
    {
        if (allowAnonymous && !allowMultiple)
            return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Anonim formlarda çoklu yanıt özelliği açık olmalıdır.");

        bool hasFileField = schema.Any(x => x.Type.Equals("file"));

        if (hasFileField && allowAnonymous)
            return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Anonim formlarda dosya yüklenemez.");

        if (allowAnonymous && linkedFormId.HasValue)
            return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Anonim yanıtlara izin veren formlar başka bir forma bağlanamaz."); ;

        return new ServiceResult<bool>(FormAccessStatus.Available, Data: true);
    }
}