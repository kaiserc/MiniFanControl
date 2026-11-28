using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MiniFanControl
{
    public class FanCurveCanvas : Control
    {
        public const int MIN_TEMP = 30;
        public const int MAX_TEMP = 95;

        public FanCurveProfile ApuProfile;
        public FanCurveProfile SysProfile;
        public bool EditSysFan = false; // false = APU Fan (Fan 1), true = Sys Fan (Fan 2: RAM/NVMe)
        public bool ShowSecondaryCurve = true;
        public int CurrentApuTemp = 45;
        public int CurrentSysTemp = 45;
        public string CurrentApuSource = "CPU";
        public string CurrentSysSource = "CPU";
        public int CurrentFan1Rpm = 0;
        public int CurrentFan2Rpm = 0;
        public bool IsDarkMode = true;
        public bool IsRadeonTheme = false;
        public bool IsAfterburnerTheme = false;
        public bool IsXmasTheme = false;
        public bool UseFahrenheit = false;

        public event EventHandler CurveModified;
        public event EventHandler BuiltInCurveDragStarted;

        private int _dragIndex = -1;
        private int _hoverIndex = -1;
        private int _selectedIndex = -1;
        private CurvePoint[] _undoSnapshot = null;
        private bool _undoWasSysFan = false;

        public bool CanUndo { get { return _undoSnapshot != null; } }

        private void CaptureUndoSnapshot()
        {
            CurvePoint[] pts = GetActivePoints();
            if (pts == null || pts.Length == 0) return;
            _undoSnapshot = FanCurveProfile.ClonePoints(pts);
            _undoWasSysFan = EditSysFan;
        }

        public bool UndoLastEdit()
        {
            if (_undoSnapshot == null || _undoWasSysFan != EditSysFan) return false;
            FanCurveProfile prof = GetActiveProfile();
            if (prof == null || prof.IsBuiltIn) return false;
            if (EditSysFan) prof.SysPoints = FanCurveProfile.ClonePoints(_undoSnapshot);
            else prof.ApuPoints = FanCurveProfile.ClonePoints(_undoSnapshot);
            _undoSnapshot = null;
            Invalidate();
            if (CurveModified != null) CurveModified(this, EventArgs.Empty);
            return true;
        }

        private static readonly Font AxisFont = new Font("Segoe UI", 7.25f, FontStyle.Regular);
        private static readonly Font PointTipFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);

        public FanCurveCanvas()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleName = "Fan Curve Editor";
            AccessibleDescription = "Interactive fan curve chart. Use Left/Right or PageUp/PageDown to select points, and Arrow keys to adjust temperature and fan percentage.";
            AccessibleRole = AccessibleRole.Chart;
            Cursor = Cursors.Cross;
        }

        private void SyncPointAccessibility(CurvePoint pt, int index, int total)
        {
            string fanTag = EditSysFan ? "System Fan" : "APU Fan";
            string unit = TelemetrySessionStats.TempUnit(UseFahrenheit);
            int dispT = TelemetrySessionStats.ToDisplayTemp(pt.Temp, UseFahrenheit);
            AccessibleName = string.Format("{0} Curve Editor - Point {1} of {2}: {3}{4} at {5}%",
                fanTag, index + 1, total, dispT, unit, pt.Pct);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            CurvePoint[] pts = GetActivePoints();
            if (pts != null && pts.Length > 0 && (_selectedIndex < 0 || _selectedIndex >= pts.Length))
            {
                _selectedIndex = 0;
            }
            if (pts != null && _selectedIndex >= 0 && _selectedIndex < pts.Length)
            {
                SyncPointAccessibility(pts[_selectedIndex], _selectedIndex, pts.Length);
            }
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.PageUp:
                case Keys.PageDown:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.Z)
            {
                if (UndoLastEdit())
                {
                    e.Handled = true;
                    return;
                }
            }
            CurvePoint[] pts = GetActivePoints();
            FanCurveProfile prof = GetActiveProfile();
            if (pts == null || pts.Length == 0 || prof == null) return;

            if (_selectedIndex < 0 || _selectedIndex >= pts.Length) _selectedIndex = 0;

            if (e.KeyCode == Keys.PageUp || e.KeyCode == Keys.OemOpenBrackets)
            {
                _selectedIndex = (_selectedIndex - 1 + pts.Length) % pts.Length;
                SyncPointAccessibility(pts[_selectedIndex], _selectedIndex, pts.Length);
                Invalidate();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.PageDown || e.KeyCode == Keys.OemCloseBrackets)
            {
                _selectedIndex = (_selectedIndex + 1) % pts.Length;
                SyncPointAccessibility(pts[_selectedIndex], _selectedIndex, pts.Length);
                Invalidate();
                e.Handled = true;
                return;
            }

            int step = e.Control ? 1 : 5;
            int dTemp = 0;
            int dPct = 0;
            if (e.KeyCode == Keys.Left) dTemp = -(e.Control ? 1 : 2);
            else if (e.KeyCode == Keys.Right) dTemp = (e.Control ? 1 : 2);
            else if (e.KeyCode == Keys.Up) dPct = step;
            else if (e.KeyCode == Keys.Down) dPct = -step;

            if (dTemp != 0 || dPct != 0)
            {
                if (prof.IsBuiltIn && BuiltInCurveDragStarted != null)
                {
                    BuiltInCurveDragStarted(this, EventArgs.Empty);
                    pts = GetActivePoints();
                    if (pts == null || _selectedIndex >= pts.Length) return;
                }
                CaptureUndoSnapshot();

                CurvePoint cur = pts[_selectedIndex];
                int minT = (_selectedIndex == 0) ? MIN_TEMP : pts[_selectedIndex - 1].Temp + 2;
                int maxT = (_selectedIndex == pts.Length - 1) ? MAX_TEMP : pts[_selectedIndex + 1].Temp - 2;
                int newTemp = Math.Max(minT, Math.Min(maxT, cur.Temp + dTemp));
                int newPct = Math.Max(0, Math.Min(100, cur.Pct + dPct));
                pts[_selectedIndex] = new CurvePoint(newTemp, newPct);
                SyncPointAccessibility(pts[_selectedIndex], _selectedIndex, pts.Length);
                Invalidate();
                if (CurveModified != null) CurveModified(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        public FanCurveProfile GetActiveProfile()
        {
            return EditSysFan ? SysProfile : ApuProfile;
        }

        private CurvePoint[] GetActivePoints()
        {
            if (EditSysFan)
                return SysProfile != null ? SysProfile.SysPoints : null;
            return ApuProfile != null ? ApuProfile.ApuPoints : null;
        }

        private CurvePoint[] GetSecondaryPoints()
        {
            if (EditSysFan)
                return ApuProfile != null ? ApuProfile.ApuPoints : null;
            return SysProfile != null ? SysProfile.SysPoints : null;
        }

        private Rectangle GetPlotRect()
        {
            int left = 36;
            int top = 12;
            int right = Width - 14;
            int bottom = Height - 22;
            return new Rectangle(left, top, Math.Max(40, right - left), Math.Max(30, bottom - top));
        }

        private PointF PointToScreen(CurvePoint pt, Rectangle plot)
        {
            float tx = (float)(pt.Temp - MIN_TEMP) / (MAX_TEMP - MIN_TEMP);
            float ty = (float)pt.Pct / 100f;
            float x = plot.Left + tx * plot.Width;
            float y = plot.Bottom - ty * plot.Height;
            return new PointF(x, y);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            CurvePoint[] pts = GetActivePoints();
            FanCurveProfile prof = GetActiveProfile();
            if (pts == null || prof == null || e.Button != MouseButtons.Left)
                return;

            Rectangle plot = GetPlotRect();
            for (int i = 0; i < pts.Length; i++)
            {
                PointF p = PointToScreen(pts[i], plot);
                float dx = e.X - p.X;
                float dy = e.Y - p.Y;
                if (dx * dx + dy * dy <= 100f)
                {
                    if (prof.IsBuiltIn && BuiltInCurveDragStarted != null)
                    {
                        BuiltInCurveDragStarted(this, EventArgs.Empty);
                    }
                    CaptureUndoSnapshot();
                    _dragIndex = i;
                    Invalidate();
                    break;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            CurvePoint[] pts = GetActivePoints();
            if (pts == null)
                return;

            Rectangle plot = GetPlotRect();

            if (_dragIndex >= 0 && _dragIndex < pts.Length)
            {
                float normX = (e.X - plot.Left) / (float)plot.Width;
                float normY = (plot.Bottom - e.Y) / (float)plot.Height;

                int newTemp = (int)Math.Round(MIN_TEMP + normX * (MAX_TEMP - MIN_TEMP));
                int newPct = (int)Math.Round(normY * 100f);

                // Hold Shift to snap to clean 5°C / 5% increments
                if ((Control.ModifierKeys & Keys.Shift) != 0)
                {
                    newTemp = (int)Math.Round(newTemp / 5.0) * 5;
                    newPct = (int)Math.Round(newPct / 5.0) * 5;
                }

                int minT = (_dragIndex == 0) ? MIN_TEMP : pts[_dragIndex - 1].Temp + 2;
                int maxT = (_dragIndex == pts.Length - 1) ? MAX_TEMP : pts[_dragIndex + 1].Temp - 2;

                newTemp = Math.Max(minT, Math.Min(maxT, newTemp));
                newPct = Math.Max(0, Math.Min(100, newPct));

                pts[_dragIndex] = new CurvePoint(newTemp, newPct);
                Invalidate();
                return;
            }

            int found = -1;
            for (int i = 0; i < pts.Length; i++)
            {
                PointF p = PointToScreen(pts[i], plot);
                float dx = e.X - p.X;
                float dy = e.Y - p.Y;
                if (dx * dx + dy * dy <= 100f)
                {
                    found = i;
                    break;
                }
            }
            if (found != _hoverIndex)
            {
                _hoverIndex = found;
                Cursor = _hoverIndex >= 0 ? Cursors.Hand : Cursors.Cross;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragIndex >= 0)
            {
                _dragIndex = -1;
                Invalidate();
                if (CurveModified != null)
                    CurveModified(this, EventArgs.Empty);
            }
        }

        private List<PointF> BuildScreenPolyline(CurvePoint[] pts, Rectangle plot)
        {
            List<PointF> list = new List<PointF>();
            list.Add(PointToScreen(new CurvePoint(MIN_TEMP, pts[0].Pct), plot));
            for (int i = 0; i < pts.Length; i++)
                list.Add(PointToScreen(pts[i], plot));
            list.Add(PointToScreen(new CurvePoint(MAX_TEMP, pts[pts.Length - 1].Pct), plot));
            return list;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            ThemePalette pal = ThemePalette.ResolveFromFlags(IsDarkMode, IsRadeonTheme, IsAfterburnerTheme, IsXmasTheme);
            Color bg = pal.CanvasBg;
            Color gridColor = pal.CanvasGrid;
            Color labelColor = pal.IsHighContrast ? SystemColors.WindowText : (IsXmasTheme
                ? Color.FromArgb(0x18, 0x18, 0x1B)
                : (IsAfterburnerTheme
                    ? Color.FromArgb(0x8A, 0x96, 0xA8)
                    : (IsRadeonTheme
                        ? Color.FromArgb(0xD6, 0xA8, 0xB4)
                        : (IsDarkMode ? Color.FromArgb(0x94, 0x99, 0xB0) : Color.FromArgb(0x47, 0x55, 0x69)))));

            Color apuColor = IsXmasTheme
                ? Color.FromArgb(0xDC, 0x26, 0x26)
                : (IsAfterburnerTheme
                    ? Color.FromArgb(0x00, 0xE6, 0x76)
                    : (IsRadeonTheme
                        ? Color.FromArgb(0xF4, 0x3F, 0x5E)
                        : (IsDarkMode ? Color.FromArgb(0x89, 0xB4, 0xFA) : Color.FromArgb(0x25, 0x63, 0xEB))));
            Color sysColor = IsXmasTheme
                ? Color.FromArgb(0x18, 0x18, 0x1B)
                : (IsAfterburnerTheme
                    ? Color.FromArgb(0x00, 0xE5, 0xFF)
                    : (IsRadeonTheme
                        ? Color.FromArgb(0xFB, 0x92, 0x3C)
                        : (IsDarkMode ? Color.FromArgb(0x2D, 0xD4, 0xBF) : Color.FromArgb(0x0D, 0x94, 0x88))));

            Color primaryColor = EditSysFan ? sysColor : apuColor;
            Color secondaryColor = EditSysFan ? apuColor : sysColor;

            Color fillTop = Color.FromArgb(IsXmasTheme ? 38 : (IsDarkMode ? 65 : 45), primaryColor.R, primaryColor.G, primaryColor.B);
            Color fillBot = Color.FromArgb(5, primaryColor.R, primaryColor.G, primaryColor.B);
            Color liveColor = IsXmasTheme
                ? Color.FromArgb(0xDC, 0x26, 0x26)
                : (IsAfterburnerTheme
                    ? Color.FromArgb(0xFF, 0x91, 0x00)
                    : (IsRadeonTheme
                        ? Color.FromArgb(0xFA, 0xCC, 0x15)
                        : (IsDarkMode ? Color.FromArgb(0xA6, 0xE3, 0xA1) : Color.FromArgb(0x16, 0xA3, 0x4A))));

            g.Clear(bg);
            Rectangle plot = GetPlotRect();
            if (IsXmasTheme && plot.Width > 120 && plot.Height > 80)
            {
                // Subtle decorative holiday snowflakes watermarked in the upper canvas corners
                Win11VisualStyle.DrawSnowflake(g, plot.Right - 36f, plot.Top + 34f, 22f, Color.FromArgb(24, 0xDC, 0x26, 0x26), 1.5f);
                Win11VisualStyle.DrawSnowflake(g, plot.Right - 76f, plot.Top + 56f, 11f, Color.FromArgb(20, 0x18, 0x18, 0x1B), 1.1f);
            }

            using (Brush lblBrush = new SolidBrush(labelColor))
            using (Pen gridPen = new Pen(gridColor, 1f))
            {
                for (int pct = 0; pct <= 100; pct += 25)
                {
                    float y = plot.Bottom - (pct / 100f) * plot.Height;
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                    string yStr = pct + "%";
                    SizeF sz = g.MeasureString(yStr, AxisFont);
                    g.DrawString(yStr, AxisFont, lblBrush, plot.Left - sz.Width - 3, y - sz.Height / 2);
                }

                for (int t = 30; t <= 90; t += 15)
                {
                    float x = plot.Left + ((float)(t - MIN_TEMP) / (MAX_TEMP - MIN_TEMP)) * plot.Width;
                    g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
                    string xStr = TelemetrySessionStats.ToDisplayTemp(t, UseFahrenheit) + TelemetrySessionStats.TempUnit(UseFahrenheit);
                    SizeF sz = g.MeasureString(xStr, AxisFont);
                    g.DrawString(xStr, AxisFont, lblBrush, x - sz.Width / 2, plot.Bottom + 3);
                }
            }

            // Safety Zone 1: >=90°C Emergency Thermal Override danger band (forces 100% fan)
            if (!pal.IsHighContrast)
            {
                float x90 = plot.Left + ((float)(90 - MIN_TEMP) / (MAX_TEMP - MIN_TEMP)) * plot.Width;
                RectangleF dangerRect = new RectangleF(x90, plot.Top, Math.Max(2f, plot.Right - x90), plot.Height);
                Color dangerCol = Color.FromArgb(IsDarkMode ? 34 : 24, 0xEF, 0x44, 0x44);
                using (SolidBrush dangerBr = new SolidBrush(dangerCol))
                {
                    g.FillRectangle(dangerBr, dangerRect);
                }
                using (Pen dangerPen = new Pen(Color.FromArgb(IsDarkMode ? 130 : 120, 0xEF, 0x44, 0x44), 1f))
                {
                    dangerPen.DashStyle = DashStyle.Dot;
                    g.DrawLine(dangerPen, x90, plot.Top, x90, plot.Bottom);
                }
                using (SolidBrush dTxt = new SolidBrush(Color.FromArgb(IsDarkMode ? 190 : 185, 0xEF, 0x44, 0x44)))
                {
                    string maxTag = TelemetrySessionStats.ToDisplayTemp(90, UseFahrenheit) + TelemetrySessionStats.TempUnit(UseFahrenheit) + " MAX";
                    SizeF mSz = g.MeasureString(maxTag, AxisFont);
                    g.DrawString(maxTag, AxisFont, dTxt, Math.Min(plot.Right - mSz.Width - 2, x90 + 3), plot.Top + 2);
                }

                // Safety Zone 2: Calibrated Anti-Stall Minimum Spin Floor band
                int minFloorBand = EditSysFan ? FanCurveProfile.SysMinSpinPct : FanCurveProfile.ApuMinSpinPct;
                if (minFloorBand > 0 && minFloorBand < 60)
                {
                    float yFloor = plot.Bottom - (minFloorBand / 100f) * plot.Height;
                    using (SolidBrush floorBr = new SolidBrush(Color.FromArgb(IsDarkMode ? 18 : 14, 0xF5, 0x9E, 0x0B)))
                    {
                        g.FillRectangle(floorBr, plot.Left, yFloor, plot.Width, plot.Bottom - yFloor);
                    }
                    using (Pen floorPen = new Pen(Color.FromArgb(IsDarkMode ? 115 : 105, 0xF5, 0x9E, 0x0B), 1f))
                    {
                        floorPen.DashStyle = DashStyle.Dash;
                        g.DrawLine(floorPen, plot.Left, yFloor, plot.Right, yFloor);
                    }
                    using (SolidBrush fTxt = new SolidBrush(Color.FromArgb(IsDarkMode ? 175 : 165, 0xD9, 0x77, 0x06)))
                    {
                        string floorLabel = string.Format("Anti-Stall Floor {0}%", minFloorBand);
                        g.DrawString(floorLabel, AxisFont, fTxt, plot.Left + 4, yFloor - 13);
                    }
                }
            }

            // Secondary (background) fan curve as dashed reference line
            CurvePoint[] secPts = ShowSecondaryCurve ? GetSecondaryPoints() : null;
            if (secPts != null && secPts.Length > 0)
            {
                List<PointF> secLine = BuildScreenPolyline(secPts, plot);
                using (Pen secPen = new Pen(Color.FromArgb(110, secondaryColor.R, secondaryColor.G, secondaryColor.B), 1.5f))
                {
                    secPen.DashStyle = DashStyle.Dash;
                    g.DrawLines(secPen, secLine.ToArray());
                }
            }

            // Primary (active editable) fan curve
            CurvePoint[] activePts = GetActivePoints();
            if (activePts != null && activePts.Length > 0)
            {
                List<PointF> pts = BuildScreenPolyline(activePts, plot);

                using (GraphicsPath areaPath = new GraphicsPath())
                {
                    areaPath.AddLines(pts.ToArray());
                    areaPath.AddLine(pts[pts.Count - 1].X, plot.Bottom, pts[0].X, plot.Bottom);
                    areaPath.CloseFigure();
                    using (LinearGradientBrush areaBrush = new LinearGradientBrush(
                        new Point(plot.Left, plot.Top),
                        new Point(plot.Left, plot.Bottom),
                        fillTop, fillBot))
                    {
                        g.FillPath(areaBrush, areaPath);
                    }
                }

                using (Pen curvePen = new Pen(primaryColor, 2.25f))
                {
                    g.DrawLines(curvePen, pts.ToArray());
                }

                // Live Temperature vertical marker + operating point on curve (using calibrated minSpinFloor)
                int activeTemp = EditSysFan ? CurrentSysTemp : CurrentApuTemp;
                int clampedTemp = Math.Max(MIN_TEMP, Math.Min(MAX_TEMP, activeTemp));
                int minFloor = EditSysFan ? FanCurveProfile.SysMinSpinPct : FanCurveProfile.ApuMinSpinPct;
                int evalPct = FanCurveProfile.EvaluateCurve(activePts, activeTemp, minFloor);
                PointF livePt = PointToScreen(new CurvePoint(clampedTemp, evalPct), plot);

                using (Pen liveDash = new Pen(Color.FromArgb(140, liveColor.R, liveColor.G, liveColor.B), 1.2f))
                {
                    liveDash.DashStyle = DashStyle.Dot;
                    g.DrawLine(liveDash, livePt.X, plot.Top, livePt.X, plot.Bottom);
                }
                using (Brush liveBrush = new SolidBrush(liveColor))
                {
                    g.FillEllipse(liveBrush, livePt.X - 3.5f, livePt.Y - 3.5f, 7f, 7f);
                }

                // Live operating badge: Source Temp • Target Duty % • Live RPM (hidden while dragging/hovering a control point)
                if (_dragIndex < 0 && _hoverIndex < 0)
                {
                    int liveRpm = EditSysFan ? CurrentFan2Rpm : CurrentFan1Rpm;
                    string srcTag = EditSysFan
                        ? (string.IsNullOrEmpty(CurrentSysSource) ? "Sys" : CurrentSysSource)
                        : (string.IsNullOrEmpty(CurrentApuSource) ? "CPU" : CurrentApuSource);
                    string tempTxt = string.Format("{0} {1}{2}",
                        srcTag,
                        TelemetrySessionStats.ToDisplayTemp(activeTemp, UseFahrenheit),
                        TelemetrySessionStats.TempUnit(UseFahrenheit));
                    string badgeTxt = liveRpm > 0
                        ? string.Format("{0} • {1}% • {2} RPM", tempTxt, evalPct, liveRpm)
                        : string.Format("{0} • {1}%", tempTxt, evalPct);

                    SizeF bSz = g.MeasureString(badgeTxt, PointTipFont);
                    float bw = bSz.Width + 10f;
                    float bh = bSz.Height + 4f;
                    float bx = Math.Max(plot.Left + 2f, Math.Min(plot.Right - bw - 2f, livePt.X - bw / 2f));
                    float by = livePt.Y - bh - 9f;
                    if (by < plot.Top + 2f) by = livePt.Y + 9f;

                    Color badgeBg = IsDarkMode ? Color.FromArgb(232, 0x18, 0x18, 0x25) : Color.FromArgb(242, 0xFF, 0xFF, 0xFF);
                    Rectangle bRect = new Rectangle((int)Math.Round(bx), (int)Math.Round(by), (int)Math.Round(bw), (int)Math.Round(bh));
                    using (Brush bFill = new SolidBrush(badgeBg))
                    using (Brush bText = new SolidBrush(liveColor))
                    {
                        Win11VisualStyle.FillRoundedRect(g, bFill, bRect, 4);
                        Win11VisualStyle.DrawRoundedCardBorder(g, bRect, Color.FromArgb(210, liveColor.R, liveColor.G, liveColor.B), 4, 1.2f);
                        g.DrawString(badgeTxt, PointTipFont, bText, bx + 5f, by + 2f);
                    }
                }

                // Active drag alignment crosshair guides to axes
                if (_dragIndex >= 0 && _dragIndex < activePts.Length)
                {
                    PointF dp = PointToScreen(activePts[_dragIndex], plot);
                    using (Pen guidePen = new Pen(Color.FromArgb(145, primaryColor), 1f))
                    {
                        guidePen.DashStyle = DashStyle.Dash;
                        g.DrawLine(guidePen, plot.Left, dp.Y, plot.Right, dp.Y);
                        g.DrawLine(guidePen, dp.X, plot.Top, dp.X, plot.Bottom);
                    }
                }

                // Draggable control points
                int maxRpm = EditSysFan ? FanCurveProfile.SysMaxRpm : FanCurveProfile.ApuMaxRpm;
                for (int i = 0; i < activePts.Length; i++)
                {
                    PointF p = PointToScreen(activePts[i], plot);
                    bool activePt = (i == _dragIndex || i == _hoverIndex || (Focused && i == _selectedIndex));
                    float r = activePt ? 6f : 4.5f;

                    using (Brush ptBg = new SolidBrush(IsDarkMode ? Color.Black : Color.White))
                    using (Pen ptPen = new Pen(activePt ? Color.FromArgb(0xF9, 0xE2, 0xAF) : primaryColor, 2.2f))
                    {
                        g.FillEllipse(ptBg, p.X - r, p.Y - r, r * 2, r * 2);
                        g.DrawEllipse(ptPen, p.X - r, p.Y - r, r * 2, r * 2);
                    }

                    if (activePt)
                    {
                        string fanTag = EditSysFan ? "Sys" : "APU";
                        int estRpm = (maxRpm > 0 && activePts[i].Pct > 0) ? (int)Math.Round((activePts[i].Pct / 100.0) * maxRpm) : 0;
                        string rpmPart = estRpm > 0 ? string.Format(" (~{0} RPM)", estRpm) : "";
                        string tip = string.Format("{0}: {1}{2} → {3}%{4}",
                            fanTag,
                            TelemetrySessionStats.ToDisplayTemp(activePts[i].Temp, UseFahrenheit),
                            TelemetrySessionStats.TempUnit(UseFahrenheit),
                            activePts[i].Pct,
                            rpmPart);
                        SizeF sz = g.MeasureString(tip, PointTipFont);
                        int tw = (int)Math.Ceiling(sz.Width) + 10;
                        int th = (int)Math.Ceiling(sz.Height) + 4;
                        int bx = Math.Max(plot.Left + 2, Math.Min(plot.Right - tw - 2, (int)Math.Round(p.X - tw / 2f)));
                        int by = (int)Math.Round(p.Y - th - 10f);
                        if (by < plot.Top + 2) by = (int)Math.Round(p.Y + 10f);
                        Rectangle tipRect = new Rectangle(bx, by, tw, th);
                        Color tipBg = IsDarkMode ? Color.FromArgb(238, 0x14, 0x16, 0x22) : Color.FromArgb(245, 0xFF, 0xFF, 0xFF);
                        using (Brush bBg = new SolidBrush(tipBg))
                        using (Brush bFg = new SolidBrush(IsDarkMode ? Color.White : Color.Black))
                        {
                            Win11VisualStyle.FillRoundedRect(g, bBg, tipRect, 4);
                            Win11VisualStyle.DrawRoundedCardBorder(g, tipRect, primaryColor, 4, 1.2f);
                            g.DrawString(tip, PointTipFont, bFg, bx + 5, by + 2);
                        }
                    }
                }
            }

            // Inline Curve Legend (top-left of plot) so primary vs dashed secondary reference curve is unmistakable
            if (plot.Width > 220)
            {
                string priLabel = EditSysFan ? "Sys Fan (Editing)" : "APU Fan (Editing)";
                string secLabel = EditSysFan ? "APU Fan (Ref)" : "Sys Fan (Ref)";
                bool hasSec = (secPts != null && secPts.Length > 0);

                Size priSz = TextRenderer.MeasureText(g, priLabel, AxisFont, new Size(160, 16), TextFormatFlags.NoPadding);
                Size secSz = hasSec ? TextRenderer.MeasureText(g, secLabel, AxisFont, new Size(140, 16), TextFormatFlags.NoPadding) : Size.Empty;
                int legW = 24 + priSz.Width + (hasSec ? (28 + secSz.Width) : 0) + 10;
                int legH = 18;
                Rectangle legRect = new Rectangle(plot.Left + 6, plot.Top + 4, legW, legH);
                Color legBg = pal.IsHighContrast
                    ? SystemColors.Window
                    : Color.FromArgb(IsDarkMode ? 210 : 228, bg);
                using (SolidBrush legBr = new SolidBrush(legBg))
                {
                    Win11VisualStyle.FillRoundedRect(g, legBr, legRect, 4);
                }
                Win11VisualStyle.DrawRoundedCardBorder(g, legRect, gridColor, 4, 1f);

                int lx = legRect.X + 7;
                int ly = legRect.Y + legH / 2;
                using (Pen priPen = new Pen(primaryColor, 2.2f))
                {
                    g.DrawLine(priPen, lx, ly, lx + 12, ly);
                }
                TextRenderer.DrawText(g, priLabel, AxisFont, new Point(lx + 16, legRect.Y + 3), pal.IsHighContrast ? SystemColors.WindowText : pal.Text, TextFormatFlags.NoPadding);

                if (hasSec)
                {
                    int sx = lx + 16 + priSz.Width + 10;
                    using (Pen secLegPen = new Pen(secondaryColor, 1.6f))
                    {
                        secLegPen.DashStyle = DashStyle.Dash;
                        g.DrawLine(secLegPen, sx, ly, sx + 12, ly);
                    }
                    TextRenderer.DrawText(g, secLabel, AxisFont, new Point(sx + 16, legRect.Y + 3), labelColor, TextFormatFlags.NoPadding);
                }
            }

            using (Pen borderPen = new Pen(gridColor, 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
            if (Focused)
            {
                ControlPaint.DrawFocusRectangle(g, new Rectangle(2, 2, Math.Max(1, Width - 4), Math.Max(1, Height - 4)));
            }
        }
    }
}
