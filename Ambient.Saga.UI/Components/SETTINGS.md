# Settings Panel Customization

## Overview

The pause menu includes a **Settings** button that opens a customizable settings panel. By default, a `DefaultSettingsPanel` is provided with common game settings, but you can easily replace it with your own implementation.

## Default Settings Panel

The `DefaultSettingsPanel` provides:
- **Audio** - Master, Music, and SFX volume sliders (placeholders)
- **Graphics** - Quality presets, Fullscreen, V-Sync toggles (placeholders)
- **Controls** - Display of current keyboard/mouse bindings (read-only)

> ?? **Note:** The default panel is a UI template only. The settings are not functional and don't affect the game. You should implement your own `ISettingsPanel` to wire up actual game settings.

## Creating a Custom Settings Panel

### Step 1: Implement `ISettingsPanel`

```csharp
using Ambient.Saga.UI.Components.Panels;

public class MyGameSettings : ISettingsPanel
{
    // Your actual game settings
    private float _masterVolume = 0.8f;
    private bool _fullscreen = true;

    public void Render(ref bool isOpen)
    {
        if (!isOpen) return;

        ImGui.SetNextWindowPos(...);
        ImGui.SetNextWindowSize(...);

        if (ImGui.Begin("My Game Settings", ref isOpen))
        {
            // Your settings UI
            ImGui.SliderFloat("Master Volume", ref _masterVolume, 0f, 1f);
            
            // Apply changes when slider moves
            if (ImGui.IsItemEdited())
            {
                AudioEngine.SetMasterVolume(_masterVolume);
            }

            if (ImGui.Checkbox("Fullscreen", ref _fullscreen))
            {
                GraphicsEngine.SetFullscreen(_fullscreen);
            }

            // ...more settings...
        }
        ImGui.End();
    }
}
```

### Step 2: Pass to ModalManager

```csharp
// In your DI setup (ServiceProviderSetup.cs or similar):
services.AddSingleton<ISettingsPanel, MyGameSettings>();

services.AddSingleton(sp =>
{
    var selector = sp.GetRequiredService<ImGuiArchetypeSelector>();
    var mediator = sp.GetRequiredService<IMediator>();
    var worldContentGenerator = sp.GetRequiredService<IWorldContentGenerator>();
    var settingsPanel = sp.GetRequiredService<ISettingsPanel>();
    
    var modalManager = new ModalManager(
        selector, 
        mediator, 
        worldContentGenerator, 
        settingsPanel  // ? Pass your custom settings
    );
    
    selector.SetModalManager(modalManager);
    return modalManager;
});
```

## Design Patterns

### Pattern 1: Simple In-Memory Settings

```csharp
public class SimpleSettings : ISettingsPanel
{
    private float _volume = 0.8f;

    public void Render(ref bool isOpen)
    {
        if (!isOpen) return;

        if (ImGui.Begin("Settings", ref isOpen))
        {
            if (ImGui.SliderFloat("Volume", ref _volume, 0f, 1f))
            {
                // Apply immediately
                MyAudioEngine.SetVolume(_volume);
            }
        }
        ImGui.End();
    }
}
```

### Pattern 2: Persistent Settings with Save/Cancel

```csharp
public class PersistentSettings : ISettingsPanel
{
    private readonly ISettingsStorage _storage;
    private float _volume;
    private float _volumeBackup;

    public PersistentSettings(ISettingsStorage storage)
    {
        _storage = storage;
        _volume = storage.GetFloat("Volume", 0.8f);
    }

    public void Render(ref bool isOpen)
    {
        if (!isOpen)
        {
            // Window just opened - backup current values
            if (_volumeBackup == 0)
            {
                _volumeBackup = _volume;
            }
            return;
        }

        if (ImGui.Begin("Settings", ref isOpen))
        {
            ImGui.SliderFloat("Volume", ref _volume, 0f, 1f);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Save"))
            {
                _storage.SetFloat("Volume", _volume);
                _storage.Save();
                MyAudioEngine.SetVolume(_volume);
                isOpen = false;
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel"))
            {
                _volume = _volumeBackup; // Restore
                isOpen = false;
            }
        }

        if (!isOpen)
        {
            // Window just closed - clear backup
            _volumeBackup = 0;
        }

        ImGui.End();
    }
}
```

### Pattern 3: Tabbed Settings (Advanced)

