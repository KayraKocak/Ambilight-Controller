#pragma warning disable WFO1000

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AmbilightControllerForm
{
    public class GlassPanel : Panel
    {
        public int BorderRadius { get; set; } = 15;
        public Color BorderColor { get; set; } = Color.FromArgb(100, 255, 255, 255);
        public float BorderThickness { get; set; } = 1f;
        public Color GlowColor { get; set; } = Color.Cyan;
        public float GlowSize { get; set; } = 3f;
        public Color GlassColor { get; set; } = Color.FromArgb(150, 20, 20, 25);

        public GlassPanel()
        {
            this.SetStyle(ControlStyles.SupportsTransparentBackColor | 
                          ControlStyles.OptimizedDoubleBuffer | 
                          ControlStyles.AllPaintingInWmPaint | 
                          ControlStyles.UserPaint, true);
            this.BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(
                (int)GlowSize, 
                (int)GlowSize, 
                this.Width - (int)GlowSize * 2 - 1, 
                this.Height - (int)GlowSize * 2 - 1);

            using (GraphicsPath path = GetRoundedPath(rect, BorderRadius))
            {
                // Fill Glass
                using (SolidBrush brush = new SolidBrush(GlassColor))
                {
                    e.Graphics.FillPath(brush, path);
                }

                // Draw outer glow
                if (GlowSize > 0 && GlowColor != Color.Transparent)
                {
                    for (int i = 1; i <= GlowSize; i++)
                    {
                        int alpha = (int)(50 * (1f - (i / GlowSize)));
                        using (Pen glowPen = new Pen(Color.FromArgb(alpha, GlowColor), i * 1.5f))
                        {
                            glowPen.LineJoin = LineJoin.Round;
                            e.Graphics.DrawPath(glowPen, path);
                        }
                    }
                }

                // Draw crisp inner border
                if (BorderThickness > 0)
                {
                    using (Pen borderPen = new Pen(BorderColor, BorderThickness))
                    {
                        e.Graphics.DrawPath(borderPen, path);
                    }
                }
            }
            
            base.OnPaint(e);
        }

        private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
    
    public class GlassSlider : Control
    {
        private int _value = 0;
        private int _min = 0;
        private int _max = 255;
        private bool _isDragging = false;

        public event EventHandler Scroll;

        public Orientation Orientation { get; set; } = Orientation.Horizontal;

        public int Minimum
        {
            get => _min;
            set { _min = value; Invalidate(); }
        }

        public int Maximum
        {
            get => _max;
            set { _max = value; Invalidate(); }
        }

        public int Value
        {
            get => _value;
            set
            {
                int val = Math.Max(_min, Math.Min(_max, value));
                if (_value != val)
                {
                    _value = val;
                    Invalidate();
                    Scroll?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        
        public Color ThumbColor { get; set; } = Color.White;
        public Color TrackColor { get; set; } = Color.FromArgb(100, 255, 255, 255);
        public Color FillColor { get; set; } = Color.Cyan;

        public GlassSlider()
        {
            this.SetStyle(ControlStyles.SupportsTransparentBackColor | 
                          ControlStyles.OptimizedDoubleBuffer | 
                          ControlStyles.AllPaintingInWmPaint | 
                          ControlStyles.UserPaint, true);
            this.BackColor = Color.Transparent;
            this.Size = new Size(200, 20);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int trackThickness = 4;
            
            float percentage = (float)(_value - _min) / Math.Max(1, _max - _min);
            if (percentage < 0) percentage = 0;
            if (percentage > 1) percentage = 1;

            Rectangle trackRect;
            Rectangle fillRect;
            Rectangle thumbRect;
            int thumbRadius = 7;

            if (Orientation == Orientation.Horizontal)
            {
                int trackY = (this.Height - trackThickness) / 2;
                trackRect = new Rectangle(10, trackY, this.Width - 20, trackThickness);
                
                int fillWidth = (int)((this.Width - 20) * percentage);
                fillRect = new Rectangle(10, trackY, fillWidth, trackThickness);
                
                int thumbX = 10 + fillWidth;
                int thumbY = this.Height / 2;
                thumbRect = new Rectangle(thumbX - thumbRadius, thumbY - thumbRadius, thumbRadius * 2, thumbRadius * 2);
            }
            else
            {
                int trackX = (this.Width - trackThickness) / 2;
                trackRect = new Rectangle(trackX, 10, trackThickness, this.Height - 20);
                
                int fillHeight = (int)((this.Height - 20) * percentage);
                // Vertical fills from bottom to top
                fillRect = new Rectangle(trackX, this.Height - 10 - fillHeight, trackThickness, fillHeight);
                
                int thumbX = this.Width / 2;
                int thumbY = this.Height - 10 - fillHeight;
                thumbRect = new Rectangle(thumbX - thumbRadius, thumbY - thumbRadius, thumbRadius * 2, thumbRadius * 2);
            }

            // Draw track
            using (SolidBrush trackBrush = new SolidBrush(TrackColor))
            {
                e.Graphics.FillRectangle(trackBrush, trackRect);
            }

            // Draw fill
            if (fillRect.Width > 0 && fillRect.Height > 0)
            {
                using (SolidBrush fillBrush = new SolidBrush(FillColor))
                {
                    e.Graphics.FillRectangle(fillBrush, fillRect);
                }
            }
            
            // Thumb glow
            using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(80, FillColor)))
            {
                e.Graphics.FillEllipse(glowBrush, thumbRect.X - 3, thumbRect.Y - 3, thumbRect.Width + 6, thumbRect.Height + 6);
            }
            
            // Thumb center
            using (SolidBrush thumbBrush = new SolidBrush(ThumbColor))
            {
                e.Graphics.FillEllipse(thumbBrush, thumbRect);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                UpdateValueFromMouse(e.X, e.Y);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isDragging)
            {
                UpdateValueFromMouse(e.X, e.Y);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _isDragging = false;
        }

        private void UpdateValueFromMouse(int x, int y)
        {
            float percentage;
            if (Orientation == Orientation.Horizontal)
            {
                int trackWidth = this.Width - 20;
                int localX = x - 10;
                if (localX < 0) localX = 0;
                if (localX > trackWidth) localX = trackWidth;
                percentage = (float)localX / trackWidth;
            }
            else
            {
                int trackHeight = this.Height - 20;
                int localY = y - 10;
                if (localY < 0) localY = 0;
                if (localY > trackHeight) localY = trackHeight;
                // Vertical from bottom to top means smaller Y is higher percentage
                percentage = 1f - ((float)localY / trackHeight);
            }

            int newValue = _min + (int)Math.Round(percentage * (_max - _min));
            this.Value = newValue;
        }
    }

    public class DummyFlatAppearance
    {
        public int BorderSize { get; set; }
        public Color BorderColor { get; set; }
        public Color MouseOverBackColor { get; set; }
        public Color MouseDownBackColor { get; set; }
        public Color CheckedBackColor { get; set; }
    }

    public class GlassButton : Control
    {
        private bool _isHovered = false;
        private bool _isPressed = false;

        public DummyFlatAppearance FlatAppearance { get; } = new DummyFlatAppearance();
        public FlatStyle FlatStyle { get; set; }
        public ContentAlignment TextAlign { get; set; }

        public void PerformClick()
        {
            this.OnClick(EventArgs.Empty);
        }

        public int BorderRadius { get; set; } = 10;
        public Color GlassColor { get; set; } = Color.FromArgb(60, 255, 255, 255);
        public Color HoverColor { get; set; } = Color.FromArgb(90, 255, 255, 255);
        public Color PressedColor { get; set; } = Color.FromArgb(40, 255, 255, 255);
        public Color GlowColor { get; set; } = Color.Transparent;
        public Color BorderColor { get; set; } = Color.FromArgb(100, 255, 255, 255);
        public float BorderThickness { get; set; } = 1f;

        private Image _glassImage;

        public GlassButton()
        {
            this.SetStyle(ControlStyles.SupportsTransparentBackColor | 
                          ControlStyles.OptimizedDoubleBuffer | 
                          ControlStyles.AllPaintingInWmPaint | 
                          ControlStyles.UserPaint, true);
            this.BackColor = Color.Transparent; // Calls the original property
            this.ForeColor = Color.White;
            this.Size = new Size(120, 40);
        }

        // Hide BackColor to trick WinForms framework into transparency, while retaining value for Form1
        public new Color BackColor
        {
            get => GlassColor;
            set
            {
                GlassColor = value;
                base.BackColor = Color.Transparent;
                Invalidate();
            }
        }

        // Hide BackgroundImage to stop Control from drawing it squarely over our rounded borders
        public new Image BackgroundImage
        {
            get => _glassImage;
            set
            {
                _glassImage = value;
                base.BackgroundImage = null;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle rect = new Rectangle(1, 1, this.Width - 2, this.Height - 2);
            using (GraphicsPath path = GetRoundedPath(rect, BorderRadius))
            {
                if (_glassImage != null)
                {
                    e.Graphics.SetClip(path);
                    e.Graphics.DrawImage(_glassImage, this.ClientRectangle);
                    e.Graphics.ResetClip();
                }
                else
                {
                    // Fill
                    Color fillColor = GlassColor;
                    if (_isPressed)
                    {
                        fillColor = ControlPaint.Dark(GlassColor, 0.1f);
                    }
                    else if (_isHovered)
                    {
                        fillColor = ControlPaint.Light(GlassColor, 0.2f);
                    }

                    using (SolidBrush brush = new SolidBrush(fillColor))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                }

                // Border/Glow
                if (GlowColor != Color.Transparent && (_isHovered || _isPressed))
                {
                    using (Pen glowPen = new Pen(Color.FromArgb(80, GlowColor), 2f))
                    {
                        e.Graphics.DrawPath(glowPen, path);
                    }
                }
                else if (BorderThickness > 0)
                {
                    using (Pen borderPen = new Pen(BorderColor, BorderThickness))
                    {
                        e.Graphics.DrawPath(borderPen, path);
                    }
                }
            }

            // Text
            if (!string.IsNullOrEmpty(this.Text))
            {
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(e.Graphics, this.Text, this.Font, this.ClientRectangle, this.ForeColor, flags);
            }
            
            // Do not call base.OnPaint to prevent standard flat drawing
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isPressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _isPressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
