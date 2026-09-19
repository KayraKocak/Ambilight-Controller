using System;
using System.Drawing;
using System.Windows.Forms;

namespace AmbilightControllerForm
{
    public class CaptureAreaOverlayForm : Form
    {
        public Rectangle[] Areas { get; private set; }
        public int GridWidth { get; private set; }
        public int GridHeight { get; private set; }
        public int MarginX { get; private set; }
        public int MarginY { get; private set; }
        public int InnerMargin { get; private set; }

        public event Action<Rectangle[]> OnAreasChanged;
        public event Action<int, int> OnGridSizeChanged;
        public event Action<int, int> OnMarginSizeChanged;
        public event Action<int> OnInnerMarginChanged;

        private bool _isAddressableMode;
        private int _pixelCount;
        private int _resizingIndex = -1;
        private bool _isResizingTop = false;
        private int _dragStartY;
        private int _dragStartRectY;
        private int _dragStartRectHeight;
        private const int DRAG_TOLERANCE = 10;

        private Button _btnSave;
        private Label _lblGridWidth;
        private NumericUpDown _numGridWidth;
        private Label _lblGridHeight;
        private NumericUpDown _numGridHeight;
        private Label _lblMarginX;
        private NumericUpDown _numMarginX;
        private Label _lblMarginY;
        private NumericUpDown _numMarginY;
        private Label _lblInnerMargin;
        private NumericUpDown _numInnerMargin;
        private Panel _controlPanel;

