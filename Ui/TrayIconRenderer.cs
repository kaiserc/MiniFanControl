using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MiniFanControl
{
    /// <summary>
    /// Dedicated GDI+ high-contrast taskbar temperature badge renderer and Win32 HICON handle manager.
    /// Extracted from MainForm to isolate native icon lifetime management and enable direct unit testing.
    /// </summary>
    public static class TrayIconRenderer
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon RenderHighContrastTempIcon(int temp, bool darkBackground, bool isGpu, out IntPtr rawHandle)
        {
            Color bg = darkBackground ? Color.Black : Color.White;
            Color fg = darkBackground ? Color.White : Color.Black;
            Color barColor = isGpu
                ? (darkBackground ? Color.FromArgb(0x22, 0xC5, 0x5E) : Color.FromArgb(0x15, 0x80, 0x3D))
                : (darkBackground ? Color.FromArgb(0x38, 0xBD, 0xF8) : Color.FromArgb(0x1D, 0x4E, 0xD8));

            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(bg);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                using (Brush barBrush = new SolidBrush(barColor))
                {
                    g.FillRectangle(barBrush, 0, 29, 32, 3);
                }

                string label = temp > 0 ? temp.ToString() : "--";
                float fontSize = label.Length >= 3 ? 15f : 21.5f;

                using (Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush textBrush = new SolidBrush(fg))
                using (StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap
                })
                {
                    g.DrawString(label, font, textBrush, new RectangleF(-1f, -1f, 34f, 30f), sf);
                }

                rawHandle = bmp.GetHicon();
                return (Icon)Icon.FromHandle(rawHandle).Clone();
            }
        }

        public static void UpdateIconSlot(
            NotifyIcon trayIcon,
            Icon formIcon,
            int temp,
            bool darkBackground,
            bool isGpu,
            ref int lastKey,
            ref IntPtr rawHandle)
        {
            int key = (temp & 0xFFFF) | (darkBackground ? 0x10000 : 0);
            if (key == lastKey && rawHandle != IntPtr.Zero) return;

            lastKey = key;
            IntPtr newHandle;
            Icon rendered = RenderHighContrastTempIcon(temp, darkBackground, isGpu, out newHandle);
            if (trayIcon != null)
            {
                Icon oldIcon = trayIcon.Icon;
                trayIcon.Icon = rendered;
                if (oldIcon != null && oldIcon != formIcon && oldIcon != SystemIcons.Application)
                {
                    try { oldIcon.Dispose(); } catch { }
                }
            }
            else if (rendered != null && rendered != formIcon && rendered != SystemIcons.Application)
            {
                try { rendered.Dispose(); } catch { }
            }

            if (rawHandle != IntPtr.Zero)
            {
                DestroyIcon(rawHandle);
            }
            rawHandle = newHandle;
        }
    }
}
