using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Terminal.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Terminal;

/// <summary>
/// Jellyfin Terminal plugin entry point.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Plugin GUID. Stable identifier for the plugin.
    /// </summary>
    public static readonly Guid PluginGuid = Guid.Parse("a5e4d291-7643-41bb-b892-91e84fc5711b");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Terminal";

    /// <inheritdoc />
    public override Guid Id => PluginGuid;

    /// <inheritdoc />
    public override string Description =>
        "Terminal web interactiva para administración del servidor Jellyfin.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "terminal",
            DisplayName = "Terminal",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
            MenuSection = "server",
            MenuIcon = "terminal",
            EnableInMainMenu = true
        };
    }
}