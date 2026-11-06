using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PitchMeetings.Configuration;

/// <summary>
/// A manual correction for one Pitch Meeting episode.
/// </summary>
public class LinkOverride
{
    /// <summary>Gets or sets the Pitch Meeting episode.</summary>
    public Guid EpisodeId { get; set; }

    /// <summary>Gets or sets the movie, series or season it belongs to; <see cref="Guid.Empty"/> = no link at all.</summary>
    public Guid TargetId { get; set; }
}

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets a value indicating whether the links are shown in the web client.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the names of the series that hold the Pitch Meeting videos (one per line or comma separated).</summary>
    public string SourceSeriesNames { get; set; } = "Pitch Meetings";

    /// <summary>Gets or sets a value indicating whether Pitch Meetings about TV shows ("… Season 5") are linked to series.</summary>
    public bool MatchSeries { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the movie/series page shows the section.</summary>
    public bool ShowOnTargetPage { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the Pitch Meeting episode page shows the section.</summary>
    public bool ShowOnEpisodePage { get; set; } = true;

    /// <summary>Gets or sets the manual corrections.</summary>
    public List<LinkOverride> Overrides { get; set; } = new();

    /// <summary>Gets or sets a value that changes on every save (cache busting for the client script).</summary>
    public long Revision { get; set; }
}
