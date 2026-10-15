namespace Skylab.Forms.Domain.Enums;

public enum FormResponseStatus
{
    NonRestrict = 0,
    Pending = 1,
    Approved = 2,
    Declined = 3,
    Provisional = 4,
    Flagged = 5,

    /// <summary>
    /// İnceleyen akış adımında düzeltme istedi; aday aynı formu yeniden gönderir ve
    /// yerini o gönderim alır. Listede, analizde ve dışa aktarımda sayılmaz.
    /// </summary>
    Returned = 6
}
