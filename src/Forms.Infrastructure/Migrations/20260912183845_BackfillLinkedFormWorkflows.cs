using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forms.Infrastructure.Migrations
{
    /// <summary>
    /// Her bağlı form çiftini iki adımlı bir akışa dönüştürür ve akışın ortasında
    /// kalmış başvuruları kaldıkları adıma bağlar. Tamamlanmış geçmiş cevaplar
    /// dönüştürülmez: eşleştirme zaman damgasına dayandığı için yanlış eşleşme
    /// riski taşır, oldukları yerde okunmaya devam ederler.
    ///
    /// Forms.LinkedFormId bu turda düşürülmez; kod artık okumasa da bir sürümlük
    /// geri dönüş penceresi bırakır.
    /// </summary>
    public partial class BackfillLinkedFormWorkflows : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- Enum karsiliklari: FormStatus.Deleted=0, FormResponseStatus{NonRestrict=0,Pending=1,Approved=2,Declined=3},
-- CollaboratorRole.Owner=3, WorkflowStatus.Published=1, WorkflowTransitionTrigger{Submitted=0,Approved=1},
-- WorkflowInstanceStatus.Active=0, WorkflowInstanceOutcome.None=0.

CREATE TEMP TABLE legacy_pair ON COMMIT DROP AS
SELECT
    parent.""Id""                      AS parent_form_id,
    child.""Id""                       AS child_form_id,
    parent.""Title""                   AS title,
    parent.""AllowMultipleResponses""  AS allow_multiple_runs,
    parent.""RequiresManualReview""    AS parent_requires_review,
    owner.""UserId""                   AS owner_user_id,
    gen_random_uuid()                  AS workflow_id,
    gen_random_uuid()                  AS version_id,
    gen_random_uuid()                  AS parent_node_id,
    gen_random_uuid()                  AS child_node_id,
    gen_random_uuid()                  AS transition_id
FROM ""Forms"" parent
JOIN ""Forms"" child ON child.""Id"" = parent.""LinkedFormId""
JOIN LATERAL (
    SELECT c.""UserId""
    FROM ""Collaborators"" c
    WHERE c.""FormId"" = parent.""Id"" AND c.""Role"" = 3
    ORDER BY c.""UserId""
    LIMIT 1
) owner ON TRUE
WHERE parent.""LinkedFormId"" IS NOT NULL
  AND parent.""Status"" <> 0
  AND child.""Status"" <> 0
  -- Ayni form iki yayinda yer alamaz; zaten akista olan cifti atla.
  AND NOT EXISTS (
      SELECT 1 FROM ""WorkflowNodes"" n
      JOIN ""WorkflowVersions"" v ON v.""Id"" = n.""WorkflowVersionId""
      WHERE n.""FormId"" IN (parent.""Id"", child.""Id"") AND v.""Status"" = 1
  );

INSERT INTO ""Workflows"" (""Id"", ""Name"", ""Description"", ""OwnerUserId"", ""Status"", ""AllowMultipleRuns"", ""CreatedAt"", ""UpdatedAt"")
SELECT workflow_id, LEFT(title, 100), NULL, owner_user_id, 1, allow_multiple_runs, now(), NULL
FROM legacy_pair;

INSERT INTO ""WorkflowVersions"" (""Id"", ""WorkflowId"", ""Version"", ""Status"", ""PublishedAt"", ""CreatedAt"", ""UpdatedAt"")
SELECT version_id, workflow_id, 1, 1, now(), now(), NULL
FROM legacy_pair;

INSERT INTO ""WorkflowNodes"" (""Id"", ""WorkflowVersionId"", ""FormId"", ""NodeKey"", ""IsStart"")
SELECT parent_node_id, version_id, parent_form_id, 'step1', TRUE FROM legacy_pair
UNION ALL
SELECT child_node_id, version_id, child_form_id, 'step2', FALSE FROM legacy_pair;

-- Tek yonlendirme yeter: red icin ayri bir kayit gerekmez, cunku tetik icin
-- tanimli yonlendirme yoksa akis zaten sonlanir.
INSERT INTO ""WorkflowTransitions"" (""Id"", ""WorkflowVersionId"", ""SourceNodeId"", ""TargetNodeId"", ""Trigger"", ""Condition"", ""Priority"")
SELECT transition_id, version_id, parent_node_id, child_node_id,
       CASE WHEN parent_requires_review THEN 1 ELSE 0 END,
       NULL, 0
FROM legacy_pair;

-- Akisin ortasinda kalanlar: ilk forma cevap vermis ama o cevaptan sonra ikinci
-- forma cevap vermemis kullanicilar. Reddedilmis cevaplar disarida: onlarda akis
-- zaten bitmisti, kullanici bastan basliyor.
CREATE TEMP TABLE legacy_unfinished ON COMMIT DROP AS
SELECT
    pair.*,
    latest.""Id""        AS response_id,
    latest.""UserId""    AS user_id,
    latest.""Status""    AS response_status,
    latest.""SubmittedAt"" AS submitted_at,
    gen_random_uuid()   AS instance_id,
    gen_random_uuid()   AS first_step_id,
    gen_random_uuid()   AS second_step_id
FROM legacy_pair pair
JOIN LATERAL (
    SELECT DISTINCT ON (r.""UserId"") r.""Id"", r.""UserId"", r.""Status"", r.""SubmittedAt""
    FROM ""Responses"" r
    WHERE r.""FormId"" = pair.parent_form_id
      AND r.""UserId"" IS NOT NULL
      AND r.""IsArchived"" = FALSE
    ORDER BY r.""UserId"", r.""SubmittedAt"" DESC
) latest ON TRUE
WHERE latest.""Status"" <> 3
  AND NOT EXISTS (
      SELECT 1 FROM ""Responses"" c
      WHERE c.""FormId"" = pair.child_form_id
        AND c.""UserId"" = latest.""UserId""
        AND c.""IsArchived"" = FALSE
        AND c.""SubmittedAt"" > latest.""SubmittedAt""
  );

INSERT INTO ""WorkflowInstances"" (""Id"", ""WorkflowId"", ""WorkflowVersionId"", ""UserId"", ""Status"", ""Outcome"", ""StartedAt"", ""CompletedAt"", ""CreatedAt"", ""UpdatedAt"")
SELECT instance_id, workflow_id, version_id, user_id, 0, 0, submitted_at, NULL, now(), NULL
FROM legacy_unfinished;

-- Cevap incelemede ise ilk adim acik kalir; onay geldiginde rota normal isler.
INSERT INTO ""WorkflowSteps"" (""Id"", ""WorkflowInstanceId"", ""NodeId"", ""ResponseId"", ""PreviousStepId"", ""SelectedTransitionId"", ""Sequence"", ""CompletedAt"", ""CreatedAt"", ""UpdatedAt"")
SELECT first_step_id, instance_id, parent_node_id, response_id, NULL,
       CASE WHEN response_status = 1 THEN NULL ELSE transition_id END,
       1,
       CASE WHEN response_status = 1 THEN NULL ELSE submitted_at END,
       now(), NULL
FROM legacy_unfinished;

-- Ilk adim gecilmisse ikinci adim acilir.
INSERT INTO ""WorkflowSteps"" (""Id"", ""WorkflowInstanceId"", ""NodeId"", ""ResponseId"", ""PreviousStepId"", ""SelectedTransitionId"", ""Sequence"", ""CompletedAt"", ""CreatedAt"", ""UpdatedAt"")
SELECT second_step_id, instance_id, child_node_id, NULL, first_step_id, NULL, 2, NULL, now(), NULL
FROM legacy_unfinished
WHERE response_status <> 1;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri alinamaz: uretilen akislari ve basvurulari, elle olusturulanlardan
            // ayirt edecek bir isaret yok. Geri donus gerekirse yedekten donulur.
        }
    }
}
