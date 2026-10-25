using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Infrastructure;

/// <summary>Modeldeki her Guid kolonu ya bir kişiyi gösterir ya da başka bir kayda başvurur; ikisi de el ile yazılır.</summary>
public static class AccountErasureColumns
{
    // AccountErasureRepository bu kolonların hepsinden kişiyi çıkarır; entegrasyon testi bunu gerçek veritabanında dener.
    public static readonly string[] Person =
    [
        "AnswerFileLink.UserId",
        "ComponentGroup.ArchivedBy",
        "ComponentGroup.OwnedBy",
        "FormAttempt.UserId",
        "FormAttemptEvent.ActorUserId",
        "FormCollaborator.UserId",
        "FormResponse.ArchivedBy",
        "FormResponse.ReviewedBy",
        "FormResponse.UserId",
        "FormWorkflow.OwnerUserId",
        "FormWorkflowInstance.UserId",
        "FormWorkflowInstance.VoidedBy"
    ];

    public static readonly string[] Reference =
    [
        "AnswerFileLink.AttachmentId",
        "AnswerFileLink.FormId",
        "AnswerFileLink.Id",
        "AnswerFileLink.MediaId",
        "AnswerFileLink.ResponseId",
        "ComponentGroup.Id",
        "Form.Id",
        "Form.LinkedFormId",
        "FormAttempt.FormId",
        "FormAttempt.Id",
        "FormAttempt.ResponseId",
        "FormAttempt.WorkflowStepId",
        "FormAttemptEvent.AttemptId",
        "FormAttemptEvent.Id",
        "FormCollaborator.FormId",
        "FormResponse.FormId",
        "FormResponse.Id",
        "FormWorkflow.Id",
        "FormWorkflowInstance.Id",
        "FormWorkflowInstance.WorkflowId",
        "FormWorkflowInstance.WorkflowVersionId",
        "FormWorkflowNode.FormId",
        "FormWorkflowNode.Id",
        "FormWorkflowNode.WorkflowVersionId",
        "FormWorkflowStep.Id",
        "FormWorkflowStep.NodeId",
        "FormWorkflowStep.PreviousStepId",
        "FormWorkflowStep.ResponseId",
        "FormWorkflowStep.SelectedTransitionId",
        "FormWorkflowStep.WorkflowInstanceId",
        "FormWorkflowTransition.Id",
        "FormWorkflowTransition.SourceNodeId",
        "FormWorkflowTransition.TargetNodeId",
        "FormWorkflowTransition.WorkflowVersionId",
        "FormWorkflowVersion.Id",
        "FormWorkflowVersion.WorkflowId",
        "ResponseAttribution.FormResponseId",
        "OutboxMessage.Id",
        "ResponseGuest.FormResponseId"
    ];

    public static IEnumerable<IProperty> GuidProperties(IModel model) =>
        model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property => property.ClrType == typeof(Guid) || property.ClrType == typeof(Guid?));

    public static string NameOf(IProperty property) => $"{property.DeclaringType.ShortName()}.{property.Name}";
}

public class AccountErasureCoverageTests
{
    [Fact]
    public void Every_guid_column_is_classified_as_a_person_or_a_reference()
    {
        using var context = new FormsDbContext(new DbContextOptionsBuilder<FormsDbContext>().UseNpgsql("Host=unused").Options);

        var columns = AccountErasureColumns.GuidProperties(context.Model).Select(AccountErasureColumns.NameOf).Order(StringComparer.Ordinal);

        // Yeni bir Guid kolonu sınıflandırılana kadar test kırılır: kişiyi gösteriyorsa önce silmeye ekle.
        Assert.Equal(AccountErasureColumns.Person.Concat(AccountErasureColumns.Reference).Order(StringComparer.Ordinal), columns);
    }
}
