using System;
using System.Drawing;
using System.Windows.Forms;

namespace AmbilightControllerForm
{
    public class CalibrationMenuForm : Form
    {
        public CalibrationProfile ResultProfile { get; private set; }
        private CalibrationProfile internalProfile;
        private Action<Color> hoverCallback;
        private Action resetCallback;

        private NumericUpDown numRMin, numGMin, numBMin;
        private NumericUpDown numR100, numG100, numB100;
        private NumericUpDown numR60, numG60, numB60;
        private NumericUpDown numR30, numG30, numB30;
        private NumericUpDown numR5, numG5, numB5;

        public CalibrationMenuForm(CalibrationProfile startProfile, Action<Color> onHover, Action onReset)
        {
            this.internalProfile = startProfile.Clone();
            this.ResultProfile = startProfile.Clone();
            this.hoverCallback = onHover;
            this.resetCallback = onReset;

            SetupUI();
            LoadValues();
        }

        private void SetupUI()
        {
            this.Size = new Size(320, 320);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(19, 20, 23);
            
            // Border Panel
            Panel border = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
            border.BackColor = Color.FromArgb(19, 20, 23);
            this.Controls.Add(border);

            // Title
            Label lblTitle = new Label
            {
                Text = "MULTI-POINT CALIBRATION",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 180, 180),
                Location = new Point(12, 12),
                AutoSize = true
            };
            border.Controls.Add(lblTitle);

            // Headers
            Label lblH1 = new Label { Text = "R", ForeColor = Color.Red, Font = new Font("Segoe UI", 8f, FontStyle.Bold), Location = new Point(120, 40), AutoSize = true };
            Label lblH2 = new Label { Text = "G", ForeColor = Color.Lime, Font = new Font("Segoe UI", 8f, FontStyle.Bold), Location = new Point(180, 40), AutoSize = true };
            Label lblH3 = new Label { Text = "B", ForeColor = Color.DeepSkyBlue, Font = new Font("Segoe UI", 8f, FontStyle.Bold), Location = new Point(240, 40), AutoSize = true };
            border.Controls.AddRange(new Control[] { lblH1, lblH2, lblH3 });

            // Min Brightness Row
            Label lblMin = new Label { Text = "Min Brightness", ForeColor = Color.White, Location = new Point(12, 63), AutoSize = true, Font = new Font("Segoe UI", 8f) };
            numRMin = CreateNum(120, 60, 255);
            numGMin = CreateNum(180, 60, 255);
            numBMin = CreateNum(240, 60, 255);
            border.Controls.AddRange(new Control[] { lblMin, numRMin, numGMin, numBMin });
            AttachHover(lblMin, numRMin, numGMin, numBMin, 0);

            // Hook up value change events to dynamically recalculate limits
            numRMin.ValueChanged += (s, e) => UpdateDynamicLimits();
            numGMin.ValueChanged += (s, e) => UpdateDynamicLimits();
            numBMin.ValueChanged += (s, e) => UpdateDynamicLimits();

            // 100% Row
            Label lbl100 = new Label { Text = "100% (Max 255)", ForeColor = Color.White, Location = new Point(12, 103), AutoSize = true, Font = new Font("Segoe UI", 8f) };
            numR100 = CreateNum(120, 100, 255);
            numG100 = CreateNum(180, 100, 255);
            numB100 = CreateNum(240, 100, 255);
            border.Controls.AddRange(new Control[] { lbl100, numR100, numG100, numB100 });
            AttachHover(lbl100, numR100, numG100, numB100, 100);

            // 60% Row
            Label lbl60 = new Label { Text = "60% (Max 153)", ForeColor = Color.White, Location = new Point(12, 143), AutoSize = true, Font = new Font("Segoe UI", 8f) };
            numR60 = CreateNum(120, 140, 153);
            numG60 = CreateNum(180, 140, 153);
            numB60 = CreateNum(240, 140, 153);
            border.Controls.AddRange(new Control[] { lbl60, numR60, numG60, numB60 });
            AttachHover(lbl60, numR60, numG60, numB60, 60);

            // 30% Row
            Label lbl30 = new Label { Text = "30% (Max 76)", ForeColor = Color.White, Location = new Point(12, 183), AutoSize = true, Font = new Font("Segoe UI", 8f) };
            numR30 = CreateNum(120, 180, 76);
            numG30 = CreateNum(180, 180, 76);
            numB30 = CreateNum(240, 180, 76);
            border.Controls.AddRange(new Control[] { lbl30, numR30, numG30, numB30 });
            AttachHover(lbl30, numR30, numG30, numB30, 30);

            // 5% Row
            Label lbl5 = new Label { Text = "5% (Max 13)", ForeColor = Color.White, Location = new Point(12, 223), AutoSize = true, Font = new Font("Segoe UI", 8f) };
            numR5 = CreateNum(120, 220, 13);
            numG5 = CreateNum(180, 220, 13);
            numB5 = CreateNum(240, 220, 13);
            border.Controls.AddRange(new Control[] { lbl5, numR5, numG5, numB5 });
            AttachHover(lbl5, numR5, numG5, numB5, 5);

