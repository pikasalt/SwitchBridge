using System;
using System.Runtime.InteropServices;
using SDL2;

namespace SwitchBridge
{
    /// <summary>
    /// Handles SDL2 gamepad input, including gyroscope and accelerometer data.
    /// Uses SDL_GameController API with sensor support (SDL 2.0.14+).
    /// Reads input through an InputMappingProfile for configurable bindings.
    /// </summary>
    public class SdlInputHandler : IDisposable
    {
        private IntPtr _controller = IntPtr.Zero;
        private bool _initialized = false;
        private int _controllerIndex = -1;

        // Gyro/accel scaling factors
        // SDL reports gyro in rad/s -> Pro Controller LSM6DS3 raw units
        // Default sensitivity: ±2000 dps = ±34.9 rad/s, range -32768..32767
        private const float GyroScale = 32767.0f / 34.9066f;

        // SDL reports accel in m/s² -> LSM6DS3 raw units
        // Default sensitivity: ±8G, 1G = 9.81 m/s²
        private const float AccelScale = 32767.0f / 78.48f;

        // Current mapping profile
        private InputMappingProfile _profile;

        // Keyboard state (tracked via SDL events since WinForms eats some keys)
        private readonly bool[] _keyState = new bool[512];
        private readonly bool[] _mouseState = new bool[6]; // buttons 1-5

        public bool IsControllerConnected => _controller != IntPtr.Zero;
        public bool HasGyro { get; private set; }
        public bool HasAccel { get; private set; }
        public string? ControllerName { get; private set; }

        public InputMappingProfile Profile
        {
            get => _profile;
            set => _profile = value;
        }

        public SdlInputHandler()
        {
            // Start with XInput defaults; will be replaced when config is loaded
            _profile = InputMappingProfile.CreateXInputDefault();
        }

        /// <summary>
        /// Initialize SDL with gamepad and sensor subsystems.
        /// </summary>
        public bool Initialize()
        {
            if (_initialized) return true;

            if (SDL.SDL_Init(SDL.SDL_INIT_GAMECONTROLLER | SDL.SDL_INIT_SENSOR) < 0)
            {
                Console.WriteLine($"SDL_Init failed: {SDL.SDL_GetError()}");
                return false;
            }

            SDL.SDL_GameControllerEventState(SDL.SDL_ENABLE);
            _initialized = true;
            TryOpenController();
            return true;
        }

