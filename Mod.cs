using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;

namespace p4g64.mouselook;

public class Mod : IMod
{
    private ILogger? _logger;
    private IModLoader? _modLoader;
    private Config _config = null!;
    private CameraManager? _camera;
    private MouseController? _mouse;

    public void Start(IModLoaderV1 loader)
    {
        _modLoader = (IModLoader)loader;
        _logger = (ILogger)_modLoader.GetLogger();

        _logger.WriteLine("[P4G Mouse-Look] Initializing Modern Mouse-Look mod v1.3.2...", _logger.ColorGreen);

        string? configDir = null;
        try
        {
            if (_modLoader is IModLoaderV3 l3)
            {
                configDir = l3.GetModConfigDirectory("p4g64.mouselook");
            }
        }
        catch { }

        _config = Config.Load(configDir);
        _config.ConfigurationUpdated += configurable =>
        {
            _logger?.WriteLine($"[P4G Mouse-Look] Settings updated live: SensX={_config.SensitivityX:F1}, SensY={_config.SensitivityY:F1}, Pitch={_config.EnablePitch}", _logger.ColorLightBlue);
        };

        var hooksController = _modLoader.GetController<IReloadedHooks>();
        if (hooksController == null || !hooksController.TryGetTarget(out var hooks))
        {
            _logger.WriteLine("[P4G Mouse-Look] ERROR: Reloaded.Hooks not found! Please enable reloaded.sharedlib.hooks in Reloaded-II.", _logger.ColorRed);
            return;
        }

        _camera = new CameraManager(hooks, _config);
        _mouse = new MouseController(_camera, _config, _logger);

        _logger.WriteLine("[P4G Mouse-Look] Modern Mouse-Look v1.3.2 loaded successfully!", _logger.ColorGreen);
        _logger.WriteLine($"[P4G Mouse-Look] Sensitivity: X={_config.SensitivityX:F1}, Y={_config.SensitivityY:F1} (Press [ / ] or - / + in-game to adjust, or configure in Reloaded-II GUI)", _logger.ColorLightBlue);
        _logger.WriteLine("[P4G Mouse-Look] Controls: Mouse to Look (Yaw + Pitch), Middle Mouse Click to Recenter, Hold [Alt] to free cursor, [~] to toggle lock.", _logger.ColorYellow);
        _logger.WriteLine("[P4G Mouse-Look] Auto-unlock active for: Camp Menu, Shop, Battles, Dialogues & Choices (Dungeon walking banter ignored).", _logger.ColorLightBlue);
    }

    public void Suspend()
    {
        _mouse?.Stop();
    }

    public void Resume()
    {
    }

    public void Unload()
    {
        _mouse?.Stop();
    }

    public bool CanUnload() => false;
    public bool CanSuspend() => false;

    public Action? Disposing => () => { _mouse?.Stop(); };
}
