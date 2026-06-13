using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AmbilightControllerForm
{
    public partial class Form1
    {
        private bool loadedUnified = false;
        private string[] loadedPresets = null;
        private int? loadedSavedCalibIndex = null;

        public bool initDone = false;

        private void SaveUnifiedSettings()
        {
            if (!initDone) return;
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
            StringBuilder sb = new StringBuilder();

            Action<string, string> writeSingle = (key, val) => {
                sb.AppendLine($";;{key}: ({val})");
                sb.AppendLine("----------------------------------");
            };
            Action<string, IEnumerable<string>> writeMulti = (key, vals) => {
                sb.AppendLine($";;{key}: {{");
                foreach (var v in vals) sb.AppendLine(v);
                sb.AppendLine("}");
                sb.AppendLine("----------------------------------");
            };

            writeSingle("AddressableMode", isAddressableMode.ToString());
            writeSingle("AddressablePixelCount", addressablePixelCount.ToString());
            writeSingle("AddressableInvert", invertPixelOrder.ToString());
            writeSingle("SmoothThreshold", currentSmoothThreshold.ToString());
            writeSingle("SmoothFrames", currentSmoothFrames.ToString());
            writeSingle("BackgroundImageName", backgroundImageName);
            writeSingle("BackgroundBlur", backgroundBlur.ToString());

            int activeCalibIndex = 3;
            if (calBtns != null && calBtns.Length == 4) {
                for (int i = 0; i < 4; i++) {
                    if (calBtns[i].FlatAppearance.BorderSize == 2) activeCalibIndex = i;
                }
            }
            writeSingle("ActiveCalibIndex", activeCalibIndex.ToString());

            if (ambilightCurve != null && ambilightCurve.Length == 4) {
                List<string> curveLines = new List<string>();
                for (int i = 0; i < 4; i++) curveLines.Add(ambilightCurve[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
                writeMulti("AmbilightCurve", curveLines);
            }

            if (ambilightSatCurve != null && ambilightSatCurve.Length == 4) {
                List<string> satLines = new List<string>();
                for (int i = 0; i < 4; i++) satLines.Add(ambilightSatCurve[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
                writeMulti("AmbilightSatCurve", satLines);
            }

            if (addressableCaptureAreas != null) {
                List<string> areaLines = new List<string>();
                foreach (var r in addressableCaptureAreas) areaLines.Add($"{r.X},{r.Y},{r.Width},{r.Height}");
                writeMulti("AddressableAreas", areaLines);
            }

            if (presetsFlow != null) {
                List<string> hexList = new List<string>();
                foreach (Control ctrl in presetsFlow.Controls) {
                    if (ctrl is GlassButton btn) {
                        if (btn.Tag is Color[] addrColors) {
                            StringBuilder psb = new StringBuilder("ADDR:");
                            for (int i = 0; i < addrColors.Length; i++) {
                                psb.Append($"#{addrColors[i].R:X2}{addrColors[i].G:X2}{addrColors[i].B:X2}");
                                if (i < addrColors.Length - 1) psb.Append(",");
                            }
                            hexList.Add(psb.ToString());
                        } else {
                            Color c = btn.BackColor;
                            hexList.Add($"#{c.R:X2}{c.G:X2}{c.B:X2}");
                        }
                    }
                }
                writeMulti("Presets", hexList);
            }

            if (calibProfiles != null && calibProfiles.Length == 3) {
                List<string> calibLines = new List<string>();
                for (int i = 0; i < 3; i++) {
                    var cp = calibProfiles[i];
                    calibLines.Add($"#{cp.Point100.R:X2}{cp.Point100.G:X2}{cp.Point100.B:X2},#{cp.Point60.R:X2}{cp.Point60.G:X2}{cp.Point60.B:X2},#{cp.Point30.R:X2}{cp.Point30.G:X2}{cp.Point30.B:X2},#{cp.Point5.R:X2}{cp.Point5.G:X2}{cp.Point5.B:X2},#{cp.PointMin.R:X2}{cp.PointMin.G:X2}{cp.PointMin.B:X2}");
                }
                writeMulti("CalibProfiles", calibLines);
            }

            File.WriteAllText(settingsPath, sb.ToString());
        }

        private bool LoadUnifiedSettings(bool verifyOnly = false)
        {
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
            if (!File.Exists(settingsPath)) return false;

            string[] lines = File.ReadAllLines(settingsPath);
            Dictionary<string, string> singleValues = new Dictionary<string, string>();
            Dictionary<string, List<string>> multiValues = new Dictionary<string, List<string>>();

            string currentMultiKey = null;
            List<string> currentMultiList = null;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed == "----------------------------------") continue;
                if (trimmed == "}") {
                    if (currentMultiKey != null && currentMultiList != null) {
                        multiValues[currentMultiKey] = currentMultiList;
                        currentMultiKey = null;
                        currentMultiList = null;
                    }
                    continue;
                }

                if (trimmed.StartsWith(";;")) {
                    int colonIdx = trimmed.IndexOf(':');
                    if (colonIdx > 0) {
                        string key = trimmed.Substring(2, colonIdx - 2).Trim();
                        string rest = trimmed.Substring(colonIdx + 1).Trim();
                        if (rest.StartsWith("(") && rest.EndsWith(")")) {
                            singleValues[key] = rest.Substring(1, rest.Length - 2);
                        } else if (rest == "{") {
                            currentMultiKey = key;
                            currentMultiList = new List<string>();
                        }
                    }
                    continue;
                }

                if (currentMultiKey != null && currentMultiList != null) {
                    currentMultiList.Add(trimmed);
                }
            }

            try {
                if (!verifyOnly) {
                    if (singleValues.TryGetValue("AddressableMode", out string amStr)) bool.TryParse(amStr, out isAddressableMode);
                    if (singleValues.TryGetValue("AddressablePixelCount", out string apcStr)) int.TryParse(apcStr, out addressablePixelCount);
                    if (singleValues.TryGetValue("AddressableInvert", out string aiStr)) bool.TryParse(aiStr, out invertPixelOrder);
                    if (singleValues.TryGetValue("SmoothThreshold", out string stStr)) int.TryParse(stStr, out currentSmoothThreshold);
                    if (singleValues.TryGetValue("SmoothFrames", out string sfStr)) int.TryParse(sfStr, out currentSmoothFrames);
                    if (singleValues.TryGetValue("BackgroundImageName", out string bgName)) backgroundImageName = bgName;
                    if (singleValues.TryGetValue("BackgroundBlur", out string bgBlur)) int.TryParse(bgBlur, out backgroundBlur);

                    if (multiValues.TryGetValue("AmbilightCurve", out List<string> ac) && ac.Count == 4) {
                        var ic = System.Globalization.CultureInfo.InvariantCulture;
                        for (int i = 0; i < 4; i++) float.TryParse(ac[i], System.Globalization.NumberStyles.Any, ic, out ambilightCurve[i]);
                    }

                    if (multiValues.TryGetValue("AmbilightSatCurve", out List<string> asc) && asc.Count == 4) {
                        var ic = System.Globalization.CultureInfo.InvariantCulture;
                        for (int i = 0; i < 4; i++) float.TryParse(asc[i], System.Globalization.NumberStyles.Any, ic, out ambilightSatCurve[i]);
                    }

                    if (multiValues.TryGetValue("AddressableAreas", out List<string> aa)) {
                        addressableCaptureAreas = new Rectangle[aa.Count];
                        for (int i = 0; i < aa.Count; i++) {
                            string[] p = aa[i].Split(',');
                            if (p.Length == 4) {
                                addressableCaptureAreas[i] = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
                            }
                        }
                    }

                    if (singleValues.TryGetValue("ActiveCalibIndex", out string aciStr)) {
                        if (int.TryParse(aciStr, out int aci)) loadedSavedCalibIndex = aci;
                    }

                    if (multiValues.TryGetValue("Presets", out List<string> pLines)) {
                        loadedPresets = pLines.ToArray();
                    }

                    if (multiValues.TryGetValue("CalibProfiles", out List<string> cpLines) && cpLines.Count == 3) {
                        for (int i = 0; i < 3; i++) {
                            string[] p = cpLines[i].Split(',');
                            if (p.Length == 5) {
                                calibProfiles[i] = new CalibrationProfile {
                                    Point100 = ColorTranslator.FromHtml(p[0]),
                                    Point60 = ColorTranslator.FromHtml(p[1]),
                                    Point30 = ColorTranslator.FromHtml(p[2]),
                                    Point5 = ColorTranslator.FromHtml(p[3]),
                                    PointMin = ColorTranslator.FromHtml(p[4])
                                };
                            }
                        }
                    }
                } else {
                    // Verify parsing logic works and variables are fully compatible
                    if (singleValues.TryGetValue("AddressablePixelCount", out string apcStr)) int.Parse(apcStr);
                    if (singleValues.TryGetValue("SmoothThreshold", out string stStr)) int.Parse(stStr);
                    if (multiValues.TryGetValue("AmbilightCurve", out List<string> ac)) {
                        if (ac.Count != 4) throw new Exception("Invalid curve length");
                        var ic = System.Globalization.CultureInfo.InvariantCulture;
                        foreach (var v in ac) float.Parse(v, System.Globalization.NumberStyles.Any, ic);
                    }
                    if (multiValues.TryGetValue("AmbilightSatCurve", out List<string> asc)) {
                        if (asc.Count != 4) throw new Exception("Invalid curve length");
                        var ic = System.Globalization.CultureInfo.InvariantCulture;
                        foreach (var v in asc) float.Parse(v, System.Globalization.NumberStyles.Any, ic);
                    }
                    if (multiValues.TryGetValue("AddressableAreas", out List<string> aa)) {
                        foreach (var v in aa) {
                            string[] p = v.Split(',');
                            if (p.Length != 4) throw new Exception("Invalid rect format");
                            foreach (var val in p) int.Parse(val);
                        }
                    }
                    if (multiValues.TryGetValue("CalibProfiles", out List<string> cpLines)) {
                        if (cpLines.Count != 3) throw new Exception("Invalid calib length");
                        foreach (var line in cpLines) {
                            string[] p = line.Split(',');
                            if (p.Length != 5) throw new Exception("Invalid profile format");
                        }
                    }
                }
                return true;
            } catch {
                return false;
            }
        }

        private void MigrateLegacySettings()
        {
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
            string[] legacyFiles = new string[] {
                "presets.txt", "smooth_settings.txt", "addressable_settings.txt", 
                "addressable_areas.txt", "calib.txt", "last_calib.txt", "ambilight_curve.txt"
            };

            bool hasLegacy = false;
            foreach (var lf in legacyFiles) {
                if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, lf))) hasLegacy = true;
            }

            if (File.Exists(settingsPath))
            {
                if (LoadUnifiedSettings(false)) return;
            }
            
            if (hasLegacy)
            {
                SaveUnifiedSettings();
                
                if (LoadUnifiedSettings(true))
                {
                    foreach (var lf in legacyFiles) {
                        try { File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, lf)); } catch {}
                    }
                }
                else
                {
                    try { File.Delete(settingsPath); } catch {}
                    MessageBox.Show("Migration to unified settings failed. The new settings file could not be parsed safely. Continuing with legacy settings.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
