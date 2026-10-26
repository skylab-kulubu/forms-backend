using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts.Collaborators;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services.Forms;

/// <summary>Editörün ve listelerin aynı biçimde gördüğü form ve akış üyeliği.</summary>
internal static class FormEditorMapping
{
    public static FormContract ContractOf(Form form, List<UserContract> users, CollaboratorRole userRole = CollaboratorRole.None, FormWorkflowRefContract? workflow = null)
    {
        var collaboratorContracts = new List<FormCollaboratorContract>();

        if (form.Collaborators != null)
        {
            foreach (var collaborator in form.Collaborators)
            {
                var userDetail = users.FirstOrDefault(u => u.Id == collaborator.UserId) ?? new UserContract(collaborator.UserId, null, null, null);
                collaboratorContracts.Add(new FormCollaboratorContract(
                    userDetail,
                    collaborator.Role
                ));
            }
        }

        return new FormContract(
            form.Id,
            form.Title,
            form.Description,
            form.Schema,
            form.Status,
            form.AllowAnonymousResponses,
            form.AllowMultipleResponses,
            form.RequiresManualReview,
            workflow,
            userRole,
            collaboratorContracts,
            form.CreatedAt,
            form.UpdatedAt,
            form.Task,
            form.ClosesAt,
            form.TimeLimitMinutes
        );
    }

    public static async Task<WorkflowFormMembership?> MembershipOfAsync(IFormWorkflowRepository workflows, Guid formId, CancellationToken cancellationToken)
    {
        var memberships = await workflows.GetFormMembershipsAsync([formId], cancellationToken);

        return memberships.TryGetValue(formId, out var membership) ? membership : null;
    }

    public static FormWorkflowRefContract? WorkflowRefOf(WorkflowFormMembership? membership) =>
        membership is null
            ? null
            : new FormWorkflowRefContract(
                membership.WorkflowId,
                membership.WorkflowName,
                membership.IsStart,
                membership.IsPublished,
                membership.AllowMultipleRuns,
                membership.Intake,
                membership.RequiresManualReview,
                [.. membership.LockedQuestions.Select(question => new FormLockedQuestionContract(question.QuestionId, [.. question.Values]))]);
}
