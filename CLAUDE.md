# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Memoria is an open-source community rewrite of Final Fantasy IX's engine built on Unity. It's not a traditional mod that patches a binary - it's a complete engine rewrite where the original FF9 code has been decompiled, integrated with new Memoria systems at the source level, and recompiled.

## Build Commands

### Initial Setup (Required Before First Build)

Run this PowerShell script as Administrator to set up the development environment:
```powershell
.\SetupProjectEnvironment.ps1
```

This script:
- Installs .NET Framework 3.5 if missing
- Installs Windows SDK 8.1 (required for native code compilation)
- Copies Unity DLLs from your installed FF9 Steam game to the `References/` folder
- **Requires FF9 to be installed via Steam** (detects via registry at `HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 377840`)

### Building

Build using MSBuild:
```bash
# First build the custom MSBuild tasks
msbuild Memoria.MSBuild/Memoria.MSBuild.csproj /t:Build /p:Configuration=Release

# Then build the entire solution
msbuild Memoria.sln /t:Build /p:Configuration=Release
```

NuGet restore is required before building:
```bash
nuget restore Memoria.sln
```

Build output goes to the `Output/` folder. The main distributable is `Output/Memoria.Patcher.exe`.

## Architecture

### Project Structure

**Core Game Assembly:**
- **Assembly-CSharp/** (~1,390 files) - The main game assembly
  - `FF9/` - Original decompiled FF9 game code (~49 files)
  - `Memoria/` - All Memoria modifications and enhancements (~413 files)
  - `Global/` - Core Unity systems (AssetManager, Sound loaders)
  - `Assets/` - UI and asset management

**Infrastructure:**
- **Memoria.Prime/** - Shared utility library (logging, CSV parsing, IL manipulation, extension methods)
- **Memoria.Injection/** - C++ DLL that hooks into Unity's mono runtime at startup
- **Memoria.Patcher/** - Installation/update tool that deploys files and manages updates
- **Memoria.Launcher/** - WPF launcher with mod manager, settings UI, and configuration
- **Memoria.Scripts/** - Battle/ability calculation scripts that can be modded at runtime

**Development Tools:**
- **Memoria.Compiler/** - Compiles script modifications
- **Memoria.Debugger/** - Process injection tool for attaching debuggers
- **Memoria.Client/** - WPF debug client for runtime game inspection

**Supporting:**
- **Memoria.MSBuild/** - Custom MSBuild tasks for deployment
- **Memoria.SteamFix/** - Registry fixes for Steam overlay issues
- **Memoria.XInputDotNetPure/** - Controller input (XInput + JoyShock)
- **Memoria.ScreenReader/** - Screen reader accessibility integration (Tolk library wrapper)
- **UnityEngine.UI/** - Custom UI components

### Assembly Relationships

```
Game.exe (Unity)
  ├─> Memoria.Injection.dll (injected at startup)
  └─> Assembly-CSharp.dll (main game)
       ├─> Memoria.Prime.dll
       ├─> Memoria.ScreenReader.dll (screen reader accessibility)
       ├─> Memoria.Scripts.dll
       └─> UnityEngine.UI.dll

Memoria.Launcher.exe (launches game, manages settings)
Memoria.Patcher.exe (installer/updater)
```

### Modding System Architecture

The modding system uses a **priority-based folder overlay** approach:

1. **Mod folders** are placed at game root level with configurable priority
2. **Asset loading** searches in priority order: highest priority mod → lowest priority mod → default game folder
3. **Three patch types** can be provided by mods:
   - `DictionaryPatch.txt` - Patches game dictionaries
   - `BattlePatch.txt` - Patches battle data
   - `TextPatch.txt` - Find/replace text with regex support

Key files for mod loading:
- `Assembly-CSharp/Global/Asset/AssetManager.cs` - Core asset loading with mod folder support
- `Assembly-CSharp/Memoria/Configuration/DataPatchers.cs` - Applies patches from mod folders
- `Assembly-CSharp/Memoria/Scripts/ScriptsLoader.cs` - Dynamically loads script DLLs from mods

### StreamingAssets Folder

Located at `{GameRoot}/StreamingAssets/`:
- `Data/` - CSV/text data (characters, items, battles, translations)
- `Scripts/` - Script compilation projects and DLLs
- `Assets/` - Asset bundles
- `Shaders/` - Custom shaders (hot-reloadable)

Mods mirror this structure in their own folders.

### Integration Approach

Memoria uses **source-level integration** rather than runtime IL patching. Original FF9 classes in the `FF9/` namespace import `using Memoria` and call Memoria methods directly. This provides cleaner integration than runtime hooking frameworks like Harmony.

## Key Code Locations

**Battle System:**
- `Assembly-CSharp/Memoria/Battle/Calculator/` - Battle calculation engine
- `Assembly-CSharp/Memoria/Battle/Scripts/` - Script execution system
- `Assembly-CSharp/Memoria/Data/Battle/` - Battle data helpers
- `Assembly-CSharp/FF9/btl_*.cs` - Original battle code integrated with Memoria

**UI System:**
- `Assembly-CSharp/Memoria/Scenes/` - Custom UI scenes
  - `BattleHUD/` - Battle interface
  - `MenuHUD/` - Menu system
  - `ControlPanel/` - In-game settings panel
- `Assembly-CSharp/Memoria/Scenes/GO*.cs` - GameObject wrapper components

**Configuration:**
- `Assembly-CSharp/Memoria/Configuration/Structure/` - Configuration data structures (40+ section classes)
- `Assembly-CSharp/Memoria/Configuration/Access/` - Static accessor classes
- Configuration loaded from `Memoria.ini` at game root

**Mod Loading:**
- `Assembly-CSharp/Global/Asset/AssetManager.cs` - Asset loading with folder priority
- `Assembly-CSharp/Memoria/Assets/Text/ModTextResources.cs` - Text resource path resolution
- `Assembly-CSharp/Memoria/Scripts/ScriptsLoader.cs` - Dynamic script loading
- `Assembly-CSharp/Memoria/Configuration/DataPatchers.cs` - Data/text patch application

## Screen Reader Accessibility

This repository includes screen reader integration to make FF9 accessible to blind and visually impaired players using JAWS, NVDA, and other screen readers.

### Architecture

**Memoria.ScreenReader/** - Screen reader integration library
- `ScreenReaderManager.cs` - Singleton manager for screen reader operations (Initialize, Speak, Shutdown)
- `Tolk.cs` - C# wrapper for Tolk library (P/Invoke declarations)
- `Tolk.dll` - Native library that interfaces with screen readers (SAPI, JAWS, NVDA, Window-Eyes, etc.)

**Assembly Dependencies:**
```
Assembly-CSharp.dll
  └─> Memoria.ScreenReader.dll
       └─> Tolk.dll (native, copied to game folder)
            └─> nvdaControllerClient32.dll, nvdaControllerClient64.dll (NVDA support)
            └─> SAAPI32.dll, SAAPI64.dll (System Access support)
```

### Integration Pattern

Screen reader support is integrated at the UI level using a consistent pattern:

1. **Initialize on scene start:**
```csharp
ScreenReaderManager.Instance.Initialize();
```

2. **Announce UI elements during navigation:**
```csharp
ScreenReaderManager.Instance.Speak("Menu item text", interrupt: false);
```

3. **Shutdown on scene end:**
```csharp
ScreenReaderManager.Instance.Shutdown();
```

**Current Integration:**
- `Assembly-CSharp/Global/TitleUI.cs` - Title screen menu (Continue, New Game, etc.)
  - Initializes screen reader in `Start()`
  - Announces menu items in `AnnounceMenuItem()` called from `OnItemSelect()`
  - Shuts down in `OnDestroy()`

**To Extend to Other Menus:**
Follow the same pattern in:
- `Assembly-CSharp/Global/MainMenuUI.cs` - Main menu (Items, Magic, Equip, Status)
- `Assembly-CSharp/Global/battle/BattleHUD/*.cs` - Battle menus
- Shop menus, dialog boxes, character selection screens, etc.

### Build Considerations

**Important:** After building, manually copy screen reader DLLs to the game folder:

From `Memoria.ScreenReader/`:
- `Tolk.dll`
- `nvdaControllerClient32.dll`, `nvdaControllerClient64.dll`
- `SAAPI32.dll`, `SAAPI64.dll`
- Any other Tolk dependencies

Copy to both:
- `{GameRoot}/x64/FF9_Data/Managed/`
- `{GameRoot}/x86/FF9_Data/Managed/`

The build system automatically deploys `Memoria.ScreenReader.dll`, but native dependencies require manual copying.

### VS2022 Build Fix

The solution previously had `FrameworkPathOverride` settings pointing to 64-bit .NET 3.5 DLLs, which caused MSBuild conflicts with AnyCPU projects. These have been removed from:
- `Assembly-CSharp/Assembly-CSharp.csproj`
- `Memoria.Prime/Memoria.Prime.csproj`
- `UnityEngine.UI/UnityEngine.UI.csproj`
- `Memoria.ScreenReader/Memoria.ScreenReader.csproj`

Projects now use standard .NET 3.5 Reference Assemblies from `C:\Program Files (x86)\Reference Assemblies\`.

## Development Notes

- The solution requires Visual Studio 2017 or later
- Native C++ project (Memoria.Injection) requires Windows SDK 8.1
- Game must be installed via Steam for development setup
- Build dependencies are extracted from `References/Dependencies.7z` (password-protected in CI)
- Output goes to `Output/` folder
- Custom MSBuild tasks in Memoria.MSBuild handle deployment during builds
