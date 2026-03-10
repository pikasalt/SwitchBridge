using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SDL2;

namespace SwitchBridge
{
    // ========================================================================
    // Enums for input source types
    // ========================================================================

    /// <summary>
    /// The type of physical input that drives a Switch button.
    /// </summary>
    public enum InputSourceType
    {
        None,
        GamepadButton,
        GamepadAxis,        // For triggers used as buttons (with threshold)
        KeyboardKey,
        MouseButton
    }

    /// <summary>
    /// All mappable Switch Pro Controller outputs.
    /// </summary>
    public enum SwitchButton
    {
        A, B, X, Y,
        L, R, ZL, ZR,
        Plus, Minus, Home, Capture,
        LStick, RStick,
        DPadUp, DPadDown, DPadLeft, DPadRight
    }

    /// <summary>
    /// Analog stick mapping targets.
    /// </summary>
    public enum SwitchStick
    {
        LeftStickX, LeftStickY,
        RightStickX, RightStickY
    }

    /// <summary>
    /// The kind of input device being configured.
    /// </summary>
    public enum InputDeviceType
    {
        GamepadSwitchPro,
        GamepadXInput,
        GamepadDualSense,
        GamepadGeneric,
        KeyboardMouse
    }

    // ========================================================================
    // Single binding: what physical input maps to a Switch output
    // ========================================================================

    /// <summary>
    /// A single input binding. Describes one physical input (button/key/click)
    /// that maps to a Switch Pro Controller output.
    /// </summary>
    public class InputBinding
    {
        public InputSourceType SourceType { get; set; } = InputSourceType.None;

        /// <summary>
        /// For GamepadButton: SDL_GameControllerButton enum value.
        /// For GamepadAxis: SDL_GameControllerAxis enum value (used as digital with threshold).
        /// For KeyboardKey: SDL_Scancode value.
        /// For MouseButton: 1=Left, 2=Middle, 3=Right, 4=X1, 5=X2.
        /// </summary>
        public int SourceId { get; set; }

        /// <summary>
        /// For axis-as-button: threshold above which the axis counts as pressed.
        /// Positive = positive direction, negative = negative direction.
        /// </summary>
        public int AxisThreshold { get; set; } = 16384;

