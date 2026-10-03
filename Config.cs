using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Reloaded.Mod.Interfaces;

namespace p4g64.mouselook;

public class Config : IUpdatableConfigurable
{
    [Browsable(false)]
    [JsonIgnore]
    public string ConfigName => "Persona 4 Golden Modern Mouse-Look";

    [Browsable(false)]
    [JsonIgnore]
    public Action Save => SaveConfig;

    [Browsable(false)]
    public event Action<IUpdatableConfigurable>? ConfigurationUpdated;

    [Browsable(false)]
    [JsonIgnore]
    private string _configPath = Path.Combine(AppContext.BaseDirectory, "Config.json");

    // --- Sensitivity Options ---

    [Category("1. Sensitivity")]
    [DisplayName("Horizontal Sensitivity (X)")]
    [Description("Horizontal mouse look sensitivity. (Default: 10.0)")]
    public float SensitivityX { get; set; } = 10.0f;

    [Category("1. Sensitivity")]
    [DisplayName("Vertical Sensitivity (Y)")]
    [Description("Vertical mouse look sensitivity. (Default: 10.0)")]
    public float SensitivityY { get; set; } = 10.0f;

    // --- Camera Options ---

    [Category("2. Camera Settings")]
    [DisplayName("Enable Vertical Look (Pitch)")]
    [Description("Allow looking up and down with mouse. (Default: False)")]
    public bool EnablePitch { get; set; } = false;

    [Category("2. Camera Settings")]
    [DisplayName("Invert Horizontal Look (X)")]
    [Description("Invert horizontal mouse look direction. (Default: False)")]
    public bool InvertX { get; set; } = false;

    [Category("2. Camera Settings")]
    [DisplayName("Invert Vertical Look (Y)")]
    [Description("Invert vertical mouse look direction. (Default: False)")]
    public bool InvertY { get; set; } = false;

    // --- Controls & Hotkeys ---

    [Category("3. Controls & Hotkeys")]
    [DisplayName("Hold Alt to Free Cursor")]
    [Description("Genshin Impact style: Hold Alt key to temporarily release and show cursor. (Default: True)")]
    public bool HoldAltToUnlock { get; set; } = true;

    [Category("3. Controls & Hotkeys")]
    [DisplayName("Middle Mouse Recenter Camera")]
    [Description("Click Middle Mouse Button (scroll wheel click) to reset pitch and snap camera behind player. (Default: True)")]
    public bool MiddleMouseResetCamera { get; set; } = true;

    [Category("3. Controls & Hotkeys")]
    [DisplayName("Toggle Look Hotkey")]
    [Description("Virtual key code to toggle mouse-look on/off. (Default: 192 / 0xC0 for ~ Tilde key)")]
    public int ToggleKey { get; set; } = 0xC0; // 0xC0 = ~ (Tilde), 0x70 = F1

    [Category("3. Controls & Hotkeys")]
    [DisplayName("Sensitivity Up Hotkey")]
    [Description("In-game hotkey to increase sensitivity. (Default: 221 / 0xDD for ']' key, or '+' key)")]
    public int SensitivityUpKey { get; set; } = 0xDD; // ']' (VK_OEM_6)

    [Category("3. Controls & Hotkeys")]
    [DisplayName("Sensitivity Down Hotkey")]
    [Description("In-game hotkey to decrease sensitivity. (Default: 219 / 0xDB for '[' key, or '-' key)")]
    public int SensitivityDownKey { get; set; } = 0xDB; // '[' (VK_OEM_4)

    // --- Auto-Unlock Triggers ---

    [Category("4. Auto Cursor Unlock")]
    [DisplayName("Auto Unlock in Menus")]
    [Description("Automatically unlock cursor in Camp Menu, Shops, Town Map, Config, and Save/Load screens. (Default: True)")]
    public bool AutoUnlockInMenu { get; set; } = true;

    [Category("4. Auto Cursor Unlock")]
    [DisplayName("Auto Unlock in Battles")]
    [Description("Automatically unlock cursor during turn-based battles. (Default: True)")]
    public bool AutoUnlockInBattle { get; set; } = true;

    [Category("4. Auto Cursor Unlock")]
    [DisplayName("Auto Unlock in Dialogues")]
    [Description("Automatically unlock cursor during interactive story dialogues and choices. (Dungeon walking banter will NOT interrupt mouse look). (Default: True)")]
    public bool AutoUnlockInDialogue { get; set; } = true;

    [Category("4. Auto Cursor Unlock")]
    [DisplayName("Auto Unlock in Cutscenes")]
    [Description("Automatically unlock cursor during cutscenes and anime movies. (Default: True)")]
    public bool AutoUnlockInCutscene { get; set; } = true;

    // --- Persistence Methods ---

    public static Config Load(string? directory = null)
    {
        string path = "";
        if (!string.IsNullOrEmpty(directory))
        {
            path = Path.Combine(directory, "Config.json");
        }
        else
        {
            string userModsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "User", "Mods", "p4g64.mouselook", "Config.json");
            try
            {
                userModsPath = Path.GetFullPath(userModsPath);
                if (File.Exists(userModsPath))
                {
                    path = userModsPath;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, "Config.json");
            }
        }

        Config config;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                config = JsonSerializer.Deserialize<Config>(json) ?? new Config();
            }
            else
            {
                config = new Config();
            }
        }
        catch
        {
            config = new Config();
        }

        config._configPath = path;
        config.SaveConfig();
        return config;
    }

    public void SaveConfig()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch { }

        ConfigurationUpdated?.Invoke(this);
    }
}
