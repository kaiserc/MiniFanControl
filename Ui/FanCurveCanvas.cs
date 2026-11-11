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
        public bool IsDarkMode = true;
        public bool IsRadeonTheme = false;

        public event EventHandler CurveModified;
        public event EventHandler BuiltInCurveDragStarted;

        private int _dragIndex = -1;
        private int _hoverIndex = -1;

        public FanCurveCanvas()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Cross;
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

            Color bg = IsRadeonTheme
                ? Color.FromArgb(0x16, 0x0A, 0x0E)
                : (IsDarkMode ? Color.FromArgb(0x16, 0x16, 0x22) : Color.FromArgb(0xF8, 0xFA, 0xFC));
            Color gridColor = IsRadeonTheme
                ? Color.FromArgb(0x42, 0x1C, 0x26)
                : (IsDarkMode ? Color.FromArgb(0x2D, 0x2F, 0x42) : Color.FromArgb(0xE2, 0xE8, 0xF0));
            Color labelColor = IsRadeonTheme
                ? Color.FromArgb(0xD6, 0xA8, 0xB4)
                : (IsDarkMode ? Color.FromArgb(0x94, 0x99, 0xB0) : Color.FromArgb(0x47, 0x55, 0x69));

            Color apuColor = IsRadeonTheme
                ? Color.FromArgb(0xF4, 0x3F, 0x5E)
                : (IsDarkMode ? Color.FromArgb(0x89, 0xB4, 0xFA) : Color.FromArgb(0x25, 0x63, 0xEB));
            Color sysColor = IsRadeonTheme
                ? Color.FromArgb(0xFB, 0x92, 0x3C)
                : (IsDarkMode ? Color.FromArgb(0x2D, 0xD4, 0xBF) : Color.FromArgb(0x0D, 0x94, 0x88));

            Color primaryColor = EditSysFan ? sysColor : apuColor;
            Color secondaryColor = EditSysFan ? apuColor : sysColor;

            Color fillTop = Color.FromArgb(IsDarkMode ? 65 : 45, primaryColor.R, primaryColor.G, primaryColor.B);
            Color fillBot = Color.FromArgb(5, primaryColor.R, primaryColor.G, primaryColor.B);
            Color liveColor = IsRadeonTheme
                ? Color.FromArgb(0xFA, 0xCC, 0x15)
                : (IsDarkMode ? Color.FromArgb(0xA6, 0xE3, 0xA1) : Color.FromArgb(0x16, 0xA3, 0x4A));

            g.Clear(bg);
            Rectangle plot = GetPlotRect();

            using (Font axisFont = new Font("Segoe UI", 7.25f, FontStyle.Regular))
            using (Brush lblBrush = new SolidBrush(labelColor))
            using (Pen gridPen = new Pen(gridColor, 1f))
            {
                for (int pct = 0; pct <= 100; pct += 25)
                {
                    float y = plot.Bottom - (pct / 100f) * plot.Height;
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                    string yStr = pct + "%";
                    SizeF sz = g.MeasureString(yStr, axisFont);
                    g.DrawString(yStr, axisFont, lblBrush, plot.Left - sz.Width - 3, y - sz.Height / 2);
                }

                for (int t = 30; t <= 90; t += 15)
                {
                    float x = plot.Left + ((float)(t - MIN_TEMP) / (MAX_TEMP - MIN_TEMP)) * plot.Width;
                    g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
                    string xStr = t + "°C";
                    SizeF sz = g.MeasureString(xStr, axisFont);
                    g.DrawString(xStr, axisFont, lblBrush, x - sz.Width / 2, plot.Bottom + 3);
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

                // Live Temperature vertical marker + operating point dot
                int clampedTemp = Math.Max(MIN_TEMP, Math.Min(MAX_TEMP, CurrentApuTemp));
                int evalPct = FanCurveProfile.EvaluateCurve(activePts, CurrentApuTemp);
                PointF livePt = PointToScreen(new CurvePoint(clampedTemp, evalPct), plot);

                using (Pen liveDash = new Pen(Color.FromArgb(140, liveColor.R, liveColor.G, liveColor.B), 1.2f))
                {
                    liveDash.DashStyle = DashStyle.Dot;
                    g.DrawLine(liveDash, livePt.X, plot.Top, livePt.X, plot.Bottom);
                }
                using (Brush liveBrush = new SolidBrush(liveColor))
                {
                    g.FillEllipse(liveBrush, livePt.X - 4.5f, livePt.Y - 4.5f, 9f, 9f);
                }

                // Draggable control points
                for (int i = 0; i < activePts.Length; i++)
                {
                    PointF p = PointToScreen(activePts[i], plot);
                    bool activePt = (i == _dragIndex || i == _hoverIndex);
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
                        string tip = string.Format("{0}: {1}°C → {2}%", fanTag, activePts[i].Temp, activePts[i].Pct);
                        using (Font f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                        using (Brush bBg = new SolidBrush(IsDarkMode ? Color.Black : Color.White))
                        using (Brush bFg = new SolidBrush(IsDarkMode ? Color.White : Color.Black))
                        {
                            SizeF sz = g.MeasureString(tip, f);
                            float bx = Math.Max(plot.Left, Math.Min(plot.Right - sz.Width - 4, p.X - sz.Width / 2));
                            float by = p.Y - sz.Height - 8;
                            if (by < 2) by = p.Y + 8;
                            g.FillRectangle(bBg, bx - 2, by - 1, sz.Width + 4, sz.Height + 2);
                            g.DrawString(tip, f, bFg, bx, by);
                        }
                    }
                }
            }

            using (Pen borderPen = new Pen(gridColor, 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    /// <summary>
    /// Full-size (552×244) Combined Live Telemetry Trend Graph Canvas.
    /// Displays rolling CPU Temp (°C), iGPU Temp (°C), APU Power (W), APU Fan (RPM), and Sys Fan (RPM)
    /// with interactive legend pills, spotlight highlighting when a metric box is clicked, dual Y-axes,
    /// auto-zoom scaling, and a time-scrubbing hover crosshair.
    /// </summary>
}
