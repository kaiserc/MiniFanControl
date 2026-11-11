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
    public struct CurvePoint
    {
        public int Temp;
        public int Pct;

        public CurvePoint(int temp, int pct)
        {
            Temp = temp;
            Pct = pct;
        }
    }

    public class FanCurveProfile
    {
        public const int MIN_SPIN_PCT = 30;
        public static int ApuMinSpinPct = 30;
        public static int SysMinSpinPct = 30;
        public static int ApuMaxRpm = 3150;
        public static int SysMaxRpm = 1940;
        public static string LastCalibratedDate = "";

        public static string FormatCalibrationStatusBadge(bool hasDualFans, out bool isCalibrated, out bool isStale)
        {
            isCalibrated = false;
            isStale = false;
            string raw = (LastCalibratedDate ?? "").Trim();
            if (string.IsNullOrEmpty(raw))
            {
                return "○ Uncalibrated (Default 30% min — Click to Calibrate)";
            }

            isCalibrated = true;
            string shortDate = raw.Length >= 10 ? raw.Substring(0, 10) : raw;
            DateTime parsedDt;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsedDt))
            {
                shortDate = parsedDt.ToString("yyyy-MM-dd");
                if ((DateTime.Now - parsedDt).TotalDays > 180)
                {
                    isStale = true;
                }
            }

            string minPart = hasDualFans
                ? string.Format("Min {0}%/{1}%", ApuMinSpinPct, SysMinSpinPct)
                : string.Format("Min {0}%", ApuMinSpinPct);

            if (isStale)
                return string.Format("⚠ Calibrated {0} (>6m ago • {1} — Re-Calibrate)", shortDate, minPart);

            return string.Format("✓ Calibrated {0} ({1})", shortDate, minPart);
        }

        public string Name;
        public bool IsBuiltIn;
        public int BuiltInCode; // 0=Balanced, 1=Performance, 2=Silent, -1=Custom
        public CurvePoint[] ApuPoints; // Fan 1: APU (CPU + iGPU)
        public CurvePoint[] SysPoints; // Fan 2: System (RAM + NVMe)

        public FanCurveProfile(string name, bool isBuiltIn, int builtInCode, CurvePoint[] apuPoints, CurvePoint[] sysPoints)
        {
            Name = name;
            IsBuiltIn = isBuiltIn;
            BuiltInCode = builtInCode;
            ApuPoints = apuPoints;
            SysPoints = sysPoints ?? ClonePoints(apuPoints);
        }

        public static CurvePoint[] ClonePoints(CurvePoint[] src)
        {
            if (src == null) return new CurvePoint[0];
            CurvePoint[] copy = new CurvePoint[src.Length];
            Array.Copy(src, copy, src.Length);
            return copy;
        }

        public FanCurveProfile CloneAsCustom(string newName)
        {
            return new FanCurveProfile(newName, false, -1, ClonePoints(ApuPoints), ClonePoints(SysPoints));
        }

        public static int EvaluateCurve(CurvePoint[] points, int tempC)
        {
            return EvaluateCurve(points, tempC, MIN_SPIN_PCT);
        }

        public static int EvaluateCurve(CurvePoint[] points, int tempC, int minSpinFloor)
        {
            if (points == null || points.Length == 0)
                return 40;

            if (minSpinFloor < 15 || minSpinFloor > 50)
                minSpinFloor = MIN_SPIN_PCT;

            int rawPct;
            bool fromZeroSegment = false;

            if (tempC <= points[0].Temp)
            {
                rawPct = points[0].Pct;
            }
            else
            {
                rawPct = points[points.Length - 1].Pct;
                for (int i = 0; i < points.Length - 1; i++)
                {
                    CurvePoint p0 = points[i];
                    CurvePoint p1 = points[i + 1];
                    if (tempC >= p0.Temp && tempC <= p1.Temp)
                    {
                        if (p1.Temp <= p0.Temp)
                        {
                            rawPct = p1.Pct;
                        }
                        else
                        {
                            fromZeroSegment = (p0.Pct == 0 && p1.Pct > 0);
                            double t = (double)(tempC - p0.Temp) / (p1.Temp - p0.Temp);
                            rawPct = (int)Math.Round(p0.Pct + t * (p1.Pct - p0.Pct));
                        }
                        break;
                    }
                }
            }

            rawPct = Math.Max(0, Math.Min(100, rawPct));
            if (rawPct == 0)
                return 0;

            // Fan motors stall below minSpinFloor (default 30% PWM on K12).
            // On a 0%->spin ramp, stay at 0% (Idle) until past midpoint, then step to >= minSpinFloor so the fan spins cleanly.
            int stepThreshold = Math.Max(12, (minSpinFloor * 3) / 5);
            if (fromZeroSegment && rawPct < stepThreshold)
                return 0;
            if (rawPct > 0 && rawPct < minSpinFloor)
                return minSpinFloor;

            return rawPct;
        }

        public int EvaluateApu(int tempC)
        {
            return EvaluateCurve(ApuPoints, tempC, ApuMinSpinPct);
        }

        public int EvaluateSys(int tempC)
        {
            return EvaluateCurve(SysPoints, tempC, SysMinSpinPct);
        }
    }

}
