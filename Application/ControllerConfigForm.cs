using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SDL2;

namespace SwitchBridge
{
    /// <summary>
    /// Controller configuration dialog. Lets users select an input device,
    /// load default mappings, and remap individual buttons by clicking on
    /// the binding value and pressing a new input.
    /// </summary>
    public class ControllerConfigForm : Form
    {
        private readonly SdlInputHandler _input;
        private InputMappingProfile _profile;

        // UI
        private readonly ComboBox _deviceCombo;
        private readonly Button _loadDefaultsBtn;
        private readonly Panel _mappingPanel;
        private readonly Button _okBtn;
        private readonly Button _cancelBtn;
        private readonly Button _saveBtn;
        private readonly Button _loadBtn;
        private readonly Label _statusLabel;

        // Mapping rows
        private readonly List<MappingRow> _buttonRows = new();
        private readonly List<StickMappingRow> _stickRows = new();

        // Rebinding state
        private MappingRow? _rebindingRow = null;
        private StickMappingRow? _rebindingStickRow = null;
        private bool _rebindingStickNegative = false;
        private readonly Timer _rebindPollTimer;

        /// <summary>
        /// The resulting profile after OK is clicked. Null if cancelled.
        /// </summary>
        public InputMappingProfile? ResultProfile { get; private set; }

        public ControllerConfigForm(SdlInputHandler input, InputMappingProfile currentProfile)
        {
            _input = input;
            _profile = CloneProfile(currentProfile);

            // Form setup
            Text = "Controller Configuration";
            Size = new Size(560, 680);
            MinimumSize = new Size(480, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            KeyPreview = true;

            // Device selection row
            var deviceLabel = new Label
            {
                Text = "Input Device:",
                Location = new Point(12, 15),
                AutoSize = true
            };
            Controls.Add(deviceLabel);

            _deviceCombo = new ComboBox
            {
                Location = new Point(110, 12),
                Width = 280,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _deviceCombo.SelectedIndexChanged += DeviceCombo_Changed;
            Controls.Add(_deviceCombo);

            _loadDefaultsBtn = new Button
            {
                Text = "Load Defaults",
                Location = new Point(400, 11),
                Width = 100
            };
            _loadDefaultsBtn.Click += LoadDefaults_Click;
            Controls.Add(_loadDefaultsBtn);

            // Status label (shows rebinding prompts)
            _statusLabel = new Label
            {
                Text = "Click a binding to remap it. Press Escape to clear.",
                Location = new Point(12, 42),
                AutoSize = true,
                ForeColor = Color.DarkGray
            };
            Controls.Add(_statusLabel);

            // Scrollable mapping panel
            _mappingPanel = new Panel
            {
                Location = new Point(12, 65),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_mappingPanel);

            // Bottom buttons
            _saveBtn = new Button { Text = "Save...", Width = 75 };
            _saveBtn.Click += Save_Click;

            _loadBtn = new Button { Text = "Load...", Width = 75 };
            _loadBtn.Click += Load_Click;

            _okBtn = new Button { Text = "OK", Width = 75, DialogResult = DialogResult.OK };
            _okBtn.Click += Ok_Click;

            _cancelBtn = new Button { Text = "Cancel", Width = 75, DialogResult = DialogResult.Cancel };

            var btnPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                Height = 40,
                Padding = new Padding(8, 4, 8, 4)
            };
            btnPanel.Controls.Add(_cancelBtn);
            btnPanel.Controls.Add(_okBtn);
            btnPanel.Controls.Add(_loadBtn);
            btnPanel.Controls.Add(_saveBtn);
            Controls.Add(btnPanel);

            AcceptButton = _okBtn;
            CancelButton = _cancelBtn;

            // Rebind polling timer (checks for gamepad input during rebinding)
            _rebindPollTimer = new Timer { Interval = 16 };
            _rebindPollTimer.Tick += RebindPoll_Tick;

            // Key handler for rebinding
            KeyDown += ConfigForm_KeyDown;

            // Populate
            PopulateDeviceList();
            BuildMappingRows();
            RefreshMappingDisplay();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _mappingPanel.Size = new Size(
                ClientSize.Width - 24,
                ClientSize.Height - 115
            );
        }

        // ====================================================================
        // Device selection
        // ====================================================================

        private void PopulateDeviceList()
        {
            _deviceCombo.Items.Clear();
            _deviceCombo.Items.Add("Keyboard + Mouse");

            // Enumerate connected SDL controllers
            int numJoysticks = SDL.SDL_NumJoysticks();
            for (int i = 0; i < numJoysticks; i++)
            {
                if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_TRUE)
                {
                    string name = SDL.SDL_GameControllerNameForIndex(i) ?? $"Controller {i}";
                    _deviceCombo.Items.Add(name);
                }
            }

            // Select based on current profile type
            if (_profile.DeviceType == InputDeviceType.KeyboardMouse)
            {
                _deviceCombo.SelectedIndex = 0;
            }
            else if (_deviceCombo.Items.Count > 1)
            {
                _deviceCombo.SelectedIndex = 1;
            }
            else
            {
                _deviceCombo.SelectedIndex = 0;
            }
        }

