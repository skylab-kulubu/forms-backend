using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Domain.Workflows;

/// <summary>
/// Bir akış tanımının yayınlanabilir olup olmadığını doğrular. Buradan geçen bir
/// tanımda çalışma zamanında rota belirsizliği oluşamaz.
/// </summary>
public static class WorkflowGraphValidator
{
    public static IReadOnlyList<WorkflowValidationError> Validate(
        IReadOnlyList<FormWorkflowNode> nodes,
        IReadOnlyList<FormWorkflowTransition> transitions,
        IReadOnlyDictionary<Guid, WorkflowNodeForm> formsByFormId)
    {
        var errors = new List<WorkflowValidationError>();

        if (nodes.Count == 0)
        {
            errors.Add(new WorkflowValidationError("emptyGraph", "Akışta en az bir form bulunmalıdır."));
            return errors;
        }

        var structureIsSound = ValidateStructure(nodes, transitions, errors);

        ValidateForms(nodes, formsByFormId, errors);

        // Yapı bozukken graf analizi yanıltıcı sonuç üretir.
        if (!structureIsSound) return errors;

        var startNode = nodes.First(node => node.IsStart);
        var graph = BuildGraph(nodes, transitions, startNode.Id);

        if (graph is null)
        {
            errors.Add(new WorkflowValidationError("cycleDetected", "Akış döngü içeriyor: bir form kendisine geri dönemez."));
            return errors;
        }

        var nodesById = nodes.ToDictionary(node => node.Id);
        var nodesByKey = nodes.ToDictionary(node => node.NodeKey, StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes.Where(node => !graph.Reachable.Contains(node.Id)))
        {
            errors.Add(new WorkflowValidationError(
                "nodeUnreachable",
                $"'{node.NodeKey}' adımına başlangıçtan ulaşılamıyor.",
                node.NodeKey));
        }

        foreach (var node in nodes.Where(node => graph.Depth[node.Id] > WorkflowLimits.MaxDepth))
        {
            errors.Add(new WorkflowValidationError(
                "depthExceeded",
                $"Bir rotada en fazla {WorkflowLimits.MaxDepth} form bulunabilir; '{node.NodeKey}' adımı bu sınırı aşıyor.",
                node.NodeKey));
        }

        ValidateTriggerGroups(transitions, nodesById, formsByFormId, errors);
        ValidateConditions(transitions, nodesById, nodesByKey, formsByFormId, graph, errors);

        return errors;
    }

