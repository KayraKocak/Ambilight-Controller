using System;
using System.Drawing;
using System.Windows.Forms;

namespace AmbilightControllerForm
{
    public class CaptureAreaOverlayForm : Form
    {
        public Rectangle[] Areas { get; private set; }
        public event Action<Rectangle[]> OnAreasChanged;

        private int _pixelCount;
        private int _resizingIndex = -1;
        private bool _isResizingTop = false;
        private int _dragStartY;
        private int _dragStartRectY;
        private int _dragStartRectHeight;
        private const int DRAG_TOLERANCE = 10;
        private Button _btnSave;

        public CaptureAreaOverlayForm(int pixelCount, Rectangle[] initialAreas = null)
        {
            _pixelCount = pixelCount;
            
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta;
            this.DoubleBuffered = true;

            InitializeAreas(initialAreas);

            _btnSave = new Button
            {
                Text = "SAVE && CLOSE",
                Size = new Size(160, 50),
                BackColor = Color.FromArgb(0, 210, 255),
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += (s, e) => {
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            this.Controls.Add(_btnSave);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _btnSave.Location = new Point((this.Width - _btnSave.Width) / 2, (this.Height - _btnSave.Height) / 2);
        }

        private void InitializeAreas(Rectangle[] initialAreas)
        {
            Screen screen = Screen.PrimaryScreen;
            int screenWidth = screen.Bounds.Width;
            int screenHeight = screen.Bounds.Height;

            if (initialAreas != null && initialAreas.Length == _pixelCount)
            {
                Areas = new Rectangle[_pixelCount];
                Array.Copy(initialAreas, Areas, _pixelCount);
            }
            else
            {
                Areas = new Rectangle[_pixelCount];
                float segmentWidth = (float)screenWidth / _pixelCount;
                for (int i = 0; i < _pixelCount; i++)
                {
                    Areas[i] = new Rectangle(
                        (int)(i * segmentWidth),
                        0,
                        (int)segmentWidth,
                        screenHeight
                    );
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(Color.White, 2f))
            {
                for (int i = 0; i < _pixelCount; i++)
                {
                    e.Graphics.DrawRectangle(pen, Areas[i]);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
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
            if (_resizingIndex != -1)
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
            else
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
