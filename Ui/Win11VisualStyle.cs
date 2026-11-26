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
