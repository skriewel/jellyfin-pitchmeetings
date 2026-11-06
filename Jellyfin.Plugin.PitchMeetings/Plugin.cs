using Jellyfin.Plugin.PitchMeetings.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.PitchMeetings;

/// <summary>
/// Pitch Meetings: links the Pitch Meeting videos of a local YouTube series to the movies and shows they are about.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    /// <summary>Raised after the configuration was saved.</summary>
    public static event EventHandler? ConfigurationSaved;

    public override string Name => "Pitch Meetings";

    public override Guid Id => Guid.Parse("263fd402-979d-4384-8f9c-889c416552bf");

    public override string Description =>
        "Shows the matching Pitch Meeting video on movie and series pages, and links each Pitch Meeting back to its movie.";

    /// <summary>Gets a key that changes with the plugin version, the client assets and every save.</summary>
    public string CacheKey => $"{Version}-{AssetHash.Value}-{Configuration.Revision}";

    private static readonly Lazy<string> AssetHash = new(() =>
    {
        var asm = typeof(Plugin).Assembly;
        using var sha = System.Security.Cryptography.SHA256.Create();
        foreach (var name in new[] { "Web.pitchmeetings.js", "Web.pitchmeetings.css" })
        {
            using var s = asm.GetManifestResourceStream($"{typeof(Plugin).Namespace}.{name}");
            if (s is null)
            {
                continue;
            }

            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var bytes = ms.ToArray();
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!)[..10].ToLowerInvariant();
    });

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            }
        };
    }

    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        if (configuration is PluginConfiguration cfg)
        {
            cfg.Revision = DateTime.UtcNow.Ticks;
            cfg.Overrides ??= new();
        }

        base.UpdateConfiguration(configuration);
        ConfigurationSaved?.Invoke(this, EventArgs.Empty);
    }
}
