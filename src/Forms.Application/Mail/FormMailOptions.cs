namespace Skylab.Forms.Application.Mail;

public class FormMailOptions
{
    public const string SectionName = "FormMail";

    public string FormCopyTemplateKey { get; set; } = "forms.form-copy";
    public string StatusChangedTemplateKey { get; set; } = "forms.status-changed";
    public string PendingReminderTemplateKey { get; set; } = "forms.pending-reminder";
    public string AttemptUpdateTemplateKey { get; set; } = "forms.attempt-update";

    public int ReminderThresholdHours { get; set; } = 36;
    public int ReminderScanIntervalMinutes { get; set; } = 30;
    public int GuestCopyDailyLimit { get; set; } = 3;
}
