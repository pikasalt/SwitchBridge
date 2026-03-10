# SwitchBridge

A two-part system for streaming Nintendo Switch gameplay with remote controller input, including gyro/IMU data.

## Architecture

```
┌──────────────┐    USB Serial    ┌──────────────┐   Bluetooth   ┌──────────────┐
│ SwitchBridge │ ───────────────► │  Pico W      │ ────────────► │  Nintendo    │
│ Application  │   (115200 baud)  │  Firmware    │  (Pro Ctrl)   │  Switch      │
│ (Windows/C#) │                  │  (C/C++)     │               │              │
│              │                  └──────────────┘               │              │
│  SDL Input   │                                                 │              │
│  + Gyro      │                                                 │              │
│              │                                                 │              │
│  Video Feed  │◄───────── HDMI Out Capture Card ◄───────────────└──────────────┘
│  (DirectShow)│                                                              
└──────────────┘                                                               
```

## Components

### Application (`/Application`)
- **Language:** C# (.NET 8) with WinForms
- **Dependencies:** SDL2-CS (controller input + gyro), DirectShowLib (video capture)
- **Features:**
  - SDL2 gamepad input with full gyro/accelerometer support
  - Live video/audio feed from capture cards or cameras
  - Serial communication to Pico W firmware
  - Resizable windowed mode, Escape toggles fullscreen
  - Menu bar: File (Exit), View (select input device)

### Firmware (`/Firmware`)
- **Platform:** Raspberry Pi Pico W (RP2040 + CYW43 Bluetooth)
- **Language:** C (Pico SDK + BTStack)
- **Protocol:** Nintendo Switch Pro Controller over Bluetooth HID
- **Features:**
  - Emulates authentic Pro Controller (buttons, sticks, IMU)
  - Receives controller state via USB CDC serial from host PC
  - Sends 0x30 standard full input reports with 6-axis IMU data at ~60Hz
  - Handles Switch pairing handshake, SPI flash reads, subcommand responses

## Serial Protocol (PC → Pico)

Binary packet format (sent at 115200 baud):

```
Byte 0:     0xAA (sync marker)
Byte 1:     0x55 (sync marker)
Byte 2-3:   Button state (uint16_t, little-endian)
Byte 4:     HAT switch / misc buttons
Byte 5-6:   Left stick X (int16_t, -32768 to 32767)
Byte 7-8:   Left stick Y (int16_t)
Byte 9-10:  Right stick X (int16_t)
Byte 11-12: Right stick Y (int16_t)
Byte 13-14: Accel X (int16_t, raw LSM6DS3 units)
Byte 15-16: Accel Y (int16_t)
Byte 17-18: Accel Z (int16_t)
Byte 19-20: Gyro X (int16_t, raw LSM6DS3 units)
Byte 21-22: Gyro Y (int16_t)
Byte 23-24: Gyro Z (int16_t)
Byte 25:    Checksum (XOR of bytes 2-24)
```

### Button Mapping (bytes 2-4)

```
Byte 2 (Right buttons):
  bit 0: Y
  bit 1: X
  bit 2: B
  bit 3: A
  bit 6: R
  bit 7: ZR

Byte 3 (Shared/Left):
  bit 0: Minus
  bit 1: Plus
  bit 2: R Stick Click
  bit 3: L Stick Click
  bit 4: Home
  bit 5: Capture

Byte 4 (Left buttons + D-Pad):
  bit 0: D-Down
  bit 1: D-Up
  bit 2: D-Right
  bit 3: D-Left
  bit 6: L
  bit 7: ZL
```

## Building

### Firmware
See `Firmware/README.md` for Pico SDK build instructions.

### Application
```bash
cd Application
dotnet build
dotnet run
```

## Hardware Requirements
- Raspberry Pi Pico W (~$6)
- USB capture card (HDMI input)
- Micro-USB cable (Pico to PC)
- Nintendo Switch (docked for Bluetooth pairing)

## References
- [dekuNukem/Nintendo_Switch_Reverse_Engineering](https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering)
- [Poohl/joycontrol](https://github.com/Poohl/joycontrol)
- [DavidPagels/retro-pico-switch](https://github.com/DavidPagels/retro-pico-switch)
