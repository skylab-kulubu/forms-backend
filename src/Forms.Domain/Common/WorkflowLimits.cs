namespace Skylab.Forms.Domain.Common;

public static class WorkflowLimits
{
    /// <summary>
    /// Bir rotada izin verilen en fazla form sayisi. Yalnizca publish dogrulamasinda
    /// kullanilir: calisma zamanindaki adim sirasi bu sayiyla sinirli degildir.
    /// </summary>
    public const int MaxDepth = 3;

    public const int MaxNodeKeyLength = 64;
}