        /// <summary>
        /// Try to open the first available game controller.
        /// </summary>
        public void TryOpenController()
        {
            if (_controller != IntPtr.Zero) return;

            int numJoysticks = SDL.SDL_NumJoysticks();
            for (int i = 0; i < numJoysticks; i++)
            {
                if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_TRUE)
                {
                    _controller = SDL.SDL_GameControllerOpen(i);
                    if (_controller != IntPtr.Zero)
                    {
                        _controllerIndex = i;
                        ControllerName = SDL.SDL_GameControllerName(_controller) ?? "Unknown";
                        Console.WriteLine($"Opened controller: {ControllerName}");

                        HasGyro = SDL.SDL_GameControllerHasSensor(_controller,
                            SDL.SDL_SensorType.SDL_SENSOR_GYRO) == SDL.SDL_bool.SDL_TRUE;
                        if (HasGyro)
                        {
                            SDL.SDL_GameControllerSetSensorEnabled(_controller,
                                SDL.SDL_SensorType.SDL_SENSOR_GYRO, SDL.SDL_bool.SDL_TRUE);
                        }

                        HasAccel = SDL.SDL_GameControllerHasSensor(_controller,
                            SDL.SDL_SensorType.SDL_SENSOR_ACCEL) == SDL.SDL_bool.SDL_TRUE;
                        if (HasAccel)
                        {
                            SDL.SDL_GameControllerSetSensorEnabled(_controller,
                                SDL.SDL_SensorType.SDL_SENSOR_ACCEL, SDL.SDL_bool.SDL_TRUE);
                        }

                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Poll SDL events and update the controller state using the mapping profile.
        /// </summary>
        public void Update(ControllerState state)
        {
            if (!_initialized) return;

            // Process SDL events
            while (SDL.SDL_PollEvent(out SDL.SDL_Event e) != 0)
            {
                switch (e.type)
                {
                    case SDL.SDL_EventType.SDL_CONTROLLERDEVICEADDED:
                        if (_controller == IntPtr.Zero)
                            TryOpenController();
                        break;

                    case SDL.SDL_EventType.SDL_CONTROLLERDEVICEREMOVED:
                        if (_controller != IntPtr.Zero)
                        {
                            SDL.SDL_GameControllerClose(_controller);
                            _controller = IntPtr.Zero;
                            _controllerIndex = -1;
                            ControllerName = null;
                            HasGyro = false;
                            HasAccel = false;
                        }
                        break;

                    case SDL.SDL_EventType.SDL_CONTROLLERSENSORUPDATE:
                        HandleSensorEvent(e.csensor, state);
                        break;

                    // Track keyboard state
                    case SDL.SDL_EventType.SDL_KEYDOWN:
                        if (e.key.keysym.scancode < (SDL.SDL_Scancode)512)
                            _keyState[(int)e.key.keysym.scancode] = true;
                        break;
                    case SDL.SDL_EventType.SDL_KEYUP:
                        if (e.key.keysym.scancode < (SDL.SDL_Scancode)512)
                            _keyState[(int)e.key.keysym.scancode] = false;
                        break;

                    // Track mouse state
                    case SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
                        if (e.button.button < 6)
                            _mouseState[e.button.button] = true;
                        break;
                    case SDL.SDL_EventType.SDL_MOUSEBUTTONUP:
                        if (e.button.button < 6)
                            _mouseState[e.button.button] = false;
                        break;
                }
            }

            // Apply button mappings
            state.BtnA = ReadButtonBinding(SwitchButton.A);
            state.BtnB = ReadButtonBinding(SwitchButton.B);
            state.BtnX = ReadButtonBinding(SwitchButton.X);
            state.BtnY = ReadButtonBinding(SwitchButton.Y);
            state.BtnL = ReadButtonBinding(SwitchButton.L);
            state.BtnR = ReadButtonBinding(SwitchButton.R);
            state.BtnZL = ReadButtonBinding(SwitchButton.ZL);
            state.BtnZR = ReadButtonBinding(SwitchButton.ZR);
            state.BtnPlus = ReadButtonBinding(SwitchButton.Plus);
            state.BtnMinus = ReadButtonBinding(SwitchButton.Minus);
            state.BtnHome = ReadButtonBinding(SwitchButton.Home);
            state.BtnCapture = ReadButtonBinding(SwitchButton.Capture);
            state.BtnLStick = ReadButtonBinding(SwitchButton.LStick);
            state.BtnRStick = ReadButtonBinding(SwitchButton.RStick);
            state.DPadUp = ReadButtonBinding(SwitchButton.DPadUp);
            state.DPadDown = ReadButtonBinding(SwitchButton.DPadDown);
            state.DPadLeft = ReadButtonBinding(SwitchButton.DPadLeft);
            state.DPadRight = ReadButtonBinding(SwitchButton.DPadRight);

            // Apply stick mappings
            state.LeftStickX = ReadStickBinding(SwitchStick.LeftStickX);
            state.LeftStickY = ReadStickBinding(SwitchStick.LeftStickY);
            state.RightStickX = ReadStickBinding(SwitchStick.RightStickX);
            state.RightStickY = ReadStickBinding(SwitchStick.RightStickY);

            // Gyro and accel are updated via sensor events
        }

        // ====================================================================
        // Read a button binding using the current mapping profile
        // ====================================================================

        private bool ReadButtonBinding(SwitchButton btn)
        {
            if (!_profile.Buttons.TryGetValue(btn, out var binding))
                return false;

            return binding.SourceType switch
            {
                InputSourceType.GamepadButton =>
                    _controller != IntPtr.Zero &&
                    SDL.SDL_GameControllerGetButton(_controller,
                        (SDL.SDL_GameControllerButton)binding.SourceId) != 0,

                InputSourceType.GamepadAxis =>
                    _controller != IntPtr.Zero &&
                    CheckAxisAsButton(
                        SDL.SDL_GameControllerGetAxis(_controller,
                            (SDL.SDL_GameControllerAxis)binding.SourceId),
                        binding.AxisThreshold),

                InputSourceType.KeyboardKey =>
                    binding.SourceId > 0 && binding.SourceId < 512 &&
                    _keyState[binding.SourceId],

                InputSourceType.MouseButton =>
                    binding.SourceId > 0 && binding.SourceId < 6 &&
                    _mouseState[binding.SourceId],

                _ => false
            };
        }

        private static bool CheckAxisAsButton(short axisValue, int threshold)
        {
            if (threshold >= 0)
                return axisValue > threshold;
            else
                return axisValue < threshold;
        }

        // ====================================================================
        // Read a stick axis binding using the current mapping profile
        // ====================================================================

        private short ReadStickBinding(SwitchStick stick)
        {
            if (!_profile.Sticks.TryGetValue(stick, out var binding))
                return 0;

            if (binding.UseKeys)
            {
                // Digital keys -> full deflection
                bool neg = binding.NegativeKey > 0 && binding.NegativeKey < 512 &&
                           _keyState[binding.NegativeKey];
                bool pos = binding.PositiveKey > 0 && binding.PositiveKey < 512 &&
                           _keyState[binding.PositiveKey];

                if (neg && !pos) return -32767;
                if (pos && !neg) return 32767;
                return 0;
            }
            else
            {
                // Gamepad axis
                if (_controller == IntPtr.Zero) return 0;

                short val = SDL.SDL_GameControllerGetAxis(_controller,
                    (SDL.SDL_GameControllerAxis)binding.GamepadAxis);

                return binding.Inverted ? (short)(-val) : val;
            }
        }

        // ====================================================================
        // Sensor handling (gyro/accel passthrough - not remappable)
        // ====================================================================

        private void HandleSensorEvent(SDL.SDL_ControllerSensorEvent sensorEvent, ControllerState state)
        {
            unsafe
            {
                float x = sensorEvent.data1;
                float y = sensorEvent.data2;
                float z = sensorEvent.data3;

                if (sensorEvent.sensor == (int)SDL.SDL_SensorType.SDL_SENSOR_GYRO)
                {
                    state.GyroX = ClampInt16(x * GyroScale);
                    state.GyroY = ClampInt16(y * GyroScale);
                    state.GyroZ = ClampInt16(z * GyroScale);
                }
                else if (sensorEvent.sensor == (int)SDL.SDL_SensorType.SDL_SENSOR_ACCEL)
                {
                    state.AccelX = ClampInt16(x * AccelScale);
                    state.AccelY = ClampInt16(y * AccelScale);
                    state.AccelZ = ClampInt16(z * AccelScale);
                }
            }
        }

        private static short ClampInt16(float value)
        {
            if (value > 32767) return 32767;
            if (value < -32768) return -32768;
            return (short)value;
        }

        public void Dispose()
        {
            if (_controller != IntPtr.Zero)
            {
                SDL.SDL_GameControllerClose(_controller);
                _controller = IntPtr.Zero;
            }

            if (_initialized)
            {
                SDL.SDL_Quit();
                _initialized = false;
            }
        }
    }
}
