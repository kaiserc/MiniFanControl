using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MiniFanControl
{
    /// <summary>
    /// Crisp, double-buffered Win11-style slider replacing legacy Win32 TrackBar.
    /// Supports optional anti-stall floor zone markers, live drag value callout,
    /// full keyboard accessibility (Arrow/Page/Home/End), mouse-wheel, and High Contrast mode.
    /// </summary>
    public class ModernSlider : Control
    {
        private int _minimum = 0;
        private int _maximum = 100;
        private int _value = 0;
        private int _floorThreshold = -1;
        private bool _dragging = false;
        private bool _hovered = false;
        private Func<ThemePalette> _paletteProvider;
        private Color? _accentOverride;
        private string _zeroLabel = null;
        private string _unitSuffix = "%";

        private static readonly Font CalloutFont = new Font("Segoe UI", 7.25f, FontStyle.Bold);

        public event EventHandler Scroll;
        public event EventHandler ValueCommitted;

        public ModernSlider()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.Slider;
            Size = new Size(200, 32);
        }

        public int Minimum
        {
            get { return _minimum; }
            set
            {
                _minimum = value;
                if (_maximum < _minimum) _maximum = _minimum;
                if (_value < _minimum) _value = _minimum;
                SyncAccessibility();
                Invalidate();
            }
        }

        public int Maximum
        {
            get { return _maximum; }
            set
            {
                _maximum = value;
                if (_minimum > _maximum) _minimum = _maximum;
                if (_value > _maximum) _value = _maximum;
                SyncAccessibility();
                Invalidate();
            }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                if (_value != clamped)
                {
                    _value = clamped;
                    SyncAccessibility();
                    Invalidate();
                }
            }
        }

        /// <summary>
        /// Optional calibrated anti-stall floor value (e.g., 22 for 22%). Values below 0 disable the marker.
        /// </summary>
        public int FloorThreshold
        {
            get { return _floorThreshold; }
            set
            {
                if (_floorThreshold != value)
                {
                    _floorThreshold = value;
                    Invalidate();
                }
            }
        }

        /// <summary>
        /// Optional label displayed for Value == 0 (e.g. "Auto"). Null renders numeric value with UnitSuffix.
        /// </summary>
        public string ZeroLabel
        {
            get { return _zeroLabel; }
            set { _zeroLabel = value; SyncAccessibility(); Invalidate(); }
        }

        public string UnitSuffix
        {
            get { return _unitSuffix; }
            set { _unitSuffix = value ?? ""; SyncAccessibility(); Invalidate(); }
        }

        public Color? AccentOverride
        {
            get { return _accentOverride; }
            set { _accentOverride = value; Invalidate(); }
        }

        public Func<ThemePalette> PaletteProvider
        {
            get { return _paletteProvider; }
            set { _paletteProvider = value; Invalidate(); }
        }

        private string FormatValueText(int v)
        {
            if (v == 0 && !string.IsNullOrEmpty(_zeroLabel)) return _zeroLabel;
            return v.ToString() + (_unitSuffix ?? "");
        }

        private void SyncAccessibility()
        {
            AccessibleDescription = FormatValueText(_value);
        }

        private RectangleF GetTrackRect()
        {
            const float padX = 10f;
            float trackH = 6f;
            float trackY = (Height - trackH) / 2f;
            float trackW = Math.Max(16f, Width - padX * 2f);
            return new RectangleF(padX, trackY, trackW, trackH);
        }

        private float ValueToX(int val, RectangleF track)
        {
            int span = Math.Max(1, _maximum - _minimum);
            float t = (float)(val - _minimum) / span;
            t = Math.Max(0f, Math.Min(1f, t));
            return track.Left + t * track.Width;
        }

        private int XToValue(int x, RectangleF track)
        {
            float t = (x - track.Left) / Math.Max(1f, track.Width);
            t = Math.Max(0f, Math.Min(1f, t));
            int raw = (int)Math.Round(_minimum + t * (_maximum - _minimum));
            return Math.Max(_minimum, Math.Min(_maximum, raw));
        }

        private void SetInteractiveValue(int newVal, bool commit)
        {
            int clamped = Math.Max(_minimum, Math.Min(_maximum, newVal));
            bool changed = (_value != clamped);
            if (changed)
            {
                _value = clamped;
                SyncAccessibility();
                Invalidate();
                if (Scroll != null) Scroll(this, EventArgs.Empty);
            }
            if (commit && ValueCommitted != null)
            {
                ValueCommitted(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            Focus();
            _dragging = true;
            Capture = true;
            int v = XToValue(e.X, GetTrackRect());
            if (v != _value)
            {
                _value = v;
                SyncAccessibility();
                if (Scroll != null) Scroll(this, EventArgs.Empty);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || !Enabled) return;
            int v = XToValue(e.X, GetTrackRect());
            if (v != _value)
            {
                _value = v;
                SyncAccessibility();
                if (Scroll != null) Scroll(this, EventArgs.Empty);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
                Invalidate();
                if (ValueCommitted != null) ValueCommitted(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (_dragging)
            {
                _dragging = false;
                Invalidate();
                if (ValueCommitted != null) ValueCommitted(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Enabled) return;
            int step = e.Delta > 0 ? 5 : -5;
            SetInteractiveValue(_value + step, true);
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
                case Keys.Home:
                case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled) return;
            int next = _value;
            bool handled = true;
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Down:
                    next = _value - (e.Shift ? 5 : 1);
                    break;
                case Keys.Right:
                case Keys.Up:
                    next = _value + (e.Shift ? 5 : 1);
                    break;
                case Keys.PageDown:
                    next = _value - 10;
                    break;
                case Keys.PageUp:
                    next = _value + 10;
                    break;
                case Keys.Home:
                    next = _minimum;
                    break;
                case Keys.End:
                    next = _maximum;
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                e.Handled = true;
                SetInteractiveValue(next, false);
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (!Enabled) return;
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Home:
                case Keys.End:
                    if (ValueCommitted != null) ValueCommitted(this, EventArgs.Empty);
                    break;
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            ThemePalette pal = _paletteProvider != null ? _paletteProvider() : ThemePalette.Resolve(ThemePreference.Dark, true);
            bool hc = pal.IsHighContrast || SystemInformation.HighContrast;

            using (SolidBrush bgBr = new SolidBrush(BackColor))
            {
                g.FillRectangle(bgBr, ClientRectangle);
            }

            RectangleF trackF = GetTrackRect();
            Rectangle track = new Rectangle(
                (int)Math.Round(trackF.X),
                (int)Math.Round(trackF.Y),
                Math.Max(8, (int)Math.Round(trackF.Width)),
                Math.Max(4, (int)Math.Round(trackF.Height)));

            Color accent = hc
                ? SystemColors.Highlight
                : (_accentOverride.HasValue ? _accentOverride.Value : pal.Accent);
            if (!Enabled)
            {
                accent = hc ? SystemColors.GrayText : Win11VisualStyle.Blend(pal.Surface2, pal.TextDim, 0.35f);
            }

            Color trackBg = hc
                ? SystemColors.Window
                : (pal.IsDark ? pal.TileBg : Win11VisualStyle.Blend(pal.Surface2, pal.Border, 0.35f));
            Color trackBorder = hc ? SystemColors.WindowFrame : pal.Border;

            // 1. Base recessed track rail
            using (SolidBrush railBr = new SolidBrush(trackBg))
            {
                Win11VisualStyle.FillRoundedRect(g, railBr, track, 3);
            }

            // 2. Optional calibrated Anti-Stall Floor zone tint & tick mark
            if (!hc && _floorThreshold > _minimum && _floorThreshold < _maximum)
            {
                float floorX = ValueToX(_floorThreshold, trackF);
                int floorW = Math.Max(4, (int)Math.Round(floorX - track.Left));
                Rectangle floorRect = new Rectangle(track.Left, track.Top, Math.Min(track.Width, floorW), track.Height);
                Color zoneCol = Color.FromArgb(Enabled ? (pal.IsDark ? 55 : 42) : 24, pal.Warning);
                using (SolidBrush zoneBr = new SolidBrush(zoneCol))
                {
                    Win11VisualStyle.FillRoundedRect(g, zoneBr, floorRect, 3);
                }

                Color tickCol = Color.FromArgb(Enabled ? 190 : 85, pal.Warning);
                using (Pen tickPen = new Pen(tickCol, 1.4f))
                {
                    g.DrawLine(tickPen, floorX, track.Top - 3, floorX, track.Bottom + 3);
                }
            }

            // 3. Active progress fill
            float thumbX = ValueToX(_value, trackF);
            int fillW = (int)Math.Round(thumbX - track.Left);
            if (fillW > 2)
            {
                Rectangle fillRect = new Rectangle(track.Left, track.Top, Math.Min(track.Width, fillW), track.Height);
                bool inFloorZone = (_floorThreshold > _minimum && _value > 0 && _value < _floorThreshold);
                Color fillCol = (!hc && inFloorZone) ? pal.Warning : accent;
                using (SolidBrush fillBr = new SolidBrush(fillCol))
                {
                    Win11VisualStyle.FillRoundedRect(g, fillBr, fillRect, 3);
                }
            }

            // Crisp hairline border on track
            Win11VisualStyle.DrawRoundedCardBorder(g, new Rectangle(track.X, track.Y, track.Width + 1, track.Height + 1), trackBorder, 3, 1f);

            // 4. Thumb with hover/drag halo
            float thumbCy = trackF.Top + trackF.Height / 2f;
            float thumbR = (_dragging || _hovered) && Enabled ? 7.5f : 6.5f;

            if ((_dragging || _hovered || Focused) && Enabled && !hc)
            {
                float haloR = thumbR + (_dragging ? 4.0f : 2.8f);
                using (SolidBrush haloBr = new SolidBrush(Color.FromArgb(_dragging ? 65 : 38, accent)))
                {
                    g.FillEllipse(haloBr, thumbX - haloR, thumbCy - haloR, haloR * 2f, haloR * 2f);
                }
            }

            Color thumbFill = hc
                ? SystemColors.Highlight
                : (!Enabled
                    ? pal.Surface2
                    : (_value == 0 && !string.IsNullOrEmpty(_zeroLabel)
                        ? Win11VisualStyle.Blend(pal.Surface2, pal.TextDim, 0.35f)
                        : accent));
            Color thumbRing = hc
                ? SystemColors.WindowFrame
                : (pal.IsDark ? Color.FromArgb(230, 248, 250, 252) : Color.White);

            using (SolidBrush outerBr = new SolidBrush(thumbRing))
            {
                g.FillEllipse(outerBr, thumbX - thumbR, thumbCy - thumbR, thumbR * 2f, thumbR * 2f);
            }
            float innerR = Math.Max(3.0f, thumbR - 2.0f);
            using (SolidBrush innerBr = new SolidBrush(thumbFill))
            {
                g.FillEllipse(innerBr, thumbX - innerR, thumbCy - innerR, innerR * 2f, innerR * 2f);
            }

            if (Focused)
            {
                Rectangle focusRect = new Rectangle(1, 1, Math.Max(2, Width - 3), Math.Max(2, Height - 3));
                ControlPaint.DrawFocusRectangle(g, focusRect);
            }

            // 5. Compact live value pill while dragging
            if (_dragging && Enabled && Height >= 26)
            {
                string tip = FormatValueText(_value);
                Size sz = TextRenderer.MeasureText(g, tip, CalloutFont, new Size(80, 16), TextFormatFlags.NoPadding);
                int pw = sz.Width + 8;
                int ph = 14;
                int px = (int)Math.Round(Math.Max(2f, Math.Min(Width - pw - 2f, thumbX - pw / 2f)));
                int py = 0;
                Rectangle pillRect = new Rectangle(px, py, pw, ph);
                using (SolidBrush pBg = new SolidBrush(hc ? SystemColors.Info : pal.TooltipBg))
                {
                    Win11VisualStyle.FillRoundedRect(g, pBg, pillRect, 3);
                }
                Win11VisualStyle.DrawRoundedCardBorder(g, pillRect, hc ? SystemColors.WindowFrame : accent, 3, 1f);
                TextRenderer.DrawText(g, tip, CalloutFont, pillRect, hc ? SystemColors.InfoText : pal.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
