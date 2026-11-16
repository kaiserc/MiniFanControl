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
        public int BuiltInCode; // 0=Balanced, 1=Performance, 2=Silent, 3=AI Workload, -1=Custom
        public bool IsProtectedExample;
        public CurvePoint[] ApuPoints; // Fan 1: APU (CPU + iGPU)
        public CurvePoint[] SysPoints; // Fan 2: System (RAM + NVMe)
        public CurvePoint[] DefaultApuPoints;
        public CurvePoint[] DefaultSysPoints;

        public FanCurveProfile(string name, bool isBuiltIn, int builtInCode, CurvePoint[] apuPoints, CurvePoint[] sysPoints, bool isProtectedExample = false)
        {
            Name = name;
            IsBuiltIn = isBuiltIn;
            BuiltInCode = builtInCode;
            ApuPoints = apuPoints;
            SysPoints = sysPoints ?? ClonePoints(apuPoints);
            IsProtectedExample = isProtectedExample;
            if (isProtectedExample)
            {
                DefaultApuPoints = ClonePoints(ApuPoints);
                DefaultSysPoints = ClonePoints(SysPoints);
            }
        }

        public void ResetToDefaults()
        {
            if (DefaultApuPoints != null && DefaultApuPoints.Length >= 2)
                ApuPoints = ClonePoints(DefaultApuPoints);
            if (DefaultSysPoints != null && DefaultSysPoints.Length >= 2)
                SysPoints = ClonePoints(DefaultSysPoints);
        }

        public static List<FanCurveProfile> CreateProtectedExamples()
        {
            List<FanCurveProfile> list = new List<FanCurveProfile>(4);

            // 1. True Zero-RPM Desktop Idle — 0% fan stop up to 48°C, stepping cleanly to spin floor at 54°C
            list.Add(new FanCurveProfile(
                "🔒 Zero-RPM Desktop Idle",
                false,
                -1,
                new CurvePoint[] { new CurvePoint(48, 0), new CurvePoint(54, 30), new CurvePoint(68, 48), new CurvePoint(80, 72), new CurvePoint(90, 100) },
                new CurvePoint[] { new CurvePoint(45, 30), new CurvePoint(58, 34), new CurvePoint(70, 42), new CurvePoint(82, 55), new CurvePoint(90, 70) },
                true));

            // 2. Gaming Sweet-Spot (Acoustic Knee) — holds below 58% PWM through 72°C where blower noise stays low
            list.Add(new FanCurveProfile(
                "🔒 Gaming Sweet-Spot (Acoustic Knee)",
                false,
                -1,
                new CurvePoint[] { new CurvePoint(42, 32), new CurvePoint(56, 44), new CurvePoint(72, 58), new CurvePoint(82, 82), new CurvePoint(90, 100) },
                new CurvePoint[] { new CurvePoint(42, 36), new CurvePoint(56, 46), new CurvePoint(70, 56), new CurvePoint(82, 72), new CurvePoint(90, 88) },
                true));

            // 3. 24/7 Server / Local LLM Sustained — continuous low-RPM airflow with elevated Sys Fan for DDR5/NVMe
            list.Add(new FanCurveProfile(
                "🔒 24/7 Server / Local LLM Sustained",
                false,
                -1,
                new CurvePoint[] { new CurvePoint(38, 34), new CurvePoint(50, 46), new CurvePoint(64, 64), new CurvePoint(76, 84), new CurvePoint(86, 100) },
                new CurvePoint[] { new CurvePoint(36, 44), new CurvePoint(48, 56), new CurvePoint(60, 70), new CurvePoint(72, 85), new CurvePoint(84, 100) },
                true));

            // 4. Max Cooling / Benchmark — early aggressive ramp keeping APU under 75°C for max Zen 4/5 Boost clocks
            list.Add(new FanCurveProfile(
                "🔒 Max Cooling / Benchmark",
                false,
                -1,
                new CurvePoint[] { new CurvePoint(35, 45), new CurvePoint(46, 62), new CurvePoint(58, 78), new CurvePoint(70, 94), new CurvePoint(78, 100) },
                new CurvePoint[] { new CurvePoint(35, 50), new CurvePoint(48, 65), new CurvePoint(60, 80), new CurvePoint(72, 95), new CurvePoint(80, 100) },
                true));

            return list;
        }

        public static FanCurveProfile SynthesizeAutoTunedCurve(FanStatus status, TelemetrySessionStats stats, out string summary)
        {
            // 1. Calibrated Anti-Stall Floor
            int apuFloor = Math.Max(18, Math.Min(45, ApuMinSpinPct));
            int sysFloor = Math.Max(18, Math.Min(45, SysMinSpinPct));

            // 2. Idle Baseline Anchor (Anti-Hunting)
            int rawIdle = 42;
            if (stats != null && stats.SampleCount > 0 && stats.MinCpuTemp > 0)
                rawIdle = stats.MinCpuTemp;
            else if (status.CpuTemp > 0)
                rawIdle = Math.Min(48, status.CpuTemp);

            int clampedIdle = Math.Max(36, Math.Min(50, rawIdle));
            int idleTemp = Math.Max(38, Math.Min(48, clampedIdle + 2));
            int spinUpTemp = Math.Min(56, idleTemp + 6);
            int spinUpPct = Math.Min(50, apuFloor + 2);

            // 3. Power Envelope & Acoustic Efficiency Knee
            int tdpWatts = 45;
            if (status.ApuPowerLimitWatts > 0)
                tdpWatts = status.ApuPowerLimitWatts;
            else if (stats != null && stats.MaxPowerWatts > 0)
                tdpWatts = Math.Max(28, stats.MaxPowerWatts);
            else if (status.ApuPowerWatts > 0)
                tdpWatts = Math.Max(28, status.ApuPowerWatts);

            int kneeTemp;
            int kneePct;
            if (tdpWatts >= 54)
            {
                kneeTemp = 66;
                kneePct = Math.Max(spinUpPct + 14, 58);
            }
            else if (tdpWatts <= 35)
            {
                kneeTemp = 70;
                kneePct = Math.Max(spinUpPct + 10, 50);
            }
            else
            {
                kneeTemp = 68;
                kneePct = Math.Max(spinUpPct + 12, 54);
            }

            int observedPeakApu = Math.Max(status.CpuTemp, status.GpuTemp);
            if (stats != null && stats.SampleCount > 0)
                observedPeakApu = Math.Max(observedPeakApu, Math.Max(stats.MaxCpuTemp, stats.MaxGpuTemp));

            if (observedPeakApu >= 84)
            {
                kneeTemp = Math.Max(spinUpTemp + 8, kneeTemp - 2);
                kneePct = Math.Min(68, kneePct + 4);
            }

            // 4. Boost Headroom & TJMax Safety Ceiling
            int boostTemp = 80;
            int boostPct = Math.Max(kneePct + 16, Math.Min(88, kneePct + 24));
            int maxTemp = (tdpWatts >= 54) ? 88 : 90;

            CurvePoint[] apuPts = new CurvePoint[]
            {
                new CurvePoint(idleTemp, 0),
                new CurvePoint(spinUpTemp, spinUpPct),
                new CurvePoint(kneeTemp, kneePct),
                new CurvePoint(boostTemp, boostPct),
                new CurvePoint(maxTemp, 100)
            };

            // 5. Coupled Sys Fan (DDR5 / NVMe / NPU) Curve
            int maxNvme = status.NvmeTemp;
            double maxRamPwr = status.RamPowerWatts;
            int maxNpu = status.NpuActivityPct;
            if (stats != null && stats.SampleCount > 0)
            {
                maxNvme = Math.Max(maxNvme, stats.MaxNvmeTemp);
                maxRamPwr = Math.Max(maxRamPwr, stats.MaxRamPowerWatts);
                maxNpu = Math.Max(maxNpu, stats.MaxNpuActivityPct);
            }

            int sysBoost = (maxNvme >= 60 || maxRamPwr >= 4.0 || maxNpu >= 20) ? 8 : 0;
            int sysP0 = sysFloor;
            int sysP1 = Math.Min(100, Math.Max(sysP0 + 4, sysFloor + 5 + sysBoost / 2));
            int sysP2 = Math.Min(100, Math.Max(sysP1 + 6, Math.Max(sysFloor + 14, 48) + sysBoost));
            int sysP3 = Math.Min(100, Math.Max(sysP2 + 8, Math.Max(sysFloor + 26, 64) + sysBoost));
            int sysP4 = Math.Min(100, Math.Max(sysP3 + 8, Math.Max(sysFloor + 38, 80) + sysBoost));

            CurvePoint[] sysPts = new CurvePoint[]
            {
                new CurvePoint(idleTemp, sysP0),
                new CurvePoint(spinUpTemp, sysP1),
                new CurvePoint(kneeTemp, sysP2),
                new CurvePoint(boostTemp, sysP3),
                new CurvePoint(maxTemp, sysP4)
            };

            string curveName = string.Format("✨ Auto-Tuned ({0}W • Min {1}%)", tdpWatts, apuFloor);
            summary = string.Format("✓ Auto-Tuned for {0}W TDP (Idle ≤{1}°C → {2}% @ {3}°C, Knee {4}% @ {5}°C, 100% @ {6}°C)",
                tdpWatts, idleTemp, spinUpPct, spinUpTemp, kneePct, kneeTemp, maxTemp);

            return new FanCurveProfile(curveName, false, -1, apuPts, sysPts, false);
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
