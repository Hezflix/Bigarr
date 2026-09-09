using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Domain;

public sealed class GrabStateMachineTests
{
    [Fact]
    public void Target_links_mirror_the_active_state()
    {
        var grab = new Grab { ReleaseTitle = "x" };
        var link = grab.AddTarget(new MovieVersion { Name = "1080p", FolderName = "x" });
        Assert.True(link.Active);

        grab.TransitionTo(GrabState.Downloading);
        Assert.True(link.Active);

        grab.TransitionTo(GrabState.ImportFailed);
        Assert.False(link.Active);
        Assert.False(grab.State.IsTerminal());

        grab.TransitionTo(GrabState.Importing);
        Assert.True(link.Active);

        grab.TransitionTo(GrabState.Imported);
        Assert.False(link.Active);
    }

    [Theory]
    [InlineData(GrabState.Imported)]
    [InlineData(GrabState.DownloadFailed)]
    [InlineData(GrabState.Removed)]
    [InlineData(GrabState.Ignored)]
    public void Terminal_states_cannot_be_left(GrabState terminal)
    {
        var grab = new Grab { ReleaseTitle = "x" };
        grab.TransitionTo(terminal);

        Assert.Throws<InvalidOperationException>(() => grab.TransitionTo(GrabState.Grabbed));
    }
}
