namespace Skylab.Forms.Application.Mail;

public class FormMailOptions
{
    public const string SectionName = "FormMail";

    // Şablon anahtarları mail sağlayıcısındaki şablonları adlandırır; boş anahtar o maili kapatır.
    public string FormCopyTemplateKey { get; set; } = "";
    public string StatusChangedTemplateKey { get; set; } = "";
    public string PendingReminderTemplateKey { get; set; } = "";
    public string AttemptUpdateTemplateKey { get; set; } = "";

    public bool SendsAny =>
        FormCopyTemplateKey.Length > 0 || StatusChangedTemplateKey.Length > 0 || PendingReminderTemplateKey.Length > 0 || AttemptUpdateTemplateKey.Length > 0;

    public int ReminderThresholdHours { get; set; } = 36;
    public int ReminderScanIntervalMinutes { get; set; } = 30;
    public int GuestCopyDailyLimit { get; set; } = 3;
}