        private void DeviceCombo_Changed(object? sender, EventArgs e)
        {
            // Don't auto-replace mappings on device change - just note it
            // User can click "Load Defaults" explicitly
        }

        private void LoadDefaults_Click(object? sender, EventArgs e)
        {
            if (_deviceCombo.SelectedIndex == 0)
            {
                _profile = InputMappingProfile.CreateKeyboardMouseDefault();
            }
            else
            {
                string name = _deviceCombo.SelectedItem?.ToString() ?? "";
                _profile = InputMappingProfile.CreateDefaultForController(name);
            }

            RefreshMappingDisplay();
        }

        // ====================================================================
        // Build the mapping rows UI
        // ====================================================================

        private void BuildMappingRows()
        {
            _mappingPanel.Controls.Clear();
            _buttonRows.Clear();
            _stickRows.Clear();

            int y = 4;

            // Section header: Buttons
            y = AddSectionHeader("Buttons", y);

            foreach (SwitchButton btn in Enum.GetValues<SwitchButton>())
            {
                var row = new MappingRow(btn, _mappingPanel, y);
                row.ValueBox.Click += (s, e) => StartRebindButton(row);
                _buttonRows.Add(row);
                y += 28;
            }

            // Section header: Sticks
            y += 8;
            y = AddSectionHeader("Analog Sticks", y);

            foreach (SwitchStick stick in Enum.GetValues<SwitchStick>())
            {
                var row = new StickMappingRow(stick, _mappingPanel, y);
                row.NegBox.Click += (s, e) => StartRebindStickKey(row, true);
                row.PosBox.Click += (s, e) => StartRebindStickKey(row, false);
                _stickRows.Add(row);
                y += 28;
            }
        }

        private int AddSectionHeader(string text, int y)
        {
            var lbl = new Label
            {
                Text = text,
                Font = new Font(Font, FontStyle.Bold),
                Location = new Point(4, y),
                AutoSize = true
            };
            _mappingPanel.Controls.Add(lbl);
            return y + 22;
        }