```csharp
public class TabbedSettings : ISettingsPanel
{
    private int _selectedTab = 0;

    public void Render(ref bool isOpen)
    {
        if (!isOpen) return;

        if (ImGui.Begin("Settings", ref isOpen))
        {
            if (ImGui.BeginTabBar("SettingsTabs"))
            {
                if (ImGui.BeginTabItem("Audio"))
                {
                    RenderAudioSettings();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Graphics"))
                {
                    RenderGraphicsSettings();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Gameplay"))
                {
                    RenderGameplaySettings();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
        ImGui.End();
    }

    private void RenderAudioSettings()
    {
        // Audio settings UI...
    }

    private void RenderGraphicsSettings()
    {
        // Graphics settings UI...
    }

    private void RenderGameplaySettings()
    {
        // Gameplay settings UI...
    }
}
```

## Common Settings Categories

Here are typical settings you might want to include:

### Audio
- Master Volume
- Music Volume
- Sound Effects Volume
- Voice Volume
- Mute All

### Graphics
- Resolution
- Fullscreen / Windowed
- V-Sync
- Quality Preset (Low/Medium/High/Ultra)
- Anti-Aliasing
- Shadow Quality
- Texture Quality
- View Distance

### Gameplay
- Difficulty
- Auto-Save
- Tutorial Hints
- UI Scale
- Language

### Controls
- Key Bindings (if you support remapping)
- Mouse Sensitivity
- Invert Mouse Y
- Gamepad Vibration

## Integration with Game Systems

### Example: Wiring to Audio Engine

```csharp
public class GameSettings : ISettingsPanel
{
    private readonly IAudioEngine _audio;
    private float _masterVolume;

    public GameSettings(IAudioEngine audio)
    {
        _audio = audio;
        _masterVolume = _audio.GetMasterVolume();
    }

    public void Render(ref bool isOpen)
    {
        if (!isOpen) return;

        if (ImGui.Begin("Settings", ref isOpen))
        {
            if (ImGui.SliderFloat("Master Volume", ref _masterVolume, 0f, 1f))
            {
                _audio.SetMasterVolume(_masterVolume);
            }
        }
        ImGui.End();
    }
}
```

### Example: Wiring to Graphics Engine

```csharp
public class GameSettings : ISettingsPanel
{
    private readonly IGraphicsEngine _graphics;
    private bool _fullscreen;
    private bool _vsync;

    public GameSettings(IGraphicsEngine graphics)
    {
        _graphics = graphics;
        _fullscreen = _graphics.IsFullscreen;
        _vsync = _graphics.IsVSyncEnabled;
    }

    public void Render(ref bool isOpen)
    {
        if (!isOpen) return;

        if (ImGui.Begin("Settings", ref isOpen))
        {
            if (ImGui.Checkbox("Fullscreen", ref _fullscreen))
            {
                _graphics.SetFullscreen(_fullscreen);
            }

            if (ImGui.Checkbox("V-Sync", ref _vsync))
            {
                _graphics.SetVSync(_vsync);
            }
        }
        ImGui.End();
    }
}
```

## Default Panel as Reference

The `DefaultSettingsPanel` source code serves as a complete reference implementation showing:
- ImGui layout and styling
- Collapsible sections (CollapsingHeader)
- Sliders, checkboxes, and combos
- Proper window sizing and centering
- Close button

You can copy and modify it as a starting point for your own settings panel.

## FAQ

**Q: Can I use the default panel and just add my own sections?**  
A: Yes! Inherit from `DefaultSettingsPanel` and override `Render()` to add your own sections while keeping the defaults.

**Q: How do I save settings to disk?**  
A: Implement an `ISettingsStorage` service that reads/writes JSON, XML, or binary config files. Call `storage.Save()` when the user clicks Save/Apply.

**Q: Can I open the settings from my own UI?**  
A: Yes! Just set `modalManager.ShowSettings = true` from anywhere in your code.

**Q: How do I add keyboard shortcuts to open settings?**  
A: Create a custom `IInputHandler` that checks for your hotkey (e.g., F5) and sets `ShowSettings = true`.

**Q: The default settings don't actually work. Is this a bug?**  
A: No, the default panel is intentionally non-functional. It's a UI template for you to customize. Wire it up to your actual game systems as shown in the examples above.

## See Also

- `ISettingsPanel.cs` - Interface definition
- `DefaultSettingsPanel.cs` - Reference implementation
- `EXTENSIBILITY.md` - Input handler customization
- `PAUSE_MENU.md` - Pause menu integration guide
