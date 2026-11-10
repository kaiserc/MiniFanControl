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
    public struct FanCalibrationProgress
    {
        public int CurrentPass;
        public int TotalPasses;
        public int CurrentStepPct;
        public int ProgressPercent;
        public string PhaseTitle;
        public string LogLine;
        public FanStatus LiveStatus;
    }

    public class FanCalibrationResult
    {
        public static readonly int[] CalibrationSteps = new int[] { 20, 22, 24, 26, 28, 30, 32, 35, 60, 100 };

        public bool HasDualFans = true;
        public bool WasCanceled = false;
        public int PassesCompleted = 0;
        public int ApuBareSpinPct = 28;
        public int SysBareSpinPct = 28;
        public int ApuMinSpinPct = 30;
        public int SysMinSpinPct = 30;
        public int ApuMaxRpm = 3150;
        public int SysMaxRpm = 1940;
        public int[] PassApuMin = new int[0];
        public int[] PassSysMin = new int[0];
        public string CalibratedAt = "";
        public string SummaryReport = "";

        public static FanCalibrationResult RunCalibration(
            HardwareBackend hw,
            DemoBackend demo,
            bool demoMode,
            int stepDelayMs,
            int numPasses = 1,
            Action<FanCalibrationProgress> onProgress = null,
            Func<bool> checkCanceled = null)
        {
            if (numPasses < 1) numPasses = 1;
            if (numPasses > 5) numPasses = 5;

            FanCalibrationResult res = new FanCalibrationResult();
            res.PassApuMin = new int[numPasses];
            res.PassSysMin = new int[numPasses];

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("======================================================================");
            sb.AppendFormat("  MINIFANCONTROL - FAN CALIBRATION REPORT ({0}-PASS VERIFICATION)\r\n", numPasses);
            sb.AppendLine("======================================================================");

            int worstApuBare = 0;
            int worstSysBare = 0;
            int maxApu = 0;
            int maxSys = 0;

            if (!demoMode && stepDelayMs > 0 && stepDelayMs < 5000)
            {
                stepDelayMs = 5000; // Incorporate a 5-second wait before measuring fan speed for rotor stabilization
            }
            int totalUnits = numPasses * (CalibrationSteps.Length + 2);
            int completedUnits = 0;
            int tickMs = stepDelayMs >= 400 ? 250 : Math.Max(10, stepDelayMs / 3);

            try
            {
                for (int pass = 1; pass <= numPasses; pass++)
                {
                    if (checkCanceled != null && checkCanceled())
                    {
                        res.WasCanceled = true;
                        break;
                    }

                    string passHeader = string.Format("--- PASS {0} OF {1} (Spin-Down to 0 RPM + 10-Step Sweep) ---", pass, numPasses);
                    sb.AppendLine(passHeader);
                    EmitProgress(onProgress, pass, numPasses, 0, CalcPct(completedUnits, totalUnits),
                        string.Format("Pass {0}/{1} • Stopping fans (waiting for 0 RPM coast-down)...", pass, numPasses),
                        passHeader, new FanStatus());

                    // 1. Command 1% manual (0x81 = manual stop) and wait until rotors coast to 0 RPM
                    if (demoMode) { demo.SetFan(1, 1); demo.SetFan(2, 1); }
                    else { hw.SetFan(1, 1); hw.SetFan(2, 1); }

                    int maxSpinDownMs = stepDelayMs > 0 ? Math.Max(stepDelayMs * 2, 5500) : 0;
                    if (demoMode && stepDelayMs < 500) maxSpinDownMs = stepDelayMs * 2;

                    int elapsedSpinDown = 0;
                    int zeroConsecutive = 0;
                    FanStatus lastSt = demoMode ? demo.GetStatus() : hw.GetStatus();

                    while (elapsedSpinDown < maxSpinDownMs)
                    {
                        if (checkCanceled != null && checkCanceled()) { res.WasCanceled = true; break; }
                        Thread.Sleep(tickMs);
                        elapsedSpinDown += tickMs;
                        lastSt = demoMode ? demo.GetStatus() : hw.GetStatus();

                        if (lastSt.Fan1Rpm == 0 && lastSt.Fan2Rpm == 0)
                        {
                            zeroConsecutive++;
                            if (zeroConsecutive >= 4) // Confirmed 0 RPM for ~1 second
                                break;
                        }
                        else
                        {
                            zeroConsecutive = 0;
                        }

                        EmitProgress(onProgress, pass, numPasses, 0, CalcPct(completedUnits + 1, totalUnits),
                            string.Format("Pass {0}/{1} • Coasting to 0 RPM (APU: {2} RPM, Sys: {3} RPM, {4:0.0}s)...",
                                pass, numPasses, lastSt.Fan1Rpm, lastSt.Fan2Rpm, elapsedSpinDown / 1000.0),
                            null, lastSt);
                    }

                    if (res.WasCanceled) break;
                    completedUnits += 2;

                    string spinDownMsg = string.Format("  [Pass {0}] Baseline Stop (1% PWM) -> APU: {1,4} RPM | Sys: {2,4} RPM (after {3:0.0}s)",
                        pass, lastSt.Fan1Rpm, lastSt.Fan2Rpm, elapsedSpinDown / 1000.0);
                    sb.AppendLine(spinDownMsg);
                    EmitProgress(onProgress, pass, numPasses, 1, CalcPct(completedUnits, totalUnits),
                        string.Format("Pass {0}/{1} • 0 RPM baseline ready", pass, numPasses),
                        spinDownMsg, lastSt);

                    int passApuMin = 0;
                    int passSysMin = 0;

                    // 2. Step through CalibrationSteps (20%..100%) with stabilization polling
                    for (int sIdx = 0; sIdx < CalibrationSteps.Length; sIdx++)
                    {
                        if (checkCanceled != null && checkCanceled()) { res.WasCanceled = true; break; }

                        int pct = CalibrationSteps[sIdx];
                        if (demoMode) { demo.SetFan(1, pct); demo.SetFan(2, pct); }
                        else { hw.SetFan(1, pct); hw.SetFan(2, pct); }

                        int elapsedStep = 0;
                        int sumF1 = 0, sumF2 = 0, sampleCount = 0;

                        if (stepDelayMs <= 0)
                        {
                            lastSt = demoMode ? demo.GetStatus() : hw.GetStatus();
                            sumF1 = lastSt.Fan1Rpm;
                            sumF2 = lastSt.Fan2Rpm;
                            sampleCount = 1;
                        }
                        else
                        {
                            while (elapsedStep < stepDelayMs)
                            {
                                if (checkCanceled != null && checkCanceled()) { res.WasCanceled = true; break; }
                                int sleepChunk = Math.Min(tickMs, stepDelayMs - elapsedStep);
                                Thread.Sleep(sleepChunk);
                                elapsedStep += sleepChunk;

                                lastSt = demoMode ? demo.GetStatus() : hw.GetStatus();

                                // Average readings during the final 35% of the step window after rotor stabilizes
                                if (elapsedStep >= (stepDelayMs * 65) / 100 || elapsedStep >= stepDelayMs)
                                {
                                    sumF1 += lastSt.Fan1Rpm;
                                    sumF2 += lastSt.Fan2Rpm;
                                    sampleCount++;
                                }

                                double remSec = Math.Max(0.0, (stepDelayMs - elapsedStep) / 1000.0);
                                EmitProgress(onProgress, pass, numPasses, pct, CalcPct(completedUnits, totalUnits),
                                    string.Format("Pass {0}/{1} • Testing {2}% PWM (stabilizing {3:0.0}s left — APU: {4} RPM)",
                                        pass, numPasses, pct, remSec, lastSt.Fan1Rpm),
                                    null, lastSt);
                            }
                        }

                        if (res.WasCanceled) break;
                        completedUnits++;

                        int avgF1 = sampleCount > 0 ? (sumF1 / sampleCount) : lastSt.Fan1Rpm;
                        int avgF2 = sampleCount > 0 ? (sumF2 / sampleCount) : lastSt.Fan2Rpm;

                        bool apuJustSpun = (avgF1 >= 250 && passApuMin == 0);
                        bool sysJustSpun = (avgF2 >= 250 && passSysMin == 0);

                        if (apuJustSpun) passApuMin = pct;
                        if (sysJustSpun) passSysMin = pct;
                        if (avgF1 > maxApu) maxApu = avgF1;
                        if (avgF2 > maxSys) maxSys = avgF2;

                        string note = "";
                        if (apuJustSpun && sysJustSpun) note = "  <-- APU & Sys Spin-Up!";
                        else if (apuJustSpun) note = "  <-- APU Fan Spin-Up!";
                        else if (sysJustSpun) note = "  <-- Sys Fan Spin-Up!";

                        string stepLine = string.Format("  [Pass {0}] Step {1,3}% PWM -> APU Fan: {2,4} RPM | Sys Fan: {3,4} RPM{4}",
                            pass, pct, avgF1, avgF2, note);
                        sb.AppendLine(stepLine);

                        EmitProgress(onProgress, pass, numPasses, pct, CalcPct(completedUnits, totalUnits),
                            string.Format("Pass {0}/{1} • Step {2}% complete (APU: {3} RPM, Sys: {4} RPM)",
                                pass, numPasses, pct, avgF1, avgF2),
                            stepLine, lastSt);
                    }

                    if (res.WasCanceled) break;

                    res.PassApuMin[pass - 1] = passApuMin;
                    res.PassSysMin[pass - 1] = passSysMin;
                    res.PassesCompleted = pass;

                    if (passApuMin > worstApuBare) worstApuBare = passApuMin;
                    if (passSysMin > worstSysBare) worstSysBare = passSysMin;
                }
            }
            finally
            {
                try
                {
                    if (demoMode) demo.SetAuto();
                    else hw.SetAuto();
                }
                catch { }
            }

            res.HasDualFans = (maxSys >= 250);
            res.ApuBareSpinPct = worstApuBare > 0 ? worstApuBare : 28;
            res.SysBareSpinPct = worstSysBare > 0 ? worstSysBare : 28;

            // Add +2% safety margin above the highest bare spin-up threshold across all passes
            res.ApuMinSpinPct = worstApuBare > 0 ? Math.Max(20, Math.Min(45, worstApuBare + 2)) : 30;
            res.SysMinSpinPct = worstSysBare > 0 ? Math.Max(20, Math.Min(45, worstSysBare + 2)) : 30;
            res.ApuMaxRpm = maxApu > 0 ? maxApu : 3150;
            res.SysMaxRpm = maxSys > 0 ? maxSys : (res.HasDualFans ? 1940 : 0);
            res.CalibratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            sb.AppendLine("----------------------------------------------------------------------");
            if (res.WasCanceled)
            {
                sb.AppendLine("  STATUS                : CANCELED BY USER (Fans restored to Auto)");
            }
            else
            {
                sb.AppendFormat("  Calibrated On         : {0}\r\n", res.CalibratedAt);
                sb.AppendFormat("  Passes Completed      : {0} / {1}\r\n", res.PassesCompleted, numPasses);
                if (numPasses > 1)
                {
                    sb.AppendFormat("  APU Bare Spin-Up/Pass : [{0}]% PWM\r\n", string.Join("%, ", Array.ConvertAll(res.PassApuMin, x => x.ToString())));
                    if (res.HasDualFans)
                        sb.AppendFormat("  Sys Bare Spin-Up/Pass : [{0}]% PWM\r\n", string.Join("%, ", Array.ConvertAll(res.PassSysMin, x => x.ToString())));
                }
                sb.AppendFormat("  Detected Fan Layout   : {0}\r\n", res.HasDualFans ? "2 Fans (APU Fan + System RAM/NVMe Fan)" : "1 Fan (APU Fan Only)");
                sb.AppendFormat("  APU Safe Min / Max RPM: {0}% PWM (Bare {1}% + 2% margin) / {2} RPM\r\n",
                    res.ApuMinSpinPct, res.ApuBareSpinPct, res.ApuMaxRpm);
                if (res.HasDualFans)
                    sb.AppendFormat("  Sys Safe Min / Max RPM: {0}% PWM (Bare {1}% + 2% margin) / {2} RPM\r\n",
                        res.SysMinSpinPct, res.SysBareSpinPct, res.SysMaxRpm);
                else
                    sb.AppendLine("  Sys Fan               : Not Detected (Single-Fan Mode Enabled)");
            }
            sb.AppendLine("======================================================================");

            res.SummaryReport = sb.ToString();
            return res;
        }

        private static int CalcPct(int done, int total)
        {
            if (total <= 0) return 0;
            return Math.Max(0, Math.Min(100, (done * 100) / total));
        }

        private static void EmitProgress(
            Action<FanCalibrationProgress> cb,
            int pass,
            int totalPasses,
            int stepPct,
            int progressPct,
            string phaseTitle,
            string logLine,
            FanStatus live)
        {
            if (cb == null) return;
            cb(new FanCalibrationProgress
            {
                CurrentPass = pass,
                TotalPasses = totalPasses,
                CurrentStepPct = stepPct,
                ProgressPercent = progressPct,
                PhaseTitle = phaseTitle,
                LogLine = logLine,
                LiveStatus = live
            });
        }
    }

}