    /// <summary>Graf analizine geçilebiliyorsa true döner.</summary>
    private static bool ValidateStructure(
        IReadOnlyList<FormWorkflowNode> nodes,
        IReadOnlyList<FormWorkflowTransition> transitions,
        List<WorkflowValidationError> errors)
    {
        var isSound = true;

        var startNodes = nodes.Where(node => node.IsStart).ToList();

        if (startNodes.Count == 0)
        {
            errors.Add(new WorkflowValidationError("startNodeMissing", "Akışta bir başlangıç formu işaretlenmelidir."));
            isSound = false;
        }
        else if (startNodes.Count > 1)
        {
            errors.Add(new WorkflowValidationError("startNodeAmbiguous", "Akışta yalnızca bir başlangıç formu olabilir."));
            isSound = false;
        }

        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.NodeKey))
            {
                errors.Add(new WorkflowValidationError("nodeKeyMissing", "Her adımın bir anahtarı olmalıdır."));
                isSound = false;
            }
            else if (node.NodeKey.Length > WorkflowLimits.MaxNodeKeyLength)
            {
                errors.Add(new WorkflowValidationError(
                    "nodeKeyTooLong",
                    $"Adım anahtarı en fazla {WorkflowLimits.MaxNodeKeyLength} karakter olabilir.",
                    node.NodeKey));
                isSound = false;
            }
        }

        var duplicateKeys = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeKey))
            .GroupBy(node => node.NodeKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var duplicate in duplicateKeys)
        {
            errors.Add(new WorkflowValidationError(
                "nodeKeyDuplicated",
                $"'{duplicate.Key}' adım anahtarı birden fazla kez kullanılmış.",
                duplicate.Key));
            isSound = false;
        }

        foreach (var duplicate in nodes.GroupBy(node => node.FormId).Where(group => group.Count() > 1))
        {
            errors.Add(new WorkflowValidationError(
                "formDuplicated",
                "Aynı form akışta birden fazla adımda kullanılamaz.",
                duplicate.First().NodeKey));
            isSound = false;
        }

        var nodeIds = nodes.Select(node => node.Id).ToHashSet();

        foreach (var transition in transitions)
        {
            if (!nodeIds.Contains(transition.SourceNodeId))
            {
                errors.Add(new WorkflowValidationError("transitionSourceMissing", "Bir yönlendirmenin kaynak adımı akışta bulunmuyor."));
                isSound = false;
            }

            if (transition.TargetNodeId.HasValue && !nodeIds.Contains(transition.TargetNodeId.Value))
            {
                errors.Add(new WorkflowValidationError("transitionTargetMissing", "Bir yönlendirmenin hedef adımı akışta bulunmuyor."));
                isSound = false;
            }
        }

        return isSound;
    }

    private static void ValidateForms(
        IReadOnlyList<FormWorkflowNode> nodes,
        IReadOnlyDictionary<Guid, WorkflowNodeForm> formsByFormId,
        List<WorkflowValidationError> errors)
    {
        foreach (var node in nodes)
        {
            var form = formsByFormId.TryGetValue(node.FormId, out var found) ? found : WorkflowNodeForm.Missing;

            if (!form.Exists)
            {
                errors.Add(new WorkflowValidationError(
                    "formMissing",
                    $"'{node.NodeKey}' adımının formu bulunamadı veya silinmiş.",
                    node.NodeKey));
                continue;
            }

            if (!form.IsOpen)
            {
                errors.Add(new WorkflowValidationError(
                    "formClosed",
                    $"'{node.NodeKey}' adımının formu kapalı.",
                    node.NodeKey));
            }

            if (form.AllowAnonymousResponses)
            {
                errors.Add(new WorkflowValidationError(
                    "formAnonymous",
                    $"'{node.NodeKey}' adımının formu anonim yanıt kabul ediyor; akışlar giriş yapmış kullanıcı gerektirir.",
                    node.NodeKey));
            }

            if (!form.WorkflowOwnerIsFormOwner)
            {
                errors.Add(new WorkflowValidationError(
                    "formNotOwned",
                    $"'{node.NodeKey}' adımının formunda Owner yetkiniz yok.",
                    node.NodeKey));
            }
        }
    }

    private static void ValidateTriggerGroups(
        IReadOnlyList<FormWorkflowTransition> transitions,
        IReadOnlyDictionary<Guid, FormWorkflowNode> nodesById,
        IReadOnlyDictionary<Guid, WorkflowNodeForm> formsByFormId,
        List<WorkflowValidationError> errors)
    {
        foreach (var group in transitions.GroupBy(transition => new { transition.SourceNodeId, transition.Trigger }))
        {
            var node = nodesById[group.Key.SourceNodeId];
            var form = formsByFormId.TryGetValue(node.FormId, out var found) ? found : WorkflowNodeForm.Missing;

            if (form.Exists) ValidateTrigger(group.Key.Trigger, node, form, errors);

            var defaultRoutes = group.Where(transition => transition.IsDefaultRoute).ToList();
            var conditionals = group.Where(transition => !transition.IsDefaultRoute).ToList();

            if (defaultRoutes.Count > 1)
            {
                errors.Add(new WorkflowValidationError(
                    "defaultRouteDuplicated",
                    $"'{node.NodeKey}' adımında aynı tetik için birden fazla koşulsuz yönlendirme var; hangisinin izleneceği belirsiz.",
                    node.NodeKey));
            }

            // Varsayılan rota zorunlu olmasaydı, hiçbir koşulun tutmadığı bir
            // başvuru çalışma zamanında rotasız kalırdı.
            if (conditionals.Count > 0 && defaultRoutes.Count == 0)
            {
                errors.Add(new WorkflowValidationError(
                    "defaultRouteMissing",
                    $"'{node.NodeKey}' adımında koşullu yönlendirme varsa, hiçbir koşul tutmadığında izlenecek koşulsuz bir yönlendirme de tanımlanmalıdır.",
                    node.NodeKey));
            }

            if (group.GroupBy(transition => transition.Priority).Any(priorities => priorities.Count() > 1))
            {
                errors.Add(new WorkflowValidationError(
                    "priorityDuplicated",
                    $"'{node.NodeKey}' adımında aynı tetik için iki yönlendirme aynı önceliği paylaşıyor.",
                    node.NodeKey));
            }
        }
    }

    /// <summary>
    /// Onay gerektiren bir adım gönderimde ilerlerse hem gönderimde hem onayda rota
    /// seçilir ve başvuruda tek aktif dal kuralı bozulur.
    /// </summary>
    private static void ValidateTrigger(
        WorkflowTransitionTrigger trigger,
        FormWorkflowNode node,
        WorkflowNodeForm form,
        List<WorkflowValidationError> errors)
    {
        var triggerIsAllowed = form.RequiresManualReview
            ? trigger is WorkflowTransitionTrigger.ResponseApproved or WorkflowTransitionTrigger.ResponseDeclined
            : trigger is WorkflowTransitionTrigger.ResponseSubmitted;

        if (triggerIsAllowed) return;

        errors.Add(new WorkflowValidationError(
            "triggerNotAllowed",
            form.RequiresManualReview
                ? $"'{node.NodeKey}' adımı onay gerektiriyor; yönlendirme yalnızca onay veya red sonrasına bağlanabilir."
                : $"'{node.NodeKey}' adımı onay gerektirmiyor; yönlendirme yalnızca cevap gönderimine bağlanabilir.",
            node.NodeKey));
    }

    private static void ValidateConditions(
        IReadOnlyList<FormWorkflowTransition> transitions,
        IReadOnlyDictionary<Guid, FormWorkflowNode> nodesById,
        IReadOnlyDictionary<string, FormWorkflowNode> nodesByKey,
        IReadOnlyDictionary<Guid, WorkflowNodeForm> formsByFormId,
        WorkflowGraph graph,
        List<WorkflowValidationError> errors)
    {
        foreach (var transition in transitions)
        {
            if (transition.Condition is null) continue;

            var sourceNode = nodesById[transition.SourceNodeId];

            foreach (var rule in transition.Condition.Rules)
                ValidateRule(rule, sourceNode, nodesByKey, formsByFormId, graph, errors);
        }
    }

    private static void ValidateRule(
        WorkflowConditionRule rule,
        FormWorkflowNode sourceNode,
        IReadOnlyDictionary<string, FormWorkflowNode> nodesByKey,
        IReadOnlyDictionary<Guid, WorkflowNodeForm> formsByFormId,
        WorkflowGraph graph,
        List<WorkflowValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(rule.QuestionId))
        {
            errors.Add(new WorkflowValidationError(
                "conditionQuestionMissing",
                $"'{sourceNode.NodeKey}' adımındaki bir koşulda soru seçilmemiş.",
                sourceNode.NodeKey));
            return;
        }

        if (!TryResolveRuleNode(rule, sourceNode, nodesByKey, out var ruleNode))
        {
            errors.Add(new WorkflowValidationError(
                "conditionNodeMissing",
                $"'{sourceNode.NodeKey}' adımındaki koşul, akışta olmayan '{rule.NodeKey}' adımına referans veriyor.",
                sourceNode.NodeKey));
            return;
        }

        // Referans verilen adım her rotada kaynaktan önce gelmiyorsa, bazı
        // başvurularda o cevap hiç oluşmamış olur ve koşul sessizce düşerdi.
        if (ruleNode.Id != sourceNode.Id && !graph.Dominators[sourceNode.Id].Contains(ruleNode.Id))
        {
            errors.Add(new WorkflowValidationError(
                "conditionNodeNotGuaranteed",
                $"'{ruleNode.NodeKey}' adımı '{sourceNode.NodeKey}' adımına giden her rotada yer almadığı için koşulda kullanılamaz.",
                sourceNode.NodeKey));
        }

        var ruleForm = formsByFormId.TryGetValue(ruleNode.FormId, out var found) ? found : WorkflowNodeForm.Missing;

        if (ruleForm.Exists && !ruleForm.QuestionIds.Contains(rule.QuestionId))
        {
            errors.Add(new WorkflowValidationError(
                "conditionQuestionMissing",
                $"'{rule.QuestionId}' sorusu '{ruleNode.NodeKey}' adımının formunda bulunmuyor.",
                sourceNode.NodeKey));
        }

        if (!HasRequiredValue(rule))
        {
            errors.Add(new WorkflowValidationError(
                "conditionValueMissing",
                $"'{sourceNode.NodeKey}' adımındaki bir koşulda karşılaştırma değeri girilmemiş.",
                sourceNode.NodeKey));
        }
    }

    private static bool TryResolveRuleNode(
        WorkflowConditionRule rule,
        FormWorkflowNode sourceNode,
        IReadOnlyDictionary<string, FormWorkflowNode> nodesByKey,
        out FormWorkflowNode ruleNode)
    {
        if (string.IsNullOrWhiteSpace(rule.NodeKey))
        {
            ruleNode = sourceNode;
            return true;
        }

        return nodesByKey.TryGetValue(rule.NodeKey, out ruleNode!);
    }

    private static bool HasRequiredValue(WorkflowConditionRule rule) => rule.Comparison switch
    {
        WorkflowConditionComparison.IsEmpty or WorkflowConditionComparison.IsNotEmpty => true,
        WorkflowConditionComparison.In or WorkflowConditionComparison.NotIn => rule.Values is { Count: > 0 },
        _ => !string.IsNullOrWhiteSpace(rule.Value)
    };

    private static WorkflowGraph? BuildGraph(
        IReadOnlyList<FormWorkflowNode> nodes,
        IReadOnlyList<FormWorkflowTransition> transitions,
        Guid startNodeId)
    {
        var outgoing = nodes.ToDictionary(node => node.Id, _ => new HashSet<Guid>());
        var incoming = nodes.ToDictionary(node => node.Id, _ => new HashSet<Guid>());
        var indegree = nodes.ToDictionary(node => node.Id, _ => 0);

        foreach (var transition in transitions)
        {
            if (transition.TargetNodeId is not { } targetId) continue;

            // Aynı iki adım arasında birden fazla koşullu yönlendirme olabilir;
            // graf analizinde bunlar tek kenar sayılır.
            if (!outgoing[transition.SourceNodeId].Add(targetId)) continue;

            incoming[targetId].Add(transition.SourceNodeId);
            indegree[targetId]++;
        }

        var queue = new Queue<Guid>(indegree.Where(entry => entry.Value == 0).Select(entry => entry.Key));
        var order = new List<Guid>(nodes.Count);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            order.Add(current);

            foreach (var next in outgoing[current])
            {
                if (--indegree[next] == 0) queue.Enqueue(next);
            }
        }

        // Topolojik sıra bütün node'ları kapsamıyorsa geriye kalanlar bir döngüdedir.
        if (order.Count != nodes.Count) return null;

        var reachable = FindReachable(outgoing, startNodeId);
        var depth = MeasureDepth(nodes, outgoing, order, startNodeId);
        var dominators = FindDominators(incoming, order, reachable);

        return new WorkflowGraph(reachable, depth, dominators);
    }

    private static HashSet<Guid> FindReachable(IReadOnlyDictionary<Guid, HashSet<Guid>> outgoing, Guid startNodeId)
    {
        var reachable = new HashSet<Guid> { startNodeId };
        var pending = new Queue<Guid>();
        pending.Enqueue(startNodeId);

        while (pending.Count > 0)
        {
            foreach (var next in outgoing[pending.Dequeue()])
            {
                if (reachable.Add(next)) pending.Enqueue(next);
            }
        }

        return reachable;
    }

    /// <summary>Başlangıçtan bir adıma giden en uzun rotadaki form sayısı.</summary>
    private static Dictionary<Guid, int> MeasureDepth(
        IReadOnlyList<FormWorkflowNode> nodes,
        IReadOnlyDictionary<Guid, HashSet<Guid>> outgoing,
        IReadOnlyList<Guid> topologicalOrder,
        Guid startNodeId)
    {
        var depth = nodes.ToDictionary(node => node.Id, _ => 0);
        depth[startNodeId] = 1;

        foreach (var id in topologicalOrder)
        {
            // Derinliği sıfır kalan adımlara başlangıçtan ulaşılamıyordur.
            if (depth[id] == 0) continue;

            foreach (var next in outgoing[id])
            {
                if (depth[next] < depth[id] + 1) depth[next] = depth[id] + 1;
            }
        }

        return depth;
    }

    /// <summary>Bir adımı domine edenler, başlangıçtan ona giden her rotada bulunanlardır.</summary>
    private static Dictionary<Guid, HashSet<Guid>> FindDominators(
        IReadOnlyDictionary<Guid, HashSet<Guid>> incoming,
        IReadOnlyList<Guid> topologicalOrder,
        IReadOnlySet<Guid> reachable)
    {
        var dominators = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var id in topologicalOrder)
        {
            if (!reachable.Contains(id)) continue;

            HashSet<Guid>? shared = null;

            // Topolojik sıra, her öncülün bu noktada zaten hesaplanmış olmasını garanti eder.
            foreach (var predecessor in incoming[id].Where(reachable.Contains))
            {
                if (shared is null) shared = new HashSet<Guid>(dominators[predecessor]);
                else shared.IntersectWith(dominators[predecessor]);
            }

            shared ??= new HashSet<Guid>();
            shared.Add(id);
            dominators[id] = shared;
        }

        return dominators;
    }

    private sealed record WorkflowGraph(
        IReadOnlySet<Guid> Reachable,
        IReadOnlyDictionary<Guid, int> Depth,
        IReadOnlyDictionary<Guid, HashSet<Guid>> Dominators);
}
