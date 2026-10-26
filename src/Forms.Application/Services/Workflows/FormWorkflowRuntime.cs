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
    private readonly TimeProvider _time;

    public FormWorkflowRuntime(
        IFormWorkflowRepository workflows,
        IFormWorkflowInstanceRepository instances,
        IFormResponseRepository responses,
        IFormsUnitOfWork uow,
        TimeProvider time)
    {
        _workflows = workflows;
        _instances = instances;
        _responses = responses;
        _uow = uow;
        _time = time;
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

        var startFormId = definition.Nodes.First(candidate => candidate.IsStart).FormId;
        var isTwoStepFlow = definition.Nodes.Count == 2;
        var journey = await BuildJourneyAsync(instance.Workflow.Name, definition, instance.Id, null, cancellationToken);

        // Paylaşılan bağlantı akışın başlangıç formudur: oradan gelen kullanıcı
        // kaldığı adıma taşınır. Diğer adımların bağlantısı doğrudan açılamaz.
        if (openStep.NodeId != node.Id && !node.IsStart)
        {
            return Result(
                new WorkflowStepOutcome(instance.Id, WorkflowActionState.RequiresPreviousStep, openStep.Sequence, null, startFormId, Journey: journey),
                "Bu formu görüntülemek için önceki adımı tamamlamanız gerekiyor.",
                isTwoStepFlow);
        }

        // Cevabı olan ama kapanmamış adım, incelemeyi bekleyen adımdır.
        if (openStep.ResponseId is not null)
        {
            return Result(
                new WorkflowStepOutcome(instance.Id, WorkflowActionState.AwaitingReview, openStep.Sequence, null, startFormId, Journey: journey),
                "Form cevabınız inceleniyor, lütfen bekleyiniz.",
                isTwoStepFlow);
        }

        // İnceleme kapalı akışta da sürdüğü için bekleyen adım durmuş sayılmaz;
        // durdurma yalnız doldurulacak formu kapsar.
        if (instance.Workflow.Intake == WorkflowIntake.Closed)
            return IntakeClosed(WorkflowIntake.Closed, instance.Id, openStep.Sequence, startFormId, isTwoStepFlow, journey);

        var openNode = definition.Nodes.First(candidate => candidate.Id == openStep.NodeId);
        var revision = openStep.Round > 1 ? await RevisionOfAsync(openStep.Id, cancellationToken) : null;

        return Result(
            new WorkflowStepOutcome(instance.Id, WorkflowActionState.ShowForm, openStep.Sequence, openNode.FormId, startFormId, Journey: journey, StepId: openStep.Id, Revision: revision),
            null,
            isTwoStepFlow);
    }

    private async Task<WorkflowRevisionContract?> RevisionOfAsync(Guid stepId, CancellationToken cancellationToken)
    {
        var facts = await _instances.GetRevisionAsync(stepId, cancellationToken);

        return facts is null ? null : new WorkflowRevisionContract(facts.Round, facts.Note, facts.ReturnedAt, facts.QuestionIds, facts.Answers);
    }

    public Task<ServiceResult<WorkflowStepOutcome>> SubmitAsync(
        Form form,
        FormResponse response,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        SubmitCoreAsync(form, response, userId, addResponse: true, cancellationToken);

    public Task<ServiceResult<WorkflowStepOutcome>> AcceptAsync(
        Form form,
        FormResponse response,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        SubmitCoreAsync(form, response, userId, addResponse: false, cancellationToken);

    public async Task<ServiceResult<WorkflowStepOutcome>> BeginRunAsync(
        Form form,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var active = await _instances.GetActiveByFormAsync(form.Id, userId, cancellationToken);

        if (active is not null)
        {
            var activeDefinition = await _workflows.GetDefinitionAsync(active.WorkflowVersionId, cancellationToken);
            if (activeDefinition is null) return DefinitionUnavailable();

            var activeNode = activeDefinition.Nodes.First(candidate => candidate.FormId == form.Id);
            var openStep = FindOpenStep(active);
            var activeStartFormId = activeDefinition.Nodes.First(candidate => candidate.IsStart).FormId;

            if (openStep is not null && openStep.NodeId == activeNode.Id && openStep.ResponseId is null)
            {
                return Result(
                    new WorkflowStepOutcome(active.Id, WorkflowActionState.ShowForm, openStep.Sequence, form.Id, activeStartFormId, StepId: openStep.Id),
                    null,
                    activeDefinition.Nodes.Count == 2);
            }

            return Result(
                new WorkflowStepOutcome(active.Id, WorkflowActionState.RequiresPreviousStep, openStep?.Sequence ?? 0, null, activeStartFormId),
                "Şu anda beklenen adım bu form değil.",
                activeDefinition.Nodes.Count == 2);
        }

        var location = await _workflows.FindPublishedNodeAsync(form.Id, cancellationToken);
        if (location is null) return Result(WorkflowStepOutcome.NotInWorkflow);

        if (!location.IsStart)
        {
            return Result(
                new WorkflowStepOutcome(null, WorkflowActionState.RequiresPreviousStep, 0, null, location.StartFormId),
                "Bu formu doldurmak için önceki adımı tamamlamanız gerekiyor.",
                location.NodeCount == 2);
        }

        var lastRun = await _instances.GetLastRunAsync(location.WorkflowId, userId, cancellationToken);

        if (await RefuseWhileRunActiveAsync(location, lastRun, cancellationToken) is { } running) return running;

        if (HasUsedOnlyRun(location, lastRun))
        {
            return new ServiceResult<WorkflowStepOutcome>(
                ServiceStatus.NotAcceptable,
                Message: "Bu akışı daha önce tamamladınız.");
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var intake = IntakeFor(location, lastRun, now);
        if (intake != WorkflowIntake.Open)
            return IntakeClosed(intake, null, 0, location.StartFormId, location.NodeCount == 2);

        var definition = await _workflows.GetDefinitionAsync(location.WorkflowVersionId, cancellationToken);
        if (definition is null) return DefinitionUnavailable();

        var node = definition.Nodes.First(candidate => candidate.Id == location.NodeId);

        var instance = new FormWorkflowInstance
        {
            WorkflowId = location.WorkflowId,
            WorkflowVersionId = location.WorkflowVersionId,
            UserId = userId,
            StartedAt = now
        };

        var step = new FormWorkflowStep { NodeId = node.Id, Sequence = 1 };
        instance.Steps.Add(step);
        _instances.Add(instance);

        await _uow.SaveChangesAsync(cancellationToken);

        return Result(
            new WorkflowStepOutcome(instance.Id, WorkflowActionState.ShowForm, 1, form.Id, location.StartFormId, StepId: step.Id),
            null,
            location.NodeCount == 2);
    }

    public async Task<ServiceResult<WorkflowStepOutcome>> TimeOutAsync(
        Guid stepId,
        CancellationToken cancellationToken = default)
    {
        var step = await _instances.GetStepForEditAsync(stepId, cancellationToken);
        if (step is null) return Result(WorkflowStepOutcome.NotInWorkflow);

        if (step.CompletedAt is not null || step.ResponseId is not null)
        {
            return new ServiceResult<WorkflowStepOutcome>(
                ServiceStatus.NotAcceptable,
                Message: "Bu adımın yönlendirmesi daha önce yapıldı.");
        }

        var instance = step.WorkflowInstance;

        if (instance.Status != WorkflowInstanceStatus.Active)
        {
            return new ServiceResult<WorkflowStepOutcome>(
                ServiceStatus.NotAcceptable,
                Message: "Başvuru artık devam etmiyor.");
        }

        var definition = await _workflows.GetDefinitionAsync(instance.WorkflowVersionId, cancellationToken);
        if (definition is null) return DefinitionUnavailable();

        var node = definition.Nodes.First(candidate => candidate.Id == step.NodeId);

        var (outcome, nextStep) = await AdvanceAsync(
            instance, definition, node, step, [], WorkflowTransitionTrigger.TimedOut, _time.GetUtcNow().UtcDateTime, cancellationToken);

        await CommitAsync(instance, nextStep, cancellationToken);

        return Result(outcome, MessageFor(outcome), definition.Nodes.Count == 2);
    }

    public async Task<WorkflowAttemptPreview?> PreviewAttemptAsync(
        Guid stepId,
        List<FormResponseSchemaItem> answers,
        CancellationToken cancellationToken = default)
    {
        var step = await _instances.GetStepForEditAsync(stepId, cancellationToken);
        if (step is null || step.CompletedAt is not null) return null;

        var definition = await _workflows.GetDefinitionAsync(step.WorkflowInstance.WorkflowVersionId, cancellationToken);
        if (definition is null) return null;

        var node = definition.Nodes.First(candidate => candidate.Id == step.NodeId);
        var context = await BuildContextAsync(step.WorkflowInstance, node.NodeKey, answers, cancellationToken);
        var onTimeout = PreviewTrigger(definition, node, WorkflowTransitionTrigger.TimedOut, context);

        return node.RequiresManualReview
            ? new WorkflowAttemptPreview(true, null, onTimeout)
            : new WorkflowAttemptPreview(false, PreviewTrigger(definition, node, WorkflowTransitionTrigger.ResponseSubmitted, context), onTimeout);
    }

    public async Task<Guid?> GetNextFormByStepAsync(
        Guid stepId,
        CancellationToken cancellationToken = default)
    {
        var step = await _instances.GetStepForEditAsync(stepId, cancellationToken);
        if (step is null || step.WorkflowInstance.Status != WorkflowInstanceStatus.Active) return null;

        var open = FindOpenStep(step.WorkflowInstance);
        if (open is null || open.Id == stepId) return null;

        var definition = await _workflows.GetDefinitionAsync(step.WorkflowInstance.WorkflowVersionId, cancellationToken);

        return definition?.Nodes.FirstOrDefault(node => node.Id == open.NodeId)?.FormId;
    }

    public async Task<WorkflowJourneyContract?> GetJourneyByStepAsync(
        Guid stepId,
        CancellationToken cancellationToken = default)
    {
        var step = await _instances.GetStepForEditAsync(stepId, cancellationToken);
        if (step is null) return null;

        var instance = step.WorkflowInstance;
        var definition = await _workflows.GetDefinitionAsync(instance.WorkflowVersionId, cancellationToken);

        return definition is null
            ? null
            : await BuildJourneyAsync(instance.Workflow.Name, definition, instance.Id, null, cancellationToken);
    }

    private async Task<ServiceResult<WorkflowStepOutcome>> SubmitCoreAsync(
        Form form,
        FormResponse response,
        Guid userId,
        bool addResponse,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var instance = await _instances.GetActiveByFormAsync(form.Id, userId, cancellationToken);

        WorkflowDefinition? definition;
        FormWorkflowNode node;
        FormWorkflowStep step;
        string workflowName;

        if (instance is null)
        {
            var location = await _workflows.FindPublishedNodeAsync(form.Id, cancellationToken);
            if (location is null) return Result(WorkflowStepOutcome.NotInWorkflow);

            if (!location.IsStart)
            {
                return Result(
                    new WorkflowStepOutcome(null, WorkflowActionState.RequiresPreviousStep, 0, null, location.StartFormId),
                    "Bu formu doldurmak için önceki adımı tamamlamanız gerekiyor.",
                    location.NodeCount == 2);
            }

            var lastRun = await _instances.GetLastRunAsync(location.WorkflowId, userId, cancellationToken);

            if (await RefuseWhileRunActiveAsync(location, lastRun, cancellationToken) is { } running) return running;

            if (HasUsedOnlyRun(location, lastRun))
            {
                return new ServiceResult<WorkflowStepOutcome>(
                    ServiceStatus.NotAcceptable,
                    Message: "Bu akışı daha önce tamamladınız.");
            }

            var intake = IntakeFor(location, lastRun, now);
            if (intake != WorkflowIntake.Open)
                return IntakeClosed(intake, null, 0, location.StartFormId, location.NodeCount == 2);

            definition = await _workflows.GetDefinitionAsync(location.WorkflowVersionId, cancellationToken);
            if (definition is null) return DefinitionUnavailable();

            node = definition.Nodes.First(candidate => candidate.Id == location.NodeId);
            workflowName = location.WorkflowName;

            // Başvuru ilk gönderimde yaratılır; formu açmak kayıt üretmez.
            instance = new FormWorkflowInstance
            {
                WorkflowId = location.WorkflowId,
                WorkflowVersionId = location.WorkflowVersionId,
                UserId = userId,
                StartedAt = now
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
            workflowName = instance.Workflow.Name;

            var openStep = FindOpenStep(instance);
            if (openStep is null) return Faulted(instance);

            if (openStep.NodeId != node.Id)
            {
                return Result(
                    new WorkflowStepOutcome(
                        instance.Id,
                        WorkflowActionState.RequiresPreviousStep,
                        openStep.Sequence,
                        null,
                        definition.Nodes.First(candidate => candidate.IsStart).FormId),
                    "Şu anda beklenen adım bu form değil.",
                    definition.Nodes.Count == 2);
            }

            if (openStep.ResponseId is not null)
            {
                return new ServiceResult<WorkflowStepOutcome>(
                    ServiceStatus.NotAcceptable,
                    Message: "Bu adımı zaten cevapladınız.");
            }

            if (addResponse && instance.Workflow.Intake == WorkflowIntake.Closed)
            {
                return IntakeClosed(
                    WorkflowIntake.Closed,
                    instance.Id,
                    openStep.Sequence,
                    definition.Nodes.First(candidate => candidate.IsStart).FormId,
                    definition.Nodes.Count == 2);
            }

            step = openStep;
        }

        // Cevap formun kendi ayarıyla kurulmuş gelir; akış içinde adımın ayarı geçerlidir.
        response.Status = node.RequiresManualReview ? FormResponseStatus.Pending : FormResponseStatus.NonRestrict;

        if (addResponse) _responses.Add(response);
        step.ResponseId = response.Id;
        instance.Touch(now);

        if (node.RequiresManualReview)
        {
            // Adım açık kalır: rota kararı inceleme sonucuna bırakılır.
            await _uow.SaveChangesAsync(cancellationToken);

            return Result(
                new WorkflowStepOutcome(
                    instance.Id,
                    WorkflowActionState.AwaitingReview,
                    step.Sequence,
                    null,
                    definition.Nodes.First(candidate => candidate.IsStart).FormId,
                    Journey: await BuildJourneyAsync(workflowName, definition, instance.Id, null, cancellationToken)),
                "Yanıtınız incelemeye alındı.",
                definition.Nodes.Count == 2);
        }

        var (outcome, nextStep) = await AdvanceAsync(
            instance, definition, node, step, response.Data, WorkflowTransitionTrigger.ResponseSubmitted, now, cancellationToken);

        await CommitAsync(instance, nextStep, cancellationToken);

        if (outcome.State is not WorkflowActionState.ShowForm)
            outcome = outcome with { Journey = await BuildJourneyAsync(workflowName, definition, instance.Id, null, cancellationToken) };

        return Result(outcome, MessageFor(outcome), definition.Nodes.Count == 2);
    }

    public Task<bool> HasPendingRouteAsync(Guid responseId, CancellationToken cancellationToken = default) =>
        _instances.HasOpenStepForResponseAsync(responseId, cancellationToken);

    public async Task<WorkflowReviewPreview?> PreviewReviewAsync(
        FormResponse response,
        CancellationToken cancellationToken = default)
    {
        var step = await _instances.GetStepByResponseAsync(response.Id, cancellationToken);

        // Rota bir kez seçilir; seçilmişse gösterilecek bir olasılık kalmaz.
        if (step is null || step.CompletedAt is not null) return null;

        var definition = await _workflows.GetDefinitionAsync(step.WorkflowInstance.WorkflowVersionId, cancellationToken);
        if (definition is null) return null;

        var node = definition.Nodes.First(candidate => candidate.Id == step.NodeId);
        var context = await BuildContextAsync(step.WorkflowInstance, node.NodeKey, response.Data, cancellationToken);

        return new WorkflowReviewPreview(
            PreviewTrigger(definition, node, WorkflowTransitionTrigger.ResponseApproved, context),
            PreviewTrigger(definition, node, WorkflowTransitionTrigger.ResponseDeclined, context));
    }

    private static WorkflowRouteTarget PreviewTrigger(
        WorkflowDefinition definition,
        FormWorkflowNode node,
        WorkflowTransitionTrigger trigger,
        WorkflowEvaluationContext context)
    {
        var decision = WorkflowTransitionResolver.Resolve(definition.Transitions, node.Id, trigger, context);

        if (decision.Transition?.TargetNodeId is not { } targetNodeId)
            return new WorkflowRouteTarget(EndsFlow: true, FormId: null);

        var target = definition.Nodes.First(candidate => candidate.Id == targetNodeId);

        return new WorkflowRouteTarget(EndsFlow: false, FormId: target.FormId);
    }

    public async Task<ServiceResult<WorkflowStepOutcome>> ReviewAsync(
        FormResponse response,
        FormResponseStatus newStatus,
        Guid reviewerId,
        string? note,
        IReadOnlyCollection<string>? questionIds = null,
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

        if (newStatus is not (FormResponseStatus.Approved or FormResponseStatus.Declined or FormResponseStatus.Returned))
        {
            return new ServiceResult<WorkflowStepOutcome>(
                ServiceStatus.NotAcceptable,
                Message: "Akış içindeki bir cevap yalnızca onaylanabilir, reddedilebilir ya da düzeltmeye gönderilebilir.");
        }

        var instance = step.WorkflowInstance;

        var definition = await _workflows.GetDefinitionAsync(instance.WorkflowVersionId, cancellationToken);
        if (definition is null) return DefinitionUnavailable();

        var node = definition.Nodes.First(candidate => candidate.Id == step.NodeId);
        var now = _time.GetUtcNow().UtcDateTime;

        if (newStatus == FormResponseStatus.Returned)
        {
            // Arşivlenen akışın formları kapalıdır; aday düzeltmeyi gönderemez.
            if (instance.Workflow?.Status == WorkflowStatus.Archived)
                return new ServiceResult<WorkflowStepOutcome>(ServiceStatus.NotAcceptable, Message: "Arşivlenmiş akışta düzeltme istenemez.");

            return await ReturnAsync(response, step, definition, node, reviewerId, note, questionIds, now, cancellationToken);
        }

        var trigger = newStatus == FormResponseStatus.Approved
            ? WorkflowTransitionTrigger.ResponseApproved
            : WorkflowTransitionTrigger.ResponseDeclined;

        response.ApplyReview(newStatus, reviewerId, note, now);

        var (outcome, nextStep) = await AdvanceAsync(instance, definition, node, step, response.Data, trigger, now, cancellationToken);

        // Akış burada bittiyse kullanıcı gerekçeyi aynı yanıtta görmeli.
        if (outcome.State is WorkflowActionState.Completed or WorkflowActionState.Declined)
            outcome = outcome with { ReviewNote = response.ReviewNote, ReviewedAt = response.ReviewedAt };

        await CommitAsync(instance, nextStep, cancellationToken);

        return Result(outcome, outcome.State == WorkflowActionState.Declined
            ? "Başvuru reddedildi."
            : MessageFor(outcome), definition.Nodes.Count == 2);
    }

    /// <summary>
    /// Adımı rota seçmeden kapatır ve aynı node'da, aynı sıra numarasıyla bir sonraki gönderimi
    /// açar. Düzeltme istenen cevap koşullarda okunmaz; yerini adayın yeni gönderimi alır.
    /// </summary>
    private async Task<ServiceResult<WorkflowStepOutcome>> ReturnAsync(
        FormResponse response,
        FormWorkflowStep step,
        WorkflowDefinition definition,
        FormWorkflowNode node,
        Guid reviewerId,
        string? note,
        IReadOnlyCollection<string>? questionIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Süreli formda deneme (form, kişi) başına tektir; yeni gönderim eski denemeye takılırdı.
        if (response.Form.HasTimeLimit)
            return new ServiceResult<WorkflowStepOutcome>(ServiceStatus.NotAcceptable, Message: "Süreli adımda düzeltme istenemez.");

        if (response.Form.HasClosedAt(now))
            return new ServiceResult<WorkflowStepOutcome>(ServiceStatus.NotAcceptable, Message: "Formun kapanış saati geçtiği için aday düzeltme gönderemez.");

        var known = response.Data.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var flagged = (questionIds ?? []).Where(known.Contains).Distinct().ToList();

        response.ApplyReview(FormResponseStatus.Returned, reviewerId, note, now);
        response.ReturnedQuestionIds = flagged.Count > 0 ? flagged : null;

        step.CompletedAt = now;
        step.SelectedTransitionId = null;
        step.WorkflowInstance.Touch(now);

        var nextRound = new FormWorkflowStep
        {
            WorkflowInstanceId = step.WorkflowInstanceId,
            NodeId = step.NodeId,
            PreviousStepId = step.Id,
            Sequence = step.Sequence,
            Round = step.Round + 1
        };

        await CommitAsync(step.WorkflowInstance, nextRound, cancellationToken);

        var startFormId = definition.Nodes.First(candidate => candidate.IsStart).FormId;

        return Result(
            new WorkflowStepOutcome(step.WorkflowInstanceId, WorkflowActionState.ShowForm, step.Sequence, node.FormId, startFormId, note, now, StepId: nextRound.Id),
            "Düzeltme istendi; aday formu önceki cevaplarıyla yeniden açabilir.",
            definition.Nodes.Count == 2);
    }

    public async Task<ServiceResult<WorkflowRunVoided>> VoidAsync(
        Guid instanceId,
        Guid actorId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var facts = await _instances.GetRunFactsAsync(instanceId, cancellationToken);
        if (facts is null) return new ServiceResult<WorkflowRunVoided>(ServiceStatus.NotFound, Message: "Başvuru bulunamadı.");

        if (WorkflowRunVoidBlock.Of(facts) is { } block)
            return new ServiceResult<WorkflowRunVoided>(ServiceStatus.NotAcceptable, Message: WorkflowRunVoidBlock.MessageOf(block));

        var instance = await _instances.GetForVoidAsync(instanceId, cancellationToken);
        if (instance is null) return new ServiceResult<WorkflowRunVoided>(ServiceStatus.NotFound, Message: "Başvuru bulunamadı.");

        var now = _time.GetUtcNow().UtcDateTime;
        var wasActive = instance.Void(actorId, note, now);

        // Cevaplar başvurunun geçmişinde kalır ama listeden ve sayımlardan çıkar: yeni başvuru kendi cevaplarıyla sayılır.
        var responseIds = instance.Steps.Where(step => step.ResponseId is not null).Select(step => step.ResponseId!.Value).ToList();

        foreach (var response in await _responses.GetForEditByIdsAsync(responseIds, cancellationToken))
        {
            if (!response.IsArchived) response.Archive(actorId, now);
        }

        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch (StorageConflictException)
        {
            return new ServiceResult<WorkflowRunVoided>(ServiceStatus.Conflict, Message: "Başvuru aynı anda değişti; sayfayı yenileyip tekrar deneyin.");
        }

        return new ServiceResult<WorkflowRunVoided>(
            ServiceStatus.Success,
            new WorkflowRunVoided(instance.UserId, facts.WorkflowName, facts.StartFormId, wasActive, now, note));
    }

    private async Task<ServiceResult<WorkflowStepOutcome>> ResolveStartAsync(
        Guid formId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var location = await _workflows.FindPublishedNodeAsync(formId, cancellationToken);
        if (location is null) return Result(WorkflowStepOutcome.NotInWorkflow);

        var isTwoStepFlow = location.NodeCount == 2;

        if (!location.IsStart)
        {
            return Result(
                new WorkflowStepOutcome(null, WorkflowActionState.RequiresPreviousStep, 0, null, location.StartFormId, Journey: await StartJourneyAsync(location, cancellationToken)),
                "Bu formu görüntülemek için önceki adımı tamamlamanız gerekiyor.",
                isTwoStepFlow);
        }

        var lastRun = await _instances.GetLastRunAsync(location.WorkflowId, userId, cancellationToken);

        if (await RefuseWhileRunActiveAsync(location, lastRun, cancellationToken) is { } running) return running;

        // Sonuçlanmış başvuru kabul durumundan önce gelir: akış kapansa da sonucu görünsün.
        if (HasUsedOnlyRun(location, lastRun)) return ClosedRun(lastRun!, isTwoStepFlow, location.StartFormId, await RunJourneyAsync(location.WorkflowName, lastRun!, cancellationToken));

        var intake = IntakeFor(location, lastRun, _time.GetUtcNow().UtcDateTime);
        if (intake != WorkflowIntake.Open)
            return IntakeClosed(intake, null, 0, location.StartFormId, isTwoStepFlow, await StartJourneyAsync(location, cancellationToken));

        var outcome = new WorkflowStepOutcome(null, WorkflowActionState.ShowForm, 1, formId, location.StartFormId, Journey: await StartJourneyAsync(location, cancellationToken));

        if (lastRun is { Status: WorkflowInstanceStatus.Voided })
        {
            outcome = outcome with
            {
                LastRun = new WorkflowLastRunContract(
                    WorkflowActionState.Reopened,
                    lastRun.VoidNote,
                    lastRun.VoidedAt,
                    await RunJourneyAsync(location.WorkflowName, lastRun, cancellationToken))
            };
        }
        else if (lastRun is { Status: WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Terminated })
        {
            outcome = outcome with
            {
                LastRun = new WorkflowLastRunContract(
                    lastRun.Outcome is WorkflowInstanceOutcome.Declined or WorkflowInstanceOutcome.TimedOut ? WorkflowActionState.Declined : WorkflowActionState.Completed,
                    lastRun.ReviewNote,
                    lastRun.ReviewedAt,
                    await RunJourneyAsync(location.WorkflowName, lastRun, cancellationToken))
            };
        }

        return Result(outcome, null, isTwoStepFlow);
    }

    /// <summary>Sonuçlanmış bir başvuruyu, kullanıcıya gösterilecek inceleme notuyla bildirir.</summary>
    private static ServiceResult<WorkflowStepOutcome> ClosedRun(WorkflowRunSummary run, bool isTwoStepFlow, Guid startFormId, WorkflowJourneyContract? journey = null)
    {
        var state = run.Status == WorkflowInstanceStatus.Faulted
            ? WorkflowActionState.Faulted
            : run.Outcome is WorkflowInstanceOutcome.Declined or WorkflowInstanceOutcome.TimedOut
                ? WorkflowActionState.Declined
                : WorkflowActionState.Completed;

        var outcome = new WorkflowStepOutcome(run.InstanceId, state, run.LastSequence, null, startFormId, run.ReviewNote, run.ReviewedAt, Journey: journey);

        return Result(outcome, state == WorkflowActionState.Declined
            ? "Başvurunuz reddedilmiştir."
            : MessageFor(outcome), isTwoStepFlow);
    }

    /// <summary>Akış kapalıyken dönen yanıt; stage sıfırsa kullanıcı henüz başlamamıştır.</summary>
    private static ServiceResult<WorkflowStepOutcome> IntakeClosed(
        WorkflowIntake intake,
        Guid? instanceId,
        int stage,
        Guid startFormId,
        bool isTwoStepFlow,
        WorkflowJourneyContract? journey = null)
    {
        var newRunsOnly = intake == WorkflowIntake.NewRunsClosed;

        var outcome = new WorkflowStepOutcome(
            instanceId,
            WorkflowActionState.Closed,
            stage,
            null,
            startFormId,
            Reason: newRunsOnly ? WorkflowClosedReason.NewRunsClosed : WorkflowClosedReason.WorkflowClosed,
            Journey: journey);

        var message = newRunsOnly
            ? "Bu akış yeni başvurulara kapatıldı."
            : stage > 0 ? "Akış kapatıldığı için başvurunuz durduruldu." : "Bu akış kapatıldı.";

        return Result(outcome, message, isTwoStepFlow);
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
        DateTime now,
        CancellationToken cancellationToken)
    {
        var context = await BuildContextAsync(instance, node.NodeKey, answers, cancellationToken);
        var decision = WorkflowTransitionResolver.Resolve(definition.Transitions, node.Id, trigger, context);

        var startFormId = definition.Nodes.First(candidate => candidate.IsStart).FormId;

        step.CompletedAt = now;
        step.SelectedTransitionId = decision.Transition?.Id;
        instance.Touch(now);

        // Arşivlenen akışta bekleyen karar yine verilir, ama başvuru sonraki adıma geçmez: kararla birlikte durur.
        if (instance.Workflow?.Status == WorkflowStatus.Archived)
        {
            step.SelectedTransitionId = null;

            return (Complete(instance, step, trigger, now, startFormId, stopped: true), null);
        }

        if (decision.Outcome == WorkflowRouteOutcome.Unresolved)
        {
            // Publish doğrulaması varsayılan rotayı zorunlu kılar; buraya düşmek
            // tanımın doğrulama dışı bir yoldan bozulduğu anlamına gelir.
            instance.Fault(now);

            return (new WorkflowStepOutcome(instance.Id, WorkflowActionState.Faulted, step.Sequence, null, startFormId), null);
        }

        if (decision.Transition?.TargetNodeId is not { } targetNodeId)
            return (Complete(instance, step, trigger, now, startFormId), null);

        var targetNode = definition.Nodes.First(candidate => candidate.Id == targetNodeId);

        var nextStep = new FormWorkflowStep
        {
            WorkflowInstanceId = instance.Id,
            NodeId = targetNode.Id,
            PreviousStepId = step.Id,
            Sequence = step.Sequence + 1
        };

        var outcome = new WorkflowStepOutcome(instance.Id, WorkflowActionState.ShowForm, nextStep.Sequence, targetNode.FormId, startFormId);

        return (outcome, nextStep);
    }

    /// <summary>Kapanan adımı, ardından yeni adımı tek transaction içinde yazar.</summary>
    private Task CommitAsync(FormWorkflowInstance instance, FormWorkflowStep? nextStep, CancellationToken cancellationToken) =>
        _uow.ExecuteInTransactionAsync(async token =>
        {
            await _uow.SaveChangesAsync(token);

            if (nextStep is not null)
            {
                // Adım yalnız context'e eklenir. Anahtarı istemci tarafında dolu olduğu
                // için koleksiyona eklemek EF'e onu mevcut satır gibi gösterirdi; öte
                // yandan Add sonrası fixup adımı koleksiyona zaten koyar, elle eklemek
                // aynı adımı listede iki kez bırakır.
                _instances.Add(nextStep);

                await _uow.SaveChangesAsync(token);
            }

            return true;
        }, cancellationToken);

    private static WorkflowStepOutcome Complete(
        FormWorkflowInstance instance,
        FormWorkflowStep step,
        WorkflowTransitionTrigger trigger,
        DateTime now,
        Guid startFormId,
        bool stopped = false)
    {
        var outcome = trigger switch
        {
            WorkflowTransitionTrigger.ResponseApproved => WorkflowInstanceOutcome.Approved,
            WorkflowTransitionTrigger.ResponseDeclined => WorkflowInstanceOutcome.Declined,
            WorkflowTransitionTrigger.TimedOut => WorkflowInstanceOutcome.TimedOut,
            _ => WorkflowInstanceOutcome.None
        };
        var declined = outcome is WorkflowInstanceOutcome.Declined or WorkflowInstanceOutcome.TimedOut;

        if (stopped) instance.Stop(now, outcome);
        else instance.Finish(outcome, now);

        return new WorkflowStepOutcome(
            instance.Id,
            declined ? WorkflowActionState.Declined : WorkflowActionState.Completed,
            step.Sequence,
            null,
            startFormId);
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

    private async Task<WorkflowJourneyContract> BuildJourneyAsync(
        string workflowName,
        WorkflowDefinition definition,
        Guid? instanceId,
        Guid? pendingNodeId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkflowJourneyStepFact> steps = instanceId is { } id
            ? await _instances.GetJourneyStepsAsync(id, cancellationToken)
            : [];

        var forms = await _workflows.GetFormHeadersAsync([.. definition.Nodes.Select(node => node.FormId).Distinct()], cancellationToken);

        return WorkflowJourneyBuilder.Build(workflowName, definition, steps, forms, pendingNodeId);
    }

    private async Task<WorkflowJourneyContract?> StartJourneyAsync(WorkflowNodeLocation location, CancellationToken cancellationToken)
    {
        var definition = await _workflows.GetDefinitionAsync(location.WorkflowVersionId, cancellationToken);
        if (definition is null) return null;

        var startNode = definition.Nodes.FirstOrDefault(node => node.IsStart);

        return await BuildJourneyAsync(location.WorkflowName, definition, null, startNode?.Id, cancellationToken);
    }

    private async Task<WorkflowJourneyContract?> RunJourneyAsync(string workflowName, WorkflowRunSummary run, CancellationToken cancellationToken)
    {
        var definition = await _workflows.GetDefinitionAsync(run.WorkflowVersionId, cancellationToken);
        if (definition is null) return null;

        var steps = await _instances.GetJourneyStepsAsync(run.InstanceId, cancellationToken);
        var forms = await _workflows.GetFormHeadersAsync([.. definition.Nodes.Select(node => node.FormId).Distinct()], cancellationToken);

        return WorkflowJourneyBuilder.Build(workflowName, definition, steps, forms, voided: run.Status == WorkflowInstanceStatus.Voided);
    }

    /// <summary>Tek başvuruluk akışta hak kullanıldı mı? Ekibin geçersiz saydığı başvuru hakkı yemez.</summary>
    /// <summary>
    /// Akışta devam eden başvurusu olan kişi yeni başvuru açamaz; görüntüleme, süreli başlatma ve ilk gönderim aynı kurala
    /// bakar. Başvurunun sürümü bu formu içermiyorsa (akış başka bir başlangıç formuyla yeniden yayımlandı) form yeni bir
    /// başın girişi gibi görünür; bu kontrol olmasa ikinci bir başvuru açılırdı.
    /// </summary>
    private async Task<ServiceResult<WorkflowStepOutcome>?> RefuseWhileRunActiveAsync(WorkflowNodeLocation location, WorkflowRunSummary? lastRun, CancellationToken cancellationToken) =>
        lastRun is { Status: WorkflowInstanceStatus.Active }
            ? Result(
                new WorkflowStepOutcome(lastRun.InstanceId, WorkflowActionState.RequiresPreviousStep, lastRun.LastSequence, null, location.StartFormId, Journey: await RunJourneyAsync(location.WorkflowName, lastRun, cancellationToken)),
                "Devam eden bir başvurunuz var; önce onu tamamlayın.",
                location.NodeCount == 2)
            : null;

    private static bool HasUsedOnlyRun(WorkflowNodeLocation location, WorkflowRunSummary? lastRun) =>
        !location.AllowMultipleRuns && lastRun is not null && lastRun.Status != WorkflowInstanceStatus.Voided;

    /// <summary>
    /// Ekibin geçersiz saydığı başvurunun sahibi, yeni başvurulara kapalı akışa da baştan girer:
    /// ekip onu içeri almayı seçti. Tamamen kapalı akış onu da almaz.
    /// </summary>
    private static WorkflowIntake IntakeFor(WorkflowNodeLocation location, WorkflowRunSummary? lastRun, DateTime now)
    {
        var intake = location.IntakeAt(now);

        return intake == WorkflowIntake.NewRunsClosed && lastRun is { Status: WorkflowInstanceStatus.Voided } ? WorkflowIntake.Open : intake;
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
            WorkflowActionState.Faulted => ServiceStatus.ConfigurationError,
            WorkflowActionState.RequiresPreviousStep => ServiceStatus.RequiresParentApproval,
            WorkflowActionState.Closed => ServiceStatus.NotAvailable,
            _ => ServiceStatus.Success
        }, outcome, message);

    private static ServiceResult<WorkflowStepOutcome> Faulted(FormWorkflowInstance instance) =>
        Result(
            new WorkflowStepOutcome(instance.Id, WorkflowActionState.Faulted, 0, null),
            "Başvurunuz beklenmeyen bir durumda; lütfen yetkiliyle iletişime geçin.");

    private static ServiceResult<WorkflowStepOutcome> DefinitionUnavailable() =>
        new(ServiceStatus.ConfigurationError, Message: "Akış tanımı okunamadı.");

    private static string? MessageFor(WorkflowStepOutcome outcome) => outcome.State switch
    {
        WorkflowActionState.ShowForm => "Yanıt kaydedildi, bir sonraki adıma geçebilirsiniz.",
        WorkflowActionState.Completed => "Tüm adımları tamamladınız.",
        WorkflowActionState.Declined => "Başvurunuz reddedilmiştir.",
        WorkflowActionState.Faulted => "Akış tanımı bu cevap için bir rota üretemedi.",
        _ => null
    };
}
