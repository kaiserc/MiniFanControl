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
    public static class AppLogoRenderer
    {
        public static Bitmap RenderConcept(int conceptId, int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                if (conceptId == 2)
                {
                    RenderConceptIsometric3D(g, size);
                }
                else if (conceptId == 3)
                {
                    RenderConceptGlassDeck3D(g, size);
                }
                else if (conceptId == 4)
                {
                    RenderConceptFrontBevel3D(g, size, 1);
                }
                else if (conceptId == 5)
                {
                    RenderConceptFrontBevel3D(g, size, 2);
                }
                else
                {
                    RenderConceptFrontBevel3D(g, size, 0);
                }
            }
            return bmp;
        }

        /// <summary>
        /// Concept 1 (Default): 3D Front-Bevel AMD Mini PC Chassis
        /// Features a 3D extruded front I/O panel + side depth, recessed top deck,
        /// 270-degree thermal gauge arc, reduced-size 6-blade fan, and a metallic AMD sticker underneath.
        /// When themeMode == 1 (conceptId == 4), renders in AMD Radeon Ruby-Red & Obsidian styling.
        /// When themeMode == 2 (conceptId == 5), renders in MSI Afterburner Toxic-Green & Gunmetal Carbon styling.
        /// </summary>
        private static void RenderConceptFrontBevel3D(Graphics g, int size, bool radeonTheme)
        {
            RenderConceptFrontBevel3D(g, size, radeonTheme ? 1 : 0);
        }

        private static void RenderConceptFrontBevel3D(Graphics g, int size, int themeMode)
        {
            bool isRadeon = (themeMode == 1);
            bool isAfterburner = (themeMode == 2);

            // 1. Ambient drop shadow under the 3D Mini PC
            using (GraphicsPath shadowPath = CreateRoundedRect(size * 0.08f, size * 0.78f, size * 0.84f, size * 0.19f, size * 0.08f))
            using (Brush shadowBrush = new SolidBrush(Color.FromArgb(115, 0, 0, 0)))
            {
                g.FillPath(shadowBrush, shadowPath);
            }

            // 2. 3D Lower Chassis Body / Extruded Front I/O Face (gives true 3D Mini PC box depth)
            float bodyX = size * 0.06f;
            float bodyW = size * 0.88f;
            float topLidH = size * 0.72f;
            float frontWallY = size * 0.56f;
            float frontWallH = size * 0.37f;
            float cornerR = size * 0.16f;

            Color chassisTop = isAfterburner ? Color.FromArgb(0x1C, 0x20, 0x26) : (isRadeon ? Color.FromArgb(0x21, 0x11, 0x16) : Color.FromArgb(0x1E, 0x29, 0x3B));
            Color chassisBot = isAfterburner ? Color.FromArgb(0x0E, 0x10, 0x13) : (isRadeon ? Color.FromArgb(0x0C, 0x06, 0x08) : Color.FromArgb(0x09, 0x0D, 0x16));
            Color chassisRim = isAfterburner ? Color.FromArgb(0x35, 0x3E, 0x4A) : (isRadeon ? Color.FromArgb(0x42, 0x1C, 0x26) : Color.FromArgb(0x47, 0x55, 0x69));

            using (GraphicsPath lowerChassis = CreateRoundedRect(bodyX, frontWallY, bodyW, frontWallH, cornerR))
            using (LinearGradientBrush chassisGrad = new LinearGradientBrush(
                new PointF(bodyX, frontWallY), new PointF(bodyX + bodyW, frontWallY + frontWallH),
                chassisTop, chassisBot))
            {
                g.FillPath(chassisGrad, lowerChassis);
                using (Pen lowerRim = new Pen(chassisRim, Math.Max(1.2f, size * 0.022f)))
                {
                    g.DrawPath(lowerRim, lowerChassis);
                }
            }

            // 3. Front I/O Details on the 3D Front Face (Power Button LED, USB-C, Dual USB-A, Vent Slots)
            float ioY = size * 0.825f;
            // Glowing Power Button on front-left
            float pwrR = Math.Max(2f, size * 0.032f);
            float pwrX = size * 0.22f;
            Color pwrGlowCol = isAfterburner ? Color.FromArgb(90, 0x00, 0xE5, 0xFF) : (isRadeon ? Color.FromArgb(90, 0xEF, 0x44, 0x44) : Color.FromArgb(90, 0xEF, 0x44, 0x44));
            Color pwrCoreCol = isAfterburner ? Color.FromArgb(0x00, 0xE5, 0xFF) : (isRadeon ? Color.FromArgb(0xFB, 0x71, 0x85) : Color.FromArgb(0xF9, 0x73, 0x16));
            using (Brush pwrGlow = new SolidBrush(pwrGlowCol))
            using (Brush pwrCore = new SolidBrush(pwrCoreCol))
            using (Pen pwrRim = new Pen(Color.White, Math.Max(0.8f, size * 0.01f)))
            {
                g.FillEllipse(pwrGlow, pwrX - pwrR * 1.6f, ioY - pwrR * 1.6f, pwrR * 3.2f, pwrR * 3.2f);
                g.FillEllipse(pwrCore, pwrX - pwrR, ioY - pwrR, pwrR * 2f, pwrR * 2f);
                g.DrawEllipse(pwrRim, pwrX - pwrR, ioY - pwrR, pwrR * 2f, pwrR * 2f);
            }

            // Front USB-A & USB-C ports + cooling vent slots
            if (size >= 32)
            {
                using (Brush portBrush = new SolidBrush(Color.FromArgb(0x06, 0x09, 0x11)))
                using (Brush usbBlue = new SolidBrush(Color.FromArgb(0x02, 0x84, 0xC7)))
                using (Pen portRim = new Pen(Color.FromArgb(0x64, 0x74, 0x8B), Math.Max(0.8f, size * 0.01f)))
                {
                    // Two USB 3.2 ports
                    for (int u = 0; u < 2; u++)
                    {
                        float ux = size * (0.33f + u * 0.13f);
                        float uw = size * 0.095f;
                        float uh = size * 0.042f;
                        g.FillRectangle(portBrush, ux, ioY - uh * 0.5f, uw, uh);
                        g.FillRectangle(usbBlue, ux + uw * 0.15f, ioY - uh * 0.15f, uw * 0.7f, uh * 0.35f);
                        g.DrawRectangle(portRim, ux, ioY - uh * 0.5f, uw, uh);
                    }
                    // USB4 / Type-C oval port
                    float cxPort = size * 0.60f;
                    float cwPort = size * 0.07f;
                    float chPort = size * 0.032f;
                    using (GraphicsPath typeC = CreateRoundedRect(cxPort, ioY - chPort * 0.5f, cwPort, chPort, chPort * 0.45f))
                    {
                        g.FillPath(portBrush, typeC);
                        g.DrawPath(portRim, typeC);
                    }
                    // Right-side vent grilles
                    using (Pen ventPen = new Pen(Color.FromArgb(0x09, 0x0D, 0x16), Math.Max(1.2f, size * 0.016f)))
                    {
                        ventPen.StartCap = LineCap.Round;
                        ventPen.EndCap = LineCap.Round;
                        for (int v = 0; v < 4; v++)
                        {
                            float vx = size * (0.72f + v * 0.038f);
                            g.DrawLine(ventPen, vx, ioY - size * 0.025f, vx, ioY + size * 0.025f);
                        }
                    }
                }
            }

            // 4. 3D Top Deck / Lid of the Mini PC (tilted perspective squircle sitting above the front face)
            float lidY = size * 0.045f;
            using (GraphicsPath topLid = CreateRoundedRect(bodyX, lidY, bodyW, topLidH, cornerR))
            using (LinearGradientBrush lidGrad = new LinearGradientBrush(
                new PointF(bodyX, lidY), new PointF(bodyX, lidY + topLidH),
                Color.FromArgb(0x24, 0x30, 0x46),
                Color.FromArgb(0x0D, 0x13, 0x22)))
            {
                g.FillPath(lidGrad, topLid);

                // Inner recessed dark chamber on the top lid
                float inset = size * 0.038f;
                using (GraphicsPath innerWell = CreateRoundedRect(
                    bodyX + inset, lidY + inset, bodyW - inset * 2f, topLidH - inset * 2f, cornerR * 0.80f))
                using (LinearGradientBrush wellGrad = new LinearGradientBrush(
                    new PointF(0, lidY), new PointF(0, lidY + topLidH),
                    Color.FromArgb(0x09, 0x0E, 0x1A),
                    Color.FromArgb(0x13, 0x1C, 0x2E)))
                {
                    g.FillPath(wellGrad, innerWell);
                }

                // Bright 3D metallic bevel rim on top lid (Cyan in Standard, Ruby Red in Radeon, Toxic Green in Afterburner)
                Color rimGradStart = isAfterburner ? Color.FromArgb(0x00, 0xE6, 0x76) : (isRadeon ? Color.FromArgb(0xFB, 0x71, 0x85) : Color.FromArgb(0x7D, 0xD3, 0xFC));
                Color rimGradEnd   = isAfterburner ? Color.FromArgb(0x00, 0x8C, 0x44) : (isRadeon ? Color.FromArgb(0xBE, 0x12, 0x3C) : Color.FromArgb(0x02, 0x84, 0xC7));
                using (LinearGradientBrush rimGrad = new LinearGradientBrush(
                    new PointF(bodyX, lidY), new PointF(bodyX + bodyW, lidY + topLidH),
                    rimGradStart, rimGradEnd))
                using (Pen lidRim = new Pen(rimGrad, Math.Max(1.5f, size * 0.028f)))
                {
                    g.DrawPath(lidRim, topLid);
                }
            }

            // 5. 270-Degree Thermal Gauge Arc on the Top Deck (Option 3 style)
            float cx = size * 0.50f;
            float cy = size * 0.355f;
            float arcR = size * 0.255f;
            DrawThermalGaugeArc(g, cx, cy, arcR, Math.Max(2f, size * 0.048f), size >= 48);

            // 6. Reduced-Size 6-Blade Fan Impeller inside the Thermal Gauge
            float fanR = size * 0.165f;
            Color bladeCol = isAfterburner ? Color.FromArgb(0xEE, 0xF2, 0xF6) : (isRadeon ? Color.FromArgb(0xFF, 0xE4, 0xE6) : Color.FromArgb(0xE2, 0xE8, 0xF0));
            Color hubCol   = isAfterburner ? Color.FromArgb(0x00, 0xE6, 0x76) : (isRadeon ? Color.FromArgb(0xE1, 0x1D, 0x48) : Color.FromArgb(0x38, 0xBD, 0xF8));
            DrawImpeller(g, cx, cy, fanR, 6, bladeCol, hubCol, -10f);

            // 7. 3D AMD RYZEN Metallic Sticker Badge underneath the fan (tucked into lower top deck)
            DrawAmdStickerBadge(g, size * 0.23f, size * 0.555f, size * 0.54f, size * 0.145f, size);
        }

        /// <summary>
        /// Concept 2: 3D Isometric Cube AMD Mini PC
        /// Shows true 3D top, left-side, and front-right perspective faces with reduced fan,
        /// 270-degree thermal gauge, and an AMD sticker badge underneath.
        /// </summary>
        private static void RenderConceptIsometric3D(Graphics g, int size)
        {
            // 3D Isometric / Stepped Mini PC Chassis with right & bottom 3D extrusion
            float frontX = size * 0.06f;
            float frontY = size * 0.05f;
            float boxW = size * 0.80f;
            float boxH = size * 0.72f;
            float depthX = size * 0.08f;
            float depthY = size * 0.16f;
            float cornerR = size * 0.14f;

            // Back/Bottom 3D extruded block
            using (GraphicsPath extruded = CreateRoundedRect(frontX + depthX, frontY + depthY, boxW, boxH, cornerR))
            using (LinearGradientBrush extGrad = new LinearGradientBrush(
                new PointF(frontX, frontY + depthY), new PointF(frontX + boxW + depthX, frontY + boxH + depthY),
                Color.FromArgb(0x0F, 0x17, 0x2A),
                Color.FromArgb(0x05, 0x08, 0x11)))
            {
                g.FillPath(extGrad, extruded);
                using (Pen extRim = new Pen(Color.FromArgb(0xEA, 0x58, 0x0C), Math.Max(1.2f, size * 0.022f)))
                {
                    g.DrawPath(extRim, extruded);
                }
            }

            // Side 3D cooling louver lines on the right extruded wall
            if (size >= 48)
            {
                using (Pen louverPen = new Pen(Color.FromArgb(0x33, 0x41, 0x55), Math.Max(1f, size * 0.015f)))
                {
                    for (int i = 0; i < 6; i++)
                    {
                        float ly = size * (0.26f + i * 0.075f);
                        g.DrawLine(louverPen, frontX + boxW + size * 0.015f, ly, frontX + boxW + depthX - size * 0.015f, ly + size * 0.03f);
                    }
                }
            }

            // Top/Front main Mini PC Deck
            using (GraphicsPath topDeck = CreateRoundedRect(frontX, frontY, boxW, boxH, cornerR))
            using (LinearGradientBrush deckGrad = new LinearGradientBrush(
                new PointF(frontX, frontY), new PointF(frontX + boxW, frontY + boxH),
                Color.FromArgb(0x1E, 0x29, 0x3B),
                Color.FromArgb(0x0B, 0x0F, 0x19)))
            {
                g.FillPath(deckGrad, topDeck);
                using (Pen deckRim = new Pen(Color.FromArgb(0x38, 0xBD, 0xF8), Math.Max(1.5f, size * 0.028f)))
                {
                    g.DrawPath(deckRim, topDeck);
                }
            }

            // Thermal gauge + smaller fan centered on the top deck
            float cx = frontX + boxW * 0.50f;
            float cy = frontY + boxH * 0.43f;
            DrawThermalGaugeArc(g, cx, cy, size * 0.24f, Math.Max(2f, size * 0.046f), size >= 48);
            DrawImpeller(g, cx, cy, size * 0.155f, 6,
                Color.FromArgb(0x38, 0xBD, 0xF8),
                Color.FromArgb(0x25, 0x63, 0xEB),
                -15f);

            // AMD Sticker underneath the fan
            DrawAmdStickerBadge(g, cx - size * 0.26f, frontY + boxH * 0.72f, size * 0.52f, size * 0.145f, size);
        }

        /// <summary>
        /// Concept 3: 3D Glass-Top K12 Mini PC with Underglow Ring
        /// Deep 3D terraced chassis with an orange/red AMD accent rim, compact turbine fan inside
        /// the 270-degree thermal gauge, and a bold silver/orange AMD sticker badge on the front lip.
        /// </summary>
        private static void RenderConceptGlassDeck3D(Graphics g, int size)
        {
            // Outer 3D Base Plinth (dark metallic base with AMD Orange underglow)
            float basePad = size * 0.05f;
            float baseW = size * 0.90f;
            float baseH = size * 0.90f;
            using (GraphicsPath basePlinth = CreateRoundedRect(basePad, basePad + size * 0.03f, baseW, baseH - size * 0.03f, size * 0.18f))
            using (LinearGradientBrush baseGrad = new LinearGradientBrush(
                new PointF(0, 0), new PointF(0, size),
                Color.FromArgb(0x33, 0x41, 0x55),
                Color.FromArgb(0x09, 0x0D, 0x16)))
            {
                g.FillPath(baseGrad, basePlinth);
                using (Pen glowRim = new Pen(Color.FromArgb(0xF9, 0x73, 0x16), Math.Max(1.5f, size * 0.025f)))
                {
                    g.DrawPath(glowRim, basePlinth);
                }
            }

            // Elevated 3D Glass Top Lid
            float lidPad = size * 0.10f;
            float lidW = size * 0.80f;
            float lidH = size * 0.74f;
            using (GraphicsPath glassLid = CreateRoundedRect(lidPad, size * 0.055f, lidW, lidH, size * 0.15f))
            using (LinearGradientBrush lidGrad = new LinearGradientBrush(
                new PointF(lidPad, size * 0.05f), new PointF(lidPad + lidW, size * 0.80f),
                Color.FromArgb(0x16, 0x20, 0x33),
                Color.FromArgb(0x0A, 0x0E, 0x17)))
            {
                g.FillPath(lidGrad, glassLid);
                using (Pen cyanRim = new Pen(Color.FromArgb(0x38, 0xBD, 0xF8), Math.Max(1.5f, size * 0.026f)))
                {
                    g.DrawPath(cyanRim, glassLid);
                }
            }

            // 270-degree Thermal Gauge + Reduced-size 6-blade Impeller
            float cx = size * 0.50f;
            float cy = size * 0.365f;
            DrawThermalGaugeArc(g, cx, cy, size * 0.245f, Math.Max(2f, size * 0.048f), size >= 48);
            DrawImpeller(g, cx, cy, size * 0.155f, 6,
                Color.FromArgb(0xF8, 0xFA, 0xFC),
                Color.FromArgb(0x38, 0xBD, 0xF8),
                0f);

            // Prominent 3D AMD Sticker Badge overlapping the lower lip
            DrawAmdStickerBadge(g, size * 0.21f, size * 0.585f, size * 0.58f, size * 0.155f, size);
        }

        private static void DrawThermalGaugeArc(Graphics g, float cx, float cy, float arcR, float arcW, bool drawTicks)
        {
            RectangleF arcRect = new RectangleF(cx - arcR, cy - arcR, arcR * 2f, arcR * 2f);

            // Subtle dark track behind the gauge
            using (Pen trackPen = new Pen(Color.FromArgb(90, 0x33, 0x41, 0x55), arcW * 1.15f))
            {
                trackPen.StartCap = LineCap.Round;
                trackPen.EndCap = LineCap.Round;
                g.DrawArc(trackPen, arcRect, 135f, 270f);
            }

            using (Pen pCool = new Pen(Color.FromArgb(0x38, 0xBD, 0xF8), arcW))
            using (Pen pMid  = new Pen(Color.FromArgb(0x4A, 0xDE, 0x80), arcW))
            using (Pen pHot  = new Pen(Color.FromArgb(0xF9, 0x73, 0x16), arcW))
            {
                pCool.StartCap = LineCap.Round;
                pHot.EndCap = LineCap.Round;
                g.DrawArc(pCool, arcRect, 135f, 90f);
                g.DrawArc(pMid,  arcRect, 225f, 90f);
                g.DrawArc(pHot,  arcRect, 315f, 90f);
            }

            if (drawTicks)
            {
                using (Pen tickPen = new Pen(Color.FromArgb(150, 0x94, 0xA3, 0xB8), Math.Max(1f, arcW * 0.22f)))
                {
                    for (int deg = 135; deg <= 405; deg += 45)
                    {
                        double rad = deg * Math.PI / 180.0;
                        float r1 = arcR - arcW * 0.85f;
                        float r2 = arcR - arcW * 0.45f;
                        g.DrawLine(tickPen,
                            cx + (float)(Math.Cos(rad) * r1),
                            cy + (float)(Math.Sin(rad) * r1),
                            cx + (float)(Math.Cos(rad) * r2),
                            cy + (float)(Math.Sin(rad) * r2));
                    }
                }
            }
        }

        /// <summary>
        /// Draws a crisp 3D metallic AMD Ryzen sticker badge underneath the fan.
        /// Includes the signature AMD orange/red corner chevron + bold "AMD" label.
        /// </summary>
        private static void DrawAmdStickerBadge(Graphics g, float x, float y, float w, float h, int iconSize)
        {
            float r = Math.Max(2f, h * 0.26f);

            // Drop shadow under sticker
            using (GraphicsPath sh = CreateRoundedRect(x, y + h * 0.12f, w, h, r))
            using (Brush sb = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            {
                g.FillPath(sb, sh);
            }

            // Brushed metallic dark-graphite & silver sticker base
            using (GraphicsPath badge = CreateRoundedRect(x, y, w, h, r))
            using (LinearGradientBrush bg = new LinearGradientBrush(
                new PointF(x, y), new PointF(x + w, y + h),
                Color.FromArgb(0x27, 0x32, 0x44),
                Color.FromArgb(0x0F, 0x14, 0x20)))
            {
                g.FillPath(bg, badge);

                // Left AMD Orange/Red accent tab inside the sticker
                float accentW = w * 0.28f;
                using (GraphicsPath leftTab = CreateRoundedRect(x, y, accentW, h, r))
                using (LinearGradientBrush orangeGrad = new LinearGradientBrush(
                    new PointF(x, y), new PointF(x + accentW, y + h),
                    Color.FromArgb(0xFF, 0x57, 0x22),
                    Color.FromArgb(0xDC, 0x26, 0x26)))
                {
                    g.FillPath(orangeGrad, leftTab);
                }

                // White geometric AMD corner arrow inside the orange tab
                float ax = x + accentW * 0.24f;
                float ay = y + h * 0.24f;
                float asz = Math.Min(accentW, h) * 0.52f;
                PointF[] arrowPts = new PointF[]
                {
                    new PointF(ax, ay),
                    new PointF(ax + asz, ay),
                    new PointF(ax + asz, ay + asz),
                    new PointF(ax + asz * 0.68f, ay + asz * 0.68f),
                    new PointF(ax + asz * 0.68f, ay + asz * 0.32f),
                    new PointF(ax + asz * 0.32f, ay + asz * 0.32f)
                };
                using (Brush wb = new SolidBrush(Color.White))
                {
                    g.FillPolygon(wb, arrowPts);
                }

                // Metallic silver/orange border around the sticker
                using (Pen borderPen = new Pen(Color.FromArgb(0xCBD5E1 >> 16 & 0xFF, 0xD5, 0xE1), Math.Max(1f, iconSize * 0.016f)))
                {
                    g.DrawPath(borderPen, badge);
                }

                // Crisp "AMD" text on the right portion of the sticker
                RectangleF textRect = new RectangleF(x + accentW * 0.92f, y - h * 0.04f, w - accentW * 0.92f, h * 1.08f);
                float fontPx = Math.Max(6.5f, h * 0.66f);
                using (Font amdFont = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush textBrush = new SolidBrush(Color.White))
                using (StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap
                })
                {
                    g.DrawString("AMD", amdFont, textBrush, textRect, sf);
                }
            }
        }

        private static void DrawImpeller(Graphics g, float cx, float cy, float radius, int blades, Color c1, Color c2, float startDeg)
        {
            using (Pen ringPen = new Pen(Color.FromArgb(140, c1.R, c1.G, c1.B), Math.Max(1.2f, radius * 0.10f)))
            {
                g.DrawEllipse(ringPen, cx - radius, cy - radius, radius * 2f, radius * 2f);
            }

            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(startDeg);

            float step = 360f / blades;
            float rInner = radius * 0.22f;
            float rOuter = radius * 0.88f;

            for (int i = 0; i < blades; i++)
            {
                using (GraphicsPath blade = new GraphicsPath())
                {
                    blade.AddBezier(
                        new PointF(rInner * 0.5f, -rInner * 0.4f),
                        new PointF(rOuter * 0.45f, -rOuter * 0.55f),
                        new PointF(rOuter * 0.92f, -rOuter * 0.35f),
                        new PointF(rOuter * 0.94f, rOuter * 0.05f));
                    blade.AddBezier(
                        new PointF(rOuter * 0.94f, rOuter * 0.05f),
                        new PointF(rOuter * 0.65f, rOuter * 0.24f),
                        new PointF(rInner * 1.2f, rInner * 0.25f),
                        new PointF(rInner * 0.5f, -rInner * 0.4f));
                    blade.CloseFigure();

                    using (LinearGradientBrush bb = new LinearGradientBrush(
                        new PointF(0, -rOuter), new PointF(rOuter, rOuter), c1, c2))
                    {
                        g.FillPath(bb, blade);
                    }
                }
                g.RotateTransform(step);
            }
            g.Restore(st);

            float hubR = radius * 0.26f;
            using (Brush hubBrush = new SolidBrush(Color.FromArgb(0x1E, 0x29, 0x3B)))
            using (Pen hubPen = new Pen(c1, Math.Max(1.2f, radius * 0.08f)))
            {
                g.FillEllipse(hubBrush, cx - hubR, cy - hubR, hubR * 2f, hubR * 2f);
                g.DrawEllipse(hubPen, cx - hubR, cy - hubR, hubR * 2f, hubR * 2f);
            }
        }

        private static GraphicsPath CreateRoundedRect(float x, float y, float w, float h, float r)
        {
            GraphicsPath path = new GraphicsPath();
            float d = r * 2f;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void WriteMultiResIco(string icoPath, int conceptId)
        {
            int[] sizes = new int[] { 256, 64, 48, 32, 16 };
            List<byte[]> pngPayloads = new List<byte[]>();

            for (int i = 0; i < sizes.Length; i++)
            {
                using (Bitmap bmp = RenderConcept(conceptId, sizes[i]))
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngPayloads.Add(ms.ToArray());
                }
            }

            using (FileStream fs = new FileStream(icoPath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write((ushort)0); // Reserved
                bw.Write((ushort)1); // Type 1 = ICO
                bw.Write((ushort)sizes.Length);

                int offset = 6 + (16 * sizes.Length);
                for (int i = 0; i < sizes.Length; i++)
                {
                    int sz = sizes[i];
                    byte[] data = pngPayloads[i];
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((ushort)1);
                    bw.Write((ushort)32);
                    bw.Write((uint)data.Length);
                    bw.Write((uint)offset);
                    offset += data.Length;
                }

                for (int i = 0; i < pngPayloads.Count; i++)
                {
                    bw.Write(pngPayloads[i]);
                }
            }
        }

        public static void WriteConceptShowcasePng(string pngPath)
        {
            int w = 960;
            int h = 360;
            using (Bitmap sheet = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.FromArgb(0x0F, 0x17, 0x2A));

                string[] titles = new string[]
                {
                    "Option 1: 3D Front-I/O Mini PC",
                    "Option 2: 3D Isometric Cube PC",
                    "Option 3: 3D Glass-Top Plinth PC"
                };
                string[] descs = new string[]
                {
                    "3D Front I/O + Power LED, Thermal Gauge, Compact Fan & AMD Sticker",
                    "3D Extruded Side Louvers, Thermal Gauge, Cyan Fan & AMD Sticker",
                    "3D Orange Underglow Plinth, Thermal Gauge, White/Cyan Fan & AMD Badge"
                };

                using (Font fTitle = new Font("Segoe UI", 12.5f, FontStyle.Bold))
                using (Font fDesc  = new Font("Segoe UI", 9.25f, FontStyle.Regular))
                using (Brush bTitle = new SolidBrush(Color.White))
                using (Brush bDesc  = new SolidBrush(Color.FromArgb(0x94, 0xA3, 0xB8)))
                using (Brush bCard  = new SolidBrush(Color.FromArgb(0x1E, 0x29, 0x3B)))
                using (Pen pBorder  = new Pen(Color.FromArgb(0x33, 0x41, 0x55), 1.5f))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int cardX = 24 + i * 310;
                        int cardY = 20;
                        int cardW = 292;
                        int cardH = 320;
                        using (GraphicsPath cp = CreateRoundedRect(cardX, cardY, cardW, cardH, 16f))
                        {
                            g.FillPath(bCard, cp);
                            g.DrawPath(pBorder, cp);
                        }

                        using (Bitmap iconBmp = RenderConcept(i + 1, 184))
                        {
                            g.DrawImage(iconBmp, cardX + (cardW - 184) / 2, cardY + 22, 184, 184);
                        }

                        StringFormat sf = new StringFormat { Alignment = StringAlignment.Center };
                        g.DrawString(titles[i], fTitle, bTitle, new RectangleF(cardX + 8, cardY + 222, cardW - 16, 28), sf);
                        g.DrawString(descs[i], fDesc, bDesc, new RectangleF(cardX + 12, cardY + 254, cardW - 24, 48), sf);
                    }
                }

                sheet.Save(pngPath, ImageFormat.Png);
            }
        }
    }

}
