using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Domain.Workflows;

namespace Skylab.Forms.Application.Services.Workflows;

public class FormWorkflowRuntime : IFormWorkflowRuntime
{
    private readonly IFormWorkflowRepository _workflows;
    private readonly IFormWorkflowInstanceRepository _instances;
    private readonly IFormResponseRepository _responses;
    private readonly IFormsUnitOfWork _uow;

    public FormWorkflowRuntime(
        IFormWorkflowRepository workflows,
        IFormWorkflowInstanceRepository instances,
        IFormResponseRepository responses,
        IFormsUnitOfWork uow)
    {
        _workflows = workflows;
        _instances = instances;
        _responses = responses;
        _uow = uow;
    }

    public async Task<ServiceResult<WorkflowStepOutcome>> ResolveDisplayAsync(
        Guid formId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _instances.GetActiveByFormAsync(formId, userId, cancellationToken);

        if (instance is null) return await ResolveStartAsync(formId, userId, cancellationToken);

        var definition = await _workflows.GetDefinitionAsync(instance.WorkflowVersionId, cancellationToken);
        if (definition is null) return DefinitionUnavailable();

        var node = definition.Nodes.First(candidate => candidate.FormId == formId);
        var openStep = FindOpenStep(instance);

        if (openStep is null) return Faulted(instance);

        // Paylaşılan bağlantı akışın başlangıç formudur: oradan gelen kullanıcı
        // kaldığı adıma taşınır. Diğer adımların bağlantısı doğrudan açılamaz.
        if (openStep.NodeId != node.Id && !node.IsStart)
        {
            return new ServiceResult<WorkflowStepOutcome>(
                ServiceStatus.RequiresParentApproval,
                Message: "Bu formu görüntülemek için önceki adımı tamamlamanız gerekiyor.");
        }

        var openNode = definition.Nodes.First(candidate => candidate.Id == openStep.NodeId);

        // Cevabı olan ama kapanmamış adım, incelemeyi bekleyen adımdır.
        var isTwoStepFlow = definition.Nodes.Count == 2;

        return openStep.ResponseId is null
            ? Result(new WorkflowStepOutcome(instance.Id, WorkflowActionState.ShowForm, openStep.Sequence, openNode.FormId), null, isTwoStepFlow)
            : Result(new WorkflowStepOutcome(instance.Id, WorkflowActionState.AwaitingReview, openStep.Sequence, null),
                "Form cevabınız inceleniyor, lütfen bekleyiniz.", isTwoStepFlow);
    }

    public async Task<ServiceResult<WorkflowStepOutcome>> SubmitAsync(
        Form form,
        FormResponse response,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _instances.GetActiveByFormAsync(form.Id, userId, cancellationToken);

        WorkflowDefinition? definition;
        FormWorkflowNode node;
        FormWorkflowStep step;

        if (instance is null)
        {
            var location = await _workflows.FindPublishedNodeAsync(form.Id, cancellationToken);
            if (location is null) return Result(WorkflowStepOutcome.NotInWorkflow);

            if (!location.IsStart)
            {
                return new ServiceResult<WorkflowStepOutcome>(
                    ServiceStatus.RequiresParentApproval,
                    Message: "Bu formu doldurmak için önceki adımı tamamlamanız gerekiyor.");
            }

            if (!location.AllowMultipleRuns && await _instances.GetLastRunAsync(location.WorkflowId, userId, cancellationToken) is not null)
            {
                return new ServiceResult<WorkflowStepOutcome>(
                    ServiceStatus.NotAcceptable,
                    Message: "Bu akışı daha önce tamamladınız.");
            }

            definition = await _workflows.GetDefinitionAsync(location.WorkflowVersionId, cancellationToken);
            if (definition is null) return DefinitionUnavailable();

            node = definition.Nodes.First(candidate => candidate.Id == location.NodeId);

            // Başvuru ilk gönderimde yaratılır; formu açmak kayıt üretmez.
            instance = new FormWorkflowInstance
            {
                WorkflowId = location.WorkflowId,
                WorkflowVersionId = location.WorkflowVersionId,
                UserId = userId
            };

            step = new FormWorkflowStep { NodeId = node.Id, Sequence = 1 };
            instance.Steps.Add(step);
            _instances.Add(instance);
        }
        else
        {
            definition = await _workflows.GetDefinitionAsync(instance.WorkflowVersionId, cancellationToken);
            if (definition is null) return DefinitionUnavailable();

            node = definition.Nodes.First(candidate => candidate.FormId == form.Id);

            var openStep = FindOpenStep(instance);
            if (openStep is null) return Faulted(instance);

            if (openStep.NodeId != node.Id)
            {
                return new ServiceResult<WorkflowStepOutcome>(
                    ServiceStatus.RequiresParentApproval,
                    Message: "Şu anda beklenen adım bu form değil.");
            }

            if (openStep.ResponseId is not null)
            {
                return new ServiceResult<WorkflowStepOutcome>(
                    ServiceStatus.NotAcceptable,
                    Message: "Bu adımı zaten cevapladınız.");
            }

            step = openStep;
        }

        _responses.Add(response);
        step.ResponseId = response.Id;

        if (form.RequiresManualReview)
        {
            // Adım açık kalır: rota kararı inceleme sonucuna bırakılır.
            await _uow.SaveChangesAsync(cancellationToken);

            return Result(
                new WorkflowStepOutcome(instance.Id, WorkflowActionState.AwaitingReview, step.Sequence, null),
                "Yanıtınız incelemeye alındı.",
                definition.Nodes.Count == 2);
        }

        var (outcome, nextStep) = await AdvanceAsync(
            instance, definition, node, step, response.Data, WorkflowTransitionTrigger.ResponseSubmitted, cancellationToken);

        await CommitAsync(instance, nextStep, cancellationToken);

        return Result(outcome, MessageFor(outcome), definition.Nodes.Count == 2);
    }

