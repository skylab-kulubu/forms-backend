namespace Skylab.Forms.Application.Mail;

public class FormMailOptions
{
    public const string SectionName = "FormMail";

    public string FormCopyTemplateId { get; set; } = "forms.form-copy";
    public string StatusChangedTemplateId { get; set; } = "forms.status-changed";
    public string PendingReminderTemplateId { get; set; } = "forms.pending-reminder";
    public string AttemptUpdateTemplateId { get; set; } = "forms.attempt-update";

    public int ReminderThresholdHours { get; set; } = 36;
    public int ReminderScanIntervalMinutes { get; set; } = 30;
}