        public CaptureAreaOverlayForm(bool isAddressableMode, int pixelCount, int gridWidth, int gridHeight, int marginX, int marginY, int innerMargin = 0, Rectangle[] initialAreas = null)
        {
            _isAddressableMode = isAddressableMode;
            _pixelCount = pixelCount;
            GridWidth = Math.Max(1, gridWidth);
            GridHeight = Math.Max(1, gridHeight);
            InnerMargin = Math.Max(0, innerMargin);
            
            Screen screen = Screen.PrimaryScreen;
            int screenWidth = screen.Bounds.Width;
            int screenHeight = screen.Bounds.Height;

            MarginX = marginX < 0 ? screenWidth / 15 : marginX;
            MarginY = marginY < 0 ? screenHeight / 15 : marginY;
            
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta;
            this.DoubleBuffered = true;

            if (_isAddressableMode)
            {
                InitializeAreas(initialAreas);
            }

            _controlPanel = new Panel
            {
                Size = new Size(1000, 60),
                BackColor = Color.FromArgb(220, 20, 20, 25),
                BorderStyle = BorderStyle.FixedSingle
            };

            _lblGridWidth = new Label
            {
                Text = "Grid W:",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(12, 18),
                AutoSize = true
            };

            _numGridWidth = new NumericUpDown
            {
                Location = new Point(75, 16),
                Size = new Size(70, 26),
                Minimum = 1,
                Maximum = decimal.MaxValue,
                Value = GridWidth,
                Font = new Font("Segoe UI", 9.5f)
            };
            _numGridWidth.ValueChanged += (s, e) => {
                GridWidth = (int)_numGridWidth.Value;
                OnGridSizeChanged?.Invoke(GridWidth, GridHeight);
                this.Invalidate();
            };

            _lblGridHeight = new Label
            {
                Text = "Grid H:",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(160, 18),
                AutoSize = true
            };

            _numGridHeight = new NumericUpDown
            {
                Location = new Point(220, 16),
                Size = new Size(70, 26),
                Minimum = 1,
                Maximum = decimal.MaxValue,
                Value = GridHeight,
                Font = new Font("Segoe UI", 9.5f)
            };
            _numGridHeight.ValueChanged += (s, e) => {
                GridHeight = (int)_numGridHeight.Value;
                OnGridSizeChanged?.Invoke(GridWidth, GridHeight);
                this.Invalidate();
            };

            _lblMarginX = new Label
            {
                Text = "Margin X:",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(305, 18),
                AutoSize = true
            };

            _numMarginX = new NumericUpDown
            {
                Location = new Point(385, 16),
                Size = new Size(70, 26),
                Minimum = 0,
                Maximum = decimal.MaxValue,
                Value = MarginX,
                Font = new Font("Segoe UI", 9.5f)
            };
            _numMarginX.ValueChanged += (s, e) => {
                MarginX = (int)_numMarginX.Value;
                if (_isAddressableMode)
                {
                    UpdateAddressableAreasFromMargins();
                }
                OnMarginSizeChanged?.Invoke(MarginX, MarginY);
                this.Invalidate();
            };

            _lblMarginY = new Label
            {
                Text = "Margin Y:",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(470, 18),
                AutoSize = true
            };

            _numMarginY = new NumericUpDown
            {
                Location = new Point(550, 16),
                Size = new Size(70, 26),
                Minimum = 0,
                Maximum = decimal.MaxValue,
                Value = MarginY,
                Font = new Font("Segoe UI", 9.5f)
            };
            _numMarginY.ValueChanged += (s, e) => {
                MarginY = (int)_numMarginY.Value;
                if (_isAddressableMode)
                {
                    UpdateAddressableAreasFromMargins();
                }
                OnMarginSizeChanged?.Invoke(MarginX, MarginY);
                this.Invalidate();
            };

            _lblInnerMargin = new Label
            {
                Text = "Inner M:",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(635, 18),
                AutoSize = true,
                Visible = _isAddressableMode
            };

            _numInnerMargin = new NumericUpDown
            {
                Location = new Point(705, 16),
                Size = new Size(70, 26),
                Minimum = 0,
                Maximum = decimal.MaxValue,
                Value = InnerMargin,
                Font = new Font("Segoe UI", 9.5f),
                Visible = _isAddressableMode
            };
            _numInnerMargin.ValueChanged += (s, e) => {
                InnerMargin = (int)_numInnerMargin.Value;
                OnInnerMarginChanged?.Invoke(InnerMargin);
                this.Invalidate();
            };

            _btnSave = new Button
            {
                Text = "SAVE && CLOSE",
                Size = new Size(130, 36),
                Location = new Point(855, 11),
                BackColor = Color.FromArgb(0, 210, 255),
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += (s, e) => {
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            _controlPanel.Controls.AddRange(new Control[] { 
                _lblGridWidth, _numGridWidth, 
                _lblGridHeight, _numGridHeight,
                _lblMarginX, _numMarginX,
                _lblMarginY, _numMarginY,
                _lblInnerMargin, _numInnerMargin,
                _btnSave 
            });
            this.Controls.Add(_controlPanel);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _controlPanel.Location = new Point((this.Width - _controlPanel.Width) / 2, 40);
        }

        private void UpdateAddressableAreasFromMargins()
        {
            Screen screen = Screen.PrimaryScreen;
            int screenWidth = screen != null ? screen.Bounds.Width : 1920;
            int screenHeight = screen != null ? screen.Bounds.Height : 1080;

            int capWidth = Math.Max(1, screenWidth - 2 * MarginX);
            int capHeight = Math.Max(1, screenHeight - 2 * MarginY);

            float segmentWidth = (float)capWidth / _pixelCount;
            Areas = new Rectangle[_pixelCount];
            for (int i = 0; i < _pixelCount; i++)
            {
                Areas[i] = new Rectangle(
                    MarginX + (int)(i * segmentWidth),
                    MarginY,
                    (int)segmentWidth,
                    capHeight
                );
            }
            OnAreasChanged?.Invoke((Rectangle[])Areas.Clone());
        }

        private void InitializeAreas(Rectangle[] initialAreas)
        {
            if (initialAreas != null && initialAreas.Length == _pixelCount)
            {
                Areas = new Rectangle[_pixelCount];
                Array.Copy(initialAreas, Areas, _pixelCount);
            }
            else
            {
                UpdateAddressableAreasFromMargins();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (_isAddressableMode && Areas != null)
            {
                using (Pen pen = new Pen(Color.White, 2f))
                {
                    for (int i = 0; i < _pixelCount; i++)
                    {
                        Rectangle rect = Areas[i];
                        e.Graphics.DrawRectangle(pen, rect);

                        // Draw 5-pixel white dots for sample points within each segment
                        DrawGridDots(e.Graphics, rect, InnerMargin);
                    }
                }
            }
            else
            {
                // Generic mode: capture area is defined by MarginX and MarginY
                Screen screen = Screen.PrimaryScreen;
                int screenWidth = screen.Bounds.Width;
                int screenHeight = screen.Bounds.Height;
                int capW = Math.Max(1, screenWidth - 2 * MarginX);
                int capH = Math.Max(1, screenHeight - 2 * MarginY);
                Rectangle captureBounds = new Rectangle(MarginX, MarginY, capW, capH);

                using (Pen pen = new Pen(Color.Cyan, 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                {
                    e.Graphics.DrawRectangle(pen, captureBounds);
                }

                // Draw 5-pixel white dots for sample points across generic capture area
                DrawGridDots(e.Graphics, captureBounds, 0);
            }
        }

        private void DrawGridDots(Graphics g, Rectangle bounds, int innerMargin = 0)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            int startX = bounds.X + innerMargin;
            int startY = bounds.Y + innerMargin;
            int endX = bounds.Right - innerMargin;
            int endY = bounds.Bottom - innerMargin;

            int capW = endX - startX;
            int capH = endY - startY;

            if (capW < 0 || capH < 0)
            {
                startX = bounds.X;
                startY = bounds.Y;
                endX = bounds.Right;
                endY = bounds.Bottom;
                capW = endX - startX;
                capH = endY - startY;
            }

            float stepX = GridWidth > 1 ? (float)capW / (GridWidth - 1) : 0f;
            float stepY = GridHeight > 1 ? (float)capH / (GridHeight - 1) : 0f;

            for (int y = 0; y < GridHeight; y++)
            {
                int ptY = GridHeight > 1 ? startY + (int)Math.Round(y * stepY) : startY + capH / 2;
                if (ptY > bounds.Bottom) break;

                for (int x = 0; x < GridWidth; x++)
                {
                    int ptX = GridWidth > 1 ? startX + (int)Math.Round(x * stepX) : startX + capW / 2;
                    if (ptX > bounds.Right) break;

                    // 5 pixel white dot (centered on sample point)
                    g.FillEllipse(Brushes.White, ptX - 2, ptY - 2, 5, 5);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_isAddressableMode && e.Button == MouseButtons.Left && Areas != null)
            {
                for (int i = 0; i < _pixelCount; i++)
                {
                    Rectangle rect = Areas[i];
                    if (e.X >= rect.Left && e.X <= rect.Right)
                    {
                        if (Math.Abs(e.Y - rect.Top) <= DRAG_TOLERANCE)
                        {
                            _resizingIndex = i;
                            _isResizingTop = true;
                            _dragStartY = e.Y;
                            _dragStartRectY = rect.Top;
                            _dragStartRectHeight = rect.Height;
                            break;
                        }
                        else if (Math.Abs(e.Y - rect.Bottom) <= DRAG_TOLERANCE)
                        {
                            _resizingIndex = i;
                            _isResizingTop = false;
                            _dragStartY = e.Y;
                            _dragStartRectY = rect.Top;
                            _dragStartRectHeight = rect.Height;
                            break;
                        }
                    }
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isAddressableMode && _resizingIndex != -1 && Areas != null)
            {
                int dy = e.Y - _dragStartY;
                Rectangle rect = Areas[_resizingIndex];

                if (_isResizingTop)
                {
                    int newY = Math.Max(0, Math.Min(_dragStartRectY + dy, _dragStartRectY + _dragStartRectHeight - 10));
                    int newHeight = _dragStartRectHeight - (newY - _dragStartRectY);
                    Areas[_resizingIndex] = new Rectangle(rect.X, newY, rect.Width, newHeight);
                }
                else
                {
                    int newHeight = Math.Max(10, Math.Min(_dragStartRectHeight + dy, this.Height - _dragStartRectY));
                    Areas[_resizingIndex] = new Rectangle(rect.X, rect.Y, rect.Width, newHeight);
                }

                this.Invalidate();
                OnAreasChanged?.Invoke((Rectangle[])Areas.Clone());
            }
            else if (_isAddressableMode && Areas != null)
            {
                bool hoverEdge = false;
                for (int i = 0; i < _pixelCount; i++)
                {
                    Rectangle rect = Areas[i];
                    if (e.X >= rect.Left && e.X <= rect.Right)
                    {
                        if (Math.Abs(e.Y - rect.Top) <= DRAG_TOLERANCE || Math.Abs(e.Y - rect.Bottom) <= DRAG_TOLERANCE)
                        {
                            hoverEdge = true;
                            break;
                        }
                    }
                }
                this.Cursor = hoverEdge ? Cursors.SizeNS : Cursors.Default;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _resizingIndex = -1;
        }
    }
}
