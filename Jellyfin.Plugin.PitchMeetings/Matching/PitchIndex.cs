using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PitchMeetings.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PitchMeetings.Matching;

/// <summary>One Pitch Meeting and what it belongs to.</summary>
public sealed class PitchLink
{
    public required Guid EpisodeId { get; init; }

    /// <summary>Gets the movies/series it is about (several when the same movie exists twice, e.g. in two libraries).</summary>
    public required IReadOnlyList<Guid> TargetIds { get; init; }

    /// <summary>Gets the season (for "… Season 5"), if the library has it.</summary>
    public Guid? SeasonId { get; init; }

    public bool Manual { get; init; }
}

/// <summary>
/// The automatic links between the Pitch Meeting episodes and the movies/series in the library.
/// Built lazily and rebuilt after library changes, so new videos and movies link themselves.
/// </summary>
public sealed class PitchIndex : IDisposable
{
    private static readonly TimeSpan MinRebuildInterval = TimeSpan.FromSeconds(10);

    private readonly ILibraryManager _library;
    private readonly ILogger<PitchIndex> _logger;
    private readonly object _lock = new();
    private Snapshot? _snapshot;
    private volatile bool _dirty = true;
    private DateTime _builtAt;

    public PitchIndex(ILibraryManager library, ILogger<PitchIndex> logger)
    {
        _library = library;
        _logger = logger;
        _library.ItemAdded += OnLibraryChanged;
        _library.ItemUpdated += OnLibraryChanged;
        _library.ItemRemoved += OnLibraryChanged;
        Plugin.ConfigurationSaved += OnConfigSaved;
    }

    public sealed class Snapshot
    {
        public Dictionary<Guid, PitchLink> ByEpisode { get; } = new();

        /// <summary>Gets movie / series / season id → Pitch Meeting episode ids.</summary>
        public Dictionary<Guid, List<Guid>> ByTarget { get; } = new();

        public HashSet<Guid> SourceSeriesIds { get; } = new();

        public List<Guid> Episodes { get; } = new();
    }

    public Snapshot Current
    {
        get
        {
            lock (_lock)
            {
                var forced = _snapshot is null;
                if (forced || (_dirty && DateTime.UtcNow - _builtAt >= MinRebuildInterval))
                {
                    _dirty = false;
                    _builtAt = DateTime.UtcNow;
                    try
                    {
                        _snapshot = Build();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Pitch Meetings: building the links failed");
                        _snapshot ??= new Snapshot();
                    }
                }

                return _snapshot!;
            }
        }
    }

    public void Invalidate(bool now = false)
    {
        _dirty = true;
        if (now)
        {
            lock (_lock)
            {
                _builtAt = DateTime.MinValue;
            }
        }
    }

    public void Dispose()
    {
        _library.ItemAdded -= OnLibraryChanged;
        _library.ItemUpdated -= OnLibraryChanged;
        _library.ItemRemoved -= OnLibraryChanged;
        Plugin.ConfigurationSaved -= OnConfigSaved;
    }

    private void OnConfigSaved(object? sender, EventArgs e) => Invalidate(true);

