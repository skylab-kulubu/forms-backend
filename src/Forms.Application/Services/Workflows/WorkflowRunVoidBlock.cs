using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services.Workflows;

/// <summary>Bir başvurunun neden geçersiz sayılamayacağı; istemci metni koda göre seçer.</summary>
public static class WorkflowRunVoidBlock
{
    public const string Voided = "voided";
    public const string WorkflowArchived = "workflowArchived";
    public const string TimedStep = "timedStep";
    public const string Repeatable = "repeatable";

    public static string? Of(WorkflowRunFacts run) => run switch
    {
        { Status: WorkflowInstanceStatus.Voided } => Voided,
        { WorkflowStatus: WorkflowStatus.Archived } => WorkflowArchived,
        // Süreli formda deneme (form, kişi) başına tektir; yeni başvuru eski denemeye takılırdı.
        { HasTimedStep: true } => TimedStep,
        // Sonuçlanmış başvuruyu geçersiz saymaya gerek yok: bu akışta kişi zaten yeniden başvurabiliyor.
        { AllowMultipleRuns: true, Status: not WorkflowInstanceStatus.Active } => Repeatable,
        _ => null
    };

    public static string MessageOf(string block) => block switch
    {
        Voided => "Başvuru zaten geçersiz sayıldı.",
        WorkflowArchived => "Arşivlenmiş akışta başvuru yeniden açılamaz.",
        TimedStep => "Süreli adımı olan akışta başvuru baştan başlatılamaz.",
        _ => "Bu akışta aday zaten yeniden başvurabiliyor."
    };
}
