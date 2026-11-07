using Bigarr.Api.Features.Migration;
using Bigarr.Api.Infrastructure.RadarrImport;

namespace Bigarr.Api.Tests.Migration;

/// <summary>A Radarr profile's qualities in Bigarr's terms: same ids, same order, same groups, same cutoff.</summary>
public sealed class RadarrProfileQualitiesTests
{
    private static readonly int[] Known = [0, 3, 4, 5, 6, 7, 9, 14, 15, 30];

    private static RadarrProfileItem Q(int id, string name, bool allowed) => new(null, null, new RadarrQualityDefinition(id, name, null, 0, null), [], allowed);

    private static RadarrProfileItem G(int id, string name, bool allowed, params RadarrProfileItem[] items) => new(id, name, null, [.. items], allowed);

    /// <summary>Radarr's usual HD profile: WEB-DL and WEBRip ranked together, cutoff on that group, no remux.</summary>
    private static RadarrProfile Films(int cutoff = 1002) => new(6, "Films", Cutoff: cutoff, UpgradeAllowed: true, Items:
    [
        Q(4, "HDTV-720p", true),
        G(1001, "WEB 720p", true, Q(14, "WEBRip-720p", true), Q(5, "WEBDL-720p", true)),
        Q(6, "Bluray-720p", true),
        Q(9, "HDTV-1080p", true),
        G(1002, "WEB 1080p", true, Q(15, "WEBRip-1080p", true), Q(3, "WEBDL-1080p", true)),
        Q(7, "Bluray-1080p", true),
        Q(30, "Remux-1080p", false),
        Q(999, "A quality Bigarr does not know", true),
    ]);

    [Fact]
    public void The_order_groups_allowed_flags_and_cutoff_carry_over()
    {
        var copy = RadarrProfileQualities.Translate(Films(), Known)!;

        Assert.Equal(1002, copy.Cutoff);
        Assert.True(copy.UpgradeAllowed);
        // A quality Radarr does not list keeps a rank, first and not allowed; one Bigarr does not know is left out.
        Assert.Equal([0], copy.Items[0].QualityIds);
        Assert.False(copy.Items[0].Allowed);
        Assert.Equal(Known.Order(), copy.Items.SelectMany(i => i.QualityIds).Order());
        var web = copy.Items.Single(i => i.GroupId == 1002);
        Assert.Equal("WEB 1080p", web.GroupName);
        Assert.Equal([15, 3], web.QualityIds);
        Assert.False(copy.Items.Single(i => i.QualityIds.SequenceEqual([30])).Allowed);
        Assert.Equal([4, 1001, 6, 9, 1002, 7], copy.Items.Where(i => i.Allowed).Select(i => i.GroupId ?? i.QualityIds[0]));
    }

    [Fact]
    public void A_cutoff_on_a_quality_or_a_quality_inside_a_group_resolves_to_its_entry()
    {
        Assert.Equal(7, RadarrProfileQualities.Translate(Films(cutoff: 7), Known)!.Cutoff);
        Assert.Equal(1002, RadarrProfileQualities.Translate(Films(cutoff: 3), Known)!.Cutoff);
    }

    [Fact]
    public void A_copy_that_would_not_make_a_valid_profile_is_refused()
    {
        // The cutoff is a quality that is not allowed, or nothing at all is listed.
        Assert.Null(RadarrProfileQualities.Translate(Films(cutoff: 30), Known));
        Assert.Null(RadarrProfileQualities.Translate(new RadarrProfile(1, "Old Radarr"), Known));
    }

    [Fact]
    public void A_group_id_out_of_range_gets_a_free_one_and_the_cutoff_follows()
    {
        var profile = new RadarrProfile(2, "Odd", Cutoff: 5, UpgradeAllowed: false, Items: [Q(7, "Bluray-1080p", true), G(5, "Odd group", true, Q(3, "WEBDL-1080p", true), Q(15, "WEBRip-1080p", true))]);

        var copy = RadarrProfileQualities.Translate(profile, Known)!;

        var group = copy.Items.Single(i => i.GroupName == "Odd group");
        Assert.True(group.GroupId >= 1000);
        Assert.Equal(group.GroupId, copy.Cutoff);
        Assert.False(copy.UpgradeAllowed);
    }
}
