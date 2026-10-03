namespace Skylab.Forms.Application.Mail;

public class FormMailOptions
{
    public const string SectionName = "FormMail";

    public string FormCopyTemplateId { get; set; } = string.Empty;
    public string StatusChangedTemplateId { get; set; } = string.Empty;
    public string PendingReminderTemplateId { get; set; } = string.Empty;
    public string AttemptUpdateTemplateId { get; set; } = string.Empty;

    public int ReminderThresholdHours { get; set; } = 36;
    public int ReminderScanIntervalMinutes { get; set; } = 30;
}
