using Bigarr.Api.Features.Discover;

namespace Bigarr.Api.Tests.Discover;

public sealed class AvailabilityTests
{
    [Theory]
    [InlineData(true, true, AvailabilityState.Available)]
    [InlineData(true, false, AvailabilityState.Available)]
    [InlineData(false, true, AvailabilityState.Processing)]
    [InlineData(false, false, AvailabilityState.NotInLibrary)]
    public void A_film_version_is_available_with_a_file_and_processing_while_monitored(bool hasFile, bool monitored, AvailabilityState expected) =>
        Assert.Equal(expected, Availability.OfFilm(hasFile, monitored));

    [Fact]
    public void Every_aired_episode_with_a_file_is_available_even_when_later_ones_have_not_aired()
    {
        Assert.Equal(AvailabilityState.Available, Availability.OfEpisodes([Aired(file: true), Aired(file: true), Future(monitored: true)]));
    }

    [Fact]
    public void Some_aired_episodes_without_a_file_make_it_partial_monitored_or_not()
    {
        Assert.Equal(AvailabilityState.PartiallyAvailable, Availability.OfEpisodes([Aired(file: true), Aired(file: false)]));
        Assert.Equal(AvailabilityState.PartiallyAvailable, Availability.OfEpisodes([Aired(file: true), Aired(file: false, monitored: false)]));
    }

    [Fact]
    public void No_file_yet_is_processing_when_something_is_monitored_and_not_in_library_otherwise()
    {
        Assert.Equal(AvailabilityState.Processing, Availability.OfEpisodes([Aired(file: false), Future(monitored: true)]));
        Assert.Equal(AvailabilityState.Processing, Availability.OfEpisodes([Future(monitored: true)]));
        Assert.Equal(AvailabilityState.NotInLibrary, Availability.OfEpisodes([Aired(file: false, monitored: false)]));
        Assert.Equal(AvailabilityState.NotInLibrary, Availability.OfEpisodes([]));
    }

    [Fact]
    public void A_file_for_an_episode_that_has_not_aired_counts()
    {
        Assert.Equal(AvailabilityState.Available, Availability.OfEpisodes([new EpisodeSlot(Aired: false, HasFile: true, Monitored: true)]));
    }

    private static EpisodeSlot Aired(bool file, bool monitored = true) => new(Aired: true, HasFile: file, Monitored: monitored);

    private static EpisodeSlot Future(bool monitored) => new(Aired: false, HasFile: false, Monitored: monitored);
}
