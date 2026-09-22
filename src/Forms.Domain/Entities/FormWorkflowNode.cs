namespace Skylab.Forms.Domain.Entities;

public class FormWorkflowNode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkflowVersionId { get; set; }
    public FormWorkflowVersion WorkflowVersion { get; set; } = null!;

    /// <summary>
    /// Form'un navigation'i bilerek yok: Form uzerindeki global silinmislik filtresi
    /// zorunlu bir navigation uzerinden node satirini tamamen dusurebilirdi. Form,
    /// repository'de acik join ile ve gerektiginde IgnoreQueryFilters ile okunur.
    /// </summary>
    public Guid FormId { get; set; }

    /// <summary>
    /// Version kopyaları arasında sabit kalan tanıtıcı. Koşullar node'a Id yerine
    /// NodeKey ile referans verir; yeni draft üretmek referansları bozmaz.
    /// </summary>
    public string NodeKey { get; set; } = string.Empty;

    public bool IsStart { get; set; } = false;

    /// <summary>
    /// Editör tuvalindeki yer, istemci piksel biriminde. Grafın anlamına etkisi yok;
    /// doğrulama bakmaz, eski tanımlarda boş kalır ve istemci otomatik yerleştirir.
    /// </summary>
    public int? PositionX { get; set; }
    public int? PositionY { get; set; }
}
