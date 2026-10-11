using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.GuestUploads;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Infrastructure.AnswerFiles;

/// <summary>
/// Girişli kişinin bir cevap dosyasının core'daki bağı: taslağına (ResponseId boş) ya da cevabına.
/// Core'un verdiği bağ kimliği saklanır, çünkü bağ ancak onunla kaldırılır.
/// </summary>
public class AnswerFileLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MediaId { get; set; }
    public Guid FormId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ResponseId { get; set; }
    public Guid? AttachmentId { get; set; }
    public AnswerFileLinkState State { get; set; }
    public int Attempts { get; set; }

    /// <summary>Arka plan işinin satıra yeniden bakacağı an; işi kalmamış cevap bağında boş.</summary>
    public DateTime? NextAttemptAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public CoreMediaOwner Owner() =>
        ResponseId is { } responseId ? CoreMediaOwner.Response(responseId) : CoreMediaOwner.Draft(FormId, UserId);

    public static IEnumerable<AnswerFileLink> ForResponse(FormResponse response, DateTime now) =>
        response.UserId is not { } userId
            ? []
            : response.Data
                .Where(item => item.Type == GuestUploadRules.FileQuestionType)
                .Select(item => Guid.TryParse(item.Answer, out var mediaId) ? mediaId : Guid.Empty)
                .Where(mediaId => mediaId != Guid.Empty)
                .Distinct()
                .Select(mediaId => new AnswerFileLink
                {
                    MediaId = mediaId,
                    FormId = response.FormId,
                    UserId = userId,
                    ResponseId = response.Id,
                    State = AnswerFileLinkState.Linking,
                    NextAttemptAt = now,
                    CreatedAt = now
                });
}

public enum AnswerFileLinkState
{
    Linking = 0,
    Linked = 1,
    Unlinking = 2
}
