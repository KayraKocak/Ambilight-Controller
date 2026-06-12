using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace AmbilightControllerForm
{
    public partial class Form1 : Form
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        public static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        public static extern uint TimeEndPeriod(uint uMilliseconds);

        // State variables
        private Color currentRgb = Color.FromArgb(59, 130, 246); // Default: Blue
        private bool isSystemOn = true;
        private bool isConnected = false;
        private bool isAmbilightActive = false;
        private bool isRainbowActive = false;
        private int rainbowHue = 0;
        private AddressableAnimationType currentAnimationType = AddressableAnimationType.SolidRainbow;
        private int animationTick = 0;

        // Hardware Streaming variables
        private SerialPort serialPort;
        private UdpClient udpClient;
        private IPEndPoint udpTarget;      // port 7778 - main live data
        private IPEndPoint udpTarget7777;   // port 7777 - preset/static color selection
        private bool useUdp = false;
        private int streamsSentThisSecond = 0;

        // Custom Titlebar dragging variables
        private bool isDraggingWindow = false;
        private Point dragStartPoint = new Point(0, 0);

        // Canvas Color Wheel variables
        private Bitmap colorWheelBmp;
        private Point selectedWheelPoint = new Point(0, 0);
        private bool isDraggingWheelPin = false;

        // GUI controls reference
        private Panel titleBar;
        private Label lblTitle;
        private Button btnMin;

        private Button btnClose;

        private Panel panelVisualizerGlow;
        private Panel panelVisualizer;
        private Button btnConnect;

        private PictureBox pboxColorWheel;
        private Label lblRgb;
        private Label lblHex;
        private Label lblDataPacket;
        private Color lastSentColor = Color.Black;
        private Panel panelColorPreview;

        private GlassSlider trkBrightness;
        private GlassSlider trkR;
        private GlassSlider trkG;
        private GlassSlider trkB;
        private bool isUpdatingSliders = false;

        private CalibrationProfile currentCalibProfile = null;
        private CalibrationProfile[] calibProfiles = new CalibrationProfile[] {
            new CalibrationProfile { Point100 = ColorTranslator.FromHtml("#FFA777") },
            new CalibrationProfile { Point100 = ColorTranslator.FromHtml("#FFD0A0") },
            new CalibrationProfile { Point100 = ColorTranslator.FromHtml("#FFECE0") }
        };
        private float currentHue = 0f;
        private float currentSat = 0f;

        private Button btnOnOff;
        private Button btnToggleAmbilight;
        private Button btnRainbow;

        private System.Windows.Forms.Timer timerRainbow;
        private FlowLayoutPanel presetsFlow;

        // Ambilight Brightness+Saturation 2D Curve
        // ambilightCurve[i]: X values (brightness output) - same behavior as old slider
        // ambilightSatCurve[i]: Y values (saturation multiplier 0..2 = 0..200%)
        private float[] ambilightCurve    = new float[4] { 0f, 0.33f, 0.66f, 1f };
        private float[] ambilightSatCurve = new float[4] { 1f, 1f, 1f, 1f }; // default 100% = no effect
        private BrightnessSaturationGraph curveGraph;

        // Ambilight Cosmetic Smoothing
        private int currentSmoothThreshold = 50;
        private int currentSmoothFrames = 5;
        private GlassSlider trkSmoothThreshold;
        private NumericUpDown numSmoothFrames;
        private Label lblSmoothFrames;
        private Label lblSmoothThreshold;

        // Addressable RGB Feature
        private bool isAddressableMode = false;
        private int addressablePixelCount = 20;
        private Color[] segmentColors = new Color[500];
        private HashSet<int> selectedSegments = new HashSet<int>();
        private int lastHoveredSegment = -1;
        private bool isSelecting = true;

        private Button btnModeToggle;
        private Label lblPixelCount;
        private NumericUpDown numPixelCount;
        private bool invertPixelOrder = false;
        private RadioButton radInvertOrder;
        private Button btnAddressableSettings;
        private Rectangle[] addressableCaptureAreas;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }

        private async void LoadAndBlurBackgroundAsync()
        {
            string bgPath = Path.Combine(Application.StartupPath, "BG.png");
            if (!File.Exists(bgPath)) return;

            int targetW = this.ClientSize.Width > 0 ? this.ClientSize.Width : 1000;
            int targetH = this.ClientSize.Height > 0 ? this.ClientSize.Height : 680;

            try
            {
                Image bgImage = null;
                await Task.Run(() =>
                {
                    using (Image img = Image.FromFile(bgPath))
                    {
                        int scale = 8;
                        using (Bitmap small = new Bitmap(targetW / scale, targetH / scale))
                        {
                            using (Graphics g = Graphics.FromImage(small))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                
                                double ratioX = (double)small.Width / img.Width;
                                double ratioY = (double)small.Height / img.Height;
                                double ratio = Math.Max(ratioX, ratioY); // Max for "Zoom to fit" (Cover)
                                
                                int drawWidth = (int)Math.Ceiling(img.Width * ratio) + 2;
                                int drawHeight = (int)Math.Ceiling(img.Height * ratio) + 2;
                                int drawX = (small.Width - drawWidth) / 2 - 1;
                                int drawY = (small.Height - drawHeight) / 2 - 1;
                                
                                g.DrawImage(img, drawX, drawY, drawWidth, drawHeight);
                            }

                            bgImage = new Bitmap(targetW, targetH);
                            using (Graphics g2 = Graphics.FromImage(bgImage))
                            {
                                g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                // Overscan slightly to avoid HighQualityBicubic edge transparency bleeding
                                g2.DrawImage(small, -4, -4, bgImage.Width + 8, bgImage.Height + 8);

                                using (SolidBrush darkBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                                {
                                    g2.FillRectangle(darkBrush, 0, 0, bgImage.Width, bgImage.Height);
                                }
                            }
                        }
                    }
                });

                this.BackgroundImage = bgImage;
                this.BackgroundImageLayout = ImageLayout.None;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to load background: " + ex.Message);
            }
        }

        public Form1()
        {
            SetupCustomStyles();
            InitializeCustomComponents();
            InitializeAmbilightCurveUI();
            GenerateColorWheel();
            
            // Set initial selector position
            selectedWheelPoint = GetWheelCoordinatesFromColor(currentRgb, pboxColorWheel);
            UpdateActiveColor(currentRgb);

            LoadAndBlurBackgroundAsync();

            // Asynchronously check for updates
            Task.Run(() => CheckForUpdates());
        }

        private void CheckForUpdates()
        {
            try
            {
                // Walk up to find the project root containing updater.py and version.txt
                string rootPath = AppDomain.CurrentDomain.BaseDirectory;
                while (!string.IsNullOrEmpty(rootPath) && 
                       !File.Exists(Path.Combine(rootPath, "updater.py")) && 
                       !File.Exists(Path.Combine(rootPath, "run.bat")))
                {
                    rootPath = Path.GetDirectoryName(rootPath);
                }
                if (string.IsNullOrEmpty(rootPath))
                {
                    rootPath = AppDomain.CurrentDomain.BaseDirectory;
                }

                string localVersionStr = "1.0";
                string versionPath = Path.Combine(rootPath, "version.txt");
                if (File.Exists(versionPath))
                {
                    string content = File.ReadAllText(versionPath);
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"version:\s*([\d\.]+)");
                    if (match.Success)
                    {
                        localVersionStr = match.Groups[1].Value;
                    }
                }

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("AmbilightControllerUpdater");
                    string remoteContent = client.GetStringAsync("https://raw.githubusercontent.com/KayraKocak/Ambilight-Controller/main/version.txt").Result;
                    var match = System.Text.RegularExpressions.Regex.Match(remoteContent, @"version:\s*([\d\.]+)");
                    if (match.Success)
                    {
                        string remoteVersionStr = match.Groups[1].Value;
                        if (double.TryParse(remoteVersionStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double remoteVersion) &&
                            double.TryParse(localVersionStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double localVersion))
                        {
                            if (remoteVersion > localVersion)
                            {
                                string updaterPath = Path.Combine(rootPath, "updater.py");
                                if (File.Exists(updaterPath))
                                {
                                    var psi = new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = "python",
                                        Arguments = $"\"{updaterPath}\"",
                                        UseShellExecute = true,
                                        WorkingDirectory = rootPath
                                    };
                                    System.Diagnostics.Process.Start(psi);
                                    
                                    // Exit application to allow updates to run
                                    Application.Exit();
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fail silently to avoid interrupting normal usage if offline
            }
        }

        private Bitmap GenerateAddressablePreview(Color[] colors, int width, int height)
        {
            if (colors == null || colors.Length == 0) return null;
            Bitmap bmp = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                float segmentWidth = (float)width / colors.Length;
                for (int i = 0; i < colors.Length; i++)
                {
                    RectangleF rect = new RectangleF(i * segmentWidth, 0, segmentWidth, height);
                    using (SolidBrush b = new SolidBrush(colors[i]))
                    {
                        g.FillRectangle(b, rect);
                    }
                }
            }
            return bmp;
        }

        private void UpdatePresetButtonState(Button btn)
        {
            Color c = btn.BackColor;
            bool isEmpty = (c.R == 24 && c.G == 25 && c.B == 28) && btn.Tag == null;
            
            btn.FlatAppearance.BorderSize = isEmpty ? 1 : 0;
            btn.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);

            if (btn.Tag is Color[] addrColors)
            {
                Image oldImg = btn.BackgroundImage;
                btn.BackgroundImage = GenerateAddressablePreview(addrColors, btn.Width, btn.Height);
                if (oldImg != null) oldImg.Dispose();
            }
            else
            {
                if (btn.BackgroundImage != null)
                {
                    btn.BackgroundImage.Dispose();
                    btn.BackgroundImage = null;
                }
            }
        }

        private void SavePresets()
        {
            if (presetsFlow == null) return;
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presets.txt");
                List<string> hexList = new List<string>();
                foreach (Control ctrl in presetsFlow.Controls)
                {
                    if (ctrl is Button btn)
                    {
                        if (btn.Tag is Color[] addrColors)
                        {
                            System.Text.StringBuilder sb = new System.Text.StringBuilder("ADDR:");
                            for (int i = 0; i < addrColors.Length; i++)
                            {
                                sb.Append($"#{addrColors[i].R:X2}{addrColors[i].G:X2}{addrColors[i].B:X2}");
                                if (i < addrColors.Length - 1) sb.Append(",");
                            }
                            hexList.Add(sb.ToString());
                        }
                        else
                        {
                            Color c = btn.BackColor;
                            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                            hexList.Add(hex);
                        }
                    }
                }
                File.WriteAllLines(filePath, hexList);
            }
            catch
            {
                // Fail silently (e.g. read-only filesystem or access restrictions)
            }
        }

        private void LoadAmbilightCurve()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ambilight_curve.txt");
                if (File.Exists(path))
                {
                    string[] parts = File.ReadAllText(path).Split(',');
                    // New format: 8 values (4 brightness X + 4 saturation Y)
                    if (parts.Length == 8)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            if (float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
                                ambilightCurve[i] = val;
                            if (float.TryParse(parts[i + 4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sat))
                                ambilightSatCurve[i] = sat;
                        }
                    }
                    // Legacy: 4 values (brightness only, set sat to defaults)
                    else if (parts.Length == 4)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            if (float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
                                ambilightCurve[i] = val;
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveAmbilightCurve()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ambilight_curve.txt");
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                string content = string.Join(",", new string[] {
                    ambilightCurve[0].ToString(ic),    ambilightCurve[1].ToString(ic),
                    ambilightCurve[2].ToString(ic),    ambilightCurve[3].ToString(ic),
                    ambilightSatCurve[0].ToString(ic), ambilightSatCurve[1].ToString(ic),
                    ambilightSatCurve[2].ToString(ic), ambilightSatCurve[3].ToString(ic)
                });
                File.WriteAllText(path, content);
            }
            catch { }
        }

        private void LoadSmoothSettings()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smooth_settings.txt");
                if (File.Exists(path))
                {
                    string[] parts = File.ReadAllText(path).Split(',');
                    if (parts.Length == 2)
                    {
                        if (int.TryParse(parts[0], out int thresh)) currentSmoothThreshold = thresh;
                        if (int.TryParse(parts[1], out int frames)) currentSmoothFrames = frames;
                    }
                }
            }
            catch { }
        }

        private void SaveSmoothSettings()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smooth_settings.txt");
                File.WriteAllText(path, $"{currentSmoothThreshold},{currentSmoothFrames}");
            }
            catch { }
        }

        private void LoadAddressableSettings()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "addressable_settings.txt");
                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path);
                    if (lines.Length >= 3)
                    {
                        if (bool.TryParse(lines[0], out bool mode)) isAddressableMode = mode;
                        if (int.TryParse(lines[1], out int count)) addressablePixelCount = count;
                        if (bool.TryParse(lines[2], out bool invert)) invertPixelOrder = invert;
                    }
                }

                string areasPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "addressable_areas.txt");
                if (File.Exists(areasPath))
                {
                    string[] lines = File.ReadAllLines(areasPath);
                    if (lines.Length == addressablePixelCount)
                    {
                        addressableCaptureAreas = new Rectangle[addressablePixelCount];
                        for (int i = 0; i < addressablePixelCount; i++)
                        {
                            string[] parts = lines[i].Split(',');
                            if (parts.Length == 4 && 
                                int.TryParse(parts[0], out int x) && 
                                int.TryParse(parts[1], out int y) && 
                                int.TryParse(parts[2], out int w) && 
                                int.TryParse(parts[3], out int h))
                            {
                                addressableCaptureAreas[i] = new Rectangle(x, y, w, h);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveAddressableSettings()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "addressable_settings.txt");
                File.WriteAllLines(path, new string[] {
                    isAddressableMode.ToString(),
                    addressablePixelCount.ToString(),
                    invertPixelOrder.ToString()
                });

                if (addressableCaptureAreas != null && addressableCaptureAreas.Length == addressablePixelCount)
                {
                    string areasPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "addressable_areas.txt");
                    string[] lines = new string[addressablePixelCount];
                    for (int i = 0; i < addressablePixelCount; i++)
                    {
                        Rectangle r = addressableCaptureAreas[i];
                        lines[i] = $"{r.X},{r.Y},{r.Width},{r.Height}";
                    }
                    File.WriteAllLines(areasPath, lines);
                }
            }
            catch { }
        }

        private void InitializeAmbilightCurveUI()
        {
            LoadAmbilightCurve();
            LoadSmoothSettings();

            // 2D Brightness-Saturation Graph
            curveGraph = new BrightnessSaturationGraph
            {
                Location = new Point(200, 325),
                Size = new Size(530, 120),
                Visible = false
            };
            curveGraph.SetValues(ambilightCurve, ambilightSatCurve);
            curveGraph.ValuesChanged += (brightVals, satVals) =>
            {
                for (int i = 0; i < 4; i++)
                {
                    ambilightCurve[i]    = brightVals[i];
                    ambilightSatCurve[i] = satVals[i];
                }
                SaveAmbilightCurve();
            };

            this.Controls.Add(curveGraph);
            curveGraph.BringToFront();

            // Smoothing controls
            lblSmoothThreshold = new Label
            {
                Text = $"Smoothing Threshold: {currentSmoothThreshold}",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 120),
                Location = new Point(200, 455),
                AutoSize = true,
                Visible = false
            };
            
            trkSmoothThreshold = new GlassSlider
            {
                Minimum = 0,
                Maximum = 765,
                Value = Math.Max(0, Math.Min(765, currentSmoothThreshold)),
                Location = new Point(200, 473),
                Size = new Size(320, 30),
                Visible = false
            };
            trkSmoothThreshold.Scroll += (s, e) => {
                lblSmoothThreshold.Text = $"Smoothing Threshold: {trkSmoothThreshold.Value}";
                currentSmoothThreshold = trkSmoothThreshold.Value;
                SaveSmoothSettings();
            };

            lblSmoothFrames = new Label
            {
                Text = $"Frames to Average: {currentSmoothFrames}",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 120),
                Location = new Point(550, 455),
                AutoSize = true,
                Visible = false
            };

            numSmoothFrames = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 100,
                Value = Math.Max(1, Math.Min(100, currentSmoothFrames)),
                Location = new Point(550, 473),
                Size = new Size(120, 25),
                BackColor = Color.FromArgb(35, 36, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f),
                Visible = false
            };
            numSmoothFrames.ValueChanged += (s, e) => {
                lblSmoothFrames.Text = $"Frames to Average: {numSmoothFrames.Value}";
                currentSmoothFrames = (int)numSmoothFrames.Value;
                SaveSmoothSettings();
            };
            
            this.Controls.Add(lblSmoothThreshold);
            this.Controls.Add(trkSmoothThreshold);
            this.Controls.Add(lblSmoothFrames);
            this.Controls.Add(numSmoothFrames);

            lblSmoothThreshold.BringToFront();
            trkSmoothThreshold.BringToFront();
            lblSmoothFrames.BringToFront();
            numSmoothFrames.BringToFront();
        }

        private string[] GetDefaultPresets()
        {
            return new string[] {
                "#3882F6", "#4D96FF", "#7B8FA1", "#FF7F3F",
                "#FFC93C", "#9ED763", "#10B981", "#0EA5E9",
                "#F87171", "#F472B6", "#A855F7", "#6366F1",
                "#6B7280", "#18191c", "#18191c", "#18191c"
            };
        }

        private void SetupCustomStyles()
        {
            this.Size = new Size(1000, 680);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(19, 20, 23); // #131417 (Deep slate)
            this.DoubleBuffered = true;
        }

        private void InitializeCustomComponents()
        {
            // ==========================================
            // 1. CUSTOM SYSTEM TITLEBAR (FRAMELESS)
            // ==========================================
            titleBar = new Panel
            {
                Size = new Size(this.Width, 40),
                Location = new Point(0, 0),
                BackColor = Color.Transparent
            };
            titleBar.MouseDown += TitleBar_MouseDown;
            titleBar.MouseMove += TitleBar_MouseMove;
            titleBar.MouseUp += TitleBar_MouseUp;

            lblTitle = new Label
            {
                Text = "LIGHT & AMBILIGHT CONTROLLER",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 120),
                Location = new Point(16, 12),
                AutoSize = true
            };
            // Support dragging from label too
            lblTitle.MouseDown += (s, e) => TitleBar_MouseDown(titleBar, e);
            lblTitle.MouseMove += (s, e) => TitleBar_MouseMove(titleBar, e);
            lblTitle.MouseUp += (s, e) => TitleBar_MouseUp(titleBar, e);

            btnClose = CreateTitleBarButton("✕", this.Width - 36, Color.FromArgb(232, 17, 35));
            btnClose.Click += (s, e) => this.Close();

            btnMin = CreateTitleBarButton("—", this.Width - 68, Color.FromArgb(40, 40, 40));
            btnMin.Click += (s, e) => this.WindowState = FormWindowState.Minimized;

            titleBar.Controls.AddRange(new Control[] { lblTitle, btnClose, btnMin });
            this.Controls.Add(titleBar);

            // ==========================================
            // 2. LEFT SIDEBAR: CONTROLS & ACTIONS
            // ==========================================
            Panel leftPanel = CreateContainerPanel(20, 60, 140, 600);

            // ACTIONS section
            Label lblActionsTitle = CreateSectionHeader("ACTIONS", 12, 12);
            leftPanel.Controls.Add(lblActionsTitle);

            btnOnOff = CreateDashboardButton("ON/OFF", "System Power Switch", 12, 36, true, 116);
            btnOnOff.Click += BtnOnOff_Click;

            btnToggleAmbilight = CreateDashboardButton("AMBILIGHT", "Sync screen colors", 12, 92, false, 116);
            btnToggleAmbilight.Click += BtnToggleAmbilight_Click;

            btnRainbow = CreateDashboardButton("RAINBOW", "Slow spectrum cycle", 12, 148, false, 116);
            btnRainbow.Click += BtnRainbow_Click;

            btnModeToggle = new Button
            {
                Text = "Generic RGB",
                Location = new Point(12, 450),
                Size = new Size(116, 25),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(45, 46, 50),
                Font = new Font("Segoe UI", 8f)
            };
            btnModeToggle.FlatAppearance.BorderSize = 0;
            btnModeToggle.Click += BtnModeToggle_Click;

            lblPixelCount = new Label
            {
                Text = "Pixel Count",
                Location = new Point(12, 485),
                Size = new Size(60, 25),
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8f),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };

            numPixelCount = new NumericUpDown
            {
                Location = new Point(75, 487),
                Size = new Size(53, 20),
                Minimum = 1,
                Maximum = 500,
                Value = 20,
                BackColor = Color.FromArgb(45, 46, 50),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };
            numPixelCount.ValueChanged += NumPixelCount_ValueChanged;

            radInvertOrder = new RadioButton
            {
                Text = "Invert pixel order",
                Location = new Point(12, 520),
                Size = new Size(120, 20),
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 7.5f),
                Visible = false,
                AutoCheck = false
            };
            radInvertOrder.Click += (s, e) => {
                radInvertOrder.Checked = !radInvertOrder.Checked;
                invertPixelOrder = radInvertOrder.Checked;
                SaveAddressableSettings();
                if (isAddressableMode) SendAddressableDataToHardware();
            };

            btnAddressableSettings = new Button
            {
                Text = "Capture Settings",
                Location = new Point(12, 550),
                Size = new Size(116, 25),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(35, 36, 40),
                Font = new Font("Segoe UI", 8f),
                Visible = false
            };
            btnAddressableSettings.FlatAppearance.BorderSize = 0;
            btnAddressableSettings.Click += BtnAddressableSettings_Click;

            leftPanel.Controls.AddRange(new Control[] { btnOnOff, btnToggleAmbilight, btnRainbow, btnModeToggle, lblPixelCount, numPixelCount, radInvertOrder, btnAddressableSettings });
            this.Controls.Add(leftPanel);

            LoadAddressableSettings();
            btnModeToggle.Text = isAddressableMode ? "Addressable RGB" : "Generic RGB";
            lblPixelCount.Visible = isAddressableMode;
            numPixelCount.Visible = isAddressableMode;
            numPixelCount.Value = Math.Max(numPixelCount.Minimum, Math.Min(numPixelCount.Maximum, addressablePixelCount));
            radInvertOrder.Visible = isAddressableMode;
            radInvertOrder.Checked = invertPixelOrder;
            btnAddressableSettings.Visible = isAddressableMode;

            // ==========================================
            // 3. CENTER PANEL: VISUALIZER & CONNECT
            // ==========================================
            panelVisualizer = new Panel
            {
                Location = new Point(200, 60),
                Size = new Size(530, 60),
                BackColor = currentRgb
            };
            panelVisualizer.Paint += PanelVisualizer_Paint;
            panelVisualizer.MouseDown += PanelVisualizer_MouseDown;
            panelVisualizer.MouseMove += PanelVisualizer_MouseMove;
            panelVisualizer.MouseUp += PanelVisualizer_MouseUp;
            typeof(Panel).InvokeMember("DoubleBuffered", System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, panelVisualizer, new object[] { true });
            this.Controls.Add(panelVisualizer);

            for (int i = 0; i < segmentColors.Length; i++) segmentColors[i] = Color.Black;

            // Connection Settings Panel
            Panel connPanel = new Panel
            {
                Location = new Point(200, 135),
                Size = new Size(530, 180),
                BackColor = Color.Transparent
            };

            ComboBox cmbMode = new ComboBox
            {
                Location = new Point(0, 0),
                Size = new Size(530, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(35, 36, 40),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            cmbMode.Items.AddRange(new object[] { "Wired (USB Serial)", "Wireless (UDP)" });

            ComboBox cmbPorts = new ComboBox
            {
                Location = new Point(0, 35),
                Size = new Size(530, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(35, 36, 40),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            try { cmbPorts.Items.AddRange(SerialPort.GetPortNames()); } catch { }
            if (cmbPorts.Items.Count > 0) cmbPorts.SelectedIndex = 0;

            TextBox txtIpAddress = new TextBox
            {
                Location = new Point(0, 35),
                Size = new Size(530, 25),
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(35, 36, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Text = "192.168.1.100",
                Visible = false
            };

            cmbMode.SelectedIndexChanged += (s, e) => {
                bool isUdp = cmbMode.SelectedIndex == 1;
                useUdp = isUdp;
                cmbPorts.Visible = !isUdp;
                txtIpAddress.Visible = isUdp;
            };

            cmbMode.SelectedIndex = 1; // Default to Wireless (triggers SelectedIndexChanged)

            btnConnect = new Button
            {
                Text = "CONNECT",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Location = new Point(185, 80),
                Size = new Size(160, 48),
                BackColor = Color.FromArgb(0, 210, 255), // Bright cyan
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnConnect.FlatAppearance.BorderSize = 0;
            btnConnect.Click += (s, e) => {
                string port = cmbPorts.SelectedItem != null ? cmbPorts.SelectedItem.ToString() : "";
                BtnConnect_Click(port, txtIpAddress.Text);
            };

            Label lblStreamRate = new Label
            {
                Text = "STREAM: 0 SPS",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 120),
                Location = new Point(0, 134),
                Size = new Size(530, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false
            };

            lblDataPacket = new Label
            {
                Text = "PACKET: [0, 0, 0]",
                Font = new Font("Consolas", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 210, 255),
                Location = new Point(0, 154),
                Size = new Size(530, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false
            };

            System.Windows.Forms.Timer timerSps = new System.Windows.Forms.Timer { Interval = 1000 };
            timerSps.Tick += (s, e) => {
                lblStreamRate.Text = $"STREAM: {System.Threading.Interlocked.Exchange(ref streamsSentThisSecond, 0)} SPS";
            };
            timerSps.Start();

            connPanel.Controls.AddRange(new Control[] { cmbMode, cmbPorts, txtIpAddress, btnConnect, lblStreamRate, lblDataPacket });
            this.Controls.Add(connPanel);

            // ==========================================
            // 4. RIGHT PANEL: COLOR PICKER & PRESETS
            // ==========================================
            Panel rightPanel = CreateContainerPanel(770, 60, 210, 600);

            // Color wheel picture box container
            pboxColorWheel = new PictureBox
            {
                Location = new Point(25, 10),
                Size = new Size(120, 120),
                Cursor = Cursors.Cross,
                SizeMode = PictureBoxSizeMode.Normal
            };
            pboxColorWheel.Paint += PboxColorWheel_Paint;
            pboxColorWheel.MouseDown += PboxColorWheel_MouseDown;
            pboxColorWheel.MouseMove += PboxColorWheel_MouseMove;
            pboxColorWheel.MouseUp += PboxColorWheel_MouseUp;
            rightPanel.Controls.Add(pboxColorWheel);

            // Brightness vertical slider
            trkBrightness = new GlassSlider
            {
                Orientation = Orientation.Vertical,
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                Location = new Point(155, 10),
                Size = new Size(30, 120),
                FillColor = Color.White
            };
            trkBrightness.Scroll += TrkBrightness_Scroll;
            rightPanel.Controls.Add(trkBrightness);

            // RGB / Hex details layout
            lblRgb = new Label
            {
                Text = "RGB: 59, 130, 246",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 180, 180),
                Location = new Point(12, 140),
                AutoSize = true
            };
            lblHex = new Label
            {
                Text = "Hex: #3882F6",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 180, 180),
                Location = new Point(12, 158),
                AutoSize = true
            };
            panelColorPreview = new Panel
            {
                Location = new Point(174, 140),
                Size = new Size(24, 24),
                BackColor = currentRgb
            };
            panelColorPreview.Paint += PanelColorPreview_Paint;
            rightPanel.Controls.AddRange(new Control[] { lblRgb, lblHex, panelColorPreview });

            // RGB horizontal sliders
            EventHandler rgbScroll = (s, e) => TrkRgb_Scroll(s, e);
            trkR = new GlassSlider { Minimum = 0, Maximum = 255, Location = new Point(10, 185), Size = new Size(190, 30), FillColor = Color.FromArgb(255, 80, 80) };
            trkG = new GlassSlider { Minimum = 0, Maximum = 255, Location = new Point(10, 220), Size = new Size(190, 30), FillColor = Color.FromArgb(80, 255, 80) };
            trkB = new GlassSlider { Minimum = 0, Maximum = 255, Location = new Point(10, 255), Size = new Size(190, 30), FillColor = Color.FromArgb(80, 80, 255) };
            trkR.Scroll += rgbScroll;
            trkG.Scroll += rgbScroll;
            trkB.Scroll += rgbScroll;
            rightPanel.Controls.AddRange(new Control[] { trkR, trkG, trkB });

            // Color Presets grid title & addition button
            Label lblPresetsTitle = CreateSectionHeader("COLOR PRESETS", 12, 290);
            rightPanel.Controls.Add(lblPresetsTitle);

            Button btnAddPreset = new Button
            {
                Text = "+ ADD CURRENT",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Location = new Point(12, 312),
                Size = new Size(186, 22),
                BackColor = Color.FromArgb(35, 36, 40),
                ForeColor = Color.FromArgb(0, 210, 255), // Bright cyan
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnAddPreset.FlatAppearance.BorderSize = 1;
            btnAddPreset.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);

            // Flow grid layout for 16 swatches
            presetsFlow = new FlowLayoutPanel
            {
                Location = new Point(10, 342),
                Size = new Size(190, 140),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0)
            };
            btnAddPreset.Click += (s, e) => {
                if (!isSystemOn) return;
                
                // Find first empty preset button
                Button firstEmpty = null;
                foreach (Control ctrl in presetsFlow.Controls)
                {
                    if (ctrl is Button btn)
                    {
                        Color c = btn.BackColor;
                        if (c.R == 24 && c.G == 25 && c.B == 28 && btn.Tag == null)
                        {
                            firstEmpty = btn;
                            break;
                        }
                    }
                }
                
                Button targetBtn = firstEmpty;
                if (targetBtn == null && presetsFlow.Controls.Count > 0)
                {
                    targetBtn = presetsFlow.Controls[presetsFlow.Controls.Count - 1] as Button;
                }

                if (targetBtn != null)
                {
                    if (isAddressableMode)
                    {
                        Color[] savedSegments = new Color[addressablePixelCount];
                        Array.Copy(segmentColors, savedSegments, addressablePixelCount);
                        targetBtn.Tag = savedSegments;
                        targetBtn.BackColor = Color.FromArgb(24, 25, 28);
                    }
                    else
                    {
                        targetBtn.Tag = null;
                        targetBtn.BackColor = currentRgb;
                    }
                    UpdatePresetButtonState(targetBtn);
                    SavePresets(); // Persist changes
                }
            };
            rightPanel.Controls.Add(btnAddPreset);

            // Calibration Header and Buttons
            Label lblCalibTitle = CreateSectionHeader("CALIBRATION", 12, 497);
            rightPanel.Controls.Add(lblCalibTitle);

            Button btnCal1 = new Button { Location = new Point(25, 522), Size = new Size(25, 25), BackColor = ColorTranslator.FromHtml("#FFA777"), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            Button btnCal2 = new Button { Location = new Point(65, 522), Size = new Size(25, 25), BackColor = ColorTranslator.FromHtml("#FFD0A0"), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            Button btnCal3 = new Button { Location = new Point(105, 522), Size = new Size(25, 25), BackColor = ColorTranslator.FromHtml("#FFECE0"), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            Button btnCalR = new Button { Location = new Point(145, 522), Size = new Size(25, 25), BackColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Text = "R", Font = new Font("Segoe UI", 6f, FontStyle.Bold), ForeColor = Color.Black };
            
            string calibPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "calib.txt");
            if (File.Exists(calibPath))
            {
                try {
                    string[] calibLines = File.ReadAllLines(calibPath);
                    for (int i = 0; i < 3 && i < calibLines.Length; i++)
                    {
                        string[] parts = calibLines[i].Split(',');
                        if (parts.Length >= 5)
                        {
                            calibProfiles[i].Point100 = ColorTranslator.FromHtml(parts[0]);
                            calibProfiles[i].Point60 = ColorTranslator.FromHtml(parts[1]);
                            calibProfiles[i].Point30 = ColorTranslator.FromHtml(parts[2]);
                            calibProfiles[i].Point5 = ColorTranslator.FromHtml(parts[3]);
                            calibProfiles[i].PointMin = ColorTranslator.FromHtml(parts[4]);
                        }
                        else if (parts.Length == 4)
                        {
                            calibProfiles[i].Point100 = ColorTranslator.FromHtml(parts[0]);
                            calibProfiles[i].Point60 = ColorTranslator.FromHtml(parts[1]); // Point50 renamed to Point60
                            calibProfiles[i].Point30 = ColorTranslator.FromHtml(parts[2]);
                            calibProfiles[i].Point5 = ColorTranslator.FromHtml(parts[3]);
                            calibProfiles[i].PointMin = Color.FromArgb(0, 0, 0);
                        }
                        else if (parts.Length == 3)
                        {
                            calibProfiles[i].Point100 = ColorTranslator.FromHtml(parts[0]);
                            calibProfiles[i].Point60 = ColorTranslator.FromHtml(parts[1]); // Point50 renamed to Point60
                            calibProfiles[i].Point5 = ColorTranslator.FromHtml(parts[2]);
                            calibProfiles[i].PointMin = Color.FromArgb(0, 0, 0);
                            
                            // Interpolate Point30 (formerly Point31) between Point5 and Point60 at v = 76
                            float t = 76f / 153f;
                            int r = (int)(calibProfiles[i].Point5.R + (calibProfiles[i].Point60.R - calibProfiles[i].Point5.R) * t);
                            int g = (int)(calibProfiles[i].Point5.G + (calibProfiles[i].Point60.G - calibProfiles[i].Point5.G) * t);
                            int b = (int)(calibProfiles[i].Point5.B + (calibProfiles[i].Point60.B - calibProfiles[i].Point5.B) * t);
                            calibProfiles[i].Point30 = Color.FromArgb(
                                Math.Min(255, Math.Max(0, r)),
                                Math.Min(255, Math.Max(0, g)),
                                Math.Min(255, Math.Max(0, b))
                            );
                        }
                    }
                } catch { }
            }

            btnCal1.BackColor = calibProfiles[0].Point100;
            btnCal2.BackColor = calibProfiles[1].Point100;
            btnCal3.BackColor = calibProfiles[2].Point100;

            Button[] calBtns = new Button[] { btnCal1, btnCal2, btnCal3, btnCalR };
            Action saveCalib = () => {
                try {
                    string[] lines = new string[3];
                    for (int i = 0; i < 3; i++) {
                        string p100 = $"#{calibProfiles[i].Point100.R:X2}{calibProfiles[i].Point100.G:X2}{calibProfiles[i].Point100.B:X2}";
                        string p60 = $"#{calibProfiles[i].Point60.R:X2}{calibProfiles[i].Point60.G:X2}{calibProfiles[i].Point60.B:X2}";
                        string p30 = $"#{calibProfiles[i].Point30.R:X2}{calibProfiles[i].Point30.G:X2}{calibProfiles[i].Point30.B:X2}";
                        string p5 = $"#{calibProfiles[i].Point5.R:X2}{calibProfiles[i].Point5.G:X2}{calibProfiles[i].Point5.B:X2}";
                        string pMin = $"#{calibProfiles[i].PointMin.R:X2}{calibProfiles[i].PointMin.G:X2}{calibProfiles[i].PointMin.B:X2}";
                        lines[i] = $"{p100},{p60},{p30},{p5},{pMin}";
                    }
                    File.WriteAllLines(calibPath, lines);
                } catch { }
            };

            EventHandler calibClick = (s, e) => {
                Button clicked = (Button)s;
                int idx = Array.IndexOf(calBtns, clicked);
                currentCalibProfile = (idx >= 0 && idx < 3) ? calibProfiles[idx] : null;

                foreach (Button b in calBtns) {
                    b.FlatAppearance.BorderSize = (b == clicked) ? 2 : 1;
                    b.FlatAppearance.BorderColor = (b == clicked) ? Color.FromArgb(0, 210, 255) : Color.FromArgb(60, 60, 60);
                }

                try
                {
                    string idxPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_calib.txt");
                    File.WriteAllText(idxPath, idx.ToString());
                }
                catch { }
            };
            MouseEventHandler calibMouseDown = (s, e) => {
                Button btn = (Button)s;
                if (e.Button == MouseButtons.Right && btn != btnCalR)
                {
                    int index = Array.IndexOf(calBtns, btn);
                    if (index >= 0 && index < 3)
                    {
                        using (CalibrationMenuForm cmf = new CalibrationMenuForm(
                            calibProfiles[index],
                            c => {
                                // Direct hardware send for hover preview
                                byte[] d = new byte[] { c.R, c.G, c.B };
                                if (useUdp) { try { udpClient?.SendAsync(d, 3, udpTarget); } catch { } } // calibration preview → 7778
                                else { try { if (serialPort != null && serialPort.IsOpen) serialPort.Write(d, 0, 3); } catch { } }
                            },
                            () => {
                                // Restore active UI color when hover stops or dialog closes
                                UpdateActiveColor(currentRgb, true, true);
                            }))
                        {
                            if (cmf.ShowDialog() == DialogResult.OK)
                            {
                                calibProfiles[index] = cmf.ResultProfile;
                                btn.BackColor = calibProfiles[index].Point100;
                                if (btn.FlatAppearance.BorderSize == 2) {
                                    currentCalibProfile = calibProfiles[index];
                                    UpdateActiveColor(currentRgb, true, true);
                                }
                                saveCalib();
                            }
                        }
                    }
                }
            };
            foreach (Button b in calBtns) {
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
                b.Click += calibClick;
                b.MouseDown += calibMouseDown;
            }

            // Load and apply last selected calibration preset index
            int savedCalibIndex = 3; // Default to Reset ("R")
            try
            {
                string idxPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_calib.txt");
                if (File.Exists(idxPath))
                {
                    if (int.TryParse(File.ReadAllText(idxPath), out int savedIdx) && savedIdx >= 0 && savedIdx < calBtns.Length)
                    {
                        savedCalibIndex = savedIdx;
                    }
                }
            }
            catch { }
            calibClick(calBtns[savedCalibIndex], EventArgs.Empty);
            
            rightPanel.Controls.AddRange(calBtns);

            // Load presets from file or fall back to defaults
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presets.txt");
            string[] presetHexes;
            if (File.Exists(filePath))
            {
                try
                {
                    presetHexes = File.ReadAllLines(filePath);
                    if (presetHexes.Length != 16)
                    {
                        presetHexes = GetDefaultPresets();
                    }
                }
                catch
                {
                    presetHexes = GetDefaultPresets();
                }
            }
            else
            {
                presetHexes = GetDefaultPresets();
            }

            // Context Menu for right-click edit/clear options
            ContextMenuStrip presetMenu = new ContextMenuStrip();
            ToolStripMenuItem itemSetToCurrent = new ToolStripMenuItem("Set to Current Color");
            ToolStripMenuItem itemChooseColor = new ToolStripMenuItem("Choose Custom Color...");
            ToolStripMenuItem itemClearPreset = new ToolStripMenuItem("Clear Preset");
            presetMenu.Items.AddRange(new ToolStripItem[] { itemSetToCurrent, itemChooseColor, itemClearPreset });

            itemSetToCurrent.Click += (s2, e2) => {
                if (presetMenu.SourceControl is Button btn)
                {
                    btn.Tag = null;
                    btn.BackColor = currentRgb;
                    UpdatePresetButtonState(btn);
                    SavePresets(); // Persist changes
                }
            };

            itemChooseColor.Click += (s2, e2) => {
                if (presetMenu.SourceControl is Button btn)
                {
                    using (ColorDialog cd = new ColorDialog())
                    {
                        cd.Color = btn.BackColor;
                        if (cd.ShowDialog() == DialogResult.OK)
                        {
                            btn.Tag = null;
                            btn.BackColor = cd.Color;
                            UpdatePresetButtonState(btn);
                            SavePresets(); // Persist changes
                        }
                    }
                }
            };

            itemClearPreset.Click += (s2, e2) => {
                if (presetMenu.SourceControl is Button btn)
                {
                    btn.Tag = null;
                    btn.BackColor = Color.FromArgb(24, 25, 28); // Reset to empty background
                    UpdatePresetButtonState(btn);
                    SavePresets(); // Persist changes
                }
            };

            // Drag and Drop State and Event Handlers
            Point dragStartPreset = Point.Empty;

            MouseEventHandler presetMouseDown = (s2, e2) => {
                if (e2.Button == MouseButtons.Left)
                {
                    dragStartPreset = e2.Location;
                }
            };

            MouseEventHandler presetMouseMove = (s2, e2) => {
                if (e2.Button == MouseButtons.Left && dragStartPreset != Point.Empty)
                {
                    Button btn = (Button)s2;
                    Color c = btn.BackColor;
                    bool isEmpty = (c.R == 24 && c.G == 25 && c.B == 28);
                    
                    if (!isEmpty)
                    {
                        int diffX = Math.Abs(e2.X - dragStartPreset.X);
                        int diffY = Math.Abs(e2.Y - dragStartPreset.Y);
                        if (diffX > 4 || diffY > 4)
                        {
                            dragStartPreset = Point.Empty;
                            btn.DoDragDrop(btn, DragDropEffects.Move);
                        }
                    }
                }
            };

            MouseEventHandler presetMouseUp = (s2, e2) => {
                dragStartPreset = Point.Empty;
            };

            DragEventHandler presetDragEnter = (s2, e2) => {
                if (e2.Data != null && e2.Data.GetDataPresent(typeof(Button)))
                {
                    e2.Effect = DragDropEffects.Move;
                }
            };

            DragEventHandler presetDragDrop = (s2, e2) => {
                if (e2.Data == null) return;
                Button targetBtn = (Button)s2;
                Button sourceBtn = (Button)e2.Data.GetData(typeof(Button));
                
                if (sourceBtn != null && sourceBtn != targetBtn)
                {
                    Color tempCol = targetBtn.BackColor;
                    object tempTag = targetBtn.Tag;

                    targetBtn.BackColor = sourceBtn.BackColor;
                    targetBtn.Tag = sourceBtn.Tag;

                    sourceBtn.BackColor = tempCol;
                    sourceBtn.Tag = tempTag;
                    
                    UpdatePresetButtonState(targetBtn);
                    UpdatePresetButtonState(sourceBtn);
                    SavePresets(); // Persist changes
                }
            };

            EventHandler presetClick = (s2, e2) => {
                if (isAmbilightActive || isRainbowActive || !isSystemOn) return;
                Button btn = (Button)s2;
                Color c = btn.BackColor;
                bool isEmpty = (c.R == 24 && c.G == 25 && c.B == 28) && btn.Tag == null;
                
                if (!isEmpty)
                {
                    ColorToHsv(c, out double h, out double s, out double v);
                    if (v > 0.01f)
                    {
                        currentSat = (float)s;
                        if (s > 0.01f)
                        {
                            currentHue = (float)h;
                        }
                    }
                    
                    if (isAddressableMode && btn.Tag is Color[] addrColors)
                    {
                        // Addressable preset with stored segment data:
                        // If pixels are specifically selected → 7778, else → 7777
                        bool hasSelection = selectedSegments.Count > 0 && selectedSegments.Count < addressablePixelCount;
                        var presetEp = hasSelection ? udpTarget : udpTarget7777;

                        for (int i = 0; i < addressablePixelCount; i++)
                        {
                            int srcIdx = (int)Math.Round((double)i / (addressablePixelCount > 1 ? addressablePixelCount - 1 : 1) * (addrColors.Length > 1 ? addrColors.Length - 1 : 1));
                            if (srcIdx < 0) srcIdx = 0;
                            if (srcIdx >= addrColors.Length) srcIdx = addrColors.Length - 1;
                            
                            segmentColors[i] = addrColors[srcIdx];
                        }
                        SendAddressableDataToHardware(presetEp);
                        panelVisualizer.Invalidate();
                        
                        // Sync UI with preset's base color
                        selectedWheelPoint = GetWheelCoordinatesFromColor(c, pboxColorWheel);
                        pboxColorWheel.Invalidate();
                        
                        if (!isUpdatingSliders && trkR != null)
                        {
                            isUpdatingSliders = true;
                            trkR.Value = c.R;
                            trkG.Value = c.G;
                            trkB.Value = c.B;
                            trkBrightness.Value = (int)(v * 100);
                            isUpdatingSliders = false;
                        }
                    }
                    else if (isAddressableMode && selectedSegments.Count == 0)
                    {
                        // No pixels selected = fill all → 7777 (static color)
                        UpdateActiveColor(c, true, false, false, udpTarget7777);
                    }
                    else if (isAddressableMode)
                    {
                        // Specific pixels selected → 7778
                        UpdateActiveColor(c);
                    }
                    else
                    {
                        // Generic mode, no animation → 7777
                        UpdateActiveColor(c, true, false, false, udpTarget7777);
                    }
                }
            };

            for (int i = 0; i < 16; i++)
            {
                Color presetCol = Color.FromArgb(24, 25, 28);
                Color[] presetAddr = null;
                try
                {
                    string line = presetHexes[i];
                    if (line.StartsWith("ADDR:"))
                    {
                        string[] parts = line.Substring(5).Split(',');
                        presetAddr = new Color[parts.Length];
                        for (int j = 0; j < parts.Length; j++) presetAddr[j] = ColorTranslator.FromHtml(parts[j]);
                    }
                    else
                    {
                        presetCol = ColorTranslator.FromHtml(line);
                    }
                }
                catch
                {
                    try
                    {
                        presetCol = ColorTranslator.FromHtml(GetDefaultPresets()[i]);
                    }
                    catch
                    {
                        presetCol = Color.FromArgb(24, 25, 28);
                    }
                }
                Button presetBtn = new Button
                {
                    Width = 40,
                    Height = 30,
                    BackColor = presetCol,
                    Tag = presetAddr,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    Margin = new Padding(2),
                    ContextMenuStrip = presetMenu,
                    AllowDrop = true
                };

                // Attach standard click, mouse drag & drop events
                presetBtn.Click += presetClick;
                presetBtn.MouseDown += presetMouseDown;
                presetBtn.MouseMove += presetMouseMove;
                presetBtn.MouseUp += presetMouseUp;
                presetBtn.DragEnter += presetDragEnter;
                presetBtn.DragDrop += presetDragDrop;

                UpdatePresetButtonState(presetBtn);
                presetsFlow.Controls.Add(presetBtn);
            }
            rightPanel.Controls.Add(presetsFlow);
            this.Controls.Add(rightPanel);

            // ==========================================
            // 5. TICKER TIMERS (RAINBOW)
            // ==========================================
            timerRainbow = new System.Windows.Forms.Timer { Interval = 30 }; // Smooth 30ms transitions
            timerRainbow.Tick += TimerRainbow_Tick;

            // ==========================================
            // 6. VERSION LABEL IN BOTTOM-RIGHT
            // ==========================================
            string localVersion = "1.0";
            try
            {
                string rootPath = AppDomain.CurrentDomain.BaseDirectory;
                while (!string.IsNullOrEmpty(rootPath) && 
                       !File.Exists(Path.Combine(rootPath, "updater.py")) && 
                       !File.Exists(Path.Combine(rootPath, "run.bat")))
                {
                    rootPath = Path.GetDirectoryName(rootPath);
                }
                if (string.IsNullOrEmpty(rootPath)) rootPath = AppDomain.CurrentDomain.BaseDirectory;

                string versionPath = Path.Combine(rootPath, "version.txt");
                if (File.Exists(versionPath))
                {
                    string content = File.ReadAllText(versionPath);
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"version:\s*([\d\.]+)");
                    if (match.Success)
                    {
                        localVersion = match.Groups[1].Value;
                    }
                }
            }
            catch { }

            Label lblVersion = new Label
            {
                Text = $"v{localVersion}",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
                ForeColor = Color.White,
                Location = new Point(this.Width - 60, this.Height - 20),
                Size = new Size(50, 15),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };
            this.Controls.Add(lblVersion);
            lblVersion.BringToFront();
        }

        // ==========================================================================
        // TITLEBAR MOUSE GRAPHICAL EVENT DRAGGERS
        // ==========================================================================
        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDraggingWindow = true;
                dragStartPoint = new Point(e.X, e.Y);
            }
        }

        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDraggingWindow)
            {
                Point screenPoint = PointToScreen(e.Location);
                this.Location = new Point(screenPoint.X - dragStartPoint.X, screenPoint.Y - dragStartPoint.Y);
            }
        }

        private void TitleBar_MouseUp(object sender, MouseEventArgs e)
        {
            isDraggingWindow = false;
        }

        // ==========================================================================
        // GDI+ COMPONENT PAINTERS AND ROUNDED CORNER GENERATORS
        // ==========================================================================
        private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void PanelVisualizer_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = GetRoundedPath(panelVisualizer.ClientRectangle, 12))
            {
                panelVisualizer.Region = new Region(path);
            }

            if (isAddressableMode)
            {
                float segmentWidth = panelVisualizer.Width / (float)addressablePixelCount;
                for (int i = 0; i < addressablePixelCount; i++)
                {
                    RectangleF rect = new RectangleF(i * segmentWidth, 0, segmentWidth, panelVisualizer.Height);
                    using (SolidBrush brush = new SolidBrush(segmentColors[i]))
                    {
                        e.Graphics.FillRectangle(brush, rect);
                    }
                    
                    // 2px border
                    using (Pen pen = new Pen(Color.FromArgb(20, 20, 20), 2f))
                    {
                        e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    }

                    if (selectedSegments.Contains(i))
                    {
                        // High contrast glow
                        Color glowColor = (segmentColors[i].GetBrightness() < 0.5f) ? Color.White : Color.Black;
                        using (Pen glowPen = new Pen(glowColor, 3f))
                        {
                            e.Graphics.DrawRectangle(glowPen, rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
                        }
                    }
                }
            }
        }

        private void PanelVisualizer_MouseDown(object sender, MouseEventArgs e)
        {
            if (!isAddressableMode) return;
            
            float segmentWidth = panelVisualizer.Width / (float)addressablePixelCount;
            int clickedIndex = (int)(e.X / segmentWidth);
            if (clickedIndex < 0) clickedIndex = 0;
            if (clickedIndex >= addressablePixelCount) clickedIndex = addressablePixelCount - 1;

            if (e.Button == MouseButtons.Left)
            {
                bool isCtrlPressed = (Control.ModifierKeys & Keys.Control) == Keys.Control;
                if (!isCtrlPressed)
                {
                    selectedSegments.Clear();
                    isSelecting = true;
                }
                else
                {
                    isSelecting = !selectedSegments.Contains(clickedIndex);
                }
            }
            
            ApplyAddressableActionCore(clickedIndex, e.Button);
            lastHoveredSegment = clickedIndex;

            panelVisualizer.Invalidate();
            if (isAddressableMode) SendAddressableDataToHardware();
        }

        private void PanelVisualizer_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isAddressableMode) return;
            
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                float segmentWidth = panelVisualizer.Width / (float)addressablePixelCount;
                int hoverIndex = (int)(e.X / segmentWidth);
                if (hoverIndex < 0) hoverIndex = 0;
                if (hoverIndex >= addressablePixelCount) hoverIndex = addressablePixelCount - 1;

                if (hoverIndex != lastHoveredSegment)
                {
                    if (lastHoveredSegment != -1)
                    {
                        int step = hoverIndex > lastHoveredSegment ? 1 : -1;
                        for (int i = lastHoveredSegment + step; i != hoverIndex + step; i += step)
                        {
                            ApplyAddressableActionCore(i, e.Button);
                        }
                    }
                    else
                    {
                        ApplyAddressableActionCore(hoverIndex, e.Button);
                    }

                    lastHoveredSegment = hoverIndex;

                    panelVisualizer.Invalidate();
                    if (isAddressableMode) SendAddressableDataToHardware();
                }
            }
        }

        private void PanelVisualizer_MouseUp(object sender, MouseEventArgs e)
        {
            lastHoveredSegment = -1;
        }

        private void ApplyAddressableActionCore(int index, MouseButtons button)
        {
            if (button == MouseButtons.Left)
            {
                if (isSelecting) selectedSegments.Add(index);
                else selectedSegments.Remove(index);
            }
            else if (button == MouseButtons.Right)
            {
                segmentColors[index] = Color.Black;
                selectedSegments.Remove(index);
            }
            else if (button == MouseButtons.Middle)
            {
                int leftIndex = -1;
                for (int i = index - 1; i >= 0; i--)
                {
                    if (segmentColors[i].R > 0 || segmentColors[i].G > 0 || segmentColors[i].B > 0)
                    {
                        leftIndex = i;
                        break;
                    }
                }

                int rightIndex = -1;
                for (int i = index + 1; i < addressablePixelCount; i++)
                {
                    if (segmentColors[i].R > 0 || segmentColors[i].G > 0 || segmentColors[i].B > 0)
                    {
                        rightIndex = i;
                        break;
                    }
                }

                if (leftIndex != -1 && rightIndex != -1)
                {
                    float distLeft = index - leftIndex;
                    float distRight = rightIndex - index;
                    float totalDist = distLeft + distRight;
                    float ratioRight = distLeft / totalDist;
                    float ratioLeft = 1f - ratioRight;

                    Color cLeft = segmentColors[leftIndex];
                    Color cRight = segmentColors[rightIndex];

                    int r = (int)(cLeft.R * ratioLeft + cRight.R * ratioRight);
                    int g = (int)(cLeft.G * ratioLeft + cRight.G * ratioRight);
                    int b = (int)(cLeft.B * ratioLeft + cRight.B * ratioRight);

                    segmentColors[index] = Color.FromArgb(255, r, g, b);
                }
            }
        }

        private void BtnModeToggle_Click(object sender, EventArgs e)
        {
            isAddressableMode = !isAddressableMode;
            btnModeToggle.Text = isAddressableMode ? "Addressable RGB" : "Generic RGB";
            lblPixelCount.Visible = isAddressableMode;
            numPixelCount.Visible = isAddressableMode;
            radInvertOrder.Visible = isAddressableMode;
            btnAddressableSettings.Visible = isAddressableMode;
            
            SaveAddressableSettings();
            
            if (isAddressableMode)
            {
                if (isAmbilightActive) StopAmbilight();
                SendAddressableDataToHardware();
            }
            else
            {
                UpdateActiveColor(currentRgb);
            }
            panelVisualizer.Invalidate();
        }

        private void NumPixelCount_ValueChanged(object sender, EventArgs e)
        {
            addressablePixelCount = (int)numPixelCount.Value;
            selectedSegments.RemoveWhere(i => i >= addressablePixelCount);
            SaveAddressableSettings();
            panelVisualizer.Invalidate();
            if (isAddressableMode) SendAddressableDataToHardware();
        }

        private void BtnAddressableSettings_Click(object sender, EventArgs e)
        {
            using (CaptureAreaOverlayForm overlay = new CaptureAreaOverlayForm(addressablePixelCount, addressableCaptureAreas))
            {
                overlay.OnAreasChanged += (areas) => {
                    addressableCaptureAreas = areas;
                };
                
                if (overlay.ShowDialog() == DialogResult.OK)
                {
                    addressableCaptureAreas = overlay.Areas;
                    SaveAddressableSettings();
                }
            }
        }

        private void PanelColorPreview_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = GetRoundedPath(panelColorPreview.ClientRectangle, 6))
            {
                panelColorPreview.Region = new Region(path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            
            // Draw real-time dynamic ambient blur "Ambilight glow" behind visualizer box
            if (isSystemOn)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = panelVisualizer.Bounds;
                rect.Inflate(12, 12); // Blur expansion area
                
                using (GraphicsPath path = GetRoundedPath(rect, 16))
                using (PathGradientBrush pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor = Color.FromArgb(160, currentRgb.R, currentRgb.G, currentRgb.B);
                    pgb.SurroundColors = new Color[] { Color.Transparent };
                    e.Graphics.FillPath(pgb, path);
                }
            }
        }

        // ==========================================================================
        // CIRCULAR HSL COLOR WHEEL GRAPHICS
        // ==========================================================================
        private void GenerateColorWheel()
        {
            int w = pboxColorWheel.Width;
            int h = pboxColorWheel.Height;
            colorWheelBmp = new Bitmap(w, h);
            
            double cx = w / 2.0;
            double cy = h / 2.0;
            double radius = Math.Min(cx, cy);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double dx = x - cx;
                    double dy = y - cy;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist <= radius)
                    {
                        double angle = Math.Atan2(dy, dx) + Math.PI; // Range [0, 2*PI]
                        double hue = angle * 180.0 / Math.PI;
                        double saturation = dist / radius;

                        colorWheelBmp.SetPixel(x, y, ColorFromHsv(hue, saturation, 1.0));
                    }
                    else
                    {
                        colorWheelBmp.SetPixel(x, y, Color.Transparent);
                    }
                }
            }
            pboxColorWheel.Image = colorWheelBmp;
        }

        private void PboxColorWheel_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            
            // Draw white selector coordinate pin circle
            int pinRadius = 6;
            Rectangle rect = new Rectangle(
                selectedWheelPoint.X - pinRadius, 
                selectedWheelPoint.Y - pinRadius, 
                pinRadius * 2, 
                pinRadius * 2
            );

            using (Pen whitePen = new Pen(Color.White, 2.5f))
            {
                e.Graphics.DrawEllipse(whitePen, rect);
            }
        }

        private void PickColorFromWheel(Point pt)
        {
            if (isAmbilightActive || isRainbowActive || !isSystemOn) return;

            int cx = pboxColorWheel.Width / 2;
            int cy = pboxColorWheel.Height / 2;
            double radius = Math.Min(cx, cy);

            double dx = pt.X - cx;
            double dy = pt.Y - cy;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            // Constraint boundaries to circular bounds
            if (dist > radius)
            {
                double angle = Math.Atan2(dy, dx);
                pt.X = (int)(cx + Math.Cos(angle) * radius);
                pt.Y = (int)(cy + Math.Sin(angle) * radius);
            }

            try
            {
                Color baseC = colorWheelBmp.GetPixel(pt.X, pt.Y);
                if (baseC.A > 0) // Clicked in solid region
                {
                    selectedWheelPoint = pt;
                    
                    // Respect brightness slider value
                    ColorToHsv(baseC, out double h, out double s, out double v);
                    currentHue = (float)h;
                    currentSat = (float)s;
                    float l = trkBrightness != null ? trkBrightness.Value / 100f : 1.0f;
                    
                    Color c = ColorFromHsv(currentHue, currentSat, l);
                    UpdateActiveColor(c, true, true, true);
                    pboxColorWheel.Invalidate();
                }
            }
            catch {}
        }

        private void PboxColorWheel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDraggingWheelPin = true;
                PickColorFromWheel(e.Location);
            }
        }

        private void PboxColorWheel_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDraggingWheelPin)
            {
                PickColorFromWheel(e.Location);
            }
        }

        private void PboxColorWheel_MouseUp(object sender, MouseEventArgs e)
        {
            isDraggingWheelPin = false;
        }

        // ==========================================================================
        // AMBILIGHT ENGINE (FAST HARDWARE-SCALED BACKGROUND SAMPLER)
        // ==========================================================================
        private async Task AmbilightCaptureLoop()
        {
            TimeBeginPeriod(1);
            try
            {
                int lastUiUpdate = Environment.TickCount;
                System.Collections.Generic.Queue<Color> smoothQueue = new System.Collections.Generic.Queue<Color>();
                long runningSr = 0, runningSg = 0, runningSb = 0;

                System.Collections.Generic.Queue<Color>[] addrSmoothQueues = null;
                long[] addrRunningSr = null, addrRunningSg = null, addrRunningSb = null;

                while (isAmbilightActive && isSystemOn)
                {
                    DxgiScreenCapture dxgiCapture = null;
                    try
                    {
                        dxgiCapture = new DxgiScreenCapture();

                        while (isAmbilightActive && isSystemOn)
                        {
                            if (isAddressableMode)
                            {
                                if (addrSmoothQueues == null || addrSmoothQueues.Length != addressablePixelCount)
                                {
                                    addrSmoothQueues = new System.Collections.Generic.Queue<Color>[addressablePixelCount];
                                    addrRunningSr = new long[addressablePixelCount];
                                    addrRunningSg = new long[addressablePixelCount];
                                    addrRunningSb = new long[addressablePixelCount];
                                    for (int i = 0; i < addressablePixelCount; i++) addrSmoothQueues[i] = new System.Collections.Generic.Queue<Color>();
                                }

                                if (addressableCaptureAreas != null && addressableCaptureAreas.Length == addressablePixelCount)
                                {
                                    Color[] rawColors = dxgiCapture.TryAcquireAddressableFrame(addressableCaptureAreas);
                                    if (rawColors != null)
                                    {
                                        for (int i = 0; i < rawColors.Length && i < segmentColors.Length; i++)
                                        {
                                            Color avgColor = rawColors[i];

                                            Color currentAverage = addrSmoothQueues[i].Count > 0
                                                ? Color.FromArgb((int)(addrRunningSr[i] / addrSmoothQueues[i].Count), (int)(addrRunningSg[i] / addrSmoothQueues[i].Count), (int)(addrRunningSb[i] / addrSmoothQueues[i].Count))
                                                : avgColor;

                                            int colorChange = Math.Abs(avgColor.R - currentAverage.R) + 
                                                              Math.Abs(avgColor.G - currentAverage.G) + 
                                                              Math.Abs(avgColor.B - currentAverage.B);

                                            if (colorChange > currentSmoothThreshold)
                                            {
                                                addrSmoothQueues[i].Clear();
                                                addrRunningSr[i] = 0; addrRunningSg[i] = 0; addrRunningSb[i] = 0;

                                                addrSmoothQueues[i].Enqueue(avgColor);
                                                addrRunningSr[i] += avgColor.R;
                                                addrRunningSg[i] += avgColor.G;
                                                addrRunningSb[i] += avgColor.B;
                                            }
                                            else
                                            {
                                                addrSmoothQueues[i].Enqueue(avgColor);
                                                addrRunningSr[i] += avgColor.R;
                                                addrRunningSg[i] += avgColor.G;
                                                addrRunningSb[i] += avgColor.B;

                                                while (addrSmoothQueues[i].Count > currentSmoothFrames && addrSmoothQueues[i].Count > 0)
                                                {
                                                    Color dequeued = addrSmoothQueues[i].Dequeue();
                                                    addrRunningSr[i] -= dequeued.R;
                                                    addrRunningSg[i] -= dequeued.G;
                                                    addrRunningSb[i] -= dequeued.B;
                                                }
                                            }

                                            segmentColors[i] = addrSmoothQueues[i].Count > 0 
                                                ? Color.FromArgb((int)(addrRunningSr[i] / addrSmoothQueues[i].Count), (int)(addrRunningSg[i] / addrSmoothQueues[i].Count), (int)(addrRunningSb[i] / addrSmoothQueues[i].Count))
                                                : avgColor;
                                        }

                                        if (isAmbilightActive && isSystemOn)
                                        {
                                            SendAddressableDataToHardware();

                                            if (Environment.TickCount - lastUiUpdate > 30)
                                            {
                                                lastUiUpdate = Environment.TickCount;
                                                this.BeginInvoke(new Action(() =>
                                                {
                                                    if (isAmbilightActive && isSystemOn)
                                                    {
                                                        panelVisualizer.Invalidate();
                                                    }
                                                }));
                                            }
                                        }
                                    }
                                }
                                await Task.Delay(1);
                                continue;
                            }

                            Color? rawColor = dxgiCapture.TryAcquireNextFrame();
                            if (rawColor.HasValue)
                            {
                                Color avgColor = rawColor.Value;
                                // 4. Cosmetic Smoothing Logic (O(1) Optimized)
                                Color currentAverage = smoothQueue.Count > 0
                                    ? Color.FromArgb((int)(runningSr / smoothQueue.Count), (int)(runningSg / smoothQueue.Count), (int)(runningSb / smoothQueue.Count))
                                    : avgColor;

                                int colorChange = Math.Abs(avgColor.R - currentAverage.R) + 
                                                  Math.Abs(avgColor.G - currentAverage.G) + 
                                                  Math.Abs(avgColor.B - currentAverage.B);

                                if (colorChange > currentSmoothThreshold)
                                {
                                    // Sudden large change: discard old smoothing data
                                    smoothQueue.Clear();
                                    runningSr = 0; runningSg = 0; runningSb = 0;

                                    smoothQueue.Enqueue(avgColor);
                                    runningSr += avgColor.R;
                                    runningSg += avgColor.G;
                                    runningSb += avgColor.B;
                                }
                                else
                                {
                                    // Small change: append to queue for moving average
                                    smoothQueue.Enqueue(avgColor);
                                    runningSr += avgColor.R;
                                    runningSg += avgColor.G;
                                    runningSb += avgColor.B;

                                    while (smoothQueue.Count > currentSmoothFrames && smoothQueue.Count > 0)
                                    {
                                        Color dequeued = smoothQueue.Dequeue();
                                        runningSr -= dequeued.R;
                                        runningSg -= dequeued.G;
                                        runningSb -= dequeued.B;
                                    }
                                }

                                Color finalColor = smoothQueue.Count > 0 
                                    ? Color.FromArgb((int)(runningSr / smoothQueue.Count), (int)(runningSg / smoothQueue.Count), (int)(runningSb / smoothQueue.Count))
                                    : avgColor;

                                // Send directly to hardware from background thread for max FPS
                                if (isAmbilightActive && isSystemOn)
                                {
                                    SendColorToHardware(finalColor);

                                    // 5. Throttle UI updates to prevent UI redraws from dragging down hardware SPS
                                    if (Environment.TickCount - lastUiUpdate > 30)
                                    {
                                        lastUiUpdate = Environment.TickCount;
                                        this.BeginInvoke(new Action(() =>
                                        {
                                            if (isAmbilightActive && isSystemOn)
                                            {
                                                UpdateActiveColor(finalColor, false);
                                            }
                                        }));
                                    }
                                }
                            }

                            // Yield execution briefly to push SPS to max physical limit
                            await Task.Delay(1);
                        }
                    }
                    catch (SharpDX.SharpDXException ex)
                    {
                        System.IO.File.AppendAllText("dxgi_error.txt", DateTime.Now + " SharpDXException: " + ex.ToString() + Environment.NewLine);
                        // DXGI access lost (e.g., UAC popup, screen resolution change, sleep)
                        // Break out of inner loop to dispose and recreate DxgiScreenCapture
                        await Task.Delay(500); 
                    }
                    catch (Exception ex)
                    {
                        System.IO.File.AppendAllText("dxgi_error.txt", DateTime.Now + " GenericException: " + ex.ToString() + Environment.NewLine);
                        // Other unexpected errors
                        await Task.Delay(500);
                    }
                    finally
                    {
                        dxgiCapture?.Dispose();
                    }
                }
            }
            finally
            {
                TimeEndPeriod(1);
            }
        }

        // ==========================================================================
        // RAINBOW SMOOTH Spectrum CYCLE TIMER
        // ==========================================================================
        private void TimerRainbow_Tick(object sender, EventArgs e)
        {
            if (!isSystemOn) return;

            animationTick++;

            if (isAddressableMode)
            {
                switch (currentAnimationType)
                {
                    case AddressableAnimationType.SolidRainbow:
                        rainbowHue = (rainbowHue + 1) % 360;
                        Color c = ColorFromHsl(rainbowHue, 1.0, 0.5);
                        for (int i = 0; i < addressablePixelCount; i++) segmentColors[i] = c;
                        break;
                        
                    case AddressableAnimationType.Wave:
                        for (int i = 0; i < addressablePixelCount; i++)
                        {
                            int hue = (animationTick * 2 + i * 360 / (addressablePixelCount > 0 ? addressablePixelCount : 1)) % 360;
                            segmentColors[i] = ColorFromHsl(hue, 1.0, 0.5);
                        }
                        break;
                        
                    case AddressableAnimationType.Chase:
                        for (int i = 0; i < addressablePixelCount; i++)
                        {
                            int pos = (animationTick / 2) % (addressablePixelCount > 0 ? addressablePixelCount : 1);
                            int dist = (i - pos + addressablePixelCount) % (addressablePixelCount > 0 ? addressablePixelCount : 1);
                            if (dist < 5) segmentColors[i] = ColorFromHsl((animationTick) % 360, 1.0, 0.5 - (dist * 0.1));
                            else segmentColors[i] = Color.Black;
                        }
                        break;
                        
                    case AddressableAnimationType.Comet:
                        int cometPos = (animationTick / 2) % ((addressablePixelCount > 0 ? addressablePixelCount : 1) * 2);
                        for (int i = 0; i < addressablePixelCount; i++)
                        {
                            int dist = cometPos - i;
                            if (dist >= 0 && dist < 10)
                            {
                                segmentColors[i] = ColorFromHsl((animationTick) % 360, 1.0, 0.5 * (1.0 - (dist / 10.0)));
                            }
                            else segmentColors[i] = Color.Black;
                        }
                        break;
                        
                    case AddressableAnimationType.Breathing:
                        rainbowHue = (animationTick / 5) % 360;
                        double lum = 0.1 + (Math.Sin(animationTick * 0.05) + 1.0) * 0.2; // 0.1 to 0.5
                        Color breathC = ColorFromHsl(rainbowHue, 1.0, lum);
                        for (int i = 0; i < addressablePixelCount; i++) segmentColors[i] = breathC;
                        break;
                        
                    case AddressableAnimationType.Sparkle:
                        rainbowHue = (animationTick / 10) % 360;
                        Color baseC = ColorFromHsl(rainbowHue, 1.0, 0.2);
                        Random rnd = new Random();
                        for (int i = 0; i < addressablePixelCount; i++)
                        {
                            if (rnd.Next(100) < 5) segmentColors[i] = Color.White;
                            else segmentColors[i] = baseC;
                        }
                        break;
                        
                    case AddressableAnimationType.ColorWipe:
                        int wipeHue = ((animationTick / (addressablePixelCount > 0 ? addressablePixelCount : 1)) * 45) % 360;
                        int wipePos = animationTick % (addressablePixelCount > 0 ? addressablePixelCount : 1);
                        for (int i = 0; i < addressablePixelCount; i++)
                        {
                            if (i <= wipePos) segmentColors[i] = ColorFromHsl(wipeHue, 1.0, 0.5);
                            else segmentColors[i] = Color.Black;
                        }
                        break;
                }
                
                SendAddressableDataToHardware();
                if (animationTick % 2 == 0) // Throttle UI redraw
                {
                    panelVisualizer.Invalidate();
                }
            }
            else
            {
                rainbowHue = (rainbowHue + 1) % 360;
                Color rgb = ColorFromHsl(rainbowHue, 1.0, 0.5);
                UpdateActiveColor(rgb);
            }
        }

        // ==========================================================================
        // APP COLOR STATE SYNC AND REPAINTING PROCESSORS
        // ==========================================================================
        // endpointOverride: if non-null, send to that endpoint instead of the default udpTarget (7778)
        private void SendAddressableDataToHardware(IPEndPoint endpointOverride = null)
        {
            if (!isConnected) return;
            var ep = endpointOverride ?? udpTarget;

            byte[] data = new byte[addressablePixelCount * 3];
            
            // Generate data packet for addressable pixels
            for (int i = 0; i < addressablePixelCount; i++)
            {
                int targetIndex = invertPixelOrder ? (addressablePixelCount - 1 - i) : i;
                Color c = segmentColors[targetIndex];
                int finalR = c.R;
                int finalG = c.G;
                int finalB = c.B;

                // Ambilight Brightness + Saturation Curve Mapping
                if (isAmbilightActive)
                {
                    int v0 = Math.Max(c.R, Math.Max(c.G, c.B));
                    if (v0 > 0)
                    {
                        float t_in = v0 / 255f;
                        float mapped_t, satMult;
                        if (t_in <= 0.3333f)
                        {
                            float factor = t_in / 0.3333f;
                            mapped_t = ambilightCurve[0] + (ambilightCurve[1] - ambilightCurve[0]) * factor;
                            satMult   = ambilightSatCurve[0] + (ambilightSatCurve[1] - ambilightSatCurve[0]) * factor;
                        }
                        else if (t_in <= 0.6666f)
                        {
                            float factor = (t_in - 0.3333f) / 0.3333f;
                            mapped_t = ambilightCurve[1] + (ambilightCurve[2] - ambilightCurve[1]) * factor;
                            satMult   = ambilightSatCurve[1] + (ambilightSatCurve[2] - ambilightSatCurve[1]) * factor;
                        }
                        else
                        {
                            float factor = (t_in - 0.6666f) / 0.3334f;
                            mapped_t = ambilightCurve[2] + (ambilightCurve[3] - ambilightCurve[2]) * factor;
                            satMult   = ambilightSatCurve[2] + (ambilightSatCurve[3] - ambilightSatCurve[2]) * factor;
                        }

                        // Apply brightness curve
                        int new_v = Math.Min(255, Math.Max(0, (int)(mapped_t * 255f)));
                        float scale = new_v / (float)v0;
                        c = Color.FromArgb(
                            Math.Min(255, (int)(c.R * scale)),
                            Math.Min(255, (int)(c.G * scale)),
                            Math.Min(255, (int)(c.B * scale))
                        );

                        // Apply saturation multiplier (0..2 range, clamped to 0..1)
                        // satMult: 1.0 = no change, 2.0 = double sat (capped), 0 = grayscale
                        if (Math.Abs(satMult - 1f) > 0.001f)
                        {
                            ColorToHsl(c, out double ch, out double cs, out double cl);
                            double newSat = Math.Min(1.0, Math.Max(0.0, cs * satMult));
                            c = ColorFromHsl(ch, newSat, cl);
                        }

                        finalR = c.R;
                        finalG = c.G;
                        finalB = c.B;
                    }
                }

                if (currentCalibProfile != null)
                {
                    int v = Math.Max(c.R, Math.Max(c.G, c.B));
                    Color w = Color.White;
                    
                    if (v <= 13)
                    {
                        float t = v / 13f;
                        w = Color.FromArgb(
                            (int)(currentCalibProfile.PointMin.R + (currentCalibProfile.Point5.R - currentCalibProfile.PointMin.R) * t),
                            (int)(currentCalibProfile.PointMin.G + (currentCalibProfile.Point5.G - currentCalibProfile.PointMin.G) * t),
                            (int)(currentCalibProfile.PointMin.B + (currentCalibProfile.Point5.B - currentCalibProfile.PointMin.B) * t)
                        );
                    }
                    else if (v <= 76)
                    {
                        float t = (v - 13) / 63f;
                        w = Color.FromArgb(
                            (int)(currentCalibProfile.Point5.R + (currentCalibProfile.Point30.R - currentCalibProfile.Point5.R) * t),
                            (int)(currentCalibProfile.Point5.G + (currentCalibProfile.Point30.G - currentCalibProfile.Point5.G) * t),
                            (int)(currentCalibProfile.Point5.B + (currentCalibProfile.Point30.B - currentCalibProfile.Point5.B) * t)
                        );
                    }
                    else if (v <= 153)
                    {
                        float t = (v - 76) / 77f;
                        w = Color.FromArgb(
                            (int)(currentCalibProfile.Point30.R + (currentCalibProfile.Point60.R - currentCalibProfile.Point30.R) * t),
                            (int)(currentCalibProfile.Point30.G + (currentCalibProfile.Point60.G - currentCalibProfile.Point30.G) * t),
                            (int)(currentCalibProfile.Point30.B + (currentCalibProfile.Point60.B - currentCalibProfile.Point30.B) * t)
                        );
                    }
                    else
                    {
                        float t = (v - 153) / 102f;
                        w = Color.FromArgb(
                            (int)(currentCalibProfile.Point60.R + (currentCalibProfile.Point100.R - currentCalibProfile.Point60.R) * t),
                            (int)(currentCalibProfile.Point60.G + (currentCalibProfile.Point100.G - currentCalibProfile.Point60.G) * t),
                            (int)(currentCalibProfile.Point60.B + (currentCalibProfile.Point100.B - currentCalibProfile.Point60.B) * t)
                        );
                    }

                    if (v > 0)
                    {
                        if (isRainbowActive || isAmbilightActive)
                        {
                            finalR = (int)(c.R * (w.R / (float)v));
                            finalG = (int)(c.G * (w.G / (float)v));
                            finalB = (int)(c.B * (w.B / (float)v));
                        }
                        else
                        {
                            float maxW = 0;
                            if (c.R > 0) maxW = Math.Max(maxW, w.R);
                            if (c.G > 0) maxW = Math.Max(maxW, w.G);
                            if (c.B > 0) maxW = Math.Max(maxW, w.B);

                            if (maxW > 0)
                            {
                                finalR = (int)(c.R * (w.R / maxW));
                                finalG = (int)(c.G * (w.G / maxW));
                                finalB = (int)(c.B * (w.B / maxW));
                            }
                        }
                    }
                    
                    finalR = Math.Min(255, Math.Max(0, finalR));
                    finalG = Math.Min(255, Math.Max(0, finalG));
                    finalB = Math.Min(255, Math.Max(0, finalB));
                }

                if (!isSystemOn)
                {
                    finalR = 0;
                    finalG = 0;
                    finalB = 0;
                }

                data[i * 3] = (byte)finalR;
                data[i * 3 + 1] = (byte)finalG;
                data[i * 3 + 2] = (byte)finalB;
            }

            // Stream to hardware
            if (useUdp)
            {
                try 
                { 
                    udpClient?.SendAsync(data, data.Length, ep); 
                    System.Threading.Interlocked.Increment(ref streamsSentThisSecond);
                } 
                catch { }
            }
            else
            {
                try 
                { 
                    if (serialPort != null && serialPort.IsOpen) 
                    {
                        serialPort.Write(data, 0, data.Length); 
                        System.Threading.Interlocked.Increment(ref streamsSentThisSecond);
                    }
                } 
                catch { }
            }

            if (lblDataPacket != null && lblDataPacket.IsHandleCreated)
            {
                lblDataPacket.BeginInvoke(new Action(() => {
                    lblDataPacket.Text = $"PACKET: [{data.Length} bytes]";
                }));
            }
        }

        // endpointOverride: if non-null, send to that endpoint instead of the default udpTarget (7778)
        private void SendColorToHardware(Color c, IPEndPoint endpointOverride = null)
        {
            if (!isConnected) return;
            var ep = endpointOverride ?? udpTarget;

            int finalR = c.R;
            int finalG = c.G;
            int finalB = c.B;

            // Ambilight Brightness + Saturation Curve Mapping
            if (isAmbilightActive)
            {
                int v0 = Math.Max(c.R, Math.Max(c.G, c.B));
                if (v0 > 0)
                {
                    float t_in = v0 / 255f;
                    float mapped_t, satMult;
                    if (t_in <= 0.3333f)
                    {
                        float factor = t_in / 0.3333f;
                        mapped_t = ambilightCurve[0] + (ambilightCurve[1] - ambilightCurve[0]) * factor;
                        satMult   = ambilightSatCurve[0] + (ambilightSatCurve[1] - ambilightSatCurve[0]) * factor;
                    }
                    else if (t_in <= 0.6666f)
                    {
                        float factor = (t_in - 0.3333f) / 0.3333f;
                        mapped_t = ambilightCurve[1] + (ambilightCurve[2] - ambilightCurve[1]) * factor;
                        satMult   = ambilightSatCurve[1] + (ambilightSatCurve[2] - ambilightSatCurve[1]) * factor;
                    }
                    else
                    {
                        float factor = (t_in - 0.6666f) / 0.3334f;
                        mapped_t = ambilightCurve[2] + (ambilightCurve[3] - ambilightCurve[2]) * factor;
                        satMult   = ambilightSatCurve[2] + (ambilightSatCurve[3] - ambilightSatCurve[2]) * factor;
                    }

                    // Apply brightness curve
                    int new_v = Math.Min(255, Math.Max(0, (int)(mapped_t * 255f)));
                    float scale = new_v / (float)v0;
                    c = Color.FromArgb(
                        Math.Min(255, (int)(c.R * scale)),
                        Math.Min(255, (int)(c.G * scale)),
                        Math.Min(255, (int)(c.B * scale))
                    );

                    // Apply saturation multiplier (0..2 range, clamped to 0..1)
                    if (Math.Abs(satMult - 1f) > 0.001f)
                    {
                        ColorToHsl(c, out double ch, out double cs, out double cl);
                        double newSat = Math.Min(1.0, Math.Max(0.0, cs * satMult));
                        c = ColorFromHsl(ch, newSat, cl);
                    }

                    finalR = c.R;
                    finalG = c.G;
                    finalB = c.B;
                }
            }

            if (currentCalibProfile != null)
            {
                int v = Math.Max(c.R, Math.Max(c.G, c.B));

                Color w = Color.White;
                
                if (v <= 13) // 0-5% (using dynamic min brightness at v=0)
                {
                    float t = v / 13f;
                    w = Color.FromArgb(
                        (int)(currentCalibProfile.PointMin.R + (currentCalibProfile.Point5.R - currentCalibProfile.PointMin.R) * t),
                        (int)(currentCalibProfile.PointMin.G + (currentCalibProfile.Point5.G - currentCalibProfile.PointMin.G) * t),
                        (int)(currentCalibProfile.PointMin.B + (currentCalibProfile.Point5.B - currentCalibProfile.PointMin.B) * t)
                    );
                }
                else if (v <= 76) // 5-30%
                {
                    float t = (v - 13) / 63f; // 76 - 13 = 63
                    w = Color.FromArgb(
                        (int)(currentCalibProfile.Point5.R + (currentCalibProfile.Point30.R - currentCalibProfile.Point5.R) * t),
                        (int)(currentCalibProfile.Point5.G + (currentCalibProfile.Point30.G - currentCalibProfile.Point5.G) * t),
                        (int)(currentCalibProfile.Point5.B + (currentCalibProfile.Point30.B - currentCalibProfile.Point5.B) * t)
                    );
                }
                else if (v <= 153) // 30-60%
                {
                    float t = (v - 76) / 77f; // 153 - 76 = 77
                    w = Color.FromArgb(
                        (int)(currentCalibProfile.Point30.R + (currentCalibProfile.Point60.R - currentCalibProfile.Point30.R) * t),
                        (int)(currentCalibProfile.Point30.G + (currentCalibProfile.Point60.G - currentCalibProfile.Point30.G) * t),
                        (int)(currentCalibProfile.Point30.B + (currentCalibProfile.Point60.B - currentCalibProfile.Point30.B) * t)
                    );
                }
                else // 60-100%
                {
                    float t = (v - 153) / 102f; // 255 - 153 = 102
                    w = Color.FromArgb(
                        (int)(currentCalibProfile.Point60.R + (currentCalibProfile.Point100.R - currentCalibProfile.Point60.R) * t),
                        (int)(currentCalibProfile.Point60.G + (currentCalibProfile.Point100.G - currentCalibProfile.Point60.G) * t),
                        (int)(currentCalibProfile.Point60.B + (currentCalibProfile.Point100.B - currentCalibProfile.Point60.B) * t)
                    );
                }

                if (v > 0)
                {
                    // Scale color by the mapped white point
                    if (isRainbowActive || isAmbilightActive)
                    {
                        finalR = (int)(c.R * (w.R / (float)v));
                        finalG = (int)(c.G * (w.G / (float)v));
                        finalB = (int)(c.B * (w.B / (float)v));
                    }
                    else
                    {
                        float maxW = 0;
                        if (c.R > 0) maxW = Math.Max(maxW, w.R);
                        if (c.G > 0) maxW = Math.Max(maxW, w.G);
                        if (c.B > 0) maxW = Math.Max(maxW, w.B);

                        if (maxW > 0)
                        {
                            finalR = (int)(c.R * (w.R / maxW));
                            finalG = (int)(c.G * (w.G / maxW));
                            finalB = (int)(c.B * (w.B / maxW));
                        }
                    }
                }
                
                // Clamp just in case
                finalR = Math.Min(255, Math.Max(0, finalR));
                finalG = Math.Min(255, Math.Max(0, finalG));
                finalB = Math.Min(255, Math.Max(0, finalB));
            }

            if (lastSentColor.R == finalR && lastSentColor.G == finalG && lastSentColor.B == finalB)
            {
                // To drastically improve performance and eliminate lag, 
                // do not flood the hardware bus (Serial/UDP) with redundant identical packets.
                System.Threading.Interlocked.Increment(ref streamsSentThisSecond);
                return;
            }

            lastSentColor = Color.FromArgb(finalR, finalG, finalB);

            // Ensure the packet is strictly exactly 3 bytes: [R, G, B]
            byte[] data = new byte[] { (byte)finalR, (byte)finalG, (byte)finalB };

            if (useUdp)
            {
                try 
                { 
                    udpClient?.SendAsync(data, 3, ep); 
                    System.Threading.Interlocked.Increment(ref streamsSentThisSecond);
                } 
                catch { }
            }
            else
            {
                try 
                { 
                    if (serialPort != null && serialPort.IsOpen) 
                    {
                        serialPort.Write(data, 0, 3); 
                        System.Threading.Interlocked.Increment(ref streamsSentThisSecond);
                    }
                } 
                catch { }
            }
        }

        private void TrkRgb_Scroll(object sender, EventArgs e)
        {
            if (isUpdatingSliders || !isSystemOn) return;
            Color c = Color.FromArgb(trkR.Value, trkG.Value, trkB.Value);
            
            // Retain Hue if grayscale, retain Hue/Sat if black, otherwise update
            ColorToHsv(c, out double h, out double s, out double v);
            if (v > 0.01f)
            {
                currentSat = (float)s;
                if (s > 0.01f)
                {
                    currentHue = (float)h;
                }
            }
            
            isUpdatingSliders = true;
            trkBrightness.Value = (int)(v * 100);
            isUpdatingSliders = false;
            
            UpdateActiveColor(c, true, false);
        }

        private void TrkBrightness_Scroll(object sender, EventArgs e)
        {
            if (isUpdatingSliders || !isSystemOn) return;
            
            // Reconstruct color with new brightness but same Hue/Sat
            float l = trkBrightness.Value / 100f;
            Color c = ColorFromHsv(currentHue, currentSat, l);
            
            isUpdatingSliders = true;
            trkR.Value = c.R;
            trkG.Value = c.G;
            trkB.Value = c.B;
            isUpdatingSliders = false;
            
            UpdateActiveColor(c, true, true);
        }

        private void UpdateActiveColor(Color c, bool streamToHardware = true, bool skipWheelPinUpdate = false, bool skipBrightnessUpdate = false, IPEndPoint endpointOverride = null)
        {
            if (isAddressableMode)
            {
                if (selectedSegments.Count > 0)
                {
                    foreach (int idx in selectedSegments)
                    {
                        segmentColors[idx] = c;
                    }
                }
                else
                {
                    for (int i = 0; i < addressablePixelCount; i++)
                    {
                        segmentColors[i] = c;
                    }
                }
                panelVisualizer.Invalidate();
                if (streamToHardware)
                {
                    // Addressable always goes to 7778 unless an override is provided
                    SendAddressableDataToHardware(endpointOverride);
                }
                streamToHardware = false;
            }

            currentRgb = c;
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";

            lblRgb.Text = $"RGB: {c.R}, {c.G}, {c.B}";
            lblHex.Text = $"Hex: {hex}";
            panelColorPreview.BackColor = c;

            if (isSystemOn)
            {
                panelVisualizer.BackColor = c;
            }

            // Sync sliders
            if (!isUpdatingSliders && trkR != null)
            {
                isUpdatingSliders = true;
                trkR.Value = c.R;
                trkG.Value = c.G;
                trkB.Value = c.B;
                if (!skipBrightnessUpdate)
                {
                    ColorToHsv(c, out double h, out double s, out double v);
                    trkBrightness.Value = (int)(v * 100);
                }
                isUpdatingSliders = false;
            }

            // Sync color wheel
            if (pboxColorWheel != null && !isDraggingWheelPin && !skipWheelPinUpdate)
            {
                selectedWheelPoint = GetWheelCoordinatesFromColor(c, pboxColorWheel);
                pboxColorWheel.Invalidate();
            }

            // Trigger main form redraw so that the ambient glow is repainted
            this.Invalidate();

            // Stream to hardware
            if (streamToHardware)
            {
                SendColorToHardware(c, endpointOverride);
            }

            if (lblDataPacket != null)
            {
                lblDataPacket.Text = $"PACKET: [{lastSentColor.R}, {lastSentColor.G}, {lastSentColor.B}]";
            }
        }

        // ==========================================================================
        // BUTTON ACTIONS EVENT HANDLERS
        // ==========================================================================
        private void BtnOnOff_Click(object sender, EventArgs e)
        {
            isSystemOn = !isSystemOn;
            if (isSystemOn)
            {
                btnOnOff.BackColor = Color.FromArgb(35, 36, 40);
                btnOnOff.FlatAppearance.BorderColor = Color.FromArgb(16, 185, 129); // green status
                UpdateActiveColor(currentRgb);
            }
            else
            {
                // Disable active tickers
                if (isAmbilightActive) StopAmbilight();
                if (isRainbowActive) StopRainbow();

                btnOnOff.BackColor = Color.FromArgb(24, 25, 28);
                btnOnOff.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);

                panelVisualizer.BackColor = Color.Black;
                this.Invalidate(); // turn off backdrop glow

                // Send black to hardware
                if (isAddressableMode)
                {
                    SendAddressableDataToHardware();
                }
                else
                {
                    SendColorToHardware(Color.Black);
                }
            }
        }

        private void BtnToggleAmbilight_Click(object sender, EventArgs e)
        {
            if (!isSystemOn) return;

            if (isAmbilightActive)
            {
                StopAmbilight();
            }
            else
            {
                if (isRainbowActive) StopRainbow();

                isAmbilightActive = true;
                btnToggleAmbilight.BackColor = Color.FromArgb(35, 36, 40);
                btnToggleAmbilight.FlatAppearance.BorderColor = Color.FromArgb(0, 210, 255); // Cyan active
                if (curveGraph != null) curveGraph.Visible = true;
                if (lblSmoothThreshold != null) lblSmoothThreshold.Visible = true;
                if (trkSmoothThreshold != null) trkSmoothThreshold.Visible = true;
                if (lblSmoothFrames != null) lblSmoothFrames.Visible = true;
                if (numSmoothFrames != null) numSmoothFrames.Visible = true;

                // Fire and forget the asynchronous background loop
                Task.Run(() => AmbilightCaptureLoop());
            }
        }

        private void StopAmbilight()
        {
            isAmbilightActive = false;
            btnToggleAmbilight.BackColor = Color.FromArgb(24, 25, 28);
            btnToggleAmbilight.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
            if (curveGraph != null) curveGraph.Visible = false;
            if (lblSmoothThreshold != null) lblSmoothThreshold.Visible = false;
            if (trkSmoothThreshold != null) trkSmoothThreshold.Visible = false;
            if (lblSmoothFrames != null) lblSmoothFrames.Visible = false;
            if (numSmoothFrames != null) numSmoothFrames.Visible = false;
            
            // Turn off rectangle visually when Ambilight is stopped as requested
            panelVisualizer.BackColor = Color.Black;
            this.Invalidate();
        }

        private void BtnRainbow_Click(object sender, EventArgs e)
        {
            if (!isSystemOn) return;

            if (isAddressableMode)
            {
                ContextMenuStrip animMenu = new ContextMenuStrip();
                animMenu.Renderer = new DarkMenuRenderer();
                animMenu.ShowImageMargin = false;
                animMenu.BackColor = Color.FromArgb(30, 30, 30);
                animMenu.ForeColor = Color.White;
                animMenu.Font = new Font("Segoe UI", 9f);

                string[] names = { "Solid Rainbow", "Wave", "Chase", "Comet", "Breathing", "Sparkle", "Color Wipe" };
                AddressableAnimationType[] types = { AddressableAnimationType.SolidRainbow, AddressableAnimationType.Wave, AddressableAnimationType.Chase, AddressableAnimationType.Comet, AddressableAnimationType.Breathing, AddressableAnimationType.Sparkle, AddressableAnimationType.ColorWipe };

                for (int i = 0; i < names.Length; i++)
                {
                    ToolStripMenuItem item = new ToolStripMenuItem(names[i]);
                    AddressableAnimationType t = types[i];
                    item.Click += (s, e2) => {
                        currentAnimationType = t;
                        StartRainbow();
                    };
                    if (currentAnimationType == t && isRainbowActive)
                    {
                        item.Checked = true;
                    }
                    animMenu.Items.Add(item);
                }
                
                ToolStripMenuItem stopItem = new ToolStripMenuItem("Stop Animation");
                stopItem.ForeColor = Color.IndianRed;
                stopItem.Click += (s, e2) => StopRainbow();
                if (isRainbowActive) animMenu.Items.Add(new ToolStripSeparator());
                if (isRainbowActive) animMenu.Items.Add(stopItem);

                animMenu.Show(btnRainbow, new Point(0, btnRainbow.Height));
            }
            else
            {
                if (isRainbowActive)
                {
                    StopRainbow();
                }
                else
                {
                    StartRainbow();
                }
            }
        }

        private void StartRainbow()
        {
            if (isAmbilightActive) StopAmbilight();
            isRainbowActive = true;
            btnRainbow.BackColor = Color.FromArgb(35, 36, 40);
            btnRainbow.FlatAppearance.BorderColor = Color.FromArgb(244, 114, 182); // Pink active
            timerRainbow.Start();
        }

        private void StopRainbow()
        {
            isRainbowActive = false;
            timerRainbow.Stop();
            btnRainbow.BackColor = Color.FromArgb(24, 25, 28);
            btnRainbow.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
        }

        private void BtnConnect_Click(string comPort, string ipAddress)
        {
            if (isConnected)
            {
                isConnected = false;
                
                try { if (serialPort != null && serialPort.IsOpen) serialPort.Close(); } catch { }
                try { if (udpClient != null) udpClient.Close(); } catch { }
                
                btnConnect.Text = "CONNECT";
                btnConnect.BackColor = Color.FromArgb(0, 210, 255);
                btnConnect.ForeColor = Color.Black;
            }
            else
            {
                btnConnect.Text = "CONNECTING...";
                btnConnect.Enabled = false;

                bool success = false;
                try
                {
                    if (useUdp)
                    {
                        udpClient    = new UdpClient();
                        udpTarget    = new IPEndPoint(IPAddress.Parse(ipAddress), 7778);
                        udpTarget7777 = new IPEndPoint(IPAddress.Parse(ipAddress), 7777);
                        success = true;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(comPort))
                        {
                            serialPort = new SerialPort(comPort, 115200);
                            serialPort.Open();
                            success = true;
                        }
                    }
                }
                catch
                {
                    success = false;
                }

                if (success)
                {
                    isConnected = true;
                    btnConnect.Enabled = true;
                    btnConnect.Text = "CONNECTED";
                    btnConnect.BackColor = Color.FromArgb(16, 185, 129); // Green connected
                    btnConnect.ForeColor = Color.White;
                }
                else
                {
                    MessageBox.Show("Failed to connect to hardware.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    btnConnect.Enabled = true;
                    btnConnect.Text = "CONNECT";
                }
            }
        }

        // ==========================================================================
        // GUI DESIGN SCAFFOLD FACTORIES
        // ==========================================================================
        private GlassPanel CreateContainerPanel(int x, int y, int w, int h)
        {
            return new GlassPanel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BorderRadius = 15,
                GlowSize = 3f,
                GlowColor = Color.FromArgb(80, 0, 255, 255), // Cyan subtle glow
                BorderColor = Color.FromArgb(40, 255, 255, 255),
                GlassColor = Color.FromArgb(180, 20, 20, 25)
            };
        }

        private Label CreateSectionHeader(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 120),
                Location = new Point(x, y),
                AutoSize = true
            };
        }

        private Button CreateDashboardButton(string mainText, string subText, int x, int y, bool preActive, int width = 256)
        {
            Button btn = new Button
            {
                Location = new Point(x, y),
                Size = new Size(width, 50),
                BackColor = preActive ? Color.FromArgb(35, 36, 40) : Color.FromArgb(24, 25, 28),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.TopLeft,
                Padding = width < 200 ? new Padding(6, 6, 0, 0) : new Padding(10, 6, 0, 0)
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = preActive ? Color.FromArgb(255, 255, 255, 100) : Color.FromArgb(60, 60, 60);

            float mainFontSize = width < 200 ? 8.0f : 9.5f;
            float subFontSize = width < 200 ? 6.5f : 7.5f;
            int textX = width < 200 ? 6 : 12;

            // Double line flat labeling
            Label lblMain = new Label
            {
                Text = mainText,
                Font = new Font("Segoe UI", mainFontSize, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(textX, 6),
                AutoSize = true
            };
            Label lblSub = new Label
            {
                Text = subText,
                Font = new Font("Segoe UI", subFontSize, FontStyle.Regular),
                ForeColor = Color.FromArgb(120, 120, 120),
                Location = new Point(textX, 26),
                AutoSize = true
            };

            // Route labels to forward click to buttons
            lblMain.Click += (s, e) => btn.PerformClick();
            lblSub.Click += (s, e) => btn.PerformClick();

            btn.Controls.AddRange(new Control[] { lblMain, lblSub });
            return btn;
        }

        private Button CreateTitleBarButton(string text, int x, Color hoverColor)
        {
            Button btn = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 120),
                Location = new Point(x, 0),
                Size = new Size(36, 40),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => { btn.BackColor = hoverColor; btn.ForeColor = Color.White; };
            btn.MouseLeave += (s, e) => { btn.BackColor = Color.Transparent; btn.ForeColor = Color.FromArgb(120, 120, 120); };
            return btn;
        }

        // ==========================================================================
        // HSL/RGB AND COORDINATES CONVERSION MATHS
        // ==========================================================================
        private Color ColorFromHsl(double h, double s, double l)
        {
            double r = 0, g = 0, b = 0;
            if (s == 0)
            {
                r = g = b = l; // achromatic
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h / 360.0 + 1.0 / 3.0);
                g = HueToRgb(p, q, h / 360.0);
                b = HueToRgb(p, q, h / 360.0 - 1.0 / 3.0);
            }
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        private double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        private void ColorToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0;
            double g = c.G / 255.0;
            double b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            h = 0; s = 0; l = (max + min) / 2.0;
            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                if (max == r)      h = ((g - b) / d + (g < b ? 6.0 : 0.0)) * 60.0;
                else if (max == g) h = ((b - r) / d + 2.0) * 60.0;
                else               h = ((r - g) / d + 4.0) * 60.0;
            }
        }

        private void ColorToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0;
            double g = c.G / 255.0;
            double b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            v = max;
            double d = max - min;
            s = max == 0 ? 0 : d / max;
            h = 0;
            if (max != min)
            {
                if (max == r) h = (g - b) / d + (g < b ? 6.0 : 0.0);
                else if (max == g) h = (b - r) / d + 2.0;
                else h = (r - g) / d + 4.0;
                h /= 6.0;
            }
            h *= 360.0;
        }

        private Color ColorFromHsv(double h, double s, double v)
        {
            int hi = Convert.ToInt32(Math.Floor(h / 60)) % 6;
            double f = h / 60 - Math.Floor(h / 60);

            v = v * 255;
            int vInt = Convert.ToInt32(v);
            int p = Convert.ToInt32(v * (1 - s));
            int q = Convert.ToInt32(v * (1 - f * s));
            int t = Convert.ToInt32(v * (1 - (1 - f) * s));

            if (hi == 0) return Color.FromArgb(255, vInt, t, p);
            else if (hi == 1) return Color.FromArgb(255, q, vInt, p);
            else if (hi == 2) return Color.FromArgb(255, p, vInt, t);
            else if (hi == 3) return Color.FromArgb(255, p, q, vInt);
            else if (hi == 4) return Color.FromArgb(255, t, p, vInt);
            else return Color.FromArgb(255, vInt, p, q);
        }

        private Point GetWheelCoordinatesFromColor(Color c, PictureBox pbox)
        {
            double r = c.R / 255.0;
            double g = c.G / 255.0;
            double b = c.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double h = 0, s = 0, v = max;

            if (max != min)
            {
                double d = max - min;
                s = max == 0 ? 0 : d / max;
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else if (max == b) h = (r - g) / d + 4;
                h /= 6.0;
            }

            double angle = (h * 360.0 - 180.0) * Math.PI / 180.0;
            double radius = Math.Min(pbox.Width / 2.0, pbox.Height / 2.0);
            double dist = s * radius;

            int cx = pbox.Width / 2;
            int cy = pbox.Height / 2;

            int sx = (int)(cx + Math.Cos(angle) * dist);
            int sy = (int)(cy + Math.Sin(angle) * dist);

            return new Point(sx, sy);
        }
    }

    // ==========================================================================
    // PREMIUM 2D BRIGHTNESS/SATURATION CURVE GRAPH
    // X-axis: brightness output (same as old 4-thumb slider)
    // Y-axis: saturation multiplier (0..2 = 0..200%), stored as 0..2 float
    // Points are fully draggable in both axes; X order is preserved.
    // ==========================================================================
    public class BrightnessSaturationGraph : Control
    {
        // _bx[i]: brightness output value 0..1 for point i  (X axis)
        // _sy[i]: saturation multiplier 0..2 for point i    (Y axis)
        private float[] _bx = new float[] { 0f, 0.33f, 0.66f, 1f };
        private float[] _sy = new float[] { 1f, 1f,    1f,    1f };

        private int  _dragging = -1;       // index of point being dragged, -1 = none
        private bool _dragHint  = false;   // first-time drag hint animation state
        private int  _hoverPt   = -1;      // index of hovered point
        private int  _animTick  = 0;       // pulse animation tick
        private System.Windows.Forms.Timer _pulseTimer;

        private const int   PAD_L  = 42;   // left padding for Y-axis label
        private const int   PAD_R  = 14;   // right padding
        private const int   PAD_T  = 16;   // top padding
        private const int   PAD_B  = 28;   // bottom padding for X-axis label
        private const int   R_NORM = 7;    // normal thumb radius
        private const int   R_HOV  = 10;  // hovered thumb radius

        private readonly Font _labelFont   = new Font("Segoe UI", 7f,   FontStyle.Bold);
        private readonly Font _axisFont    = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        private readonly Font _pctFont     = new Font("Segoe UI", 6.5f, FontStyle.Bold);

        // Fired whenever any point moves
        public event Action<float[], float[]> ValuesChanged;

        public BrightnessSaturationGraph()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(18, 19, 22);
            Cursor    = Cursors.Default;

            // Subtle pulse animation for the control points
            _pulseTimer = new System.Windows.Forms.Timer { Interval = 33 };
            _pulseTimer.Tick += (s, e) => { _animTick++; Invalidate(); };
            _pulseTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _pulseTimer?.Stop(); _pulseTimer?.Dispose(); }
            base.Dispose(disposing);
        }

        public void SetValues(float[] brightness, float[] saturation)
        {
            for (int i = 0; i < 4; i++)
            {
                _bx[i] = Math.Max(0f, Math.Min(1f,  brightness[i]));
                _sy[i] = Math.Max(0f, Math.Min(2f, saturation[i]));
            }
            Invalidate();
        }

        // ── Coordinate helpers ─────────────────────────────────────────────────
        private Rectangle GraphRect => new Rectangle(PAD_L, PAD_T, Width - PAD_L - PAD_R, Height - PAD_T - PAD_B);

        private PointF ToScreen(float bx, float sy)
        {
            var gr = GraphRect;
            float x = gr.Left + bx * gr.Width;
            float y = gr.Bottom - (sy / 2f) * gr.Height;  // Y=0 at bottom, Y=2 at top
            return new PointF(x, y);
        }

        private (float bx, float sy) FromScreen(int px, int py)
        {
            var gr = GraphRect;
            float bx = Math.Max(0f, Math.Min(1f,  (px - gr.Left) / (float)gr.Width));
            float sy = Math.Max(0f, Math.Min(2f, (1f - (py - gr.Top) / (float)gr.Height) * 2f));
            return (bx, sy);
        }

        private int HitTest(int px, int py)
        {
            for (int i = 0; i < 4; i++)
            {
                PointF pt = ToScreen(_bx[i], _sy[i]);
                float dx = px - pt.X, dy = py - pt.Y;
                if (dx * dx + dy * dy <= (R_HOV + 3) * (R_HOV + 3)) return i;
            }
            return -1;
        }

        // ── Mouse handling ─────────────────────────────────────────────────────
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _dragging = HitTest(e.X, e.Y);
            if (_dragging >= 0) Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int prev = _hoverPt;
            _hoverPt = HitTest(e.X, e.Y);
            if (_hoverPt != prev) Invalidate();
            Cursor = _hoverPt >= 0 || _dragging >= 0 ? Cursors.SizeAll : Cursors.Default;

            if (_dragging < 0) return;

            var (newBx, newSy) = FromScreen(e.X, e.Y);

            // Constrain X order (left neighbour must stay to the left)
            float xMin = _dragging > 0 ? _bx[_dragging - 1] + 0.01f : 0f;
            float xMax = _dragging < 3 ? _bx[_dragging + 1] - 0.01f : 1f;
            newBx = Math.Max(xMin, Math.Min(xMax, newBx));

            _bx[_dragging] = newBx;
            _sy[_dragging] = newSy;
            Invalidate();

            ValuesChanged?.Invoke((float[])_bx.Clone(), (float[])_sy.Clone());
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = -1;
            Cursor = _hoverPt >= 0 ? Cursors.SizeAll : Cursors.Default;
        }

        // ── Painting ───────────────────────────────────────────────────────────
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode      = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint  = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode  = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            var gr  = GraphRect;
            var bg  = Color.FromArgb(18, 19, 22);
            var cyan = Color.FromArgb(0, 210, 255);

            // ── Background fill with subtle gradient ──────────────────────────
            using (var bgBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                ClientRectangle,
                Color.FromArgb(22, 23, 27),
                Color.FromArgb(14, 15, 18),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                g.FillRectangle(bgBrush, ClientRectangle);

            // ── Grid lines ───────────────────────────────────────────────────
            // Horizontal grid: 0%, 50%, 100%, 150%, 200% saturation
            float[] yGridVals = { 0f, 0.5f, 1f, 1.5f, 2f };
            string[] yGridLabels = { "0%", "50%", "100%", "150%", "200%" };
            for (int gi = 0; gi < yGridVals.Length; gi++)
            {
                PointF p = ToScreen(0, yGridVals[gi]);
                bool isSpecial = (gi == 2); // 100% line is highlighted
                using (var pen = new Pen(isSpecial
                    ? Color.FromArgb(55, 0, 210, 255)
                    : Color.FromArgb(28, 255, 255, 255), isSpecial ? 1.2f : 0.8f))
                {
                    pen.DashStyle = isSpecial
                        ? System.Drawing.Drawing2D.DashStyle.Solid
                        : System.Drawing.Drawing2D.DashStyle.Dash;
                    g.DrawLine(pen, gr.Left, p.Y, gr.Right, p.Y);
                }
                // Y-axis tick labels
                SizeF lsz = g.MeasureString(yGridLabels[gi], _pctFont);
                using (var br = new SolidBrush(isSpecial ? Color.FromArgb(130, 0, 210, 255) : Color.FromArgb(60, 255, 255, 255)))
                    g.DrawString(yGridLabels[gi], _pctFont, br, gr.Left - lsz.Width - 3, p.Y - lsz.Height / 2);
            }

            // Vertical grid: at each of the 4 brightness X positions
            float[] xGridNorm = { 0f, 1f/3f, 2f/3f, 1f };
            string[] xBrightLabels = { "0%", "33%", "66%", "100%" };
            for (int gi = 0; gi < xGridNorm.Length; gi++)
            {
                float gx = gr.Left + xGridNorm[gi] * gr.Width;
                using (var pen = new Pen(Color.FromArgb(22, 255, 255, 255), 0.8f))
                {
                    pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    g.DrawLine(pen, gx, gr.Top, gx, gr.Bottom);
                }
            }

            // ── Border box ───────────────────────────────────────────────────
            using (var pen = new Pen(Color.FromArgb(38, 0, 210, 255), 1f))
                g.DrawRectangle(pen, gr);

            // ── Smooth Catmull-Rom spline ─────────────────────────────────────
            // Build screen points sorted by X
            PointF[] pts = new PointF[4];
            for (int i = 0; i < 4; i++) pts[i] = ToScreen(_bx[i], _sy[i]);

            // Clamp all points inside the graph rect
            for (int i = 0; i < 4; i++)
            {
                pts[i] = new PointF(
                    Math.Max(gr.Left, Math.Min(gr.Right,  pts[i].X)),
                    Math.Max(gr.Top,  Math.Min(gr.Bottom, pts[i].Y)));
            }

            // Generate dense polyline via Catmull-Rom
            List<PointF> curve = new List<PointF>();
            const int STEPS = 60;
            for (int seg = 0; seg < 3; seg++)
            {
                PointF p0 = seg > 0     ? pts[seg - 1] : pts[0];
                PointF p1 = pts[seg];
                PointF p2 = pts[seg + 1];
                PointF p3 = seg < 2     ? pts[seg + 2] : pts[3];
                for (int s = 0; s <= STEPS; s++)
                {
                    float tt = s / (float)STEPS;
                    float tt2 = tt * tt, tt3 = tt2 * tt;
                    float x = 0.5f * ((2*p1.X) + (-p0.X+p2.X)*tt + (2*p0.X-5*p1.X+4*p2.X-p3.X)*tt2 + (-p0.X+3*p1.X-3*p2.X+p3.X)*tt3);
                    float y = 0.5f * ((2*p1.Y) + (-p0.Y+p2.Y)*tt + (2*p0.Y-5*p1.Y+4*p2.Y-p3.Y)*tt2 + (-p0.Y+3*p1.Y-3*p2.Y+p3.Y)*tt3);
                    x = Math.Max(gr.Left, Math.Min(gr.Right, x));
                    y = Math.Max(gr.Top,  Math.Min(gr.Bottom, y));
                    curve.Add(new PointF(x, y));
                }
            }

            if (curve.Count >= 2)
            {
                PointF[] curveArr = curve.ToArray();
                // 3-pass glow: outer glow → inner glow → bright core
                using (var pen = new Pen(Color.FromArgb(18, 0, 210, 255), 14f)) g.DrawLines(pen, curveArr);
                using (var pen = new Pen(Color.FromArgb(50, 0, 210, 255), 7f))  g.DrawLines(pen, curveArr);
                using (var pen = new Pen(Color.FromArgb(255, 0, 210, 255), 1.8f)) g.DrawLines(pen, curveArr);
            }

            // ── Control points ────────────────────────────────────────────────
            double pulse = Math.Sin(_animTick * 0.12) * 0.5 + 0.5;  // 0..1 oscillation

            for (int i = 0; i < 4; i++)
            {
                PointF pt   = pts[i];
                bool isHov  = (i == _hoverPt || i == _dragging);
                int   r     = isHov ? R_HOV : R_NORM;
                float pulseR = isHov ? 0 : (float)(pulse * 3.5);

                // Outer pulse ring
                if (!isHov)
                {
                    using (var pen = new Pen(Color.FromArgb((int)(30 + pulse * 60), 0, 210, 255), 1.5f))
                        g.DrawEllipse(pen,
                            pt.X - r - pulseR, pt.Y - r - pulseR,
                            (r + pulseR) * 2, (r + pulseR) * 2);
                }

                // Glow halo
                using (var pen = new Pen(Color.FromArgb(isHov ? 110 : 55, 0, 210, 255), isHov ? 5f : 3.5f))
                    g.DrawEllipse(pen, pt.X - r - 3, pt.Y - r - 3, (r + 3) * 2, (r + 3) * 2);

                // Inner white fill
                using (var br = new SolidBrush(isHov ? Color.White : Color.FromArgb(220, 255, 255, 255)))
                    g.FillEllipse(br, pt.X - r, pt.Y - r, r * 2, r * 2);

                // Cyan ring
                using (var pen = new Pen(Color.FromArgb(0, 210, 255), isHov ? 2.5f : 1.8f))
                    g.DrawEllipse(pen, pt.X - r, pt.Y - r, r * 2, r * 2);

                // Sat % label near the point
                int satPct = (int)Math.Round(_sy[i] * 100);
                string satLabel = $"{satPct}%";
                SizeF slsz = g.MeasureString(satLabel, _pctFont);
                float lx = pt.X - slsz.Width / 2;
                float ly = pt.Y - r - slsz.Height - 3;
                if (ly < gr.Top) ly = pt.Y + r + 3;
                using (var br = new SolidBrush(Color.FromArgb(0, 210, 255)))
                    g.DrawString(satLabel, _pctFont, br, lx, ly);
            }

            // ── Axis labels ───────────────────────────────────────────────────
            // X-axis: "BRIGHTNESS" centered below the graph
            {
                string xl = "BRIGHTNESS";
                SizeF xsz = g.MeasureString(xl, _axisFont);
                using (var br = new SolidBrush(Color.FromArgb(90, 0, 210, 255)))
                    g.DrawString(xl, _axisFont, br, gr.Left + gr.Width / 2f - xsz.Width / 2f, gr.Bottom + 6);
            }

            // Y-axis: "SATURATION" rotated along left edge
            {
                string yl = "SATURATION";
                var state = g.Save();
                SizeF ysz = g.MeasureString(yl, _axisFont);
                g.TranslateTransform(10, gr.Top + gr.Height / 2f + ysz.Width / 2f);
                g.RotateTransform(-90);
                using (var br = new SolidBrush(Color.FromArgb(90, 0, 210, 255)))
                    g.DrawString(yl, _axisFont, br, 0, 0);
                g.Restore(state);
            }

            // ── Decorative diamond (bottom-right corner) ──────────────────────
            {
                int dmx = gr.Right - 12, dmy = gr.Bottom - 12;
                int dms = 6;
                PointF[] diamond = {
                    new PointF(dmx,      dmy - dms),
                    new PointF(dmx + dms, dmy),
                    new PointF(dmx,      dmy + dms),
                    new PointF(dmx - dms, dmy)
                };
                using (var br = new SolidBrush(Color.FromArgb(60, 0, 210, 255)))
                    g.FillPolygon(br, diamond);
            }
        }
    }

    public enum AddressableAnimationType
    {
        SolidRainbow,
        Wave,
        Chase,
        Comet,
        Breathing,
        Sparkle,
        ColorWipe
    }

    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }
    }

    public class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(50, 50, 50);
        public override Color MenuItemBorder => Color.FromArgb(50, 50, 50);
        public override Color ToolStripDropDownBackground => Color.FromArgb(30, 30, 30);
        public override Color ImageMarginGradientBegin => Color.FromArgb(30, 30, 30);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(30, 30, 30);
        public override Color ImageMarginGradientEnd => Color.FromArgb(30, 30, 30);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(50, 50, 50);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(50, 50, 50);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(40, 40, 40);
        public override Color MenuItemPressedGradientMiddle => Color.FromArgb(40, 40, 40);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(40, 40, 40);
    }
}