        private void RefreshMappingDisplay()
        {
            foreach (var row in _buttonRows)
            {
                if (_profile.Buttons.TryGetValue(row.SwitchBtn, out var binding))
                {
                    row.ValueBox.Text = binding.DisplayName;
                    row.ValueBox.ForeColor = binding.SourceType == InputSourceType.None
                        ? Color.Gray : SystemColors.ControlText;
                }
                else
                {
                    row.ValueBox.Text = "(unbound)";
                    row.ValueBox.ForeColor = Color.Gray;
                }
            }

            foreach (var row in _stickRows)
            {
                if (_profile.Sticks.TryGetValue(row.Stick, out var binding))
                {
                    if (binding.UseKeys)
                    {
                        row.NegBox.Text = binding.DisplayName.Split('/')[0].Trim();
                        row.PosBox.Text = binding.DisplayName.Contains('/') ?
                            binding.DisplayName.Split('/')[1].Trim() : "";
                        row.NegBox.Visible = true;
                        row.PosBox.Visible = true;
                    }
                    else
                    {
                        row.NegBox.Text = binding.DisplayName;
                        row.PosBox.Text = "";
                        row.NegBox.Visible = true;
                        row.PosBox.Visible = false;
                    }
                }
                else
                {
                    row.NegBox.Text = "(unbound)";
                    row.PosBox.Text = "";
                    row.NegBox.Visible = true;
                    row.PosBox.Visible = false;
                }
            }
        }

        // ====================================================================
        // Rebinding logic
        // ====================================================================

        private void StartRebindButton(MappingRow row)
        {
            CancelRebind();
            _rebindingRow = row;
            row.ValueBox.Text = "[ Press input... ]";
            row.ValueBox.BackColor = Color.LightYellow;
            _statusLabel.Text = "Press a button, key, or mouse button to bind. Escape to clear.";
            _statusLabel.ForeColor = Color.DarkBlue;
            _rebindPollTimer.Start();
        }

        private void StartRebindStickKey(StickMappingRow row, bool negative)
        {
            CancelRebind();
            _rebindingStickRow = row;
            _rebindingStickNegative = negative;

            var box = negative ? row.NegBox : row.PosBox;
            box.Text = "[ Press key... ]";
            box.BackColor = Color.LightYellow;

            string dir = negative ? "negative (left/up)" : "positive (right/down)";
            _statusLabel.Text = $"Press a key for {dir} direction. Escape to clear.";
            _statusLabel.ForeColor = Color.DarkBlue;
            _rebindPollTimer.Start();
        }

        private void CancelRebind()
        {
            _rebindPollTimer.Stop();

            if (_rebindingRow != null)
            {
                _rebindingRow.ValueBox.BackColor = SystemColors.Control;
                _rebindingRow = null;
            }

            if (_rebindingStickRow != null)
            {
                _rebindingStickRow.NegBox.BackColor = SystemColors.Control;
                _rebindingStickRow.PosBox.BackColor = SystemColors.Control;
                _rebindingStickRow = null;
            }

            _statusLabel.Text = "Click a binding to remap it. Press Escape to clear.";
            _statusLabel.ForeColor = Color.DarkGray;
        }

        private void CompleteButtonRebind(InputBinding newBinding)
        {
            if (_rebindingRow == null) return;

            _profile.Buttons[_rebindingRow.SwitchBtn] = newBinding;
            CancelRebind();
            RefreshMappingDisplay();
        }

        private void CompleteStickKeyRebind(int scancode)
        {
            if (_rebindingStickRow == null) return;

            var stick = _rebindingStickRow.Stick;
            if (!_profile.Sticks.ContainsKey(stick))
            {
                _profile.Sticks[stick] = new StickAxisBinding { UseKeys = true };
            }

            var binding = _profile.Sticks[stick];
            binding.UseKeys = true;

            if (_rebindingStickNegative)
                binding.NegativeKey = scancode;
            else
                binding.PositiveKey = scancode;

            CancelRebind();
            RefreshMappingDisplay();
        }

