using System.Reflection;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PitchMeetings.Matching;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.PitchMeetings.Api;

[ApiController]
[Route("PitchMeetings")]
public class PitchMeetingsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly PitchIndex _index;
    private readonly ILibraryManager _library;

    public PitchMeetingsController(PitchIndex index, ILibraryManager library)
    {
        _index = index;
        _library = library;
    }

    // ------------------------------------------------------------------ client assets (anonymous: loaded before login)

    [HttpGet("ClientScript")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    public ActionResult GetClientScript()
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return NotFound();
        }

        var cfg = plugin.Configuration;
        var clientConfig = new
        {
            cfg.Enabled,
            cfg.ShowOnTargetPage,
            cfg.ShowOnEpisodePage,
            Version = plugin.CacheKey
        };
        var script = "window.PitchMeetingsConfig = " + JsonSerializer.Serialize(clientConfig, JsonOptions) + ";\n"
            + ReadResource("Web.pitchmeetings.js");
        Response.Headers.CacheControl = "public, max-age=86400";
        return Content(script, "application/javascript; charset=utf-8");
    }

    [HttpGet("ClientStyle")]
    [AllowAnonymous]
    [Produces("text/css")]
    public ActionResult GetClientStyle()
    {
        Response.Headers.CacheControl = "public, max-age=86400";
        return Content(ReadResource("Web.pitchmeetings.css"), "text/css; charset=utf-8");
    }

    // ------------------------------------------------------------------ links for a details page

    /// <summary>
    /// For a movie/series/season: the Pitch Meetings about it. For a Pitch Meeting: what it is about.
    /// Only ids; the client loads the items through the normal (permission-checked) items API.
    /// </summary>
    [HttpGet("Links/{itemId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetLinks([FromRoute] Guid itemId)
    {
        var snap = _index.Current;
        if (snap.ByEpisode.TryGetValue(itemId, out var link))
        {
            var targets = link.SeasonId is Guid sid ? new List<Guid> { sid } : link.TargetIds.ToList();
            return Ok(new { kind = "pitch", targets, pitchMeetings = Array.Empty<Guid>() });
        }

        if (snap.Episodes.Contains(itemId))
        {
            return Ok(new { kind = "pitch", targets = Array.Empty<Guid>(), pitchMeetings = Array.Empty<Guid>() });
        }

        snap.ByTarget.TryGetValue(itemId, out var meetings);
        return Ok(new { kind = "target", targets = Array.Empty<Guid>(), pitchMeetings = (IEnumerable<Guid>?)meetings ?? Array.Empty<Guid>() });
    }

    // ------------------------------------------------------------------ admin (configuration page)

    [HttpGet("Status")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult GetStatus()
    {
        var snap = _index.Current;
        var rows = new List<object>();
        foreach (var id in snap.Episodes)
        {
            var ep = _library.GetItemById(id);
            if (ep is null)
            {
                continue;
            }

            snap.ByEpisode.TryGetValue(id, out var link);
            var targets = new List<object>();
            if (link is not null)
            {
                var ids = link.SeasonId is Guid sid ? new[] { sid } : link.TargetIds;
                foreach (var t in ids)
                {
                    var item = _library.GetItemById(t);
                    if (item is not null)
                    {
                        targets.Add(new { id = item.Id, label = Label(item) });
                    }
                }
            }

            rows.Add(new
            {
                id = ep.Id,
                name = ep.Name,
                season = ep.ParentIndexNumber,
                episode = ep.IndexNumber,
                date = ep.PremiereDate,
                manual = link?.Manual ?? false,
                targets
            });
        }

        rows.Reverse(); // newest first
        return Ok(new { sourceSeriesFound = snap.SourceSeriesIds.Count, total = snap.Episodes.Count, linked = snap.ByEpisode.Count, rows });
    }

    /// <summary>Everything a Pitch Meeting can be linked to by hand: movies, series and their seasons.</summary>
    [HttpGet("Candidates")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult GetCandidates()
    {
        var snap = _index.Current;
        var items = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Season },
            Recursive = true,
            IsVirtualItem = false
        });
        var list = items
            .Where(i => !snap.SourceSeriesIds.Contains(i.Id) && !(i is Season s && snap.SourceSeriesIds.Contains(s.SeriesId)))
            .Select(i => new { id = i.Id, label = Label(i) })
            .OrderBy(i => i.label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return Ok(list);
    }

    [HttpPost("Rebuild")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult Rebuild()
    {
        _index.Invalidate(true);
        var snap = _index.Current;
        return Ok(new { total = snap.Episodes.Count, linked = snap.ByEpisode.Count });
    }

    private static string Label(BaseItem item)
    {
        var year = item.ProductionYear is int y ? $" ({y})" : string.Empty;
        return item switch
        {
            Season s => $"{s.SeriesName} – {s.Name}",
            MediaBrowser.Controller.Entities.TV.Series => $"{item.Name}{year} · Serie",
            _ => item.Name + year
        };
    }

    private static string ReadResource(string relativeName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = $"{typeof(Plugin).Namespace}.{relativeName}";
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
