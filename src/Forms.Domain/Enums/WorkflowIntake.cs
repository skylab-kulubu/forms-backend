namespace Skylab.Forms.Domain.Enums;

/// <summary>Akışın kimi içeri aldığı. İnceleme her durumda sürer.</summary>
public enum WorkflowIntake
{
    Open = 0,

    /// <summary>Yeni başvuru başlatılamaz; devam edenler kaldıkları adımdan sürer.</summary>
    NewRunsClosed = 1,

    /// <summary>
    /// Devam eden başvurular da doldurulacak formda durur. Başvurulara yazılmaz; akış
    /// yeniden açılınca sürerler.
    /// </summary>
    Closed = 2
}