        // Handle keyboard input during rebinding
        private void ConfigForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (_rebindingRow != null)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    // Clear the binding
                    CompleteButtonRebind(new InputBinding { SourceType = InputSourceType.None });
                }
                else
                {
                    // Map WinForms key to SDL scancode
                    int scancode = WinFormsKeyToSdlScancode(e.KeyCode);
                    if (scancode != 0)
                    {
                        CompleteButtonRebind(new InputBinding
                        {
                            SourceType = InputSourceType.KeyboardKey,
                            SourceId = scancode
                        });
                    }
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (_rebindingStickRow != null)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    CompleteStickKeyRebind(0);
                }
                else
                {
                    int scancode = WinFormsKeyToSdlScancode(e.KeyCode);
                    if (scancode != 0)
                    {
                        CompleteStickKeyRebind(scancode);
                    }
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        // Poll for gamepad button presses during rebinding
        private void RebindPoll_Tick(object? sender, EventArgs e)
        {
            if (_rebindingRow == null && _rebindingStickRow == null)
            {
                _rebindPollTimer.Stop();
                return;
            }

            // Poll SDL events
            while (SDL.SDL_PollEvent(out SDL.SDL_Event ev) != 0)
            {
                if (_rebindingRow != null)
                {
                    switch (ev.type)
                    {
                        case SDL.SDL_EventType.SDL_CONTROLLERBUTTONDOWN:
                            CompleteButtonRebind(new InputBinding
                            {
                                SourceType = InputSourceType.GamepadButton,
                                SourceId = ev.cbutton.button
                            });
                            return;

                        case SDL.SDL_EventType.SDL_CONTROLLERAXISMOTION:
                            // Only trigger on strong deflection
                            if (Math.Abs(ev.caxis.axisValue) > 24000)
                            {
                                CompleteButtonRebind(new InputBinding
                                {
                                    SourceType = InputSourceType.GamepadAxis,
                                    SourceId = ev.caxis.axis,
                                    AxisThreshold = ev.caxis.axisValue > 0 ? 16384 : -16384
                                });
                                return;
                            }
                            break;

                        case SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
                            CompleteButtonRebind(new InputBinding
                            {
                                SourceType = InputSourceType.MouseButton,
                                SourceId = ev.button.button
                            });
                            return;
                    }
                }
                else if (_rebindingStickRow != null)
                {
                    // For stick rebinding, we only accept keyboard keys or gamepad axes
                    switch (ev.type)
                    {
                        case SDL.SDL_EventType.SDL_CONTROLLERAXISMOTION:
                            if (Math.Abs(ev.caxis.axisValue) > 24000)
                            {
                                // Set this stick to gamepad axis mode
                                var stick = _rebindingStickRow.Stick;
                                _profile.Sticks[stick] = new StickAxisBinding
                                {
                                    UseKeys = false,
                                    GamepadAxis = ev.caxis.axis,
                                    Inverted = false
                                };
                                CancelRebind();
                                RefreshMappingDisplay();
                                return;
                            }
                            break;
                    }
                }
            }
        }

        // ====================================================================
        // Save / Load
        // ====================================================================

        private void Save_Click(object? sender, EventArgs e)
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                DefaultExt = "json",
                FileName = $"{_profile.Name}.json"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _profile.SaveToFile(dlg.FileName);
            }
        }

        private void Load_Click(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                var loaded = InputMappingProfile.LoadFromFile(dlg.FileName);
                if (loaded != null)
                {
                    _profile = loaded;
                    RefreshMappingDisplay();
                }
                else
                {
                    MessageBox.Show("Failed to load mapping file.",
                        "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void Ok_Click(object? sender, EventArgs e)
        {
            ResultProfile = _profile;
        }

        // ====================================================================
        // WinForms key -> SDL scancode mapping
        // ====================================================================

        private static int WinFormsKeyToSdlScancode(Keys key)
        {
            // Map common keys. This covers the most-used keys; extend as needed.
            return key switch
            {
                Keys.A => (int)SDL.SDL_Scancode.SDL_SCANCODE_A,
                Keys.B => (int)SDL.SDL_Scancode.SDL_SCANCODE_B,
                Keys.C => (int)SDL.SDL_Scancode.SDL_SCANCODE_C,
                Keys.D => (int)SDL.SDL_Scancode.SDL_SCANCODE_D,
                Keys.E => (int)SDL.SDL_Scancode.SDL_SCANCODE_E,
                Keys.F => (int)SDL.SDL_Scancode.SDL_SCANCODE_F,
                Keys.G => (int)SDL.SDL_Scancode.SDL_SCANCODE_G,
                Keys.H => (int)SDL.SDL_Scancode.SDL_SCANCODE_H,
                Keys.I => (int)SDL.SDL_Scancode.SDL_SCANCODE_I,
                Keys.J => (int)SDL.SDL_Scancode.SDL_SCANCODE_J,
                Keys.K => (int)SDL.SDL_Scancode.SDL_SCANCODE_K,
                Keys.L => (int)SDL.SDL_Scancode.SDL_SCANCODE_L,
                Keys.M => (int)SDL.SDL_Scancode.SDL_SCANCODE_M,
                Keys.N => (int)SDL.SDL_Scancode.SDL_SCANCODE_N,
                Keys.O => (int)SDL.SDL_Scancode.SDL_SCANCODE_O,
                Keys.P => (int)SDL.SDL_Scancode.SDL_SCANCODE_P,
                Keys.Q => (int)SDL.SDL_Scancode.SDL_SCANCODE_Q,
                Keys.R => (int)SDL.SDL_Scancode.SDL_SCANCODE_R,
                Keys.S => (int)SDL.SDL_Scancode.SDL_SCANCODE_S,
                Keys.T => (int)SDL.SDL_Scancode.SDL_SCANCODE_T,
                Keys.U => (int)SDL.SDL_Scancode.SDL_SCANCODE_U,
                Keys.V => (int)SDL.SDL_Scancode.SDL_SCANCODE_V,
                Keys.W => (int)SDL.SDL_Scancode.SDL_SCANCODE_W,
                Keys.X => (int)SDL.SDL_Scancode.SDL_SCANCODE_X,
                Keys.Y => (int)SDL.SDL_Scancode.SDL_SCANCODE_Y,
                Keys.Z => (int)SDL.SDL_Scancode.SDL_SCANCODE_Z,
                Keys.D0 => (int)SDL.SDL_Scancode.SDL_SCANCODE_0,
                Keys.D1 => (int)SDL.SDL_Scancode.SDL_SCANCODE_1,
                Keys.D2 => (int)SDL.SDL_Scancode.SDL_SCANCODE_2,
                Keys.D3 => (int)SDL.SDL_Scancode.SDL_SCANCODE_3,
                Keys.D4 => (int)SDL.SDL_Scancode.SDL_SCANCODE_4,
                Keys.D5 => (int)SDL.SDL_Scancode.SDL_SCANCODE_5,
                Keys.D6 => (int)SDL.SDL_Scancode.SDL_SCANCODE_6,
                Keys.D7 => (int)SDL.SDL_Scancode.SDL_SCANCODE_7,
                Keys.D8 => (int)SDL.SDL_Scancode.SDL_SCANCODE_8,
                Keys.D9 => (int)SDL.SDL_Scancode.SDL_SCANCODE_9,
                Keys.F1 => (int)SDL.SDL_Scancode.SDL_SCANCODE_F1,
                Keys.F2 => (int)SDL.SDL_Scancode.SDL_SCANCODE_F2,
                Keys.F3 => (int)SDL.SDL_Scancode.SDL_SCANCODE_F3,
                Keys.F4 => (int)SDL.SDL_Scancode.SDL_SCANCODE_F4,
                Keys.F5 => (int)SDL.SDL_Scancode.SDL_SCANCODE_F5,
                Keys.F6 => (int)SDL.SDL_Scancode.SDL_SCANCODE_F6,
                Keys.F7 => (int)SDL.SDL_Scancode.SDL_SCANCODE_F7,
                Keys.F8 => (int)SDL.SDL_Scancode.SDL_SCANCODE_F8,
                Keys.F9 => (int)SDL.SDL_Scancode.SDL_SCANCODE_F9,
                Keys.F10 => (int)SDL.SDL_Scancode.SDL_SCANCODE_F10,
                Keys.F11 => (int)SDL.SDL_Scancode.SDL_SCANCODE_F11,
                Keys.F12 => (int)SDL.SDL_Scancode.SDL_SCANCODE_F12,
                Keys.Space => (int)SDL.SDL_Scancode.SDL_SCANCODE_SPACE,
                Keys.Return => (int)SDL.SDL_Scancode.SDL_SCANCODE_RETURN,
                Keys.Back => (int)SDL.SDL_Scancode.SDL_SCANCODE_BACKSPACE,
                Keys.Tab => (int)SDL.SDL_Scancode.SDL_SCANCODE_TAB,
                Keys.LShiftKey => (int)SDL.SDL_Scancode.SDL_SCANCODE_LSHIFT,
                Keys.RShiftKey => (int)SDL.SDL_Scancode.SDL_SCANCODE_RSHIFT,
                Keys.LControlKey => (int)SDL.SDL_Scancode.SDL_SCANCODE_LCTRL,
                Keys.RControlKey => (int)SDL.SDL_Scancode.SDL_SCANCODE_RCTRL,
                Keys.LMenu => (int)SDL.SDL_Scancode.SDL_SCANCODE_LALT,
                Keys.RMenu => (int)SDL.SDL_Scancode.SDL_SCANCODE_RALT,
                Keys.Up => (int)SDL.SDL_Scancode.SDL_SCANCODE_UP,
                Keys.Down => (int)SDL.SDL_Scancode.SDL_SCANCODE_DOWN,
                Keys.Left => (int)SDL.SDL_Scancode.SDL_SCANCODE_LEFT,
                Keys.Right => (int)SDL.SDL_Scancode.SDL_SCANCODE_RIGHT,
                Keys.Home => (int)SDL.SDL_Scancode.SDL_SCANCODE_HOME,
                Keys.End => (int)SDL.SDL_Scancode.SDL_SCANCODE_END,
                Keys.Insert => (int)SDL.SDL_Scancode.SDL_SCANCODE_INSERT,
                Keys.Delete => (int)SDL.SDL_Scancode.SDL_SCANCODE_DELETE,
                Keys.PageUp => (int)SDL.SDL_Scancode.SDL_SCANCODE_PAGEUP,
                Keys.PageDown => (int)SDL.SDL_Scancode.SDL_SCANCODE_PAGEDOWN,
                Keys.OemMinus => (int)SDL.SDL_Scancode.SDL_SCANCODE_MINUS,
                Keys.Oemplus => (int)SDL.SDL_Scancode.SDL_SCANCODE_EQUALS,
                Keys.OemOpenBrackets => (int)SDL.SDL_Scancode.SDL_SCANCODE_LEFTBRACKET,
                Keys.OemCloseBrackets => (int)SDL.SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET,
                Keys.OemSemicolon => (int)SDL.SDL_Scancode.SDL_SCANCODE_SEMICOLON,
                Keys.OemQuotes => (int)SDL.SDL_Scancode.SDL_SCANCODE_APOSTROPHE,
                Keys.Oemcomma => (int)SDL.SDL_Scancode.SDL_SCANCODE_COMMA,
                Keys.OemPeriod => (int)SDL.SDL_Scancode.SDL_SCANCODE_PERIOD,
                Keys.OemQuestion => (int)SDL.SDL_Scancode.SDL_SCANCODE_SLASH,
                Keys.OemBackslash or Keys.OemPipe => (int)SDL.SDL_Scancode.SDL_SCANCODE_BACKSLASH,
                Keys.Oemtilde => (int)SDL.SDL_Scancode.SDL_SCANCODE_GRAVE,
                _ => 0
            };
        }

        // ====================================================================
        // Helper: deep clone a profile
        // ====================================================================

        private static InputMappingProfile CloneProfile(InputMappingProfile src)
        {
            var json = src.ToJson();
            return InputMappingProfile.FromJson(json) ?? InputMappingProfile.CreateXInputDefault();
        }

        // ====================================================================
        // Inner classes for mapping row UI elements
        // ====================================================================

        /// <summary>
        /// A single button mapping row in the config panel.
        /// Shows: [Switch Button Label]  [Assigned Input (clickable)]
        /// </summary>
        private class MappingRow
        {
            public SwitchButton SwitchBtn { get; }
            public Label NameLabel { get; }
            public Label ValueBox { get; }

            public MappingRow(SwitchButton btn, Panel parent, int y)
            {
                SwitchBtn = btn;

                NameLabel = new Label
                {
                    Text = FormatButtonName(btn),
                    Location = new Point(8, y + 3),
                    Width = 120,
                    TextAlign = ContentAlignment.MiddleRight
                };

                ValueBox = new Label
                {
                    Text = "(unbound)",
                    Location = new Point(136, y),
                    Width = 200,
                    Height = 24,
                    BorderStyle = BorderStyle.FixedSingle,
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = SystemColors.Control,
                    Cursor = Cursors.Hand
                };

                parent.Controls.Add(NameLabel);
                parent.Controls.Add(ValueBox);
            }

            private static string FormatButtonName(SwitchButton btn)
            {
                return btn switch
                {
                    SwitchButton.A => "A",
                    SwitchButton.B => "B",
                    SwitchButton.X => "X",
                    SwitchButton.Y => "Y",
                    SwitchButton.L => "L",
                    SwitchButton.R => "R",
                    SwitchButton.ZL => "ZL",
                    SwitchButton.ZR => "ZR",
                    SwitchButton.Plus => "+ (Plus)",
                    SwitchButton.Minus => "- (Minus)",
                    SwitchButton.Home => "Home",
                    SwitchButton.Capture => "Capture",
                    SwitchButton.LStick => "L Stick Click",
                    SwitchButton.RStick => "R Stick Click",
                    SwitchButton.DPadUp => "D-Pad Up",
                    SwitchButton.DPadDown => "D-Pad Down",
                    SwitchButton.DPadLeft => "D-Pad Left",
                    SwitchButton.DPadRight => "D-Pad Right",
                    _ => btn.ToString()
                };
            }
        }

        /// <summary>
        /// A stick axis mapping row. Shows the axis name and either a single
        /// gamepad axis label or two key labels (negative / positive).
        /// </summary>
        private class StickMappingRow
        {
            public SwitchStick Stick { get; }
            public Label NameLabel { get; }
            public Label NegBox { get; }
            public Label PosBox { get; }

            public StickMappingRow(SwitchStick stick, Panel parent, int y)
            {
                Stick = stick;

                string name = stick switch
                {
                    SwitchStick.LeftStickX => "Left Stick X",
                    SwitchStick.LeftStickY => "Left Stick Y",
                    SwitchStick.RightStickX => "Right Stick X",
                    SwitchStick.RightStickY => "Right Stick Y",
                    _ => stick.ToString()
                };

                NameLabel = new Label
                {
                    Text = name,
                    Location = new Point(8, y + 3),
                    Width = 120,
                    TextAlign = ContentAlignment.MiddleRight
                };

                NegBox = new Label
                {
                    Text = "",
                    Location = new Point(136, y),
                    Width = 95,
                    Height = 24,
                    BorderStyle = BorderStyle.FixedSingle,
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = SystemColors.Control,
                    Cursor = Cursors.Hand
                };

                PosBox = new Label
                {
                    Text = "",
                    Location = new Point(240, y),
                    Width = 95,
                    Height = 24,
                    BorderStyle = BorderStyle.FixedSingle,
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = SystemColors.Control,
                    Cursor = Cursors.Hand
                };

                parent.Controls.Add(NameLabel);
                parent.Controls.Add(NegBox);
                parent.Controls.Add(PosBox);
            }
        }
    }
}
