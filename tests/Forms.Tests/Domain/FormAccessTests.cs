using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Tests.Domain;

public class FormAccessTests
{
    // Satır: eylem. Sütunlar: None, Viewer, Editor, Owner, rolü olmayan hizmet yöneticisi.
    [Theory]
    [InlineData(FormAction.ViewForm, false, true, true, true, true)]
    [InlineData(FormAction.ViewResponses, false, true, true, true, true)]
    [InlineData(FormAction.ViewShortLink, false, true, true, true, true)]
    [InlineData(FormAction.HandleResponses, false, true, true, true, false)]
    [InlineData(FormAction.OpenEditor, false, false, true, true, true)]
    [InlineData(FormAction.ManageShortLink, false, false, true, true, true)]
    [InlineData(FormAction.EditForm, false, false, true, true, false)]
    [InlineData(FormAction.VoidApplication, false, false, true, true, false)]
    [InlineData(FormAction.DeleteForm, false, false, false, true, false)]
    public void Each_role_may_do_what_the_matrix_says(FormAction action, bool none, bool viewer, bool editor, bool owner, bool admin)
    {
        Assert.Equal(
            new[] { none, viewer, editor, owner, admin },
            new[]
            {
                FormAccess.Allows(CollaboratorRole.None, false, action),
                FormAccess.Allows(CollaboratorRole.Viewer, false, action),
                FormAccess.Allows(CollaboratorRole.Editor, false, action),
                FormAccess.Allows(CollaboratorRole.Owner, false, action),
                FormAccess.Allows(CollaboratorRole.None, true, action)
            });
    }

    [Fact]
    public void A_role_the_enum_does_not_define_may_do_nothing() =>
        Assert.False(FormAccess.Allows((CollaboratorRole)99, false, FormAction.ViewResponses));
}
