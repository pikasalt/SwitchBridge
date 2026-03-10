using System;
using System.IO.Ports;

namespace SwitchBridge
{
    /// <summary>
    /// Handles serial communication with the Pico W firmware.
    /// Sends binary controller state packets at the protocol-defined format.
    /// </summary>
    public class PicoSerialLink : IDisposable
    {
        private SerialPort? _port;
        private readonly byte[] _packetBuffer = new byte[26]; // Full packet size

        public bool IsConnected => _port?.IsOpen ?? false;
        public string? PortName => _port?.PortName;

        public PicoSerialLink()
        {
            // Pre-fill sync bytes
            _packetBuffer[0] = 0xAA;
            _packetBuffer[1] = 0x55;
        }

        /// <summary>
        /// Get available serial port names.
        /// </summary>
        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        /// <summary>
        /// Connect to the Pico on the specified COM port.
        /// </summary>
        public bool Connect(string portName)
        {
            try
            {
                Disconnect();

                _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                {
                    WriteTimeout = 50,
                    ReadTimeout = 50,
                    DtrEnable = true,
                    RtsEnable = true
                };

                _port.Open();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Serial connect error: {ex.Message}");
                _port?.Dispose();
                _port = null;
                return false;
            }
        }

        /// <summary>
        /// Disconnect from the Pico.
        /// </summary>
        public void Disconnect()
        {
            if (_port?.IsOpen == true)
            {
                try { _port.Close(); } catch { }
            }
            _port?.Dispose();
            _port = null;
        }

        /// <summary>
        /// Send a controller state packet to the Pico.
        /// </summary>
        public void SendState(ControllerState state)
        {
            if (_port?.IsOpen != true) return;

            // Byte 2: Right-side buttons
            byte btnLo = 0;
            if (state.BtnY) btnLo |= 0x01;
            if (state.BtnX) btnLo |= 0x02;
            if (state.BtnB) btnLo |= 0x04;
            if (state.BtnA) btnLo |= 0x08;
            if (state.BtnR) btnLo |= 0x40;
            if (state.BtnZR) btnLo |= 0x80;

            // Byte 3: Shared buttons
            byte btnHi = 0;
            if (state.BtnMinus) btnHi |= 0x01;
            if (state.BtnPlus) btnHi |= 0x02;
            if (state.BtnRStick) btnHi |= 0x04;
            if (state.BtnLStick) btnHi |= 0x08;
            if (state.BtnHome) btnHi |= 0x10;
            if (state.BtnCapture) btnHi |= 0x20;

            // Byte 4: D-Pad + left-side buttons
            byte btnMisc = 0;
            if (state.DPadDown) btnMisc |= 0x01;
            if (state.DPadUp) btnMisc |= 0x02;
            if (state.DPadRight) btnMisc |= 0x04;
            if (state.DPadLeft) btnMisc |= 0x08;
            if (state.BtnL) btnMisc |= 0x40;
            if (state.BtnZL) btnMisc |= 0x80;

            _packetBuffer[2] = btnLo;
            _packetBuffer[3] = btnHi;
            _packetBuffer[4] = btnMisc;

            // Sticks (int16 LE)
            WriteInt16(_packetBuffer, 5, state.LeftStickX);
            WriteInt16(_packetBuffer, 7, state.LeftStickY);
            WriteInt16(_packetBuffer, 9, state.RightStickX);
            WriteInt16(_packetBuffer, 11, state.RightStickY);

            // IMU (int16 LE)
            WriteInt16(_packetBuffer, 13, state.AccelX);
            WriteInt16(_packetBuffer, 15, state.AccelY);
            WriteInt16(_packetBuffer, 17, state.AccelZ);
            WriteInt16(_packetBuffer, 19, state.GyroX);
            WriteInt16(_packetBuffer, 21, state.GyroY);
            WriteInt16(_packetBuffer, 23, state.GyroZ);

            // Checksum (XOR of bytes 2-24)
            byte checksum = 0;
            for (int i = 2; i < 25; i++)
                checksum ^= _packetBuffer[i];
            _packetBuffer[25] = checksum;

            try
            {
                _port.Write(_packetBuffer, 0, _packetBuffer.Length);
            }
            catch (Exception)
            {
                // Port disconnected or write failure
                Disconnect();
            }
        }

        private static void WriteInt16(byte[] buf, int offset, short value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
