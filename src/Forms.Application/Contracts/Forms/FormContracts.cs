using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Application.Contracts.Collaborators;
using Skylab.Forms.Application.Contracts.Identity;

namespace Skylab.Forms.Application.Contracts.Forms;

public record FormContract(
    Guid Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema,
    FormStatus Status,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    bool RequiresManualReview,
    FormWorkflowRefContract? Workflow,
    CollaboratorRole userRole,
    List<FormCollaboratorContract> Collaborators,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    EventRefContract? Event = null
);

    public record FormDisplayContract(
    Guid Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema,
    Guid? EventId = null
);

public record FormSummaryContract(
    Guid Id,
    string Title,
    FormStatus Status,
    CollaboratorRole UserRole,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    bool RequiresManualReview,
    FormWorkflowRefContract? Workflow,
    DateTime? UpdatedAt,
    int ResponseCount,
    Guid? EventId,
    EventRefContract? Event = null
);

public record FormAllSummaryContract(
    Guid Id,
    string Title,
    FormStatus Status,
    UserContract CreatedBy,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    bool RequiresManualReview,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int ResponseCount,
    EventRefContract? Event = null
);

/// <summary>
/// Formun hangi akışta yer aldığı. Akışta değilse null. Yayınlanmış bir akışta yer
/// alıyorsa LockedQuestions, yönlendirmenin dayandığı soruları ve o soruların
/// değiştirilemeyecek seçenek adlarını verir.
/// </summary>
public record FormWorkflowRefContract(
    Guid Id,
    string Name,
    bool IsStart,
    bool IsPublished,
    List<FormLockedQuestionContract> LockedQuestions
);

/// <param name="Values">
/// Koşulun karşılaştırdığı seçenek adları. Cevaplar seçeneğin görünen adıyla
/// saklandığı için bu adları değiştirmek yönlendirmeyi sessizce bozar.
/// </param>
public record FormLockedQuestionContract(
    string Id,
    List<string> Values
);

public record FormMetaContract(
    string Title,
    string? Description
);

public record EventRefContract(
    Guid Id,
    string? Name
);
