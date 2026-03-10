# SwitchBridge Firmware

Raspberry Pi Pico W firmware that emulates a Nintendo Switch Pro Controller over Bluetooth HID, receiving controller state (including 6-axis IMU data) from the SwitchBridge desktop application via USB serial.

## Hardware Required

- **Raspberry Pi Pico W** (~$6) — the **W** variant is required for Bluetooth. A standard Pico will not work.
- **Micro-USB cable** — to connect the Pico W to your PC (for flashing and serial data)

## Prerequisites

You need three things installed: the Pico SDK, CMake, and the ARM cross-compiler.

### Option A: Windows (Recommended — All-in-One Installer)

The easiest path on Windows is the official installer that bundles everything:

1. Download [Pico Setup for Windows](https://github.com/raspberrypi/pico-setup-windows/releases)
2. Run the installer — it installs the Pico SDK, CMake, ARM GCC, and Visual Studio Code integration
3. After installation, open the **Pico - Developer Command Prompt** shortcut it creates (this has all environment variables pre-set)

### Option B: Manual Setup (Windows, Linux, or macOS)

#### 1. ARM GCC Toolchain

- **Windows:** Download from [ARM Developer](https://developer.arm.com/downloads/-/gnu-rm) — get the `.exe` installer and add it to your PATH
- **Linux (Debian/Ubuntu):**
  ```bash
  sudo apt install gcc-arm-none-eabi libnewlib-arm-none-eabi build-essential
  ```
- **macOS:**
  ```bash
  brew install --cask gcc-arm-embedded
  ```

Verify:
```bash
arm-none-eabi-gcc --version
```

#### 2. CMake (3.13+)

- **Windows:** [cmake.org/download](https://cmake.org/download/) — use the `.msi` installer, check "Add to PATH"
- **Linux:**
  ```bash
  sudo apt install cmake
  ```
- **macOS:**
  ```bash
  brew install cmake
  ```

Verify:
```bash
cmake --version
```

#### 3. Pico SDK

```bash
# Clone the SDK
git clone https://github.com/raspberrypi/pico-sdk.git
cd pico-sdk

# Initialize all submodules (required for TinyUSB and BTStack)
git submodule update --init

# BTStack specifically is needed for Bluetooth — make sure it's there
ls lib/btstack/
# Should contain src/, platform/, etc.
```

Set the environment variable pointing to the SDK:

```bash
# Linux / macOS (add to ~/.bashrc or ~/.zshrc for persistence)
export PICO_SDK_PATH=/absolute/path/to/pico-sdk

# Windows (PowerShell)
$env:PICO_SDK_PATH = "C:\path\to\pico-sdk"

# Windows (Command Prompt)
set PICO_SDK_PATH=C:\path\to\pico-sdk
```

> **Important:** The SDK path must be absolute, not relative. CMake will fail with confusing errors otherwise.

## Building

### Linux / macOS

```bash
cd Firmware
mkdir build
cd build
cmake ..
make -j$(nproc)
```

### Windows (Pico Developer Command Prompt)

```cmd
cd Firmware
mkdir build
cd build
cmake -G "NMake Makefiles" ..
nmake
```

### Windows (PowerShell with MinGW/MSYS2)

```powershell
cd Firmware
mkdir build
cd build
cmake -G "MinGW Makefiles" ..
mingw32-make -j4
```

### Windows (Visual Studio)

```powershell
cd Firmware
mkdir build
cd build
cmake -G "Visual Studio 17 2022" -A ARM ..
# Open the .sln in Visual Studio and build
```

If the build succeeds, you'll find `switchbridge_fw.uf2` in the `build/` directory.

## Flashing the Pico W

1. **Unplug** the Pico W from USB
2. **Hold down the BOOTSEL button** (small white button near the USB port)
3. **While holding BOOTSEL**, plug the Pico W into your PC via micro-USB
4. A removable drive called **RPI-RP2** will appear in your file browser
5. **Drag and drop** `switchbridge_fw.uf2` onto the RPI-RP2 drive
6. The Pico automatically reboots with the new firmware — the drive disappears

> After flashing, the Pico will show up as a USB serial device (COM port on Windows, `/dev/ttyACM0` on Linux). This is the data link that the SwitchBridge application connects to.

## Pairing with the Nintendo Switch

1. Make sure the Pico W is powered (plugged into your PC via USB)
2. On the Switch, go to **System Settings → Controllers and Sensors → Change Grip/Order**
3. The Pico W advertises itself as **"Pro Controller"**
4. It should pair automatically within a few seconds
5. The onboard LED blinks fast while searching and slow once connected

### Re-pairing

If the Pico W loses its pairing (e.g., after a power cycle), you may need to:

1. Power cycle the Pico W (unplug and replug USB)
2. Go back into **Change Grip/Order** on the Switch
3. It should reconnect

> **Note:** The current firmware requires re-pairing through the Change Grip/Order screen each time. Persistent pairing (auto-reconnect on power-on) would require storing link keys in flash, which is a planned improvement.

## Project Structure

```
Firmware/
├── CMakeLists.txt               # Build configuration
├── README.md                    # This file
├── include/
│   ├── switchbridge.h           # Shared types, constants, function declarations
│   ├── btstack_config.h         # BTStack Bluetooth stack configuration
│   └── tusb_config.h            # TinyUSB (USB CDC serial) configuration
└── src/
    ├── main.c                   # Entry point, main loop, LED status
    ├── bt_hid.c                 # Bluetooth HID device (Pro Controller emulation)
    ├── serial_input.c           # USB CDC serial packet parsing
    └── switch_protocol.c        # Switch protocol handler (subcommands, SPI, reports)
```

## How It Works

The firmware runs three concurrent tasks in a cooperative loop:

1. **USB CDC Serial** — TinyUSB provides a virtual serial port over USB. The application sends 26-byte binary packets at 115200 baud containing button state, stick positions, and IMU data. The `serial_input.c` module parses these into a `controller_state_t` struct using a ring buffer with sync-byte framing and XOR checksums.

2. **Bluetooth HID** — BTStack manages the Bluetooth Classic connection. The firmware advertises as a Nintendo Switch Pro Controller (VID `0x057E`, PID `0x2009`). When the Switch connects, it sends a series of subcommands (device info requests, SPI flash reads for calibration data, IMU enable, LED settings, etc.). The `switch_protocol.c` module handles all of these with appropriate faked responses.

3. **Input Reports** — At ~60Hz, the main loop packs the current controller state into a standard 0x30 input report. This includes 3-byte packed 12-bit stick values and three copies of the 6-axis IMU sample (accelerometer + gyroscope), matching the real Pro Controller's report format.

## Troubleshooting

### Build Errors

**"PICO_SDK_PATH is not set"** — Set the environment variable as described above. It must be an absolute path.

**"Cannot find pico_sdk_import.cmake"** — Your `PICO_SDK_PATH` is pointing to the wrong directory. It should point to the root of the pico-sdk repo (the folder containing `pico_sdk_init.cmake`).

**"btstack/src/btstack.h not found"** — BTStack submodule wasn't initialized. Run:
```bash
cd $PICO_SDK_PATH
git submodule update --init lib/btstack
```

**"tinyusb not found"** — Same issue, different submodule:
```bash
cd $PICO_SDK_PATH
git submodule update --init lib/tinyusb
```

**Library name mismatches** — If your SDK version uses different CMake target names for BTStack libraries, check `$PICO_SDK_PATH/src/rp2_common/CMakeLists.txt` for the exact names available. Common alternatives include `pico_btstack_classic` vs `pico_btstack_hci_classic`.

### Flashing Issues

**RPI-RP2 drive doesn't appear** — Make sure you're holding BOOTSEL *before* plugging in USB. On some cables, the data pins don't connect (charge-only cables). Try a different micro-USB cable.

**"File too large" or copy fails** — The `.uf2` file should be under 1MB. If it's much larger, something went wrong in the build.

### Bluetooth Issues

**Switch doesn't see the controller** — Make sure you're in **Change Grip/Order**, not just the regular controller settings. The Pico only pairs in this mode. Also verify the LED is blinking fast (searching mode).

**Pairs but then disconnects** — The Switch may be rejecting the subcommand responses. This can happen if the SPI calibration data is malformed. Check serial output for any error messages (if you add UART debugging).

**Intermittent disconnects during gameplay** — This can happen if the Bluetooth bandwidth is saturated, especially with IMU data at high rates. The firmware sends at 60Hz which is within spec, but environmental interference can contribute.

## References

- [dekuNukem/Nintendo_Switch_Reverse_Engineering](https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering) — Comprehensive reverse engineering of the Pro Controller protocol
- [Poohl/joycontrol](https://github.com/Poohl/joycontrol) — Bluetooth HID implementation and protocol research
- [DavidPagels/retro-pico-switch](https://github.com/DavidPagels/retro-pico-switch) — Pico W BTStack integration reference
- [Raspberry Pi Pico SDK Documentation](https://www.raspberrypi.com/documentation/pico-sdk/)
