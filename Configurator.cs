using Reloaded.Mod.Interfaces;

namespace p4g64.mouselook;

public class Configurator : IConfiguratorV3
{
    private string? _modDirectory;
    private string? _configDirectory;
    private Config? _config;

    public void SetModDirectory(string modDirectory) => _modDirectory = modDirectory;

    public void SetConfigDirectory(string configDirectory) => _configDirectory = configDirectory;

    public void SetContext(in ConfiguratorContext context) { }

    public IConfigurable[] GetConfigurations()
    {
        string dir = _configDirectory ?? _modDirectory ?? AppContext.BaseDirectory;
        _config = Config.Load(dir);
        return new IConfigurable[] { _config };
    }

    public bool TryRunCustomConfiguration() => false;

    public void Migrate(string oldDirectory, string newDirectory) { }
}
