using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MiniFanControl
{
    public class LivePerformanceCanvas : Control
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public TelemetrySessionStats Stats { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public FanStatus Status { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsDarkMode { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsRadeonTheme { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsAfterburnerTheme { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool SplitView { get; set; }

        public event EventHandler ResetPeaksRequested;

        private int _hoverMouseX = -1;
        private int _hoverMouseY = -1;
        private int _hoverSampleIdx = -1;

        private readonly Rectangle[] _pillRects = new Rectangle[3]; // 0: View Toggle, 1: Reset Peaks, 2: Save PNG

        private int _currentFtCeiling = 50;
        private int _ftCeilingHoldCounter = 0;
        private int _currentFpsCeiling = 80;
        private int _fpsCeilingHoldCounter = 0;

        private static readonly Font FontTitle = new Font("Segoe UI", 8.25f, FontStyle.Bold);
        private static readonly Font FontHeader = new Font("Segoe UI", 7.75f, FontStyle.Bold);
        private static readonly Font FontAxis = new Font("Segoe UI", 7.0f, FontStyle.Regular);
        private static readonly Font FontPill = new Font("Segoe UI", 7.0f, FontStyle.Bold);
        private static readonly Font FontBadge = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        private static readonly Font FontHud = new Font("Segoe UI", 7.25f, FontStyle.Bold);
        private static readonly Font FontStandby = new Font("Segoe UI", 9.0f, FontStyle.Regular);

        public LivePerformanceCanvas()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            IsDarkMode = true;
            SplitView = true; // Default to Digital Foundry split twin graph view
            Cursor = Cursors.Cross;
        }

        public void UpdateStatus(FanStatus st)
        {
            Status = st;
        }

        public void SaveGraphAsPng(string path)
        {
            try
            {
                using (Bitmap bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height)))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                    bmp.Save(path, ImageFormat.Png);
                }
            }
            catch { }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverSampleIdx != -1 || _hoverMouseX != -1)
            {
                _hoverSampleIdx = -1;
                _hoverMouseX = -1;
                _hoverMouseY = -1;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            bool overPill = false;
            for (int i = 0; i < _pillRects.Length; i++)
            {
                if (_pillRects[i].Width > 0 && _pillRects[i].Contains(e.Location))
                {
                    overPill = true;
                    break;
                }
            }
            Cursor = overPill ? Cursors.Hand : Cursors.Cross;

            _hoverMouseX = e.X;
            _hoverMouseY = e.Y;

            int total = (Stats != null && Stats.FpsHistory != null) ? Stats.FpsHistory.Count : 0;
            if (total > 0 && e.X >= 42 && e.X <= Width - 46)
            {
                int take = Math.Min(total, 120);
                int start = total - take;
                float normX = (float)(e.X - 42) / Math.Max(1, Width - 88);
                normX = Math.Max(0f, Math.Min(1f, normX));
                int idx = start + (int)Math.Round(normX * (take - 1));
                idx = Math.Max(0, Math.Min(total - 1, idx));
                if (idx != _hoverSampleIdx)
                {
                    _hoverSampleIdx = idx;
                    Invalidate();
                }
            }
            else if (_hoverSampleIdx != -1)
            {
                _hoverSampleIdx = -1;
                Invalidate();
            }
            else
            {
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Right)
            {
                ContextMenu cm = new ContextMenu();
                cm.MenuItems.Add(SplitView ? "Switch to Combined Graph View" : "Switch to Digital Foundry Twin Split View", (s, args) =>
                {
                    SplitView = !SplitView;
                    Invalidate();
                });
                cm.MenuItems.Add("-");
                cm.MenuItems.Add("Save Performance Benchmark Snapshot as PNG...", (s, args) =>
                {
                    using (SaveFileDialog sfd = new SaveFileDialog())
                    {
                        sfd.Filter = "PNG Image (*.png)|*.png";
                        sfd.FileName = string.Format("performance-benchmark-{0:yyyyMMdd-HHmmss}.png", DateTime.Now);
                        if (sfd.ShowDialog() == DialogResult.OK)
                        {
                            SaveGraphAsPng(sfd.FileName);
                        }
                    }
                });
                cm.MenuItems.Add("-");
                cm.MenuItems.Add("Reset Session Peaks & Averages", (s, args) =>
                {
                    if (ResetPeaksRequested != null) ResetPeaksRequested(this, EventArgs.Empty);
                });
                cm.Show(this, e.Location);
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            for (int i = 0; i < _pillRects.Length; i++)
            {
                if (_pillRects[i].Width > 0 && _pillRects[i].Contains(e.Location))
                {
                    if (i == 0) // View Toggle
                    {
                        SplitView = !SplitView;
                        Invalidate();
                    }
                    else if (i == 1) // Reset Peaks
                    {
                        if (ResetPeaksRequested != null) ResetPeaksRequested(this, EventArgs.Empty);
                        Invalidate();
                    }
                    else if (i == 2) // Save PNG
                    {
                        using (SaveFileDialog sfd = new SaveFileDialog())
                        {
                            sfd.Filter = "PNG Image (*.png)|*.png";
                            sfd.FileName = string.Format("performance-benchmark-{0:yyyyMMdd-HHmmss}.png", DateTime.Now);
                            if (sfd.ShowDialog() == DialogResult.OK)
                            {
                                SaveGraphAsPng(sfd.FileName);
                            }
                        }
                    }
                    return;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Color bg = IsRadeonTheme ? Color.FromArgb(0x10, 0x08, 0x0A)
                : (IsAfterburnerTheme ? Color.FromArgb(0x11, 0x14, 0x18)
                : (IsDarkMode ? Color.FromArgb(0x11, 0x11, 0x1B) : Color.FromArgb(0xF8, 0xFA, 0xFC)));

            Color borderCol = IsRadeonTheme ? Color.FromArgb(0x60, 0xED, 0x1C, 0x24)
                : (IsAfterburnerTheme ? Color.FromArgb(0x60, 0x00, 0xE5, 0xFF)
                : (IsDarkMode ? Color.FromArgb(0x31, 0x32, 0x44) : Color.FromArgb(0xCB, 0xD5, 0xE1)));

            using (SolidBrush bgBr = new SolidBrush(bg))
            {
                g.FillRectangle(bgBr, ClientRectangle);
            }
            using (Pen bPen = new Pen(borderCol, 1f))
            {
                g.DrawRectangle(bPen, 0, 0, Width - 1, Height - 1);
            }

            // 1. Draw Top Status Ribbon & HUD Badges (y = 4 to y = 32)
            try
            {
                RenderCore(g);
            }
            catch { }
        }

        internal void RenderForTest(Graphics g)
        {
            RenderCore(g);
        }

        private void RenderCore(Graphics g)
        {
            DrawTopRibbon(g);

            // 2. Draw Graphs
            if (SplitView)
            {
                // Top Half: FRAME-TIME (MS)
                int ftTop = 36;
                int ftH = (Height - 56) / 2;
                Rectangle ftRect = new Rectangle(4, ftTop, Width - 8, ftH);
                DrawDfFrametimeSection(g, ftRect);

                // Bottom Half: FRAME-RATE (FPS)
                int fpsTop = ftTop + ftH + 4;
                int fpsH = Height - 20 - fpsTop;
                Rectangle fpsRect = new Rectangle(4, fpsTop, Width - 8, fpsH);
                DrawDfFramerateSection(g, fpsRect);
            }
            else
            {
                // Single Unified Combined Graph
                Rectangle combRect = new Rectangle(4, 36, Width - 8, Height - 56);
                DrawCombinedPerformanceSection(g, combRect);
            }

            // 3. Draw Bottom Timeline Reference (y = Height - 18 to y = Height - 2)
            DrawBottomTimeline(g);
        }

        private void DrawTopRibbon(Graphics g)
        {
            int curFps = Status.RtssFps > 0 ? Status.RtssFps : ((Stats != null && Stats.FpsHistory.Count > 0) ? Stats.FpsHistory[Stats.FpsHistory.Count - 1] : 0);
            double curFt = Status.RtssFrametimeMs > 0 ? Status.RtssFrametimeMs : (curFps > 0 ? (1000.0 / curFps) : ((Stats != null && Stats.FrametimeHistory.Count > 0) ? Stats.FrametimeHistory[Stats.FrametimeHistory.Count - 1] / 10.0 : 0.0));
            int avgFps = (Stats != null && Stats.AvgFps > 0) ? (int)Math.Round(Stats.AvgFps) : (Status.RtssAvgFps > 0 ? Status.RtssAvgFps : curFps);
            int low01 = Status.RtssP01LowFps > 0 ? Status.RtssP01LowFps : ((Stats != null && Stats.P01LowFps > 0) ? Stats.P01LowFps : 0);
            int low1 = Status.RtssP1LowFps > 0 ? Status.RtssP1LowFps : ((Stats != null && Stats.P1LowFps > 0) ? Stats.P1LowFps : 0);

            string game = CapFrameXOverlayForm.FormatGameName(Status.RtssProcessName);
            bool isGameActive = !string.IsNullOrEmpty(game) && curFps > 0;
            string gameTag = isGameActive ? ("🎮 " + game) : (Status.RtssRunning ? "⏱ RTSS Active (Desktop / Idle)" : "⏱ RTSS Standby");

            Color textCol = IsRadeonTheme ? Color.FromArgb(0xFD, 0xBA, 0x74)
                : (IsAfterburnerTheme ? Color.FromArgb(0x00, 0xE5, 0xFF)
                : (IsDarkMode ? Color.FromArgb(0xCD, 0xD6, 0xF4) : Color.FromArgb(0x0F, 0x17, 0x2A)));

            Color dimCol = IsDarkMode ? Color.FromArgb(0x94, 0xA3, 0xB8) : Color.FromArgb(0x64, 0x74, 0x8B);
            Color dotCol = isGameActive ? Color.FromArgb(0x22, 0xC5, 0x5E) : (Status.RtssRunning ? Color.FromArgb(0xF5, 0x9E, 0x0B) : Color.FromArgb(0x94, 0xA3, 0xB8));

            // Status dot
            using (SolidBrush dotBr = new SolidBrush(dotCol))
            {
                g.FillEllipse(dotBr, 10, 14, 8, 8);
            }
            TextRenderer.DrawText(g, gameTag, FontTitle, new Point(22, 10), isGameActive ? textCol : dimCol, TextFormatFlags.NoPadding);

            int x = 22 + TextRenderer.MeasureText(g, gameTag, FontTitle, new Size(300, 20), TextFormatFlags.NoPadding).Width + 14;

            // Live FPS & Stats Badges
            if (curFps > 0)
            {
                Color fpsCol = Color.FromArgb(0x38, 0xBD, 0xF8); // Sky Blue
                string fpsStr = string.Format("FPS: {0}", curFps);
                TextRenderer.DrawText(g, fpsStr, FontBadge, new Point(x, 10), fpsCol, TextFormatFlags.NoPadding);
                x += TextRenderer.MeasureText(g, fpsStr, FontBadge, new Size(120, 20), TextFormatFlags.NoPadding).Width + 10;

                Color ftCol = Color.FromArgb(0x22, 0xD3, 0xEE); // Electric Cyan
                string ftStr = string.Format("FT: {0:0.0} ms", curFt);
                TextRenderer.DrawText(g, ftStr, FontBadge, new Point(x, 10), ftCol, TextFormatFlags.NoPadding);
                x += TextRenderer.MeasureText(g, ftStr, FontBadge, new Size(120, 20), TextFormatFlags.NoPadding).Width + 10;

                if (avgFps > 0)
                {
                    string avgStr = string.Format("Avg: {0}", avgFps);
                    TextRenderer.DrawText(g, avgStr, FontTitle, new Point(x, 10), Color.FromArgb(0x93, 0xC5, 0xFD), TextFormatFlags.NoPadding);
                    x += TextRenderer.MeasureText(g, avgStr, FontTitle, new Size(120, 20), TextFormatFlags.NoPadding).Width + 10;
                }

                if (low01 > 0)
                {
                    Color lowCol = Color.FromArgb(0xFB, 0x71, 0x85); // Rose
                    string lowStr = string.Format("0.1%: {0}", low01);
                    TextRenderer.DrawText(g, lowStr, FontTitle, new Point(x, 10), lowCol, TextFormatFlags.NoPadding);
                    x += TextRenderer.MeasureText(g, lowStr, FontTitle, new Size(120, 20), TextFormatFlags.NoPadding).Width + 10;
                }

                if (low1 > 0)
                {
                    Color low1Col = Color.FromArgb(0xFB, 0x92, 0x3C); // Amber Orange
                    string low1Str = string.Format("1%: {0}", low1);
                    TextRenderer.DrawText(g, low1Str, FontTitle, new Point(x, 10), low1Col, TextFormatFlags.NoPadding);
                    x += TextRenderer.MeasureText(g, low1Str, FontTitle, new Size(120, 20), TextFormatFlags.NoPadding).Width + 10;
                }
            }
            else
            {
                TextRenderer.DrawText(g, "(Waiting for 3D Application / RTSS Frame Stream)", FontAxis, new Point(x, 12), dimCol, TextFormatFlags.NoPadding);
            }

            // Right-aligned Action Pills
            int pillY = 7;
            int pillH = 22;
            int rightX = Width - 8;

            // 1. Save PNG Pill
            string pngTxt = "💾 Snapshot";
            Size pngSz = TextRenderer.MeasureText(g, pngTxt, FontPill, new Size(120, 20), TextFormatFlags.NoPadding);
            int pngW = pngSz.Width + 14;
            rightX -= pngW;
            _pillRects[2] = new Rectangle(rightX, pillY, pngW, pillH);
            DrawActionPill(g, _pillRects[2], pngTxt, false, Color.FromArgb(0x22, 0xD3, 0xEE));

            // 2. Reset Peaks Pill
            rightX -= 6;
            string resetTxt = "↺ Reset";
            Size resetSz = TextRenderer.MeasureText(g, resetTxt, FontPill, new Size(120, 20), TextFormatFlags.NoPadding);
            int resetW = resetSz.Width + 14;
            rightX -= resetW;
            _pillRects[1] = new Rectangle(rightX, pillY, resetW, pillH);
            DrawActionPill(g, _pillRects[1], resetTxt, false, Color.FromArgb(0xF5, 0x9E, 0x0B));

            // 3. View Mode Toggle Pill
            rightX -= 6;
            string viewTxt = SplitView ? "◫ Split (DF)" : "◰ Combined";
            Size viewSz = TextRenderer.MeasureText(g, viewTxt, FontPill, new Size(140, 20), TextFormatFlags.NoPadding);
            int viewW = viewSz.Width + 14;
            rightX -= viewW;
            _pillRects[0] = new Rectangle(rightX, pillY, viewW, pillH);
            DrawActionPill(g, _pillRects[0], viewTxt, true, Color.FromArgb(0x38, 0xBD, 0xF8));
        }

        private void DrawActionPill(Graphics g, Rectangle rect, string text, bool isHighlighted, Color accent)
        {
            Color pillBg = isHighlighted
                ? Color.FromArgb(40, accent.R, accent.G, accent.B)
                : (IsDarkMode ? Color.FromArgb(0x18, 0x18, 0x25) : Color.FromArgb(0xE2, 0xE8, 0xF0));

            Color pillBorder = isHighlighted ? accent : (IsDarkMode ? Color.FromArgb(0x45, 0x47, 0x5A) : Color.FromArgb(0xCB, 0xD5, 0xE1));
            Color pillText = isHighlighted ? (IsDarkMode ? Color.White : Color.Black) : (IsDarkMode ? Color.FromArgb(0xCD, 0xD6, 0xF4) : Color.FromArgb(0x33, 0x41, 0x55));

            using (SolidBrush bgBr = new SolidBrush(pillBg))
            using (Pen borderPen = new Pen(pillBorder, 1f))
            {
                g.FillRectangle(bgBr, rect);
                g.DrawRectangle(borderPen, rect);
            }
            TextRenderer.DrawText(g, text, FontPill, rect, pillText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void DrawDfFrametimeSection(Graphics g, Rectangle rect)
        {
            using (SolidBrush cardBr = new SolidBrush(IsDarkMode ? Color.FromArgb(0x14, 0x14, 0x20) : Color.FromArgb(0xFA, 0xFA, 0xFC)))
            using (Pen bPen = new Pen(IsDarkMode ? Color.FromArgb(0x28, 0x2A, 0x36) : Color.FromArgb(0xE2, 0xE8, 0xF0), 1f))
            {
                g.FillRectangle(cardBr, rect);
                g.DrawRectangle(bPen, rect);
            }

            double curFt = Status.RtssFrametimeMs > 0 ? Status.RtssFrametimeMs : (Status.RtssFps > 0 ? (1000.0 / Status.RtssFps) : ((Stats != null && Stats.FrametimeHistory.Count > 0) ? Stats.FrametimeHistory[Stats.FrametimeHistory.Count - 1] / 10.0 : 0.0));

            // Section Header
            string ftHdr = curFt > 0
                ? string.Format("FRAME-TIME (MS)  •  NOW: {0:0.0} ms  •  JITTER / STUTTER ANALYSIS", curFt)
                : "FRAME-TIME (MS)  •  DIGITAL FOUNDRY STYLE INVERTED JITTER SCALE";
            TextRenderer.DrawText(g, ftHdr, FontHeader, new Point(rect.X + 8, rect.Y + 4), Color.FromArgb(0x22, 0xD3, 0xEE), TextFormatFlags.NoPadding);

            Rectangle plot = new Rectangle(rect.X + 38, rect.Y + 20, Math.Max(20, rect.Width - 84), Math.Max(10, rect.Height - 26));

            // Dynamic ceiling tracking
            int targetFtCeiling = CapFrameXOverlayForm.CalculateDfFrametimeCeiling(curFt, (Stats != null) ? Stats.FrametimeHistory : null);
            if (targetFtCeiling > _currentFtCeiling)
            {
                _currentFtCeiling = targetFtCeiling;
                _ftCeilingHoldCounter = 0;
            }
            else if (targetFtCeiling < _currentFtCeiling)
            {
                _ftCeilingHoldCounter++;
                if (_ftCeilingHoldCounter >= 12)
                {
                    _currentFtCeiling = targetFtCeiling;
                    _ftCeilingHoldCounter = 0;
                }
            }
            else _ftCeilingHoldCounter = 0;
            int ftCeiling = _currentFtCeiling;

            // Plot Grid & Reference Guides
            using (Pen gridPen = new Pen(IsDarkMode ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0), 1f) { DashStyle = DashStyle.Dash })
            using (Pen divPen = new Pen(IsDarkMode ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0), 1f))
            {
                // Center dividing line
                int midX = plot.Left + plot.Width / 2;
                g.DrawLine(divPen, midX, plot.Top, midX, plot.Bottom);
                g.DrawLine(divPen, plot.Right, plot.Top, plot.Right, plot.Bottom);

                // Horizontal midpoint line
                float midY = plot.Top + plot.Height / 2f;
                g.DrawLine(gridPen, plot.Left, midY, plot.Right, midY);

                // 16.6 ms guide line (60 FPS)
                if (16.6f <= ftCeiling)
                {
                    float y16 = plot.Top + (16.6f / ftCeiling) * plot.Height;
                    using (Pen p16 = new Pen(Color.FromArgb(45, 0x22, 0xC5, 0x5E), 1f) { DashStyle = DashStyle.Dot })
                    {
                        g.DrawLine(p16, plot.Left, y16, plot.Right, y16);
                    }
                }

                // 33.3 ms guide line (30 FPS)
                if (33.3f <= ftCeiling)
                {
                    float y33 = plot.Top + (33.3f / ftCeiling) * plot.Height;
                    using (Pen p33 = new Pen(Color.FromArgb(45, 0xF5, 0x9E, 0x0B), 1f) { DashStyle = DashStyle.Dot })
                    {
                        g.DrawLine(p33, plot.Left, y33, plot.Right, y33);
                    }
                }
            }

            // Right Axis Labels: 0 (top), mid (middle), ftCeiling (bottom) - Inverted Digital Foundry scale
            Color axisCol = IsDarkMode ? Color.FromArgb(0x94, 0xA3, 0xB8) : Color.FromArgb(0x64, 0x74, 0x8B);
            TextRenderer.DrawText(g, "0 ms", FontAxis, new Point(plot.Right + 4, plot.Top - 2), axisCol, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, (ftCeiling / 2) + " ms", FontAxis, new Point(plot.Right + 4, (int)Math.Round(plot.Top + plot.Height / 2f - 6)), axisCol, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, ftCeiling + " ms", FontAxis, new Point(plot.Right + 4, plot.Bottom - 10), axisCol, TextFormatFlags.NoPadding);

            if (Stats == null || Stats.FrametimeHistory == null || Stats.FrametimeHistory.Count == 0)
            {
                TextRenderer.DrawText(g, "Waiting for RTSS frame samples...", FontStandby, plot, axisCol, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int total = Stats.FrametimeHistory.Count;
            int take = Math.Min(total, 120);
            int start = total - take;

            PointF[] pts = new PointF[Math.Max(2, take)];
            if (take == 1)
            {
                float ftMs = (start < Stats.FrametimeHistory.Count && Stats.FrametimeHistory[start] > 0)
                    ? Stats.FrametimeHistory[start] / 10.0f
                    : 16.6f;
                float norm = Math.Max(0f, Math.Min(1f, ftMs / (float)ftCeiling));
                float y = plot.Top + norm * plot.Height;
                pts[0] = new PointF(plot.Left, y);
                pts[1] = new PointF(plot.Right, y);
            }
            else
            {
                for (int i = 0; i < take; i++)
                {
                    int idx = start + i;
                    float ftMs = (idx < Stats.FrametimeHistory.Count && Stats.FrametimeHistory[idx] > 0)
                        ? Stats.FrametimeHistory[idx] / 10.0f
                        : 16.6f;
                    float norm = Math.Max(0f, Math.Min(1f, ftMs / (float)ftCeiling));
                    float x = plot.Left + (plot.Width * i) / (float)(take - 1);
                    float y = plot.Top + norm * plot.Height;
                    pts[i] = new PointF(x, y);
                }
            }

            // Neon Cyan / Lime Jitter Line
            Color ftCol = Color.FromArgb(0x22, 0xD3, 0xEE);
            using (Pen ftPen = new Pen(ftCol, 1.6f))
            {
                ftPen.LineJoin = LineJoin.Round;
                g.DrawLines(ftPen, pts);
            }

            // Endpoint Badge
            if (pts.Length > 0 && curFt > 0)
            {
                PointF last = pts[pts.Length - 1];
                using (SolidBrush dotBr = new SolidBrush(ftCol))
                {
                    g.FillEllipse(dotBr, last.X - 3f, last.Y - 3f, 6f, 6f);
                }

                string badgeTxt = string.Format("{0:0.0} ms", curFt);
                Size bSz = TextRenderer.MeasureText(g, badgeTxt, FontBadge, new Size(90, 18), TextFormatFlags.NoPadding);
                int bw = bSz.Width + 8;
                int bh = 15;
                int bx = (int)Math.Round(last.X - bw - 8);
                if (bx < plot.Left + 2) bx = plot.Left + 2;
                int by = (int)Math.Round(last.Y - bh / 2f);
                if (by < plot.Top + 1) by = plot.Top + 1;
                if (by + bh > plot.Bottom - 1) by = plot.Bottom - 1 - bh;
                Rectangle badgeRect = new Rectangle(bx, by, bw, bh);

                using (Pen tickPen = new Pen(Color.FromArgb(180, ftCol), 1f))
                {
                    g.DrawLine(tickPen, last.X, last.Y, bx + bw, by + bh / 2f);
                }
                using (SolidBrush bBg = new SolidBrush(Color.FromArgb(235, 10, 14, 20)))
                using (Pen bBorder = new Pen(ftCol, 1.2f))
                {
                    g.FillRectangle(bBg, badgeRect);
                    g.DrawRectangle(bBorder, badgeRect);
                }
                TextRenderer.DrawText(g, badgeTxt, FontBadge, new Point(bx + 4, by + 1), ftCol, TextFormatFlags.NoPadding);
            }

            // Hover crosshair and HUD tooltip
            if (_hoverMouseX >= plot.Left && _hoverMouseX <= plot.Right && rect.Contains(_hoverMouseX, _hoverMouseY))
            {
                using (Pen crossPen = new Pen(Color.FromArgb(160, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(crossPen, _hoverMouseX, plot.Top, _hoverMouseX, plot.Bottom);
                }
                float normX = (float)(_hoverMouseX - plot.Left) / Math.Max(1, plot.Width);
                normX = Math.Max(0f, Math.Min(1f, normX));
                int sIdx = start + (int)Math.Round(normX * (take - 1));
                sIdx = Math.Max(0, Math.Min(total - 1, sIdx));

                int secAgo = (int)Math.Round((take - 1 - (sIdx - start)) * 1.0);
                double ft = (sIdx < Stats.FrametimeHistory.Count && Stats.FrametimeHistory[sIdx] > 0)
                    ? Stats.FrametimeHistory[sIdx] / 10.0
                    : 16.6;
                int equivFps = ft > 0 ? (int)Math.Round(1000.0 / ft) : 0;
                string hud = string.Format("{0} | Frametime: {1:0.0} ms (~{2} FPS)", secAgo == 0 ? "Now" : "-" + secAgo + "s", ft, equivFps);

                DrawHoverHud(g, plot, hud, _hoverMouseX, _hoverMouseY);
            }
        }

        private void DrawDfFramerateSection(Graphics g, Rectangle rect)
        {
            using (SolidBrush cardBr = new SolidBrush(IsDarkMode ? Color.FromArgb(0x14, 0x14, 0x20) : Color.FromArgb(0xFA, 0xFA, 0xFC)))
            using (Pen bPen = new Pen(IsDarkMode ? Color.FromArgb(0x28, 0x2A, 0x36) : Color.FromArgb(0xE2, 0xE8, 0xF0), 1f))
            {
                g.FillRectangle(cardBr, rect);
                g.DrawRectangle(bPen, rect);
            }

            int total = (Stats != null && Stats.FpsHistory != null) ? Stats.FpsHistory.Count : 0;
            int take = Math.Min(total, 120);
            int start = total - take;

            int curFps = Status.RtssFps > 0 ? Status.RtssFps : ((total > 0 && Stats.FpsHistory[total - 1] > 0) ? Stats.FpsHistory[total - 1] : 0);
            int avgFps = (Stats != null && Stats.AvgFps > 0) ? (int)Math.Round(Stats.AvgFps) : (Status.RtssAvgFps > 0 ? Status.RtssAvgFps : curFps);
            int low01 = Status.RtssP01LowFps > 0 ? Status.RtssP01LowFps : ((Stats != null && Stats.P01LowFps > 0) ? Stats.P01LowFps : 0);

            // Section Header
            string fpsHdr = curFps > 0
                ? string.Format("FRAME-RATE (FPS)  •  LIVE: {0} FPS  •  AVG: {1}  •  0.1% LOW: {2}", curFps, avgFps, low01 > 0 ? low01.ToString() : "--")
                : "FRAME-RATE (FPS)  •  CAPFRAMEX STYLE RUNNING BENCHMARK HISTORY";
            TextRenderer.DrawText(g, fpsHdr, FontHeader, new Point(rect.X + 8, rect.Y + 4), Color.FromArgb(0x38, 0xBD, 0xF8), TextFormatFlags.NoPadding);

            Rectangle plot = new Rectangle(rect.X + 38, rect.Y + 20, Math.Max(20, rect.Width - 84), Math.Max(10, rect.Height - 26));

            // Dynamic ceiling tracking
            int targetFpsCeiling = CapFrameXOverlayForm.CalculateDfCeiling(curFps, (Stats != null) ? Stats.FpsHistory : null);
            if (targetFpsCeiling > _currentFpsCeiling)
            {
                _currentFpsCeiling = targetFpsCeiling;
                _fpsCeilingHoldCounter = 0;
            }
            else if (targetFpsCeiling < _currentFpsCeiling)
            {
                _fpsCeilingHoldCounter++;
                if (_fpsCeilingHoldCounter >= 12)
                {
                    _currentFpsCeiling = targetFpsCeiling;
                    _fpsCeilingHoldCounter = 0;
                }
            }
            else _fpsCeilingHoldCounter = 0;
            int fpsCeiling = _currentFpsCeiling;

            int[] levels;
            if (fpsCeiling == 60) levels = new int[] { 60, 40, 20, 0 };
            else if (fpsCeiling == 80) levels = new int[] { 80, 60, 40, 20, 0 };
            else if (fpsCeiling == 90) levels = new int[] { 90, 60, 30, 0 };
            else if (fpsCeiling == 120) levels = new int[] { 120, 80, 40, 0 };
            else if (fpsCeiling == 180) levels = new int[] { 180, 120, 60, 0 };
            else if (fpsCeiling == 240) levels = new int[] { 240, 160, 80, 0 };
            else
            {
                int step = fpsCeiling / 3;
                levels = new int[] { fpsCeiling, step * 2, step, 0 };
            }

            // Plot Grid & Reference Guides
            using (Pen gridPen = new Pen(IsDarkMode ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0), 1f) { DashStyle = DashStyle.Dash })
            using (Pen divPen = new Pen(IsDarkMode ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0), 1f))
            {
                int midX = plot.Left + plot.Width / 2;
                g.DrawLine(divPen, midX, plot.Top, midX, plot.Bottom);
                g.DrawLine(divPen, plot.Right, plot.Top, plot.Right, plot.Bottom);

                for (int i = 0; i < levels.Length; i++)
                {
                    float norm = (float)levels[i] / fpsCeiling;
                    float y = plot.Bottom - norm * plot.Height;
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);

                    // Right Axis Label
                    Color aCol = (levels[i] == 60) ? Color.FromArgb(0x22, 0xC5, 0x5E) : (IsDarkMode ? Color.FromArgb(0x94, 0xA3, 0xB8) : Color.FromArgb(0x64, 0x74, 0x8B));
                    TextRenderer.DrawText(g, levels[i].ToString(), FontAxis, new Point(plot.Right + 4, (int)Math.Round(y - 6)), aCol, TextFormatFlags.NoPadding);
                }
            }

            if (total == 0)
            {
                Color axisCol = IsDarkMode ? Color.FromArgb(0x94, 0xA3, 0xB8) : Color.FromArgb(0x64, 0x74, 0x8B);
                TextRenderer.DrawText(g, "Waiting for RTSS frame samples...", FontStandby, plot, axisCol, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            // 1. 0.1% Low Translucent Area & Line
            Color lowCol = Color.FromArgb(0xFB, 0x71, 0x85); // Rose
            if (Stats.P01LowHistory != null && Stats.P01LowHistory.Count > 0)
            {
                PointF[] lowPts = new PointF[Math.Max(2, take)];
                if (take == 1)
                {
                    int lowVal = (start < Stats.P01LowHistory.Count && Stats.P01LowHistory[start] > 0)
                        ? Stats.P01LowHistory[start]
                        : (int)Math.Max(1, Math.Round(curFps * 0.75));
                    float norm = Math.Max(0f, Math.Min(1f, (float)lowVal / fpsCeiling));
                    float y = plot.Bottom - norm * plot.Height;
                    lowPts[0] = new PointF(plot.Left, y);
                    lowPts[1] = new PointF(plot.Right, y);
                }
                else
                {
                    for (int i = 0; i < take; i++)
                    {
                        int idx = start + i;
                        int lowVal = (idx < Stats.P01LowHistory.Count && Stats.P01LowHistory[idx] > 0)
                            ? Stats.P01LowHistory[idx]
                            : (int)Math.Max(1, Math.Round(curFps * 0.75));
                        float norm = Math.Max(0f, Math.Min(1f, (float)lowVal / fpsCeiling));
                        float x = plot.Left + (plot.Width * i) / (float)(take - 1);
                        float y = plot.Bottom - norm * plot.Height;
                        lowPts[i] = new PointF(x, y);
                    }
                }

                // Fill underneath 0.1% low
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddLines(lowPts);
                    path.AddLine(plot.Right, plot.Bottom, plot.Left, plot.Bottom);
                    path.CloseFigure();
                    using (SolidBrush lowFill = new SolidBrush(Color.FromArgb(25, lowCol.R, lowCol.G, lowCol.B)))
                    {
                        g.FillPath(lowFill, path);
                    }
                }
                using (Pen lowPen = new Pen(Color.FromArgb(160, lowCol), 1.2f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLines(lowPen, lowPts);
                }
            }

            // 2. Avg FPS Dashed Line
            Color avgCol = Color.FromArgb(0x93, 0xC5, 0xFD);
            if (Stats.AvgFpsHistory != null && Stats.AvgFpsHistory.Count > 0)
            {
                PointF[] avgPts = new PointF[Math.Max(2, take)];
                if (take == 1)
                {
                    int aVal = (start < Stats.AvgFpsHistory.Count && Stats.AvgFpsHistory[start] > 0) ? Stats.AvgFpsHistory[start] : avgFps;
                    float norm = Math.Max(0f, Math.Min(1f, (float)aVal / fpsCeiling));
                    float y = plot.Bottom - norm * plot.Height;
                    avgPts[0] = new PointF(plot.Left, y);
                    avgPts[1] = new PointF(plot.Right, y);
                }
                else
                {
                    for (int i = 0; i < take; i++)
                    {
                        int idx = start + i;
                        int aVal = (idx < Stats.AvgFpsHistory.Count && Stats.AvgFpsHistory[idx] > 0) ? Stats.AvgFpsHistory[idx] : avgFps;
                        float norm = Math.Max(0f, Math.Min(1f, (float)aVal / fpsCeiling));
                        float x = plot.Left + (plot.Width * i) / (float)(take - 1);
                        float y = plot.Bottom - norm * plot.Height;
                        avgPts[i] = new PointF(x, y);
                    }
                }
                using (Pen avgPen = new Pen(avgCol, 1.4f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLines(avgPen, avgPts);
                }
            }

            // 3. Live Instantaneous FPS Line
            Color fpsCol = Color.FromArgb(0x38, 0xBD, 0xF8);
            PointF[] fpsPts = new PointF[Math.Max(2, take)];
            if (take == 1)
            {
                int fVal = (start < Stats.FpsHistory.Count && Stats.FpsHistory[start] > 0) ? Stats.FpsHistory[start] : 0;
                float norm = Math.Max(0f, Math.Min(1f, (float)fVal / fpsCeiling));
                float y = plot.Bottom - norm * plot.Height;
                fpsPts[0] = new PointF(plot.Left, y);
                fpsPts[1] = new PointF(plot.Right, y);
            }
            else
            {
                for (int i = 0; i < take; i++)
                {
                    int idx = start + i;
                    int fVal = (idx < Stats.FpsHistory.Count && Stats.FpsHistory[idx] > 0) ? Stats.FpsHistory[idx] : 0;
                    float norm = Math.Max(0f, Math.Min(1f, (float)fVal / fpsCeiling));
                    float x = plot.Left + (plot.Width * i) / (float)(take - 1);
                    float y = plot.Bottom - norm * plot.Height;
                    fpsPts[i] = new PointF(x, y);
                }
            }

            using (Pen fpsPen = new Pen(fpsCol, 1.8f))
            {
                fpsPen.LineJoin = LineJoin.Round;
                g.DrawLines(fpsPen, fpsPts);
            }

            // Endpoint Badge
            if (fpsPts.Length > 0 && curFps > 0)
            {
                PointF last = fpsPts[fpsPts.Length - 1];
                using (SolidBrush dotBr = new SolidBrush(fpsCol))
                {
                    g.FillEllipse(dotBr, last.X - 3.5f, last.Y - 3.5f, 7f, 7f);
                }

                string badgeTxt = string.Format("{0} FPS", curFps);
                Size bSz = TextRenderer.MeasureText(g, badgeTxt, FontBadge, new Size(90, 18), TextFormatFlags.NoPadding);
                int bw = bSz.Width + 8;
                int bh = 15;
                int bx = (int)Math.Round(last.X - bw - 8);
                if (bx < plot.Left + 2) bx = plot.Left + 2;
                int by = (int)Math.Round(last.Y - bh / 2f);
                if (by < plot.Top + 1) by = plot.Top + 1;
                if (by + bh > plot.Bottom - 1) by = plot.Bottom - 1 - bh;
                Rectangle badgeRect = new Rectangle(bx, by, bw, bh);

                using (Pen tickPen = new Pen(Color.FromArgb(180, fpsCol), 1f))
                {
                    g.DrawLine(tickPen, last.X, last.Y, bx + bw, by + bh / 2f);
                }
                using (SolidBrush bBg = new SolidBrush(Color.FromArgb(235, 10, 14, 20)))
                using (Pen bBorder = new Pen(fpsCol, 1.2f))
                {
                    g.FillRectangle(bBg, badgeRect);
                    g.DrawRectangle(bBorder, badgeRect);
                }
                TextRenderer.DrawText(g, badgeTxt, FontBadge, new Point(bx + 4, by + 1), fpsCol, TextFormatFlags.NoPadding);
            }

            // Hover crosshair and HUD tooltip
            if (_hoverMouseX >= plot.Left && _hoverMouseX <= plot.Right && rect.Contains(_hoverMouseX, _hoverMouseY))
            {
                using (Pen crossPen = new Pen(Color.FromArgb(160, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(crossPen, _hoverMouseX, plot.Top, _hoverMouseX, plot.Bottom);
                }
                float normX = (float)(_hoverMouseX - plot.Left) / Math.Max(1, plot.Width);
                normX = Math.Max(0f, Math.Min(1f, normX));
                int sIdx = start + (int)Math.Round(normX * (take - 1));
                sIdx = Math.Max(0, Math.Min(total - 1, sIdx));

                int secAgo = (int)Math.Round((take - 1 - (sIdx - start)) * 1.0);
                int f = (sIdx < Stats.FpsHistory.Count) ? Stats.FpsHistory[sIdx] : 0;
                int a = (sIdx < Stats.AvgFpsHistory.Count) ? Stats.AvgFpsHistory[sIdx] : 0;
                int l = (sIdx < Stats.P01LowHistory.Count) ? Stats.P01LowHistory[sIdx] : 0;
                string hud = string.Format("{0} | Live: {1} FPS • Avg: {2} • 0.1% Low: {3}", secAgo == 0 ? "Now" : "-" + secAgo + "s", f, a, l > 0 ? l.ToString() : "--");

                DrawHoverHud(g, plot, hud, _hoverMouseX, _hoverMouseY);
            }
        }

        private void DrawCombinedPerformanceSection(Graphics g, Rectangle rect)
        {
            using (SolidBrush cardBr = new SolidBrush(IsDarkMode ? Color.FromArgb(0x14, 0x14, 0x20) : Color.FromArgb(0xFA, 0xFA, 0xFC)))
            using (Pen bPen = new Pen(IsDarkMode ? Color.FromArgb(0x28, 0x2A, 0x36) : Color.FromArgb(0xE2, 0xE8, 0xF0), 1f))
            {
                g.FillRectangle(cardBr, rect);
                g.DrawRectangle(bPen, rect);
            }

            int curFps = Status.RtssFps > 0 ? Status.RtssFps : ((Stats != null && Stats.FpsHistory.Count > 0) ? Stats.FpsHistory[Stats.FpsHistory.Count - 1] : 0);
            double curFt = Status.RtssFrametimeMs > 0 ? Status.RtssFrametimeMs : (curFps > 0 ? (1000.0 / curFps) : 0.0);

            string hdr = string.Format("COMBINED PERFORMANCE TIMELINE  •  LIVE: {0} FPS  •  FRAMETIME: {1:0.0} ms", curFps, curFt);
            TextRenderer.DrawText(g, hdr, FontHeader, new Point(rect.X + 8, rect.Y + 4), Color.FromArgb(0x38, 0xBD, 0xF8), TextFormatFlags.NoPadding);

            Rectangle plot = new Rectangle(rect.X + 44, rect.Y + 22, Math.Max(20, rect.Width - 92), Math.Max(10, rect.Height - 30));

            int total = (Stats != null && Stats.FpsHistory != null) ? Stats.FpsHistory.Count : 0;
            int take = Math.Min(total, 120);
            int start = total - take;

            int targetFpsCeiling = CapFrameXOverlayForm.CalculateDfCeiling(curFps, (Stats != null) ? Stats.FpsHistory : null);
            int fpsCeiling = Math.Max(60, targetFpsCeiling);
            float ftCeiling = 50f;

            // Horizontal Grid Lines & Dual Axes
            using (Pen gridPen = new Pen(IsDarkMode ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0), 1f) { DashStyle = DashStyle.Dash })
            {
                int steps = 4;
                for (int i = 0; i <= steps; i++)
                {
                    float norm = (float)i / steps;
                    float y = plot.Bottom - norm * plot.Height;
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);

                    // Left Axis: FPS
                    int fpsVal = (int)Math.Round(norm * fpsCeiling);
                    TextRenderer.DrawText(g, fpsVal.ToString(), FontAxis, new Point(rect.X + 6, (int)Math.Round(y - 6)), Color.FromArgb(0x38, 0xBD, 0xF8), TextFormatFlags.NoPadding);

                    // Right Axis: Frametime ms
                    float ftVal = norm * ftCeiling;
                    TextRenderer.DrawText(g, string.Format("{0:0} ms", ftVal), FontAxis, new Point(plot.Right + 4, (int)Math.Round(y - 6)), Color.FromArgb(0x22, 0xD3, 0xEE), TextFormatFlags.NoPadding);
                }
            }

            if (total == 0)
            {
                Color axisCol = IsDarkMode ? Color.FromArgb(0x94, 0xA3, 0xB8) : Color.FromArgb(0x64, 0x74, 0x8B);
                TextRenderer.DrawText(g, "Waiting for RTSS frame samples...", FontStandby, plot, axisCol, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            // 1. Frametime Series (Right axis, plotted as Amber/Cyan line)
            Color ftCol = Color.FromArgb(0x22, 0xD3, 0xEE);
            PointF[] ftPts = new PointF[Math.Max(2, take)];
            if (take == 1)
            {
                float ftMs = (start < Stats.FrametimeHistory.Count && Stats.FrametimeHistory[start] > 0) ? Stats.FrametimeHistory[start] / 10.0f : 16.6f;
                float norm = Math.Max(0f, Math.Min(1f, ftMs / ftCeiling));
                float y = plot.Bottom - norm * plot.Height;
                ftPts[0] = new PointF(plot.Left, y);
                ftPts[1] = new PointF(plot.Right, y);
            }
            else
            {
                for (int i = 0; i < take; i++)
                {
                    int idx = start + i;
                    float ftMs = (idx < Stats.FrametimeHistory.Count && Stats.FrametimeHistory[idx] > 0) ? Stats.FrametimeHistory[idx] / 10.0f : 16.6f;
                    float norm = Math.Max(0f, Math.Min(1f, ftMs / ftCeiling));
                    float x = plot.Left + (plot.Width * i) / (float)(take - 1);
                    float y = plot.Bottom - norm * plot.Height;
                    ftPts[i] = new PointF(x, y);
                }
            }
            using (Pen ftPen = new Pen(Color.FromArgb(160, ftCol), 1.4f) { DashStyle = DashStyle.Dash })
            {
                g.DrawLines(ftPen, ftPts);
            }

            // 2. Live FPS Series (Left axis, plotted as Sky Blue line)
            Color fpsCol = Color.FromArgb(0x38, 0xBD, 0xF8);
            PointF[] fpsPts = new PointF[Math.Max(2, take)];
            if (take == 1)
            {
                int fVal = (start < Stats.FpsHistory.Count && Stats.FpsHistory[start] > 0) ? Stats.FpsHistory[start] : 0;
                float norm = Math.Max(0f, Math.Min(1f, (float)fVal / fpsCeiling));
                float y = plot.Bottom - norm * plot.Height;
                fpsPts[0] = new PointF(plot.Left, y);
                fpsPts[1] = new PointF(plot.Right, y);
            }
            else
            {
                for (int i = 0; i < take; i++)
                {
                    int idx = start + i;
                    int fVal = (idx < Stats.FpsHistory.Count && Stats.FpsHistory[idx] > 0) ? Stats.FpsHistory[idx] : 0;
                    float norm = Math.Max(0f, Math.Min(1f, (float)fVal / fpsCeiling));
                    float x = plot.Left + (plot.Width * i) / (float)(take - 1);
                    float y = plot.Bottom - norm * plot.Height;
                    fpsPts[i] = new PointF(x, y);
                }
            }
            using (Pen fpsPen = new Pen(fpsCol, 2.0f))
            {
                fpsPen.LineJoin = LineJoin.Round;
                g.DrawLines(fpsPen, fpsPts);
            }

            // Hover crosshair and HUD
            if (_hoverMouseX >= plot.Left && _hoverMouseX <= plot.Right && rect.Contains(_hoverMouseX, _hoverMouseY))
            {
                using (Pen crossPen = new Pen(Color.FromArgb(160, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(crossPen, _hoverMouseX, plot.Top, _hoverMouseX, plot.Bottom);
                }
                float normX = (float)(_hoverMouseX - plot.Left) / Math.Max(1, plot.Width);
                normX = Math.Max(0f, Math.Min(1f, normX));
                int sIdx = start + (int)Math.Round(normX * (take - 1));
                sIdx = Math.Max(0, Math.Min(total - 1, sIdx));

                int secAgo = (int)Math.Round((take - 1 - (sIdx - start)) * 1.0);
                int f = (sIdx < Stats.FpsHistory.Count) ? Stats.FpsHistory[sIdx] : 0;
                double ft = (sIdx < Stats.FrametimeHistory.Count && Stats.FrametimeHistory[sIdx] > 0) ? Stats.FrametimeHistory[sIdx] / 10.0 : 0.0;
                string hud = string.Format("{0} | FPS: {1} • Frametime: {2:0.0} ms", secAgo == 0 ? "Now" : "-" + secAgo + "s", f, ft);

                DrawHoverHud(g, plot, hud, _hoverMouseX, _hoverMouseY);
            }
        }

        private void DrawBottomTimeline(Graphics g)
        {
            Color tCol = IsDarkMode ? Color.FromArgb(0x64, 0x74, 0x8B) : Color.FromArgb(0x94, 0xA3, 0xB8);
            int y = Height - 16;
            int left = 44;
            int right = Width - 46;
            int span = right - left;

            string[] markers = new string[] { "-120s", "-90s", "-60s", "-30s", "-15s", "Now" };
            float[] pos = new float[] { 0f, 0.25f, 0.50f, 0.75f, 0.875f, 1f };

            for (int i = 0; i < markers.Length; i++)
            {
                int x = left + (int)Math.Round(pos[i] * span);
                TextFormatFlags flags = (i == 0) ? TextFormatFlags.Left : (i == markers.Length - 1 ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, markers[i], FontAxis, new Rectangle(x - 24, y, 48, 14), tCol, flags);
            }
        }

        private void DrawHoverHud(Graphics g, Rectangle plot, string text, int mouseX, int mouseY)
        {
            Size sz = TextRenderer.MeasureText(g, text, FontHud, new Size(340, 22), TextFormatFlags.NoPadding);
            int w = sz.Width + 14;
            int h = 18;
            int x = mouseX + 12;
            if (x + w > plot.Right) x = mouseX - w - 12;
            if (x < plot.Left + 2) x = plot.Left + 2;

            int y = mouseY - h / 2;
            if (y < plot.Top + 2) y = plot.Top + 2;
            if (y + h > plot.Bottom - 2) y = plot.Bottom - 2 - h;

            Rectangle rect = new Rectangle(x, y, w, h);
            using (SolidBrush bgBr = new SolidBrush(Color.FromArgb(235, 15, 23, 42)))
            using (Pen borderPen = new Pen(Color.FromArgb(0x38, 0xBD, 0xF8), 1.2f))
            {
                g.FillRectangle(bgBr, rect);
                g.DrawRectangle(borderPen, rect);
            }
            TextRenderer.DrawText(g, text, FontHud, new Point(x + 7, y + 2), Color.White, TextFormatFlags.NoPadding);
        }
    }
}