    private void OnLibraryChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is Movie or Series or Season or Episode)
        {
            _dirty = true;
        }
    }

    // ------------------------------------------------------------------ building

    private sealed record Target(BaseItem Item, DateTime? Released);

    private Snapshot Build()
    {
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var snap = new Snapshot();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var sourceNames = new HashSet<string>(
            (cfg.SourceSeriesNames ?? string.Empty)
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .SelectMany(TitleParser.Keys));

        var allSeries = Query(BaseItemKind.Series);
        foreach (var s in allSeries)
        {
            if (TitleParser.Keys(s.Name).Any(sourceNames.Contains))
            {
                snap.SourceSeriesIds.Add(s.Id);
            }
        }

        if (snap.SourceSeriesIds.Count == 0)
        {
            _logger.LogInformation("Pitch Meetings: no series named '{Names}' found", cfg.SourceSeriesNames);
            return snap;
        }

        var movies = new Dictionary<string, List<Target>>(StringComparer.Ordinal);
        foreach (var m in Query(BaseItemKind.Movie))
        {
            AddTarget(movies, m);
        }

        var series = new Dictionary<string, List<Target>>(StringComparer.Ordinal);
        if (cfg.MatchSeries)
        {
            foreach (var s in allSeries.Where(s => !snap.SourceSeriesIds.Contains(s.Id)))
            {
                AddTarget(series, s);
            }
        }

        var episodes = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            AncestorIds = snap.SourceSeriesIds.ToArray(),
            Recursive = true,
            IsVirtualItem = false
        });

        var overrides = new Dictionary<Guid, Guid>();
        foreach (var o in cfg.Overrides ?? new List<LinkOverride>())
        {
            overrides[o.EpisodeId] = o.TargetId;
        }

        var manual = 0;
        foreach (var ep in episodes.OrderBy(e => e.PremiereDate ?? e.DateCreated))
        {
            snap.Episodes.Add(ep.Id);
            PitchLink? link;
            if (overrides.TryGetValue(ep.Id, out var forced))
            {
                link = ManualLink(ep.Id, forced);
                manual++;
            }
            else
            {
                link = AutoLink(ep, movies, series);
            }

            if (link is null)
            {
                continue;
            }

            snap.ByEpisode[ep.Id] = link;
            foreach (var t in link.TargetIds)
            {
                AddReverse(snap, t, ep.Id);
            }

            if (link.SeasonId is Guid sid)
            {
                AddReverse(snap, sid, ep.Id);
            }
        }

        _logger.LogInformation(
            "Pitch Meetings: linked {Linked} of {Total} videos ({Manual} manual) in {Ms} ms",
            snap.ByEpisode.Count,
            snap.Episodes.Count,
            manual,
            sw.ElapsedMilliseconds);
        return snap;
    }

    private IReadOnlyList<BaseItem> Query(BaseItemKind kind) => _library.GetItemList(new InternalItemsQuery
    {
        IncludeItemTypes = new[] { kind },
        Recursive = true,
        IsVirtualItem = false
    });

    private static void AddTarget(Dictionary<string, List<Target>> index, BaseItem item)
    {
        var released = item.PremiereDate
            ?? (item.ProductionYear is int y && y > 1800 ? new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null);
        var t = new Target(item, released);
        foreach (var key in TitleParser.Keys(item.Name).Concat(TitleParser.Keys(item.OriginalTitle)).Distinct())
        {
            if (!index.TryGetValue(key, out var list))
            {
                index[key] = list = new List<Target>();
            }

            list.Add(t);
        }
    }

    private static void AddReverse(Snapshot snap, Guid target, Guid episode)
    {
        if (!snap.ByTarget.TryGetValue(target, out var list))
        {
            snap.ByTarget[target] = list = new List<Guid>();
        }

        if (!list.Contains(episode))
        {
            list.Add(episode);
        }
    }

    private PitchLink? ManualLink(Guid episodeId, Guid targetId)
    {
        if (targetId == Guid.Empty)
        {
            return null; // explicitly "no link"
        }

        var item = _library.GetItemById(targetId);
        return item switch
        {
            Season season => new PitchLink
            {
                EpisodeId = episodeId,
                TargetIds = new[] { season.SeriesId },
                SeasonId = season.Id,
                Manual = true
            },
            null => null,
            _ => new PitchLink { EpisodeId = episodeId, TargetIds = new[] { item.Id }, Manual = true }
        };
    }

    private PitchLink? AutoLink(BaseItem ep, Dictionary<string, List<Target>> movies, Dictionary<string, List<Target>> series)
    {
        var parsed = TitleParser.Parse(ep.Name);
        if (parsed.Keys.Count == 0)
        {
            return null;
        }

        var uploaded = ep.PremiereDate ?? ep.DateCreated;
        var pools = parsed.Season is null ? new[] { movies, series } : new[] { series };
        foreach (var key in parsed.Keys)
        {
            foreach (var pool in pools)
            {
                if (!pool.TryGetValue(key, out var candidates))
                {
                    continue;
                }

                var chosen = Choose(candidates, parsed.Year, uploaded);
                if (chosen.Count == 0)
                {
                    continue;
                }

                Guid? seasonId = null;
                if (parsed.Season is int sn && chosen[0].Item is Series show)
                {
                    seasonId = FindSeason(show, sn);
                }

                return new PitchLink
                {
                    EpisodeId = ep.Id,
                    TargetIds = chosen.Select(c => c.Item.Id).ToList(),
                    SeasonId = seasonId
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Picks among same-titled movies (The Mummy 1999/2017, Dune 1984/2021): the year in the title if
    /// there is one, otherwise the newest release that was out when the video was uploaded.
    /// Returns every copy of the winner (same title and year), e.g. 4K and HD libraries.
    /// </summary>
    private static List<Target> Choose(List<Target> candidates, int? year, DateTime uploaded)
    {
        var distinct = candidates.DistinctBy(c => c.Item.Id).ToList();
        Target? best;
        if (year is int y)
        {
            best = distinct.FirstOrDefault(c => c.Item.ProductionYear == y)
                ?? distinct.FirstOrDefault(c => c.Item.ProductionYear is int py && Math.Abs(py - y) <= 1);
        }
        else if (distinct.Count == 1)
        {
            best = distinct[0];
        }
        else
        {
            var cutoff = uploaded.AddDays(60);
            best = distinct.Where(c => c.Released is DateTime r && r <= cutoff).MaxBy(c => c.Released)
                ?? distinct.Where(c => c.Released is not null).MinBy(c => c.Released)
                ?? distinct[0];
        }

        if (best is null)
        {
            return new List<Target>();
        }

        var bestType = best.Item.GetType();
        return distinct
            .Where(c => c.Item.GetType() == bestType && c.Item.ProductionYear == best.Item.ProductionYear)
            .ToList();
    }

    private Guid? FindSeason(Series show, int number)
    {
        var seasons = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Season },
            ParentId = show.Id,
            IsVirtualItem = false
        });
        return seasons.FirstOrDefault(s => s.IndexNumber == number)?.Id;
    }
}
