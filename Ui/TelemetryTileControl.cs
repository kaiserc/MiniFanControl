using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MiniFanControl
{
    /// <summary>
    /// Self-contained hardware telemetry metric tile (header, peak range, tabular hero value,
    /// live sub-stat, two detail rows, clipped live sparkline, hover/spotlight borders, and
    /// keyboard/mouse activation). Replaces 24+ duplicated Panel/Label fields in MainForm.
    /// </summary>
    public sealed class TelemetryTileControl : DoubleBufferedPanel
    {
        private static readonly Font TitleFont         = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        private static readonly Font PeakFont          = new Font("Segoe UI", 7.25f, FontStyle.Bold);
        private static readonly Font SubFont           = new Font("Segoe UI", 7.75f, FontStyle.Bold);
        private static readonly Font DetailFont        = new Font("Segoe UI", 7.5f, FontStyle.Regular);
        private static readonly Font ToolTipChartFont  = new Font("Segoe UI", 7.25f, FontStyle.Bold);
        private static readonly Font ValMonoLarge      = CreateTabularMetricFont(14.0f);
        private static readonly Font ValMonoRpm        = CreateTabularMetricFont(11.25f);
        private static readonly Font HiddenLargeFont   = new Font("Segoe UI", 14.5f, FontStyle.Bold);
        private static readonly Font HiddenRpmFont     = new Font("Segoe UI", 11.5f, FontStyle.Bold);

        private readonly int _metricKind;
        private readonly bool _isRpmTile;
        private readonly Label _valueLabel;

        private string _title = "";
        private Color _titleForeColor = Color.FromArgb(0x94, 0xA3, 0xB8);
        private string _peakText = "";
        private string _subText = "";
        private Color _subColor = Color.Empty;
        private string _detailText = "";
        private string _detailText2 = "";
        private Color _detail2Color = Color.Empty;
        private bool _isHovered;
        private bool _isSpotlit;

        public int MetricKind { get { return _metricKind; } }
        public bool IsRpmTile { get { return _isRpmTile; } }

        public string Title
        {
            get { return _title; }
            set { _title = value ?? ""; }
        }

        public Color TitleForeColor
        {
            get { return _titleForeColor; }
            set { _titleForeColor = value; }
        }

        public string ValueText
        {
            get { return _valueLabel.Text; }
            set
            {
                string next = value ?? "";
                if (_valueLabel.Text != next) _valueLabel.Text = next;
            }
        }

        public Color ValueForeColor
        {
            get { return _valueLabel.ForeColor; }
            set
            {
                if (_valueLabel.ForeColor != value) _valueLabel.ForeColor = value;
            }
        }

        public bool IsSpotlit
        {
            get { return _isSpotlit; }
            set
            {
                if (_isSpotlit != value)
                {
                    _isSpotlit = value;
                    Invalidate();
                }
            }
        }

        public Func<ThemePalette> PaletteProvider { get; set; }
        public Func<List<int>> SamplesProvider { get; set; }
        public Func<bool> IsDeciWattsProvider { get; set; }

        /// <summary>
        /// Hidden value-label mirror retained for UI layout anti-clipping self-tests.
        /// </summary>
        internal Label ValueLabel { get { return _valueLabel; } }

        public event EventHandler TileActivated;
        public event EventHandler ResetPeaksRequested;

        public TelemetryTileControl(int metricKind, string initialTitle, string initialValue, bool isRpmTile = false)
        {
            _metricKind = metricKind;
            _isRpmTile = isRpmTile;
            _title = initialTitle ?? "";
            Cursor = Cursors.Hand;

            _valueLabel = new Label
            {
                Text = initialValue ?? "",
                Font = isRpmTile ? HiddenRpmFont : HiddenLargeFont,
                Location = new Point(7, 18),
                Size = new Size(243, 33),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };

            KeyboardActivated += (s, e) => RaiseTileActivated();
        }

        public void ApplyTheme(ThemePalette pal)
        {
            if (pal == null) return;
            BackColor = pal.TileBg;
            _titleForeColor = pal.TileTitleFg;
            _valueLabel.BackColor = pal.TileBg;
            _valueLabel.ForeColor = pal.TileValueFg;
            Invalidate();
        }

        public void UpdateMetric(
            string title,
            string valueText,
            Color valueForeColor,
            string peakText,
            string subText,
            string detailText,
            string detailText2,
            Color subColor,
            Color detail2Color,
            string accessibleName)
        {
            _title = title ?? "";
            ValueText = valueText;
            ValueForeColor = valueForeColor;
            _peakText = peakText ?? "";
            _subText = subText ?? "";
            _detailText = detailText ?? "";
            _detailText2 = detailText2 ?? "";
            _subColor = subColor;
            _detail2Color = detail2Color;
            if (!string.IsNullOrEmpty(accessibleName))
                AccessibleName = accessibleName;
            Invalidate();
        }

        public void ClearDetails()
        {
            _peakText = "";
            _subText = "";
            _subColor = Color.Empty;
            _detailText = "";
            _detailText2 = "";
            _detail2Color = Color.Empty;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (!_isHovered)
            {
                _isHovered = true;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_isHovered)
            {
                _isHovered = false;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                EventHandler h = ResetPeaksRequested;
                if (h != null) h(this, EventArgs.Empty);
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                RaiseTileActivated();
            }
        }

        private void RaiseTileActivated()
        {
            EventHandler h = TileActivated;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintTile(e.Graphics);
            base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _valueLabel != null && !_valueLabel.IsDisposed)
            {
                _valueLabel.Dispose();
            }
            base.Dispose(disposing);
        }

        private void PaintTile(Graphics g)
        {
            ThemePalette pal = PaletteProvider != null
                ? PaletteProvider()
                : ThemePalette.Resolve(ThemePreference.Dark, true);
            if (pal == null) pal = ThemePalette.Resolve(ThemePreference.Dark, true);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color lineCol = ResolveSeriesColor(_metricKind, pal);
            int cw = ClientSize.Width;
            int ch = ClientSize.Height;
            if (cw <= 0 || ch <= 0) return;

            Color dimCol = pal.TextDim;
            Color textCol = pal.Text;
            bool isXmas = pal.IsXmas;

            // Clean rounded fill with subtle hover elevation
            Color tileFill = _isHovered && !pal.IsHighContrast
                ? Win11VisualStyle.Blend(BackColor, lineCol, pal.IsDark ? 0.09f : 0.05f)
                : BackColor;
            using (SolidBrush fillBr = new SolidBrush(tileFill))
            {
                Win11VisualStyle.FillRoundedRect(g, fillBr, new Rectangle(0, 0, cw, ch), 6);
            }

            // Subtle top ambient sheen tinted with the sensor's signature color
            int sheenH = Math.Min(24, Math.Max(8, ch / 4));
            using (LinearGradientBrush sheen = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(1, cw), sheenH),
                Color.FromArgb(_isHovered ? 34 : (isXmas ? 14 : (pal.IsDark ? 26 : 15)), lineCol),
                Color.FromArgb(0, lineCol),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(sheen, 3, 1, Math.Max(1, cw - 6), sheenH);
            }
            if (isXmas && cw > 90 && ch > 50)
            {
                Win11VisualStyle.DrawSnowflake(g, cw - 18f, 22f, 11f, Color.FromArgb(20, pal.Accent), 1.1f);
            }

            // Sleek inset rounded 3px left accent strip matching the metric series color
            using (SolidBrush accentBrush = new SolidBrush(lineCol))
            {
                Win11VisualStyle.FillRoundedRect(g, accentBrush, new Rectangle(2, 6, 3, Math.Max(6, ch - 12)), 1);
            }

            // Row 1 (y = 4..17): Title (left) + Peak tracker (right)
            int peakW = 0;
            if (!string.IsNullOrEmpty(_peakText))
            {
                Size peakSz = TextRenderer.MeasureText(g, _peakText, PeakFont, new Size(cw - 14, 15),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                peakW = Math.Min(cw - 54, peakSz.Width + 4);
                Rectangle peakRect = new Rectangle(cw - 6 - peakW, 4, peakW, 13);
                TextRenderer.DrawText(g, _peakText, PeakFont, peakRect, dimCol,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            if (!string.IsNullOrEmpty(_title))
            {
                int titleMaxW = Math.Max(40, cw - 14 - peakW);
                Rectangle titleRect = new Rectangle(9, 4, titleMaxW, 13);
                TextRenderer.DrawText(g, _title, TitleFont, titleRect, _titleForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            // Row 2 (y = 17..41): Hero Value in tabular monospace (left) + Primary Live Sub-Stat (right)
            int valRight = 10;
            string valTxt = _valueLabel.Text;
            if (!string.IsNullOrEmpty(valTxt))
            {
                Font valDrawFont = _isRpmTile ? ValMonoRpm : ValMonoLarge;
                Size valSz = TextRenderer.MeasureText(g, valTxt, valDrawFont, new Size(cw - 16, 25),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                int valW = Math.Min(cw - 16, valSz.Width + 2);
                Rectangle valRect = new Rectangle(9, 17, valW, 24);
                TextRenderer.DrawText(g, valTxt, valDrawFont, valRect, _valueLabel.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                valRight = 9 + valW + 6;
            }
            if (!string.IsNullOrEmpty(_subText) && cw - 6 - valRight >= 28)
            {
                Rectangle subRect = new Rectangle(valRight, 18, cw - 6 - valRight, 22);
                Color subFg = _subColor.IsEmpty ? textCol : _subColor;
                TextRenderer.DrawText(g, _subText, SubFont, subRect, subFg,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            // Row 3 (y = 42..56): Full-width Detail Line 1
            if (!string.IsNullOrEmpty(_detailText))
            {
                Rectangle detailRect = new Rectangle(9, 42, Math.Max(20, cw - 15), 14);
                TextRenderer.DrawText(g, _detailText, DetailFont, detailRect, dimCol,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            // Row 4 (y = 57..71): Full-width Detail Line 2
            if (!string.IsNullOrEmpty(_detailText2))
            {
                Rectangle detailRect2 = new Rectangle(9, 57, Math.Max(20, cw - 15), 14);
                Color d2Fg = _detail2Color.IsEmpty ? dimCol : _detail2Color;
                TextRenderer.DrawText(g, _detailText2, DetailFont, detailRect2, d2Fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            // Row 5 (y = 77..ch-4): Wide, cleanly separated & clipped live sparkline
            List<int> samples = SamplesProvider != null ? SamplesProvider() : null;
            if (samples != null && samples.Count > 0)
            {
                Rectangle sparkRect = new Rectangle(6, 77, Math.Max(20, cw - 10), Math.Max(14, ch - 81));
                bool isDeciWatts = IsDeciWattsProvider != null && IsDeciWattsProvider();
                GraphicsState gs = g.Save();
                try
                {
                    g.SetClip(sparkRect);
                    DrawMiniSparklineChart(g, sparkRect, samples, lineCol, "", false, pal, isDeciWatts);
                }
                finally
                {
                    g.Restore(gs);
                }
            }

            if (_isSpotlit)
            {
                Win11VisualStyle.DrawRoundedCardBorder(g, new Rectangle(0, 0, cw, ch), lineCol, 6, 2f);
            }
            else if (_isHovered && !pal.IsHighContrast)
            {
                Win11VisualStyle.DrawRoundedCardBorder(g, new Rectangle(0, 0, cw, ch),
                    Win11VisualStyle.Blend(pal.TileBorder, lineCol, 0.72f), 6, 1.4f);
            }
            else if (!pal.IsHighContrast)
            {
                Win11VisualStyle.DrawRoundedCardBorder(g, new Rectangle(0, 0, cw, ch), pal.TileBorder, 6, 1f);
            }
            else
            {
                Win11VisualStyle.DrawRoundedCardBorder(g, new Rectangle(0, 0, cw, ch), SystemColors.WindowFrame, 4, 1f);
            }
        }

        /// <summary>
        /// Resolves the signature series color for a given metric kind and active theme palette.
        /// Shared by both TelemetryTileControl rendering and rich tooltip mini-charts.
        /// </summary>
        public static Color ResolveSeriesColor(int chartKind, ThemePalette pal)
        {
            if (pal == null) pal = ThemePalette.Resolve(ThemePreference.Dark, true);
            if (pal.IsXmas)
            {
                switch (chartKind)
                {
                    case 1:
                    case 3:
                    case 4:
                    case 6:
                        return pal.Accent;
                    default:
                        return pal.SecondaryAccent;
                }
            }

            switch (chartKind)
            {
                case 1:
                    return pal.IsRadeon ? pal.Accent : (pal.IsDark ? Color.FromArgb(0x89, 0xB4, 0xFA) : Color.FromArgb(0x1D, 0x4E, 0xD8));
                case 2:
                    return pal.IsRadeon ? Color.FromArgb(0x38, 0xBD, 0xF8) : Color.FromArgb(0x22, 0xC5, 0x5E);
                case 6:
                    return pal.IsDark ? Color.FromArgb(0x22, 0xD3, 0xEE) : Color.FromArgb(0x08, 0x91, 0xB2);
                case 3:
                    return pal.IsRadeon ? pal.SecondaryAccent : Color.FromArgb(0xF5, 0x9E, 0x0B);
                case 7:
                    return Color.FromArgb(0x81, 0x8C, 0xF8);
                case 9:
                    return pal.IsDark ? Color.FromArgb(0xE2, 0xE8, 0xF0) : Color.FromArgb(0x47, 0x55, 0x69);
                case 4:
                    return Color.FromArgb(0xC0, 0x84, 0xFC);
                case 5:
                    return Color.FromArgb(0x2D, 0xD4, 0xBF);
                default:
                    return pal.Accent;
            }
        }

        /// <summary>
        /// Draws a smooth, auto-scaled gradient sparkline chart into <paramref name="rect"/>.
        /// Shared by inline tile sparklines and rich hover tooltip charts.
        /// </summary>
        public static void DrawMiniSparklineChart(
            Graphics g,
            Rectangle rect,
            List<int> samples,
            Color lineColor,
            string unit,
            bool showLabels,
            ThemePalette pal,
            bool isTenths = false)
        {
            if (rect.Width < 12 || rect.Height < 6) return;
            if (pal == null) pal = ThemePalette.Resolve(ThemePreference.Dark, true);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color chartBg = pal.IsRadeon
                ? Color.FromArgb(0x0D, 0x06, 0x08)
                : (pal.IsDark ? Color.FromArgb(0x11, 0x11, 0x1B) : Color.FromArgb(0xF1, 0xF5, 0xF9));
            Color gridCol = pal.IsRadeon
                ? Color.FromArgb(55, 0xED, 0x1C, 0x24)
                : (pal.IsDark ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(45, 0, 0, 0));

            if (showLabels)
            {
                using (SolidBrush bgBr = new SolidBrush(chartBg))
                {
                    g.FillRectangle(bgBr, rect);
                }
                using (Pen gridPen = new Pen(gridCol, 1f))
                {
                    int midY = rect.Y + rect.Height / 2;
                    g.DrawLine(gridPen, rect.X + 1, midY, rect.Right - 1, midY);
                    g.DrawRectangle(gridPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                }
            }

            if (samples == null || samples.Count == 0) return;

            int minVal = int.MaxValue;
            int maxVal = int.MinValue;
            for (int i = 0; i < samples.Count; i++)
            {
                int v = samples[i];
                if (v < minVal) minVal = v;
                if (v > maxVal) maxVal = v;
            }

            float scaleMin = minVal - 1.5f;
            float scaleMax = maxVal + 1.5f;
            if (scaleMax - scaleMin < 4f)
            {
                float mid = (minVal + maxVal) * 0.5f;
                scaleMin = mid - 2f;
                scaleMax = mid + 2f;
            }

            int padX = showLabels ? 4 : 1;
            int padTop = showLabels ? 14 : 3;
            int padBot = showLabels ? 4 : 2;
            float plotX = rect.X + padX;
            float plotW = Math.Max(4, rect.Width - padX * 2);
            float plotY = rect.Y + padTop;
            float plotH = Math.Max(4, rect.Height - padTop - padBot);

            int count = samples.Count;
            PointF[] pts = new PointF[Math.Max(2, count)];
            if (count == 1)
            {
                float y = plotY + plotH * 0.5f;
                pts[0] = new PointF(plotX, y);
                pts[1] = new PointF(plotX + plotW, y);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    float x = plotX + (plotW * i) / (count - 1);
                    float norm = (samples[i] - scaleMin) / (scaleMax - scaleMin);
                    norm = Math.Max(0f, Math.Min(1f, norm));
                    float y = plotY + plotH * (1f - norm);
                    pts[i] = new PointF(x, y);
                }
            }

            PointF[] poly = new PointF[pts.Length + 2];
            Array.Copy(pts, 0, poly, 0, pts.Length);
            poly[pts.Length] = new PointF(pts[pts.Length - 1].X, rect.Bottom - 1);
            poly[pts.Length + 1] = new PointF(pts[0].X, rect.Bottom - 1);

            using (LinearGradientBrush fillBr = new LinearGradientBrush(
                new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height)),
                Color.FromArgb(showLabels ? 115 : 85, lineColor),
                Color.FromArgb(8, lineColor),
                LinearGradientMode.Vertical))
            {
                g.FillPolygon(fillBr, poly);
            }

            using (Pen linePen = new Pen(lineColor, showLabels ? 1.8f : 1.4f))
            {
                linePen.LineJoin = LineJoin.Round;
                g.DrawLines(linePen, pts);
            }

            PointF lastPt = pts[pts.Length - 1];
            using (SolidBrush dotBr = new SolidBrush(lineColor))
            {
                float r = showLabels ? 3f : 2f;
                g.FillEllipse(dotBr, lastPt.X - r, lastPt.Y - r, r * 2f, r * 2f);
            }

            if (showLabels)
            {
                Color labelFg = pal.IsDark ? Color.FromArgb(215, 255, 255, 255) : Color.FromArgb(215, 15, 23, 42);
                int curVal = samples[samples.Count - 1];
                string header = isTenths
                    ? string.Format("LIVE GRAPH ({0:0.0}–{1:0.0}{2}) • NOW {3:0.0}{2}", minVal / 10.0, maxVal / 10.0, unit, curVal / 10.0)
                    : string.Format("LIVE GRAPH ({0}–{1}{2}) • NOW {3}{2}", minVal, maxVal, unit, curVal);
                TextRenderer.DrawText(g, header, ToolTipChartFont, new Point(rect.X + 4, rect.Y + 1), labelFg);
            }
        }

        private static Font CreateTabularMetricFont(float ptSize)
        {
            try
            {
                using (FontFamily probe = new FontFamily("Cascadia Mono"))
                {
                    if (probe != null) return new Font("Cascadia Mono", ptSize, FontStyle.Bold);
                }
            }
            catch { }
            try
            {
                return new Font("Consolas", ptSize, FontStyle.Bold);
            }
            catch
            {
                return new Font("Segoe UI", ptSize, FontStyle.Bold);
            }
        }
    }
}