    public Task<bool> HasPendingRouteAsync(Guid responseId, CancellationToken cancellationToken = default) =>
        _instances.HasOpenStepForResponseAsync(responseId, cancellationToken);

    public async Task<ServiceResult<WorkflowStepOutcome>> ReviewAsync(
        FormResponse response,
        FormResponseStatus newStatus,
        Guid reviewerId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var step = await _instances.GetStepByResponseAsync(response.Id, cancellationToken);
        if (step is null) return Result(WorkflowStepOutcome.NotInWorkflow);

        // Rota bir kez yazılır: onay/red arasında gidip gelmek geçmişi bozar.
        if (step.CompletedAt is not null)
        {
            return new ServiceResult<WorkflowStepOutcome>(
                ServiceStatus.NotAcceptable,
                Message: "Bu adımın yönlendirmesi daha önce yapıldı; yeniden değerlendirilemez.");
        }

        if (newStatus is not (FormResponseStatus.Approved or FormResponseStatus.Declined))
        {
            return new ServiceResult<WorkflowStepOutcome>(
                ServiceStatus.NotAcceptable,
                Message: "Akış içindeki bir cevap yalnızca onaylanabilir veya reddedilebilir.");
        }

        var instance = step.WorkflowInstance;

        var definition = await _workflows.GetDefinitionAsync(instance.WorkflowVersionId, cancellationToken);
        if (definition is null) return DefinitionUnavailable();

        var node = definition.Nodes.First(candidate => candidate.Id == step.NodeId);

        var trigger = newStatus == FormResponseStatus.Approved
            ? WorkflowTransitionTrigger.ResponseApproved
            : WorkflowTransitionTrigger.ResponseDeclined;

        response.ApplyReview(newStatus, reviewerId, note, DateTime.UtcNow);

        var (outcome, nextStep) = await AdvanceAsync(instance, definition, node, step, response.Data, trigger, cancellationToken);

        // Akış burada bittiyse kullanıcı gerekçeyi aynı yanıtta görmeli.
        if (outcome.State is WorkflowActionState.Completed or WorkflowActionState.Declined)
            outcome = outcome with { ReviewNote = response.ReviewNote, ReviewedAt = response.ReviewedAt };

        await CommitAsync(instance, nextStep, cancellationToken);

        return Result(outcome, outcome.State == WorkflowActionState.Declined
            ? "Başvuru reddedildi."
            : MessageFor(outcome), definition.Nodes.Count == 2);
    }

