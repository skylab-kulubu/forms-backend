using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Tests.Domain;

public class FormAttemptRulesTests
{
    [Theory]
    [InlineData(FormAttemptStatus.Started, false, false, true)]
    [InlineData(FormAttemptStatus.Provisional, true, true, true)]
    [InlineData(FormAttemptStatus.NoSubmission, false, false, true)]
    [InlineData(FormAttemptStatus.NoSubmission, true, false, false)]
    [InlineData(FormAttemptStatus.NoSubmission, false, true, false)]
    [InlineData(FormAttemptStatus.Submitted, false, false, false)]
    [InlineData(FormAttemptStatus.Opened, false, false, false)]
    public void Who_can_get_more_time(FormAttemptStatus status, bool workflowStep, bool closedByTeam, bool extendable) =>
        Assert.Equal(extendable, FormAttempt.CanBeExtended(status, workflowStep ? Guid.NewGuid() : null, closedByTeam));

    [Theory]
    [InlineData(-1, true, false)]
    [InlineData(-3, false, true)]
    public void The_grace_window_runs_two_minutes_past_the_deadline(int minutesFromDeadline, bool accepts, bool expired)
    {
        var now = DateTime.UtcNow;
        var attempt = new FormAttempt { Status = FormAttemptStatus.Started, DeadlineAt = now.AddMinutes(minutesFromDeadline) };

        Assert.Equal((accepts, expired), (attempt.AcceptsSubmissionAt(now), attempt.HasExpiredAt(now)));
    }
}
