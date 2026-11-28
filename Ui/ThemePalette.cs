using System;
using System.Drawing;
using System.Windows.Forms;

namespace MiniFanControl
{
    /// <summary>
    /// Centralized color palette for all MiniFanControl themes (Dark, Light, AMD Radeon,
    /// MSI Afterburner, Xmas Edition, and Windows High Contrast).
    /// Eliminates duplicated theme ternaries across forms, tooltips, dialogs, menus, and canvases.
    /// </summary>
    public sealed class ThemePalette
    {
        public Color Bg;
        public Color Surface;
        public Color Surface2;
        public Color TileBg;
        public Color Border;
        public Color TileBorder;
        public Color WindowBorder;
        public Color Text;
        public Color TextDim;
        public Color Accent;
        public Color SecondaryAccent;
        public Color ActiveFg;
        public Color TileTitleFg;
        public Color TileValueFg;
        public Color TooltipBg;
        public Color TooltipBorder;
        public Color CanvasBg;
        public Color CanvasGrid;
        public Color Danger;
        public Color Warning;
        public Color Success;

        public bool IsDark;
        public bool IsRadeon;
        public bool IsAfterburner;
        public bool IsXmas;
        public bool IsHighContrast;

        public static ThemePalette Resolve(ThemePreference pref, bool systemDark)
        {
            if (SystemInformation.HighContrast)
            {
                return new ThemePalette
                {
                    Bg = SystemColors.Window,
                    Surface = SystemColors.Control,
                    Surface2 = SystemColors.Control,
                    TileBg = SystemColors.Window,
                    Border = SystemColors.WindowFrame,
                    TileBorder = SystemColors.WindowFrame,
                    WindowBorder = SystemColors.Highlight,
                    Text = SystemColors.WindowText,
                    TextDim = SystemColors.ControlText,
                    Accent = SystemColors.Highlight,
                    SecondaryAccent = SystemColors.HotTrack,
                    ActiveFg = SystemColors.HighlightText,
                    TileTitleFg = SystemColors.HotTrack,
                    TileValueFg = SystemColors.WindowText,
                    TooltipBg = SystemColors.Info,
                    TooltipBorder = SystemColors.WindowFrame,
                    CanvasBg = SystemColors.Window,
                    CanvasGrid = SystemColors.GrayText,
                    Danger = SystemColors.Highlight,
                    Warning = SystemColors.Highlight,
                    Success = SystemColors.Highlight,
                    IsDark = false,
                    IsRadeon = false,
                    IsAfterburner = false,
                    IsXmas = false,
                    IsHighContrast = true
                };
            }

            if (pref == ThemePreference.Xmas)
            {
                return new ThemePalette
                {
                    Bg = Color.White,
                    Surface = Color.White,
                    Surface2 = Color.FromArgb(0xF4, 0xF4, 0xF5),
                    TileBg = Color.White,
                    Border = Color.FromArgb(0x18, 0x18, 0x1B),
                    TileBorder = Color.FromArgb(0x27, 0x27, 0x2A),
                    WindowBorder = Color.FromArgb(0xDC, 0x26, 0x26),
                    Text = Color.FromArgb(0x09, 0x09, 0x0B),
                    TextDim = Color.FromArgb(0x3F, 0x3F, 0x46),
                    Accent = Color.FromArgb(0xDC, 0x26, 0x26),
                    SecondaryAccent = Color.FromArgb(0x18, 0x18, 0x1B),
                    ActiveFg = Color.White,
                    TileTitleFg = Color.FromArgb(0xDC, 0x26, 0x26),
                    TileValueFg = Color.FromArgb(0x09, 0x09, 0x0B),
                    TooltipBg = Color.FromArgb(0xFF, 0xFF, 0xFF),
                    TooltipBorder = Color.FromArgb(0x18, 0x18, 0x1B),
                    CanvasBg = Color.White,
                    CanvasGrid = Color.FromArgb(0xE4, 0xE4, 0xE7),
                    Danger = Color.FromArgb(0xDC, 0x26, 0x26),
                    Warning = Color.FromArgb(0xB9, 0x1C, 0x1C),
                    Success = Color.FromArgb(0x99, 0x1B, 0x1B),
                    IsDark = false,
                    IsRadeon = false,
                    IsAfterburner = false,
                    IsXmas = true,
                    IsHighContrast = false
                };
            }

            if (pref == ThemePreference.Radeon)
            {
                return new ThemePalette
                {
                    Bg = Color.FromArgb(0x14, 0x0A, 0x0D),
                    Surface = Color.FromArgb(0x21, 0x11, 0x16),
                    Surface2 = Color.FromArgb(0x34, 0x19, 0x21),
                    TileBg = Color.FromArgb(0x0D, 0x05, 0x07),
                    Border = Color.FromArgb(0x52, 0x1A, 0x24),
                    TileBorder = Color.FromArgb(0x46, 0x19, 0x22),
                    WindowBorder = Color.FromArgb(0x9F, 0x12, 0x39),
                    Text = Color.FromArgb(0xFF, 0xF1, 0xF2),
                    TextDim = Color.FromArgb(0xCB, 0x9E, 0xA8),
                    Accent = Color.FromArgb(0xE1, 0x1D, 0x48),
                    SecondaryAccent = Color.FromArgb(0xF9, 0x73, 0x16),
                    ActiveFg = Color.White,
                    TileTitleFg = Color.FromArgb(0xFD, 0xA4, 0xAF),
                    TileValueFg = Color.White,
                    TooltipBg = Color.FromArgb(0x16, 0x0A, 0x0E),
                    TooltipBorder = Color.FromArgb(0xE1, 0x1D, 0x48),
                    CanvasBg = Color.FromArgb(0x16, 0x0A, 0x0E),
                    CanvasGrid = Color.FromArgb(0x42, 0x1C, 0x26),
                    Danger = Color.FromArgb(0xFB, 0x71, 0x85),
                    Warning = Color.FromArgb(0xFB, 0x92, 0x3C),
                    Success = Color.FromArgb(0x4A, 0xDE, 0x80),
                    IsDark = true,
                    IsRadeon = true,
                    IsAfterburner = false,
                    IsXmas = false,
                    IsHighContrast = false
                };
            }

            if (pref == ThemePreference.Afterburner)
            {
                return new ThemePalette
                {
                    Bg = Color.FromArgb(0x13, 0x15, 0x19),
                    Surface = Color.FromArgb(0x1C, 0x20, 0x26),
                    Surface2 = Color.FromArgb(0x25, 0x2B, 0x35),
                    TileBg = Color.FromArgb(0x0B, 0x0D, 0x10),
                    Border = Color.FromArgb(0x22, 0x35, 0x2A),
                    TileBorder = Color.FromArgb(0x20, 0x33, 0x28),
                    WindowBorder = Color.FromArgb(0x16, 0x65, 0x34),
                    Text = Color.FromArgb(0xEE, 0xF2, 0xF6),
                    TextDim = Color.FromArgb(0x8A, 0x96, 0xA8),
                    Accent = Color.FromArgb(0x00, 0xE6, 0x76),
                    SecondaryAccent = Color.FromArgb(0x00, 0xE5, 0xFF),
                    ActiveFg = Color.Black,
                    TileTitleFg = Color.FromArgb(0x00, 0xE6, 0x76),
                    TileValueFg = Color.White,
                    TooltipBg = Color.FromArgb(0x11, 0x14, 0x18),
                    TooltipBorder = Color.FromArgb(0x00, 0xE6, 0x76),
                    CanvasBg = Color.FromArgb(0x13, 0x15, 0x19),
                    CanvasGrid = Color.FromArgb(0x28, 0x30, 0x3A),
                    Danger = Color.FromArgb(0xFF, 0x52, 0x52),
                    Warning = Color.FromArgb(0xFF, 0x91, 0x00),
                    Success = Color.FromArgb(0x00, 0xE6, 0x76),
                    IsDark = true,
                    IsRadeon = false,
                    IsAfterburner = true,
                    IsXmas = false,
                    IsHighContrast = false
                };
            }

            bool useDark = (pref == ThemePreference.Dark) || (pref == ThemePreference.System && systemDark);
            if (useDark)
            {
                return new ThemePalette
                {
                    Bg = Color.FromArgb(0x14, 0x14, 0x1E),
                    Surface = Color.FromArgb(0x22, 0x22, 0x32),
                    Surface2 = Color.FromArgb(0x2E, 0x30, 0x44),
                    TileBg = Color.FromArgb(0x0D, 0x0E, 0x16),
                    Border = Color.FromArgb(0x36, 0x3A, 0x52),
                    TileBorder = Color.FromArgb(0x2C, 0x30, 0x44),
                    WindowBorder = Color.FromArgb(0x3B, 0x42, 0x61),
                    Text = Color.FromArgb(0xF8, 0xFA, 0xFC),
                    TextDim = Color.FromArgb(0x94, 0x99, 0xB0),
                    Accent = Color.FromArgb(0x89, 0xB4, 0xFA),
                    SecondaryAccent = Color.FromArgb(0x2D, 0xD4, 0xBF),
                    ActiveFg = Color.FromArgb(0x11, 0x11, 0x1B),
                    TileTitleFg = Color.FromArgb(0x94, 0xA3, 0xB8),
                    TileValueFg = Color.White,
                    TooltipBg = Color.FromArgb(0x18, 0x18, 0x25),
                    TooltipBorder = Color.FromArgb(0x45, 0x47, 0x5A),
                    CanvasBg = Color.FromArgb(0x16, 0x16, 0x22),
                    CanvasGrid = Color.FromArgb(0x2D, 0x2F, 0x42),
                    Danger = Color.FromArgb(0xFB, 0x71, 0x85),
                    Warning = Color.FromArgb(0xFB, 0xBF, 0x24),
                    Success = Color.FromArgb(0x4A, 0xDE, 0x80),
                    IsDark = true,
                    IsRadeon = false,
                    IsAfterburner = false,
                    IsXmas = false,
                    IsHighContrast = false
                };
            }

            return new ThemePalette
            {
                Bg = Color.FromArgb(0xF3, 0xF4, 0xF6),
                Surface = Color.FromArgb(0xFF, 0xFF, 0xFF),
                Surface2 = Color.FromArgb(0xE5, 0xE7, 0xEB),
                TileBg = Color.White,
                Border = Color.FromArgb(0xCB, 0xD5, 0xE1),
                TileBorder = Color.FromArgb(0xD8, 0xDF, 0xE8),
                WindowBorder = Color.FromArgb(0xCB, 0xD5, 0xE1),
                Text = Color.FromArgb(0x0F, 0x17, 0x2A),
                TextDim = Color.FromArgb(0x4B, 0x55, 0x63),
                Accent = Color.FromArgb(0x1D, 0x4E, 0xD8),
                SecondaryAccent = Color.FromArgb(0x0D, 0x94, 0x88),
                ActiveFg = Color.White,
                TileTitleFg = Color.FromArgb(0x47, 0x55, 0x69),
                TileValueFg = Color.FromArgb(0x0F, 0x17, 0x2A),
                TooltipBg = Color.White,
                TooltipBorder = Color.FromArgb(0x94, 0xA3, 0xB8),
                CanvasBg = Color.FromArgb(0xF8, 0xFA, 0xFC),
                CanvasGrid = Color.FromArgb(0xE2, 0xE8, 0xF0),
                Danger = Color.FromArgb(0xDC, 0x26, 0x26),
                Warning = Color.FromArgb(0xB4, 0x53, 0x09),
                Success = Color.FromArgb(0x15, 0x80, 0x3D),
                IsDark = false,
                IsRadeon = false,
                IsAfterburner = false,
                IsXmas = false,
                IsHighContrast = false
            };
        }

        public static ThemePalette ResolveFromFlags(bool isDark, bool isRadeon, bool isAfterburner, bool isXmas)
        {
            if (isXmas) return Resolve(ThemePreference.Xmas, false);
            if (isRadeon) return Resolve(ThemePreference.Radeon, true);
            if (isAfterburner) return Resolve(ThemePreference.Afterburner, true);
            return Resolve(isDark ? ThemePreference.Dark : ThemePreference.Light, isDark);
        }
    }
}
