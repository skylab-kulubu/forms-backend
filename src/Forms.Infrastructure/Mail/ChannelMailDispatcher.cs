using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Mail;

public class ChannelMailDispatcher : IMailDispatcher
{
    private const int Capacity = 1000;

    private readonly Channel<EmailMessage> _channel;
    private readonly ILogger<ChannelMailDispatcher> _logger;

    public ChannelMailDispatcher(ILogger<ChannelMailDispatcher> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
    }

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public bool Enqueue(EmailMessage request)
    {
        if (_channel.Writer.TryWrite(request)) return true;

        _logger.LogError("Mail kuyruğu dolu, istek düşürüldü (template {TemplateKey})", request.TemplateKey);
        return false;
    }
}
