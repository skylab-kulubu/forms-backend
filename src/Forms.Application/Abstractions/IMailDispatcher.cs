using Skylab.Forms.Application.Contracts.Mail;

namespace Skylab.Forms.Application.Abstractions;

/// <summary>Gönderilecek mailin kalıcı kuyruğu; mail kuyruğa girdikten sonra bir yeniden başlatmada kaybolmaz.</summary>
public interface IMailDispatcher
{
    /// <returns>Kuyruk yazılamadıysa false; çağıranın işi bundan etkilenmez.</returns>
    Task<bool> EnqueueAsync(EmailMessage message, CancellationToken ct = default);
}