    private async Task<ServiceResult<WorkflowStepOutcome>> ResolveStartAsync(
        Guid formId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var location = await _workflows.FindPublishedNodeAsync(formId, cancellationToken);
        if (location is null) return Result(WorkflowStepOutcome.NotInWorkflow);

        if (!location.IsStart)
        {
            return new ServiceResult<WorkflowStepOutcome>(
                ServiceStatus.RequiresParentApproval,
                Message: "Bu formu görüntülemek için önceki adımı tamamlamanız gerekiyor.");
        }

        var lastRun = await _instances.GetLastRunAsync(location.WorkflowId, userId, cancellationToken);

        // Aktif başvuru bu formu içermiyorsa kullanıcı akışın başka bir dalında demektir.
        if (lastRun is { Status: WorkflowInstanceStatus.Active })
        {
            return new ServiceResult<WorkflowStepOutcome>(
                ServiceStatus.RequiresParentApproval,
                Message: "Devam eden bir başvurunuz var; önce onu tamamlayın.");
        }

        var isTwoStepFlow = location.NodeCount == 2;

        if (lastRun is not null && !location.AllowMultipleRuns) return ClosedRun(lastRun, isTwoStepFlow);

        return Result(new WorkflowStepOutcome(null, WorkflowActionState.ShowForm, 1, formId), null, isTwoStepFlow);
    }

    /// <summary>Sonuçlanmış bir başvuruyu, kullanıcıya gösterilecek inceleme notuyla bildirir.</summary>
    private static ServiceResult<WorkflowStepOutcome> ClosedRun(WorkflowRunSummary run, bool isTwoStepFlow)
    {
        var state = run.Status == WorkflowInstanceStatus.Faulted
            ? WorkflowActionState.Faulted
            : run.Outcome == WorkflowInstanceOutcome.Declined
                ? WorkflowActionState.Declined
                : WorkflowActionState.Completed;

        var outcome = new WorkflowStepOutcome(run.InstanceId, state, 0, null, run.ReviewNote, run.ReviewedAt);

        return Result(outcome, state == WorkflowActionState.Declined
            ? "Başvurunuz reddedilmiştir."
            : MessageFor(outcome), isTwoStepFlow);
    }

    /// <summary>
    /// Adımı kapatır ve rotayı yazar. Sonraki adım burada context'e eklenmez:
    /// kapanış UPDATE'i ile yeni adımın INSERT'ü aynı kayıt işleminde gönderilirse
    /// EF INSERT'ü önce yazar ve "tek açık adım" kısmi tekil indeksi ihlal edilir.
    /// </summary>
    private async Task<(WorkflowStepOutcome Outcome, FormWorkflowStep? NextStep)> AdvanceAsync(
        FormWorkflowInstance instance,
        WorkflowDefinition definition,
        FormWorkflowNode node,
        FormWorkflowStep step,
        List<FormResponseSchemaItem> answers,
        WorkflowTransitionTrigger trigger,
        CancellationToken cancellationToken)
    {
        var context = await BuildContextAsync(instance, node.NodeKey, answers, cancellationToken);
        var decision = WorkflowTransitionResolver.Resolve(definition.Transitions, node.Id, trigger, context);

        var now = DateTime.UtcNow;

        step.CompletedAt = now;
        step.SelectedTransitionId = decision.Transition?.Id;

        if (decision.Outcome == WorkflowRouteOutcome.Unresolved)
        {
            // Publish doğrulaması varsayılan rotayı zorunlu kılar; buraya düşmek
            // tanımın doğrulama dışı bir yoldan bozulduğu anlamına gelir.
            instance.Status = WorkflowInstanceStatus.Faulted;
            instance.CompletedAt = now;

            return (new WorkflowStepOutcome(instance.Id, WorkflowActionState.Faulted, step.Sequence, null), null);
        }

        if (decision.Transition?.TargetNodeId is not { } targetNodeId)
            return (Complete(instance, step, trigger, now), null);

        var targetNode = definition.Nodes.First(candidate => candidate.Id == targetNodeId);

        var nextStep = new FormWorkflowStep
        {
            WorkflowInstanceId = instance.Id,
            NodeId = targetNode.Id,
            PreviousStepId = step.Id,
            Sequence = step.Sequence + 1
        };

        var outcome = new WorkflowStepOutcome(instance.Id, WorkflowActionState.ShowForm, nextStep.Sequence, targetNode.FormId);

        return (outcome, nextStep);
    }

    /// <summary>Kapanan adımı, ardından yeni adımı tek transaction içinde yazar.</summary>
    private Task CommitAsync(FormWorkflowInstance instance, FormWorkflowStep? nextStep, CancellationToken cancellationToken) =>
        _uow.ExecuteInTransactionAsync(async token =>
        {
            await _uow.SaveChangesAsync(token);

            if (nextStep is not null)
            {
                // Adım açıkça eklenir: anahtarı istemci tarafında dolu olduğu için
                // yalnız koleksiyona eklemek EF'e onu mevcut satır gibi gösterir.
                _instances.Add(nextStep);
                instance.Steps.Add(nextStep);

                await _uow.SaveChangesAsync(token);
            }

            return true;
        }, cancellationToken);

