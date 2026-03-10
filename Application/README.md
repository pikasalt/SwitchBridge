# SwitchBridge Application

Windows desktop application that captures controller input (including gyro/accelerometer), displays a video feed from a capture card, and forwards everything to a Raspberry Pi Pico W over USB serial.

## Prerequisites

### .NET 8 SDK

Download and install from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0).

Verify the installation:

```powershell
dotnet --version
# Should output 8.0.x or higher
```

### SDL2 Native Library

The NuGet package `ppy.SDL2-CS` provides C# bindings, but **not** the native `SDL2.dll`. You need to grab it separately.

1. Go to [SDL2 Releases](https://github.com/libsdl-org/SDL/releases) (look for the latest SDL2, not SDL3)
2. Download the **Windows VC** package (e.g., `SDL2-2.x.x-win32-x64.zip`)
3. Extract `SDL2.dll` from the zip
4. Place it in the project root (`Application/SDL2.dll`) or next to the built `.exe`

> **Note:** Make sure you grab the **x64** version if you're building for x64, which is the default.

### IDE (Optional)

Any of these work:
- **Visual Studio 2022** — Open `SwitchBridge.csproj` directly
- **VS Code** — With the C# Dev Kit extension
- **Command line** — Just `dotnet` CLI, no IDE needed

## Building

### Command Line

```powershell
cd Application

# Restore NuGet packages (SDL2-CS, DirectShowLib, System.IO.Ports)
dotnet restore

# Build
dotnet build

# Run
dotnet run
```

The built output goes to `bin/Debug/net8.0-windows/`. Make sure `SDL2.dll` is in that folder (or the project root) before running.

### Visual Studio

1. Open `SwitchBridge.csproj` in Visual Studio 2022
2. NuGet restore happens automatically
3. Press **F5** to build and run
4. Make sure `SDL2.dll` is in the project directory (set **Copy to Output Directory → Copy if newer** in properties if you add it to the project)

## NuGet Dependencies

These are automatically restored by `dotnet restore`:

| Package | Version | Purpose |
|---|---|---|
| `ppy.SDL2-CS` | 1.0.741-alpha | SDL2 C# bindings (gamepad, gyro, sensors) |
| `DirectShowLib` | 2.1.0 | Video capture from capture cards / cameras |
| `System.IO.Ports` | 8.0.0 | Serial communication with the Pico W |

## Project Structure

```
Application/
├── SwitchBridge.csproj          # Project file and dependencies
├── Program.cs                   # Entry point
├── MainForm.cs                  # Main window (video display, menus, input loop)
├── SdlInputHandler.cs           # SDL2 gamepad + gyro input with mapping support
├── InputMapping.cs              # Mapping profiles, presets, and serialization
├── ControllerConfigForm.cs      # Controller remapping GUI dialog
├── ControllerState.cs           # Data class for controller state
├── PicoSerialLink.cs            # Serial protocol to Pico W
└── VideoCaptureHandler.cs       # DirectShow video capture
```

## Usage

1. **Connect a controller** — Plug in or pair a gamepad before launching. The app auto-detects it and loads a suitable default mapping (Switch Pro, Xbox, DualSense, or keyboard+mouse).

2. **Launch the app** — `dotnet run` or run the `.exe`.

3. **Select a video source** — Go to **View** in the menu bar and pick your capture card or camera.

4. **Connect to the Pico** — Go to **Connection** and select the COM port where the Pico W is connected. It usually shows up as `COMx` after flashing the firmware.

5. **Configure controls** — Go to **File → Controller Config...** to remap buttons, change input device, or switch to keyboard+mouse. Mappings can be saved/loaded as JSON files.

6. **Fullscreen** — Press **Escape** to toggle fullscreen (hides menu bar and status bar). Press **Escape** again to return to windowed mode.

## Controller Configuration

The config dialog (**File → Controller Config...**) supports:

- **Device selection** — Choose between connected gamepads or keyboard+mouse from the dropdown
- **Load Defaults** — Applies a preset mapping based on the selected device type
- **Remapping** — Click any binding value box, then press the desired input (gamepad button, key, mouse button, or axis). Press **Escape** while rebinding to clear that binding.
- **Stick axes** — For keyboard mode, sticks show two boxes (negative/positive direction). For gamepad mode, you can also push a stick to bind a gamepad axis.
- **Save/Load** — Export and import mappings as `.json` files

### Default Presets

- **Switch Pro Controller** — 1:1 passthrough
- **XInput (Xbox)** — A/B and X/Y swapped to match physical positions
- **DualSense (PS5)** — Cross/Circle/Square/Triangle mapped by position
- **Keyboard + Mouse** — WASD (left stick), Arrow keys (right stick), JKUI (face buttons), mouse clicks (ZL/ZR), and more

## Troubleshooting

**"SDL_Init failed"** — Make sure `SDL2.dll` is in the same folder as the `.exe` or in the project root. Also verify it's the right architecture (x64 vs x86).

**No controllers detected** — SDL2 needs to be initialized before it can see controllers. If you plugged in a controller after launching, the app should auto-detect it via SDL events, but if not, try restarting.

**Capture card not appearing in View menu** — The app uses DirectShow, so your capture card needs a DirectShow-compatible driver. Most USB capture cards include one. Virtual cameras (OBS Virtual Camera, etc.) also appear here.

**COM port not appearing** — The Pico W must be flashed with the SwitchBridge firmware and plugged in. It registers as a USB CDC serial device. You may need to install the Pico's USB driver on first connection — Windows usually handles this automatically.

**Build error about unsafe code** — The `.csproj` already includes `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`. If you're getting this error, make sure you're building the `.csproj` file and not a separate solution that doesn't include this setting.
