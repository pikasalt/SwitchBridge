using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;

namespace SwitchBridge
{
    /// <summary>
    /// Main application window. Displays the video feed from a capture device
    /// and manages controller input forwarding to the Pico W.
    /// </summary>
    public class MainForm : Form
    {
        // Components
        private readonly SdlInputHandler _input = new();
        private readonly PicoSerialLink _serial = new();
        private readonly VideoCaptureHandler _capture = new();
        private readonly ControllerState _state = new();

        // UI elements
        private readonly MenuStrip _menuStrip;
        private readonly ToolStripMenuItem _fileMenu;
        private readonly ToolStripMenuItem _viewMenu;
        private readonly ToolStripMenuItem _connectionMenu;
        private readonly PictureBox _videoDisplay;
        private readonly StatusStrip _statusStrip;
        private readonly ToolStripStatusLabel _statusController;
        private readonly ToolStripStatusLabel _statusSerial;
        private readonly ToolStripStatusLabel _statusCapture;

        // Timers
        private readonly Timer _inputTimer;   // ~60Hz input polling + serial send
        private readonly Timer _frameTimer;   // Video frame display

        private bool _isFullscreen = false;
        private FormWindowState _prevWindowState;
        private FormBorderStyle _prevBorderStyle;

        public MainForm()
        {
            // Form setup
            Text = "SwitchBridge";
            Size = new Size(960, 600);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(640, 400);
            BackColor = Color.Black;
            KeyPreview = true;

            // Menu strip
            _menuStrip = new MenuStrip();

            // File menu
            _fileMenu = new ToolStripMenuItem("File");
            _fileMenu.DropDownItems.Add("Controller Config...", null, (s, e) => OpenControllerConfig());
            _fileMenu.DropDownItems.Add(new ToolStripSeparator());
            _fileMenu.DropDownItems.Add("Exit", null, (s, e) => Close());
            _menuStrip.Items.Add(_fileMenu);

            // View menu (populated dynamically with capture devices)
            _viewMenu = new ToolStripMenuItem("View");
            _viewMenu.DropDownOpening += ViewMenu_Opening;
            _menuStrip.Items.Add(_viewMenu);

            // Connection menu (serial port selection)
            _connectionMenu = new ToolStripMenuItem("Connection");
            _connectionMenu.DropDownOpening += ConnectionMenu_Opening;
            _menuStrip.Items.Add(_connectionMenu);

            MainMenuStrip = _menuStrip;
            Controls.Add(_menuStrip);

            // Video display
            _videoDisplay = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            Controls.Add(_videoDisplay);
            _videoDisplay.BringToFront(); // Ensure it's behind the menu

            // Status strip
            _statusStrip = new StatusStrip();
            _statusController = new ToolStripStatusLabel("Controller: None")
            {
                Spring = false,
                BorderSides = ToolStripStatusLabelBorderSides.Right
            };
            _statusSerial = new ToolStripStatusLabel("Pico: Disconnected")
            {
                Spring = false,
                BorderSides = ToolStripStatusLabelBorderSides.Right
            };
            _statusCapture = new ToolStripStatusLabel("Capture: None")
            {
                Spring = true
            };
            _statusStrip.Items.AddRange(new ToolStripItem[]
            {
                _statusController, _statusSerial, _statusCapture
            });
            Controls.Add(_statusStrip);

            // Input polling timer (~60 Hz)
            _inputTimer = new Timer { Interval = 16 }; // ~62.5 Hz
            _inputTimer.Tick += InputTimer_Tick;

            // Frame display timer (~30 fps to keep UI responsive)
            _frameTimer = new Timer { Interval = 33 }; // ~30 fps
            _frameTimer.Tick += FrameTimer_Tick;

            // Events
            KeyDown += MainForm_KeyDown;
            FormClosing += MainForm_FormClosing;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // Initialize SDL
            if (!_input.Initialize())
            {
                MessageBox.Show("Failed to initialize SDL2.\nController input will be unavailable.",
                    "SDL Init Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // Auto-detect controller and load appropriate default profile
            _input.Profile = InputMappingProfile.CreateDefaultForController(_input.ControllerName);

            // Start timers
            _inputTimer.Start();
            _frameTimer.Start();
        }

        // ====================================================================
        // Controller Configuration
        // ====================================================================

        private void OpenControllerConfig()
        {
            // Pause input polling while config is open
            _inputTimer.Stop();

            using var configForm = new ControllerConfigForm(_input, _input.Profile);
            if (configForm.ShowDialog(this) == DialogResult.OK && configForm.ResultProfile != null)
            {
                _input.Profile = configForm.ResultProfile;
            }

            _inputTimer.Start();
        }

        // ====================================================================
        // Input & Serial
        // ====================================================================

        private void InputTimer_Tick(object? sender, EventArgs e)
        {
            // Update controller state from SDL
            _input.Update(_state);

            // Send to Pico if connected
            if (_serial.IsConnected)
            {
                _serial.SendState(_state);
            }

            // Update status bar
            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            // Controller status
            if (_input.IsControllerConnected)
            {
                string gyroStatus = _input.HasGyro ? " [Gyro]" : "";
                _statusController.Text = $"Controller: Connected{gyroStatus}";
                _statusController.ForeColor = Color.Green;
            }
            else
            {
                _statusController.Text = "Controller: None";
                _statusController.ForeColor = Color.Gray;
            }

            // Serial status
            if (_serial.IsConnected)
            {
                _statusSerial.Text = $"Pico: {_serial.PortName}";
                _statusSerial.ForeColor = Color.Green;
            }
            else
            {
                _statusSerial.Text = "Pico: Disconnected";
                _statusSerial.ForeColor = Color.Gray;
            }

            // Capture status
            if (_capture.IsCapturing)
            {
                _statusCapture.Text = $"Capture: {_capture.FrameWidth}x{_capture.FrameHeight}";
                _statusCapture.ForeColor = Color.Green;
            }
            else
            {
                _statusCapture.Text = "Capture: None";
                _statusCapture.ForeColor = Color.Gray;
            }
        }

        // ====================================================================
        // Video display
        // ====================================================================

        private void FrameTimer_Tick(object? sender, EventArgs e)
        {
            if (!_capture.IsCapturing) return;

            var frame = _capture.GetFrame();
            if (frame != null)
            {
                var oldImage = _videoDisplay.Image;
                _videoDisplay.Image = frame;
                oldImage?.Dispose();
            }
        }

        // ====================================================================
        // View menu - capture device selection
        // ====================================================================

        private void ViewMenu_Opening(object? sender, EventArgs e)
        {
            _viewMenu.DropDownItems.Clear();

            var devices = VideoCaptureHandler.GetDevices();

            if (devices.Count == 0)
            {
                var noDevices = new ToolStripMenuItem("No capture devices found")
                {
                    Enabled = false
                };
                _viewMenu.DropDownItems.Add(noDevices);
            }
            else
            {
                foreach (var device in devices)
                {
                    var item = new ToolStripMenuItem(device.Name);
                    var dev = device; // Capture for closure
                    item.Click += (s, ev) => SelectCaptureDevice(dev);
                    _viewMenu.DropDownItems.Add(item);
                }
            }

            // Separator + stop option if currently capturing
            if (_capture.IsCapturing)
            {
                _viewMenu.DropDownItems.Add(new ToolStripSeparator());
                var stop = new ToolStripMenuItem("Stop Capture");
                stop.Click += (s, ev) =>
                {
                    _capture.StopCapture();
                    _videoDisplay.Image = null;
                };
                _viewMenu.DropDownItems.Add(stop);
            }
        }

        private void SelectCaptureDevice(CaptureDeviceInfo device)
        {
            _capture.StopCapture();
            _videoDisplay.Image = null;

            if (!_capture.StartCapture(device))
            {
                MessageBox.Show($"Failed to start capture from:\n{device.Name}",
                    "Capture Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ====================================================================
        // Connection menu - serial port selection
        // ====================================================================

        private void ConnectionMenu_Opening(object? sender, EventArgs e)
        {
            _connectionMenu.DropDownItems.Clear();

            var ports = PicoSerialLink.GetAvailablePorts();

            if (ports.Length == 0)
            {
                var noPorts = new ToolStripMenuItem("No COM ports available")
                {
                    Enabled = false
                };
                _connectionMenu.DropDownItems.Add(noPorts);
            }
            else
            {
                foreach (var port in ports)
                {
                    var item = new ToolStripMenuItem(port);
                    var p = port; // Capture for closure
                    item.Checked = (_serial.IsConnected && _serial.PortName == p);
                    item.Click += (s, ev) =>
                    {
                        if (_serial.IsConnected && _serial.PortName == p)
                        {
                            _serial.Disconnect();
                        }
                        else
                        {
                            if (!_serial.Connect(p))
                            {
                                MessageBox.Show($"Failed to connect to {p}",
                                    "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    };
                    _connectionMenu.DropDownItems.Add(item);
                }
            }

            // Disconnect option
            if (_serial.IsConnected)
            {
                _connectionMenu.DropDownItems.Add(new ToolStripSeparator());
                var disconnect = new ToolStripMenuItem("Disconnect");
                disconnect.Click += (s, ev) => _serial.Disconnect();
                _connectionMenu.DropDownItems.Add(disconnect);
            }
        }

        // ====================================================================
        // Fullscreen toggle
        // ====================================================================

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
        }

        private void ToggleFullscreen()
        {
            if (_isFullscreen)
            {
                // Restore windowed mode
                FormBorderStyle = _prevBorderStyle;
                WindowState = _prevWindowState;
                _menuStrip.Visible = true;
                _statusStrip.Visible = true;
            }
            else
            {
                // Save current state
                _prevWindowState = WindowState;
                _prevBorderStyle = FormBorderStyle;

                // Go fullscreen
                _menuStrip.Visible = false;
                _statusStrip.Visible = false;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }

            _isFullscreen = !_isFullscreen;
        }

        // ====================================================================
        // Cleanup
        // ====================================================================

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _inputTimer.Stop();
            _frameTimer.Stop();

            _capture.Dispose();
            _serial.Dispose();
            _input.Dispose();
        }
    }
}