    private static WorkflowStepOutcome Complete(
        FormWorkflowInstance instance,
        FormWorkflowStep step,
        WorkflowTransitionTrigger trigger,
        DateTime now)
    {
        var declined = trigger == WorkflowTransitionTrigger.ResponseDeclined;

        instance.Status = declined ? WorkflowInstanceStatus.Terminated : WorkflowInstanceStatus.Completed;
        instance.CompletedAt = now;
        instance.Outcome = trigger switch
        {
            WorkflowTransitionTrigger.ResponseApproved => WorkflowInstanceOutcome.Approved,
            WorkflowTransitionTrigger.ResponseDeclined => WorkflowInstanceOutcome.Declined,
            _ => WorkflowInstanceOutcome.None
        };

        return new WorkflowStepOutcome(
            instance.Id,
            declined ? WorkflowActionState.Declined : WorkflowActionState.Completed,
            step.Sequence,
            null);
    }

    private async Task<WorkflowEvaluationContext> BuildContextAsync(
        FormWorkflowInstance instance,
        string currentNodeKey,
        List<FormResponseSchemaItem> currentAnswers,
        CancellationToken cancellationToken)
    {
        var answers = new Dictionary<string, IReadOnlyList<FormResponseSchemaItem>>();

        // Yeni başlayan bir başvurunun geçmişi yoktur; sorguyu boşuna çalıştırma.
        if (instance.Steps.Any(step => step.CompletedAt is not null))
        {
            foreach (var past in await _instances.GetAnswersAsync(instance.Id, cancellationToken))
                answers[past.NodeKey] = past.Answers;
        }

        // Değerlendirilen cevap henüz kaydedilmedi; bağlama elle eklenir.
        answers[currentNodeKey] = currentAnswers;

        return new WorkflowEvaluationContext(currentNodeKey, answers);
    }

    /// <summary>Açık adımın tekliğini WorkflowSteps üzerindeki kısmi tekil indeks garanti eder.</summary>
    private static FormWorkflowStep? FindOpenStep(FormWorkflowInstance instance) =>
        instance.Steps.SingleOrDefault(step => step.CompletedAt is null);

    /// <summary>İki adımlı akışlarda eski istemcinin 1..5 aşamasını üretebilmesi için işaretler.</summary>
    private static ServiceResult<WorkflowStepOutcome> Result(WorkflowStepOutcome outcome, string? message, bool isTwoStepFlow) =>
        Result(outcome with { IsLegacyTwoStepFlow = isTwoStepFlow }, message);

    private static ServiceResult<WorkflowStepOutcome> Result(WorkflowStepOutcome outcome, string? message = null) =>
        new(outcome.State switch
        {
            WorkflowActionState.AwaitingReview => ServiceStatus.PendingApproval,
            WorkflowActionState.Completed => ServiceStatus.Completed,
            WorkflowActionState.Declined => ServiceStatus.Declined,
            WorkflowActionState.Faulted => ServiceStatus.NotAvailable,
            _ => ServiceStatus.Success
        }, outcome, message);

    private static ServiceResult<WorkflowStepOutcome> Faulted(FormWorkflowInstance instance) =>
        Result(
            new WorkflowStepOutcome(instance.Id, WorkflowActionState.Faulted, 0, null),
            "Başvurunuz beklenmeyen bir durumda; lütfen yetkiliyle iletişime geçin.");

    private static ServiceResult<WorkflowStepOutcome> DefinitionUnavailable() =>
        new(ServiceStatus.NotAvailable, Message: "Akış tanımı okunamadı.");

    private static string? MessageFor(WorkflowStepOutcome outcome) => outcome.State switch
    {
        WorkflowActionState.ShowForm => "Yanıt kaydedildi, bir sonraki adıma geçebilirsiniz.",
        WorkflowActionState.Completed => "Tüm adımları tamamladınız.",
        WorkflowActionState.Declined => "Başvurunuz reddedilmiştir.",
        WorkflowActionState.Faulted => "Akış tanımı bu cevap için bir rota üretemedi.",
        _ => null
    };
}
