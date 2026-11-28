using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MiniFanControl
{
    /// <summary>
    /// Zero-dependency Windows 11 DWM frame styling and crisp WinForms card/button polish.
    /// Safely no-ops on older Windows builds, headless CI, or High Contrast mode.
    /// </summary>
    public static class Win11VisualStyle
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);

        /// <summary>
        /// Applies native Windows 10/11 dark scrollbars and explorer chrome to scrollable panels, sliders, and inputs.
        /// Safely no-ops on unsupported OS builds or High Contrast mode.
        /// </summary>
        public static void ApplyDarkExplorerTheme(Control ctrl, bool isDark)
        {
            if (ctrl == null || ctrl.IsDisposed || SystemInformation.HighContrast) return;
            Action apply = () =>
            {
                if (ctrl.IsDisposed || !ctrl.IsHandleCreated) return;
                try
                {
                    SetWindowTheme(ctrl.Handle, isDark ? "DarkMode_Explorer" : "Explorer", null);
                }
                catch { }
            };

            if (ctrl.IsHandleCreated)
            {
                apply();
            }
            else
            {
                EventHandler onCreated = null;
                onCreated = (s, e) =>
                {
                    ctrl.HandleCreated -= onCreated;
                    apply();
                };
                ctrl.HandleCreated += onCreated;
            }
        }

        /// <summary>
        /// Applies native Windows 10/11 dark dropdown chrome ("DarkMode_CFD") to a WinForms ComboBox.
        /// </summary>
        public static void ApplyDarkComboTheme(ComboBox cb, ThemePalette palette)
        {
            if (cb == null || cb.IsDisposed || palette == null) return;
            if (palette.IsHighContrast)
            {
                cb.BackColor = SystemColors.Window;
                cb.ForeColor = SystemColors.WindowText;
                return;
            }

            cb.FlatStyle = FlatStyle.Standard;
            cb.BackColor = palette.Surface2;
            cb.ForeColor = palette.Text;

            Action applyTheme = () =>
            {
                if (cb.IsDisposed || !cb.IsHandleCreated) return;
                try
                {
                    SetWindowTheme(cb.Handle, palette.IsDark ? "DarkMode_CFD" : "Explorer", null);
                }
                catch { }
            };

            if (cb.IsHandleCreated)
            {
                applyTheme();
            }
            else
            {
                EventHandler onCreated = null;
                onCreated = (s, e) =>
                {
                    cb.HandleCreated -= onCreated;
                    applyTheme();
                };
                cb.HandleCreated += onCreated;
            }
            cb.Invalidate();
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> _themedControlHooks =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Control, object>();

        /// <summary>
        /// Attaches a theme-aware owner-draw renderer to a DropDownList ComboBox so dark themes never flash stark white items.
        /// Safe to call repeatedly; hooks are attached once per ComboBox instance.
        /// </summary>
        public static void EnableThemedComboBox(ComboBox cb, Func<ThemePalette> paletteProvider)
        {
            if (cb == null || paletteProvider == null) return;
            object existing;
            if (_themedControlHooks.TryGetValue(cb, out existing)) return;
            _themedControlHooks.Add(cb, new object());

            cb.FlatStyle = FlatStyle.Standard;
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            if (cb.ItemHeight < 18) cb.ItemHeight = 18;
            cb.DrawItem += (s, e) =>
            {
                if (e.Index < 0 || e.Index >= cb.Items.Count) return;
                ThemePalette pal = paletteProvider();
                if (pal == null || pal.IsHighContrast)
                {
                    e.DrawBackground();
                    string txtHc = cb.GetItemText(cb.Items[e.Index]);
                    TextRenderer.DrawText(e.Graphics, txtHc, cb.Font, e.Bounds,
                        (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : SystemColors.WindowText,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    return;
                }

                bool isEditBox = (e.State & DrawItemState.ComboBoxEdit) != 0;
                bool isSelected = !isEditBox && (e.State & DrawItemState.Selected) != 0;
                bool isDisabled = !cb.Enabled;

                Color itemBg = isEditBox
                    ? pal.Surface2
                    : (isSelected ? Blend(pal.Surface, pal.Accent, pal.IsDark ? 0.30f : 0.18f) : pal.Surface);
                Color itemFg = isDisabled
                    ? pal.TextDim
                    : (isSelected ? (pal.IsDark ? Color.White : pal.Text) : pal.Text);

                using (SolidBrush bgBr = new SolidBrush(itemBg))
                {
                    e.Graphics.FillRectangle(bgBr, e.Bounds);
                }

                if (isSelected)
                {
                    using (SolidBrush barBr = new SolidBrush(pal.Accent))
                    {
                        e.Graphics.FillRectangle(barBr, e.Bounds.X, e.Bounds.Y + 2, 3, Math.Max(4, e.Bounds.Height - 4));
                    }
                }

                Rectangle textRect = new Rectangle(e.Bounds.X + (isEditBox ? 4 : 7), e.Bounds.Y, Math.Max(10, e.Bounds.Width - 10), e.Bounds.Height);
                string txt = cb.GetItemText(cb.Items[e.Index]);
                TextRenderer.DrawText(e.Graphics, txt, cb.Font, textRect, itemFg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }

        private sealed class CheckBoxHoverState
        {
            public bool Hovered;
        }

        /// <summary>
        /// Replaces stark white Win32 unchecked squares and unreadable etched disabled text with crisp Win11-styled checkboxes.
        /// Preserves full native keyboard, accessibility, and High Contrast behavior.
        /// </summary>
        public static void EnableThemedCheckBox(CheckBox cb, Func<ThemePalette> paletteProvider)
        {
            if (cb == null || paletteProvider == null) return;
            object existing;
            if (_themedControlHooks.TryGetValue(cb, out existing)) return;
            CheckBoxHoverState state = new CheckBoxHoverState();
            _themedControlHooks.Add(cb, state);

            cb.MouseEnter += (s, e) => { state.Hovered = true; cb.Invalidate(); };
            cb.MouseLeave += (s, e) => { state.Hovered = false; cb.Invalidate(); };
            cb.GotFocus += (s, e) => cb.Invalidate();
            cb.LostFocus += (s, e) => cb.Invalidate();

            cb.Paint += (s, e) =>
            {
                if (SystemInformation.HighContrast) return;
                ThemePalette pal = paletteProvider();
                if (pal == null || pal.IsHighContrast) return;

                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                using (SolidBrush bgBr = new SolidBrush(cb.BackColor))
                {
                    g.FillRectangle(bgBr, cb.ClientRectangle);
                }

                const int boxSize = 14;
                int boxX = 1;
                int boxY = Math.Max(1, (cb.Height - boxSize) / 2);
                Rectangle boxRect = new Rectangle(boxX, boxY, boxSize, boxSize);

                bool enabled = cb.Enabled;
                bool isChecked = cb.Checked;

                if (isChecked)
                {
                    Color fillCol = enabled ? pal.Accent : Blend(pal.Surface2, pal.Accent, 0.40f);
                    using (SolidBrush fillBr = new SolidBrush(fillCol))
                    {
                        FillRoundedRect(g, fillBr, boxRect, 3);
                    }
                    Color checkCol = enabled ? pal.ActiveFg : Blend(pal.TextDim, pal.Surface, 0.25f);
                    using (Pen checkPen = new Pen(checkCol, 1.85f))
                    {
                        checkPen.StartCap = LineCap.Round;
                        checkPen.EndCap = LineCap.Round;
                        checkPen.LineJoin = LineJoin.Round;
                        g.DrawLines(checkPen, new PointF[]
                        {
                            new PointF(boxX + 3.2f, boxY + 7.2f),
                            new PointF(boxX + 5.9f, boxY + 10.0f),
                            new PointF(boxX + 10.8f, boxY + 4.2f)
                        });
                    }
                }
                else
                {
                    Color wellBg = enabled
                        ? (state.Hovered ? Blend(pal.TileBg, pal.Accent, pal.IsDark ? 0.16f : 0.10f) : pal.TileBg)
                        : Blend(pal.Surface, pal.TileBg, 0.50f);
                    Color bdrCol = !enabled
                        ? Blend(pal.Border, pal.Surface, 0.40f)
                        : (state.Hovered || cb.Focused ? pal.Accent : Blend(pal.Border, pal.TextDim, pal.IsDark ? 0.35f : 0.20f));
                    using (SolidBrush wellBr = new SolidBrush(wellBg))
                    {
                        FillRoundedRect(g, wellBr, boxRect, 3);
                    }
                    DrawRoundedCardBorder(g, new Rectangle(boxX, boxY, boxSize + 1, boxSize + 1), bdrCol, 3, (state.Hovered || cb.Focused) ? 1.25f : 1f);
                }

                if (cb.Focused)
                {
                    DrawRoundedCardBorder(g, new Rectangle(0, Math.Max(0, boxY - 1), boxSize + 3, boxSize + 3),
                        Color.FromArgb(140, pal.Accent), 4, 1f);
                }

                Color textCol = enabled ? pal.Text : Blend(pal.TextDim, pal.Surface, 0.32f);
                Rectangle textRect = new Rectangle(boxX + boxSize + 6, 0, Math.Max(8, cb.Width - (boxX + boxSize + 7)), cb.Height);
                TextRenderer.DrawText(g, cb.Text, cb.Font, textRect, textCol,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }

        /// <summary>
        /// Converts a System.Drawing.Color to a Win32 COLORREF (0x00BBGGRR).
        /// </summary>
        public static int ToColorRef(Color c)
        {
            return (c.B << 16) | (c.G << 8) | c.R;
        }

        /// <summary>
        /// Applies native Windows 11 DWM dark titlebar, rounded window corners, and theme-matched caption/border colors.
        /// </summary>
        public static bool ApplyWindowFrame(Form form, bool isDark, Color captionColor, Color textColor, Color borderColor)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return false;
            if (SystemInformation.HighContrast) return false;

            try
            {
                IntPtr hwnd = form.Handle;
                int darkFlag = isDark ? 1 : 0;
                int hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int));
                if (hr != 0)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref darkFlag, sizeof(int));
                }

                // Windows 11 (Build 22000+) rounded corners, caption color, text color, and subtle border tint
                int cornerPref = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

                int capRef = ToColorRef(captionColor);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref capRef, sizeof(int));

                int txtRef = ToColorRef(textColor);
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref txtRef, sizeof(int));

                int bdrRef = ToColorRef(borderColor);
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref bdrRef, sizeof(int));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a rounded rectangle GraphicsPath clamped safely for small rectangles.
        /// </summary>
        public static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            int maxR = Math.Min(rect.Width, rect.Height) / 2;
            int r = Math.Max(0, Math.Min(radius, maxR));
            if (r <= 1)
            {
                path.AddRectangle(rect);
                return path;
            }

            int d = r * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Draws a crisp anti-aliased rounded hairline border around a card or panel.
        /// </summary>
        public static void DrawRoundedCardBorder(Graphics g, Rectangle bounds, Color borderColor, int radius, float penWidth)
        {
            if (g == null || bounds.Width < 4 || bounds.Height < 4) return;
            SmoothingMode prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                Rectangle r = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                using (GraphicsPath path = CreateRoundedRectPath(r, radius))
                using (Pen pen = new Pen(borderColor, penWidth))
                {
                    g.DrawPath(pen, path);
                }
            }
            finally
            {
                g.SmoothingMode = prev;
            }
        }

        /// <summary>
        /// Blends two colors cleanly by factor t in [0..1].
        /// </summary>
        public static Color Blend(Color baseColor, Color tintColor, float t)
        {
            if (t <= 0f) return baseColor;
            if (t >= 1f) return tintColor;
            int r = (int)Math.Round(baseColor.R + (tintColor.R - baseColor.R) * t);
            int g = (int)Math.Round(baseColor.G + (tintColor.G - baseColor.G) * t);
            int b = (int)Math.Round(baseColor.B + (tintColor.B - baseColor.B) * t);
            return Color.FromArgb(
                Math.Max(0, Math.Min(255, r)),
                Math.Max(0, Math.Min(255, g)),
                Math.Max(0, Math.Min(255, b)));
        }

        /// <summary>
        /// Fills a rounded rectangle smoothly using anti-aliased GDI+.
        /// </summary>
        public static void FillRoundedRect(Graphics g, Brush brush, Rectangle bounds, int radius)
        {
            if (g == null || brush == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            SmoothingMode prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (GraphicsPath path = CreateRoundedRectPath(bounds, radius))
                {
                    g.FillPath(brush, path);
                }
            }
            finally
            {
                g.SmoothingMode = prev;
            }
        }

        /// <summary>
        /// Applies crisp Win11-style hairline border and hover/pressed feedback to a flat WinForms button.
        /// </summary>
        public static void StyleModernButton(Button btn, Color bg, Color fg, Color border, bool isDark)
        {
            if (btn == null) return;
            btn.FlatStyle = FlatStyle.Flat;
            btn.BackColor = bg;
            btn.ForeColor = fg;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = border;
            btn.FlatAppearance.MouseOverBackColor = Blend(bg, isDark ? Color.White : Color.Black, isDark ? 0.11f : 0.06f);
            btn.FlatAppearance.MouseDownBackColor = Blend(bg, isDark ? Color.White : Color.Black, isDark ? 0.19f : 0.12f);
        }

        /// <summary>
        /// Applies cohesive active/inactive pill/segment styling with crisp borders and hover states.
        /// </summary>
        public static void StylePillButton(Button btn, ThemePalette palette, bool active, Color? activeAccent = null, Color? activeFgOverride = null)
        {
            if (btn == null || palette == null) return;
            if (palette.IsHighContrast)
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.BackColor = active ? SystemColors.Highlight : SystemColors.Control;
                btn.ForeColor = active ? SystemColors.HighlightText : SystemColors.ControlText;
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.BorderColor = SystemColors.WindowFrame;
                return;
            }

            Color accent = activeAccent.HasValue ? activeAccent.Value : palette.Accent;
            Color fg = active ? (activeFgOverride.HasValue ? activeFgOverride.Value : palette.ActiveFg) : palette.Text;
            Color bg = active ? accent : palette.Surface2;
            Color bdr = active ? Blend(accent, palette.IsDark ? Color.White : Color.Black, 0.20f) : palette.Border;
            StyleModernButton(btn, bg, fg, bdr, palette.IsDark || active);
        }

        /// <summary>
        /// Renders a 12x12 rounded color swatch bitmap for theme selector menus.
        /// </summary>
        public static Bitmap CreateColorSwatch(Color fill, Color accent, Color border)
        {
            Bitmap bmp = new Bitmap(14, 14);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                Rectangle outer = new Rectangle(1, 1, 11, 11);
                using (SolidBrush br = new SolidBrush(fill))
                {
                    FillRoundedRect(g, br, outer, 3);
                }
                using (SolidBrush accBr = new SolidBrush(accent))
                {
                    g.FillEllipse(accBr, 4, 4, 6, 6);
                }
                DrawRoundedCardBorder(g, new Rectangle(1, 1, 12, 12), border, 3, 1f);
            }
            return bmp;
        }

        /// <summary>
        /// Applies a matching dark/light/themed renderer to ContextMenuStrip popups (replaces stark white WinForms default).
        /// </summary>
        public static void ApplyThemeToMenu(ContextMenuStrip menu, ThemePalette palette)
        {
            if (menu == null || palette == null || palette.IsHighContrast) return;
            menu.Renderer = new ThemedToolStripRenderer(palette);
            menu.BackColor = palette.Surface;
            menu.ForeColor = palette.Text;
            ApplyMenuItemColors(menu.Items, palette);
        }

        private static void ApplyMenuItemColors(ToolStripItemCollection items, ThemePalette palette)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                ToolStripItem item = items[i];
                item.BackColor = palette.Surface;
                item.ForeColor = palette.Text;
                ToolStripMenuItem mi = item as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems)
                {
                    mi.DropDown.BackColor = palette.Surface;
                    mi.DropDown.ForeColor = palette.Text;
                    ApplyMenuItemColors(mi.DropDownItems, palette);
                }
            }
        }

        private sealed class ThemedColorTable : ProfessionalColorTable
        {
            private readonly ThemePalette _p;
            private readonly Color _hover;

            public ThemedColorTable(ThemePalette p)
            {
                _p = p;
                _hover = Blend(p.Surface, p.Accent, p.IsDark ? 0.24f : 0.14f);
            }

            public override Color ToolStripDropDownBackground { get { return _p.Surface; } }
            public override Color ImageMarginGradientBegin { get { return _p.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return _p.Surface; } }
            public override Color ImageMarginGradientEnd { get { return _p.Surface; } }
            public override Color MenuBorder { get { return _p.Border; } }
            public override Color MenuItemBorder { get { return Blend(_p.Border, _p.Accent, 0.55f); } }
            public override Color MenuItemSelected { get { return _hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return _hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return _hover; } }
            public override Color MenuItemPressedGradientBegin { get { return _hover; } }
            public override Color MenuItemPressedGradientEnd { get { return _hover; } }
            public override Color SeparatorDark { get { return _p.Border; } }
            public override Color SeparatorLight { get { return _p.Surface; } }
            public override Color CheckBackground { get { return Blend(_p.Surface2, _p.Accent, 0.35f); } }
            public override Color CheckSelectedBackground { get { return Blend(_p.Surface2, _p.Accent, 0.45f); } }
            public override Color CheckPressedBackground { get { return Blend(_p.Surface2, _p.Accent, 0.50f); } }
        }

        private sealed class ThemedToolStripRenderer : ToolStripProfessionalRenderer
        {
            private readonly ThemePalette _p;

            public ThemedToolStripRenderer(ThemePalette p) : base(new ThemedColorTable(p))
            {
                _p = p;
                RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? _p.Text : _p.TextDim;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = _p.TextDim;
                base.OnRenderArrow(e);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                Rectangle r = new Rectangle(e.ImageRectangle.X + 2, e.ImageRectangle.Y + 2, e.ImageRectangle.Width - 4, e.ImageRectangle.Height - 4);
                using (SolidBrush bg = new SolidBrush(Blend(_p.Surface2, _p.Accent, 0.28f)))
                {
                    FillRoundedRect(e.Graphics, bg, r, 3);
                }
                SmoothingMode prev = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                try
                {
                    using (Pen checkPen = new Pen(_p.Accent, 1.8f))
                    {
                        checkPen.StartCap = LineCap.Round;
                        checkPen.EndCap = LineCap.Round;
                        int cx = r.X + r.Width / 2;
                        int cy = r.Y + r.Height / 2;
                        e.Graphics.DrawLine(checkPen, cx - 3, cy, cx - 1, cy + 3);
                        e.Graphics.DrawLine(checkPen, cx - 1, cy + 3, cx + 4, cy - 3);
                    }
                }
                finally
                {
                    e.Graphics.SmoothingMode = prev;
                }
            }
        }

        /// <summary>
        /// Draws a crisp 6-point crystalline vector snowflake watermark for the White/Black/Red Xmas theme.
        /// </summary>
        public static void DrawSnowflake(Graphics g, float cx, float cy, float radius, Color color, float penWidth = 1.25f)
        {
            if (g == null || radius < 3f) return;
            SmoothingMode prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (Pen pen = new Pen(color, penWidth))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    for (int i = 0; i < 6; i++)
                    {
                        double rad = (i * 60.0 - 90.0) * Math.PI / 180.0;
                        float dx = (float)Math.Cos(rad);
                        float dy = (float)Math.Sin(rad);
                        float x2 = cx + dx * radius;
                        float y2 = cy + dy * radius;
                        g.DrawLine(pen, cx, cy, x2, y2);

                        // Delicate crystalline V-branches at 62% along each spoke
                        float bx = cx + dx * (radius * 0.62f);
                        float by = cy + dy * (radius * 0.62f);
                        float branchLen = radius * 0.30f;
                        double a1 = rad + 0.62;
                        double a2 = rad - 0.62;
                        g.DrawLine(pen, bx, by, bx + (float)Math.Cos(a1) * branchLen, by + (float)Math.Sin(a1) * branchLen);
                        g.DrawLine(pen, bx, by, bx + (float)Math.Cos(a2) * branchLen, by + (float)Math.Sin(a2) * branchLen);
                    }
                    // Inner crystalline hexagon ring
                    float innerR = radius * 0.28f;
                    PointF[] hex = new PointF[6];
                    for (int i = 0; i < 6; i++)
                    {
                        double rad = (i * 60.0 - 90.0) * Math.PI / 180.0;
                        hex[i] = new PointF(cx + (float)Math.Cos(rad) * innerR, cy + (float)Math.Sin(rad) * innerR);
                    }
                    g.DrawPolygon(pen, hex);
                }
            }
            finally
            {
                g.SmoothingMode = prev;
            }
        }
    }
}
