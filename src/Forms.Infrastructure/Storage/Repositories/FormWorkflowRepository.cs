using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

public sealed class FormWorkflowRepository : IFormWorkflowRepository
{
    private readonly FormsDbContext _context;

    public FormWorkflowRepository(FormsDbContext context)
    {
        _context = context;
    }

    // Bir formun yayındaki tek bir akışta yer alması publish sırasında güvence
    // altına alınır; burada tekil sonuç beklenir.
    public Task<WorkflowNodeLocation?> FindPublishedNodeAsync(Guid formId, CancellationToken ct = default) =>
        _context.WorkflowNodes.AsNoTracking()
            .Where(node => node.FormId == formId)
            .Where(node => node.WorkflowVersion.Status == WorkflowStatus.Published)
            .Where(node => node.WorkflowVersion.Workflow.Status != WorkflowStatus.Archived)
            .Select(node => new WorkflowNodeLocation(
                node.WorkflowVersion.WorkflowId,
                node.WorkflowVersionId,
                node.Id,
                node.NodeKey,
                node.IsStart,
                node.WorkflowVersion.Workflow.AllowMultipleRuns))
            .FirstOrDefaultAsync(ct);

    public async Task<WorkflowDefinition?> GetDefinitionAsync(Guid workflowVersionId, CancellationToken ct = default)
    {
        var version = await _context.WorkflowVersions.AsNoTracking()
            .Include(candidate => candidate.Nodes)
            .Include(candidate => candidate.Transitions)
            .FirstOrDefaultAsync(candidate => candidate.Id == workflowVersionId, ct);

        return version is null
            ? null
            : new WorkflowDefinition(version.WorkflowId, version.Id, [.. version.Nodes], [.. version.Transitions]);
    }
}
