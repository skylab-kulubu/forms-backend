using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Application.Services.AccountErasure;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Infrastructure.AnswerFiles;
using Skylab.Forms.Infrastructure.Storage;
using Skylab.Forms.Tests.Infrastructure;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AccountErasureTests(FormsApiFactory api)
{
    [Fact]
    public async Task Erasing_a_person_leaves_no_row_that_names_them()
    {
        var person = Guid.NewGuid();
        await SeedEveryPersonColumnAsync(person);

        // Listeye yeni bir kişi kolonu eklendiyse tohuma da eklenmeli; yoksa aşağıdaki kontrol boşa geçer.
        Assert.Empty((await RowsNamingAsync(person)).Where(column => column.Value == 0).Select(column => column.Key));

        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IAccountErasureService>().EraseAsync(new AccountErasureCommand(Guid.NewGuid(), person, []));

        Assert.Empty((await RowsNamingAsync(person)).Where(column => column.Value > 0).Select(column => column.Key));
    }

    private async Task SeedEveryPersonColumnAsync(Guid person)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var colleague = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var form = new Form { Title = "Silme senaryosu" };
        form.Collaborators.Add(new FormCollaborator { UserId = colleague, Role = CollaboratorRole.Owner });
        form.Collaborators.Add(new FormCollaborator { UserId = person, Role = CollaboratorRole.Viewer });

        var colleagueAttempt = new FormAttempt { Form = form, UserId = colleague };
        colleagueAttempt.Events.Add(new FormAttemptEvent { Type = FormAttemptEventType.Extended, ActorUserId = person });

        var workflow = new FormWorkflow { OwnerUserId = person };

        db.AddRange(
            form,
            new FormResponse { Form = form, UserId = person },
            new FormResponse { Form = form, UserId = colleague, ReviewedBy = person, IsArchived = true, ArchivedBy = person, ArchivedAt = now },
            new ComponentGroup { OwnedBy = person, ArchivedBy = person, ArchivedAt = now },
            colleagueAttempt,
            new FormAttempt { Form = form, UserId = person },
            new FormWorkflowInstance
            {
                Workflow = workflow,
                WorkflowVersion = new FormWorkflowVersion { Workflow = workflow },
                UserId = person,
                Status = WorkflowInstanceStatus.Voided,
                VoidedBy = person
            },
            new AnswerFileLink { MediaId = Guid.NewGuid(), FormId = form.Id, UserId = person, State = AnswerFileLinkState.Linking, CreatedAt = now });

        await db.SaveChangesAsync();
    }

    /// <summary>Her kişi kolonunda kişiyi gösteren satır sayısı.</summary>
    private async Task<Dictionary<string, int>> RowsNamingAsync(Guid person)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var rows = new Dictionary<string, int>();

        foreach (var property in AccountErasureColumns.GuidProperties(db.Model).Where(p => AccountErasureColumns.Person.Contains(AccountErasureColumns.NameOf(p))))
        {
            // Tablo ve sütun adı modelden gelir, parametre olamaz; kişinin kimliği parametredir.
            var sql = $$"""SELECT count(*)::int AS "Value" FROM "{{((IEntityType)property.DeclaringType).GetTableName()}}" WHERE "{{property.GetColumnName()}}" = {0}""";

            rows[AccountErasureColumns.NameOf(property)] = await db.Database.SqlQueryRaw<int>(sql, person).SingleAsync();
        }

        return rows;
    }
}