        /// <summary>
        /// Human-readable display name for this binding.
        /// </summary>
        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                return SourceType switch
                {
                    InputSourceType.None => "(unbound)",
                    InputSourceType.GamepadButton => GetGamepadButtonName(SourceId),
                    InputSourceType.GamepadAxis => GetGamepadAxisName(SourceId, AxisThreshold),
                    InputSourceType.KeyboardKey => GetKeyName(SourceId),
                    InputSourceType.MouseButton => GetMouseButtonName(SourceId),
                    _ => "???"
                };
            }
        }

        private static string GetGamepadButtonName(int id)
        {
            return ((SDL.SDL_GameControllerButton)id) switch
            {
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A => "A",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B => "B",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X => "X",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y => "Y",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK => "Back/Select",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE => "Guide",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START => "Start",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK => "L Stick Click",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK => "R Stick Click",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER => "LB",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER => "RB",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP => "D-Pad Up",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN => "D-Pad Down",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT => "D-Pad Left",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT => "D-Pad Right",
                SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_MISC1 => "Misc/Capture",
                _ => $"Button {id}"
            };
        }

        private static string GetGamepadAxisName(int id, int threshold)
        {
            string dir = threshold >= 0 ? "+" : "-";
            string name = ((SDL.SDL_GameControllerAxis)id) switch
            {
                SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX => "Left X",
                SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY => "Left Y",
                SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX => "Right X",
                SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY => "Right Y",
                SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT => "LT",
                SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT => "RT",
                _ => $"Axis {id}"
            };
            return $"{name} {dir}";
        }

        private static string GetKeyName(int scancode)
        {
            var key = SDL.SDL_GetKeyFromScancode((SDL.SDL_Scancode)scancode);
            string name = SDL.SDL_GetKeyName(key);
            return string.IsNullOrEmpty(name) ? $"Key {scancode}" : name;
        }

        private static string GetMouseButtonName(int button)
        {
            return button switch
            {
                1 => "Mouse Left",
                2 => "Mouse Middle",
                3 => "Mouse Right",
                4 => "Mouse X1",
                5 => "Mouse X2",
                _ => $"Mouse {button}"
            };
        }
    }

    // ========================================================================
    // Stick binding: which gamepad axis or keys drive an analog stick axis
    // ========================================================================

    /// <summary>
    /// Binding for one axis of a stick. Can be a gamepad axis or two keys
    /// (negative direction key + positive direction key).
    /// </summary>
    public class StickAxisBinding
    {
        /// <summary>If true, driven by keyboard keys. If false, driven by a gamepad axis.</summary>
        public bool UseKeys { get; set; }

        /// <summary>Gamepad axis (SDL_GameControllerAxis) when UseKeys is false.</summary>
        public int GamepadAxis { get; set; }

        /// <summary>Whether to invert the gamepad axis.</summary>
        public bool Inverted { get; set; }

        /// <summary>Keyboard scancode for negative direction (left / up).</summary>
        public int NegativeKey { get; set; }

        /// <summary>Keyboard scancode for positive direction (right / down).</summary>
        public int PositiveKey { get; set; }

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                if (UseKeys)
                {
                    string neg = GetKeyName(NegativeKey);
                    string pos = GetKeyName(PositiveKey);
                    return $"{neg} / {pos}";
                }
                else
                {
                    string name = ((SDL.SDL_GameControllerAxis)GamepadAxis) switch
                    {
                        SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX => "Left Stick X",
                        SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY => "Left Stick Y",
                        SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX => "Right Stick X",
                        SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY => "Right Stick Y",
                        _ => $"Axis {GamepadAxis}"
                    };
                    return Inverted ? $"{name} (inv)" : name;
                }
            }
        }

        private static string GetKeyName(int scancode)
        {
            if (scancode == 0) return "(none)";
            var key = SDL.SDL_GetKeyFromScancode((SDL.SDL_Scancode)scancode);
            string name = SDL.SDL_GetKeyName(key);
            return string.IsNullOrEmpty(name) ? $"Key {scancode}" : name;
        }
    }

    // ========================================================================
    // Complete input mapping profile
    // ========================================================================

    /// <summary>
    /// A complete mapping profile that maps physical inputs to all Switch
    /// Pro Controller outputs.
    /// </summary>
    public class InputMappingProfile
    {
        public string Name { get; set; } = "Custom";
        public InputDeviceType DeviceType { get; set; } = InputDeviceType.GamepadGeneric;

        /// <summary>Button mappings: Switch button -> physical input binding.</summary>
        public Dictionary<SwitchButton, InputBinding> Buttons { get; set; } = new();

        /// <summary>Stick axis mappings.</summary>
        public Dictionary<SwitchStick, StickAxisBinding> Sticks { get; set; } = new();

        // ====================================================================
        // Default profile factories
        // ====================================================================

        /// <summary>
        /// Create a default profile for a Switch Pro Controller.
        /// This is 1:1 since SDL already maps it correctly.
        /// </summary>
        public static InputMappingProfile CreateSwitchProDefault()
        {
            var p = new InputMappingProfile
            {
                Name = "Switch Pro Controller",
                DeviceType = InputDeviceType.GamepadSwitchPro
            };

            // Buttons - direct 1:1 mapping
            p.Buttons[SwitchButton.A] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A };
            p.Buttons[SwitchButton.B] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B };
            p.Buttons[SwitchButton.X] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X };
            p.Buttons[SwitchButton.Y] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y };

            p.Buttons[SwitchButton.L] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER };
            p.Buttons[SwitchButton.R] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER };
            p.Buttons[SwitchButton.ZL] = new InputBinding { SourceType = InputSourceType.GamepadAxis, SourceId = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT, AxisThreshold = 16384 };
            p.Buttons[SwitchButton.ZR] = new InputBinding { SourceType = InputSourceType.GamepadAxis, SourceId = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT, AxisThreshold = 16384 };

            p.Buttons[SwitchButton.Plus] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START };
            p.Buttons[SwitchButton.Minus] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK };
            p.Buttons[SwitchButton.Home] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE };
            p.Buttons[SwitchButton.Capture] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_MISC1 };

            p.Buttons[SwitchButton.LStick] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK };
            p.Buttons[SwitchButton.RStick] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK };

            p.Buttons[SwitchButton.DPadUp] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP };
            p.Buttons[SwitchButton.DPadDown] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN };
            p.Buttons[SwitchButton.DPadLeft] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT };
            p.Buttons[SwitchButton.DPadRight] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT };

            // Sticks - direct axis mapping
            p.Sticks[SwitchStick.LeftStickX] = new StickAxisBinding { GamepadAxis = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX };
            p.Sticks[SwitchStick.LeftStickY] = new StickAxisBinding { GamepadAxis = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY };
            p.Sticks[SwitchStick.RightStickX] = new StickAxisBinding { GamepadAxis = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX };
            p.Sticks[SwitchStick.RightStickY] = new StickAxisBinding { GamepadAxis = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY };

            return p;
        }

        /// <summary>
        /// Create a default profile for an XInput controller (Xbox layout).
        /// Maps Xbox layout to Switch Pro layout (A/B and X/Y swapped to match
        /// physical positions rather than labels).
        /// </summary>
        public static InputMappingProfile CreateXInputDefault()
        {
            var p = new InputMappingProfile
            {
                Name = "XInput Controller",
                DeviceType = InputDeviceType.GamepadXInput
            };

            // Xbox -> Switch positional mapping:
            // Xbox A (bottom) -> Switch B (bottom)
            // Xbox B (right)  -> Switch A (right)
            // Xbox X (left)   -> Switch Y (left)
            // Xbox Y (top)    -> Switch X (top)
            p.Buttons[SwitchButton.B] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A };
            p.Buttons[SwitchButton.A] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B };
            p.Buttons[SwitchButton.Y] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X };
            p.Buttons[SwitchButton.X] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y };

            p.Buttons[SwitchButton.L] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER };
            p.Buttons[SwitchButton.R] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER };
            p.Buttons[SwitchButton.ZL] = new InputBinding { SourceType = InputSourceType.GamepadAxis, SourceId = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT, AxisThreshold = 16384 };
            p.Buttons[SwitchButton.ZR] = new InputBinding { SourceType = InputSourceType.GamepadAxis, SourceId = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT, AxisThreshold = 16384 };

            p.Buttons[SwitchButton.Plus] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START };
            p.Buttons[SwitchButton.Minus] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK };
            p.Buttons[SwitchButton.Home] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE };
            p.Buttons[SwitchButton.Capture] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_MISC1 };

            p.Buttons[SwitchButton.LStick] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK };
            p.Buttons[SwitchButton.RStick] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK };

            p.Buttons[SwitchButton.DPadUp] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP };
            p.Buttons[SwitchButton.DPadDown] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN };
            p.Buttons[SwitchButton.DPadLeft] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT };
            p.Buttons[SwitchButton.DPadRight] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT };

            // Sticks
            p.Sticks[SwitchStick.LeftStickX] = new StickAxisBinding { GamepadAxis = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX };
            p.Sticks[SwitchStick.LeftStickY] = new StickAxisBinding { GamepadAxis = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY };
            p.Sticks[SwitchStick.RightStickX] = new StickAxisBinding { GamepadAxis = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX };
            p.Sticks[SwitchStick.RightStickY] = new StickAxisBinding { GamepadAxis = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY };

            return p;
        }

        /// <summary>
        /// Create a default profile for DualSense (PS5) controller.
        /// Maps by physical position (cross=B, circle=A, square=Y, triangle=X).
        /// </summary>
        public static InputMappingProfile CreateDualSenseDefault()
        {
            var p = new InputMappingProfile
            {
                Name = "DualSense Controller",
                DeviceType = InputDeviceType.GamepadDualSense
            };

            // PS -> Switch positional mapping:
            // Cross (bottom) -> B, Circle (right) -> A
            // Square (left)  -> Y, Triangle (top)  -> X
            p.Buttons[SwitchButton.B] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A };
            p.Buttons[SwitchButton.A] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B };
            p.Buttons[SwitchButton.Y] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X };
            p.Buttons[SwitchButton.X] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y };

            p.Buttons[SwitchButton.L] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER };
            p.Buttons[SwitchButton.R] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER };
            p.Buttons[SwitchButton.ZL] = new InputBinding { SourceType = InputSourceType.GamepadAxis, SourceId = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT, AxisThreshold = 16384 };
            p.Buttons[SwitchButton.ZR] = new InputBinding { SourceType = InputSourceType.GamepadAxis, SourceId = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT, AxisThreshold = 16384 };

            p.Buttons[SwitchButton.Plus] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START };
            p.Buttons[SwitchButton.Minus] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK };
            p.Buttons[SwitchButton.Home] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE };
            p.Buttons[SwitchButton.Capture] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_MISC1 };

            p.Buttons[SwitchButton.LStick] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK };
            p.Buttons[SwitchButton.RStick] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK };

            p.Buttons[SwitchButton.DPadUp] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP };
            p.Buttons[SwitchButton.DPadDown] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN };
            p.Buttons[SwitchButton.DPadLeft] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT };
            p.Buttons[SwitchButton.DPadRight] = new InputBinding { SourceType = InputSourceType.GamepadButton, SourceId = (int)SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT };

            // Sticks
            p.Sticks[SwitchStick.LeftStickX] = new StickAxisBinding { GamepadAxis = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX };
            p.Sticks[SwitchStick.LeftStickY] = new StickAxisBinding { GamepadAxis = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY };
            p.Sticks[SwitchStick.RightStickX] = new StickAxisBinding { GamepadAxis = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX };
            p.Sticks[SwitchStick.RightStickY] = new StickAxisBinding { GamepadAxis = (int)SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY };

            return p;
        }

        /// <summary>
        /// Create a default keyboard + mouse mapping profile.
        /// WASD for left stick, arrow keys for right stick, mouse for gyro aim (future).
        /// </summary>
        public static InputMappingProfile CreateKeyboardMouseDefault()
        {
            var p = new InputMappingProfile
            {
                Name = "Keyboard + Mouse",
                DeviceType = InputDeviceType.KeyboardMouse
            };

            // Face buttons: J=A, K=B, U=X, I=Y (right hand cluster)
            p.Buttons[SwitchButton.A] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_J };
            p.Buttons[SwitchButton.B] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_K };
            p.Buttons[SwitchButton.X] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_U };
            p.Buttons[SwitchButton.Y] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_I };

            // Shoulders/triggers
            p.Buttons[SwitchButton.L] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_Q };
            p.Buttons[SwitchButton.R] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_E };
            p.Buttons[SwitchButton.ZL] = new InputBinding { SourceType = InputSourceType.MouseButton, SourceId = 3 }; // Right click
            p.Buttons[SwitchButton.ZR] = new InputBinding { SourceType = InputSourceType.MouseButton, SourceId = 1 }; // Left click

            // System
            p.Buttons[SwitchButton.Plus] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_RETURN };
            p.Buttons[SwitchButton.Minus] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_BACKSPACE };
            p.Buttons[SwitchButton.Home] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_HOME };
            p.Buttons[SwitchButton.Capture] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_F12 };

            // Stick clicks
            p.Buttons[SwitchButton.LStick] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_LSHIFT };
            p.Buttons[SwitchButton.RStick] = new InputBinding { SourceType = InputSourceType.MouseButton, SourceId = 2 }; // Middle click

            // D-Pad: numpad or TFGH
            p.Buttons[SwitchButton.DPadUp] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_T };
            p.Buttons[SwitchButton.DPadDown] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_G };
            p.Buttons[SwitchButton.DPadLeft] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_F };
            p.Buttons[SwitchButton.DPadRight] = new InputBinding { SourceType = InputSourceType.KeyboardKey, SourceId = (int)SDL.SDL_Scancode.SDL_SCANCODE_H };

            // Left stick: WASD
            p.Sticks[SwitchStick.LeftStickX] = new StickAxisBinding
            {
                UseKeys = true,
                NegativeKey = (int)SDL.SDL_Scancode.SDL_SCANCODE_A,
                PositiveKey = (int)SDL.SDL_Scancode.SDL_SCANCODE_D
            };
            p.Sticks[SwitchStick.LeftStickY] = new StickAxisBinding
            {
                UseKeys = true,
                NegativeKey = (int)SDL.SDL_Scancode.SDL_SCANCODE_W,
                PositiveKey = (int)SDL.SDL_Scancode.SDL_SCANCODE_S
            };

            // Right stick: Arrow keys
            p.Sticks[SwitchStick.RightStickX] = new StickAxisBinding
            {
                UseKeys = true,
                NegativeKey = (int)SDL.SDL_Scancode.SDL_SCANCODE_LEFT,
                PositiveKey = (int)SDL.SDL_Scancode.SDL_SCANCODE_RIGHT
            };
            p.Sticks[SwitchStick.RightStickY] = new StickAxisBinding
            {
                UseKeys = true,
                NegativeKey = (int)SDL.SDL_Scancode.SDL_SCANCODE_UP,
                PositiveKey = (int)SDL.SDL_Scancode.SDL_SCANCODE_DOWN
            };

            return p;
        }

        /// <summary>
        /// Auto-detect and create a suitable default profile based on controller name.
        /// </summary>
        public static InputMappingProfile CreateDefaultForController(string? controllerName)
        {
            if (string.IsNullOrEmpty(controllerName))
                return CreateXInputDefault();

            string lower = controllerName.ToLowerInvariant();

            if (lower.Contains("pro controller") || lower.Contains("nintendo"))
                return CreateSwitchProDefault();
            if (lower.Contains("dualsense") || lower.Contains("ps5") || lower.Contains("dualshock") || lower.Contains("ps4"))
                return CreateDualSenseDefault();

            // Default to XInput mapping for anything else
            return CreateXInputDefault();
        }

        // ====================================================================
        // Serialization (save/load to JSON)
        // ====================================================================

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, JsonOpts);
        }

        public static InputMappingProfile? FromJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<InputMappingProfile>(json, JsonOpts);
            }
            catch
            {
                return null;
            }
        }

        public void SaveToFile(string path)
        {
            File.WriteAllText(path, ToJson());
        }

        public static InputMappingProfile? LoadFromFile(string path)
        {
            if (!File.Exists(path)) return null;
            return FromJson(File.ReadAllText(path));
        }
    }
}
