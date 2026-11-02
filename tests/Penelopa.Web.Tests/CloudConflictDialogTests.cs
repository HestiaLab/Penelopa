using Bunit;
using Penelopa.Web.Services;
using Penelopa.Web.Shared;
using Xunit;

namespace Penelopa.Web.Tests;

public class CloudConflictDialogTests : BunitContext
{
    [Fact]
    public void ThreeActions_InvokeTheirCallbacks()
    {
        var reload = 0;
        var copy = 0;
        var export = 0;
        var cut = Render<CloudConflictDialog>(parameters => parameters
            .Add(component => component.Remote, new CloudDocumentSnapshot(
                """{"Version":3}""",
                9,
                "\"remote\""))
            .Add(component => component.OnReloadRemote, () => reload++)
            .Add(component => component.OnSaveAsCopy, () => copy++)
            .Add(component => component.OnExportLocal, () => export++));

        cut.Find("[data-testid=cloud-conflict-reload]").Click();
        cut.Find("[data-testid=cloud-conflict-copy]").Click();
        cut.Find("[data-testid=cloud-conflict-export]").Click();

        Assert.Equal(1, reload);
        Assert.Equal(1, copy);
        Assert.Equal(1, export);
        Assert.Contains("revision 9", cut.Markup);
    }
}