            // Buttons
            Button btnSave = new Button { Text = "SAVE", Location = new Point(190, 270), Size = new Size(100, 30), BackColor = Color.FromArgb(0, 210, 255), ForeColor = Color.Black, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (s, e) => {
                SaveValuesToProfile();
                this.DialogResult = DialogResult.OK;
                resetCallback?.Invoke();
                this.Close();
            };

            Button btnCancel = new Button { Text = "CANCEL", Location = new Point(80, 270), Size = new Size(100, 30), BackColor = Color.FromArgb(35, 36, 40), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnCancel.FlatAppearance.BorderSize = 1;
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
            btnCancel.Click += (s, e) => {
                this.DialogResult = DialogResult.Cancel;
                resetCallback?.Invoke();
                this.Close();
            };

            border.Controls.AddRange(new Control[] { btnSave, btnCancel });

            // Restore global hover state when leaving the form controls entirely
            this.MouseLeave += (s, e) => resetCallback?.Invoke();
            border.MouseLeave += (s, e) => resetCallback?.Invoke();
        }

        private NumericUpDown CreateNum(int x, int y, int max)
        {
            return new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(50, 22),
                Minimum = 0,
                Maximum = max,
                BackColor = Color.FromArgb(24, 25, 28),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 8.5f)
            };
        }

        private void AttachHover(Label lbl, NumericUpDown r, NumericUpDown g, NumericUpDown b, int level)
        {
            EventHandler hoverEvent = (s, e) => {
                Color c = Color.FromArgb((int)r.Value, (int)g.Value, (int)b.Value);
                hoverCallback?.Invoke(c);
            };

            lbl.MouseEnter += hoverEvent;
            r.MouseEnter += hoverEvent;
            g.MouseEnter += hoverEvent;
            b.MouseEnter += hoverEvent;
            
            // Real-time update when values change (assuming mouse is already hovering over it)
            r.ValueChanged += hoverEvent;
            g.ValueChanged += hoverEvent;
            b.ValueChanged += hoverEvent;
        }

        private void UpdateDynamicLimits()
        {
            int minR = (int)numRMin.Value;
            int minG = (int)numGMin.Value;
            int minB = (int)numBMin.Value;

            Action<NumericUpDown, int, int> safeUpdate = (num, minVal, maxVal) => {
                num.Minimum = 0;
                num.Maximum = 255;
                num.Value = Math.Max(minVal, Math.Min(maxVal, num.Value));
                num.Maximum = maxVal;
                num.Minimum = minVal;
            };

            // 5% Row
            safeUpdate(numR5, minR, minR + (int)Math.Round(0.05f * (255 - minR)));
            safeUpdate(numG5, minG, minG + (int)Math.Round(0.05f * (255 - minG)));
            safeUpdate(numB5, minB, minB + (int)Math.Round(0.05f * (255 - minB)));

            // 30% Row
            safeUpdate(numR30, minR, minR + (int)Math.Round(0.30f * (255 - minR)));
            safeUpdate(numG30, minG, minG + (int)Math.Round(0.30f * (255 - minG)));
            safeUpdate(numB30, minB, minB + (int)Math.Round(0.30f * (255 - minB)));

            // 60% Row
            safeUpdate(numR60, minR, minR + (int)Math.Round(0.60f * (255 - minR)));
            safeUpdate(numG60, minG, minG + (int)Math.Round(0.60f * (255 - minG)));
            safeUpdate(numB60, minB, minB + (int)Math.Round(0.60f * (255 - minB)));

            // 100% Row
            safeUpdate(numR100, minR, 255);
            safeUpdate(numG100, minG, 255);
            safeUpdate(numB100, minB, 255);
        }

        private void LoadValues()
        {
            Action<NumericUpDown, int> safeSet = (num, val) => {
                num.Minimum = 0;
                num.Maximum = 255;
                num.Value = Math.Max(0, Math.Min(255, val));
            };

            safeSet(numRMin, internalProfile.PointMin.R);
            safeSet(numGMin, internalProfile.PointMin.G);
            safeSet(numBMin, internalProfile.PointMin.B);

            safeSet(numR100, internalProfile.Point100.R);
            safeSet(numG100, internalProfile.Point100.G);
            safeSet(numB100, internalProfile.Point100.B);

            safeSet(numR60, internalProfile.Point60.R);
            safeSet(numG60, internalProfile.Point60.G);
            safeSet(numB60, internalProfile.Point60.B);

            safeSet(numR30, internalProfile.Point30.R);
            safeSet(numG30, internalProfile.Point30.G);
            safeSet(numB30, internalProfile.Point30.B);

            safeSet(numR5, internalProfile.Point5.R);
            safeSet(numG5, internalProfile.Point5.G);
            safeSet(numB5, internalProfile.Point5.B);

            UpdateDynamicLimits();
        }

        private void SaveValuesToProfile()
        {
            ResultProfile.PointMin = Color.FromArgb((int)numRMin.Value, (int)numGMin.Value, (int)numBMin.Value);
            ResultProfile.Point100 = Color.FromArgb((int)numR100.Value, (int)numG100.Value, (int)numB100.Value);
            ResultProfile.Point60 = Color.FromArgb((int)numR60.Value, (int)numG60.Value, (int)numB60.Value);
            ResultProfile.Point30 = Color.FromArgb((int)numR30.Value, (int)numG30.Value, (int)numB30.Value);
            ResultProfile.Point5 = Color.FromArgb((int)numR5.Value, (int)numG5.Value, (int)numB5.Value);
        }
    }
}
