using Skylab.Forms.Application.Validators;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Tests.Application;

public class FormValidatorTests
{
    [Fact]
    public void Limit_messages_name_the_limits_the_checks_use()
    {
        var longTask = FormValidator.ValidateTiming(false, false, new FormTask { Content = new string('a', AttemptLimits.MaxTaskLength + 1) }, null);
        var longLimit = FormValidator.ValidateTiming(false, false, null, AttemptLimits.MaxTimeLimitMinutes + 1);

        Assert.Equal("Görev metni en fazla 50.000 karakter olabilir.", longTask.Message);
        Assert.Equal("Kişisel süre 1 dakika ile 30 gün arasında olmalıdır.", longLimit.Message);
    }
}
