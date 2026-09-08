using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Terminal.Configuration;

/// <summary>
/// Plugin configuration for Jellyfin Terminal.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the custom default shell executable path (optional).
    /// </summary>
    public string CustomShellPath { get; set; } = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
    }
}