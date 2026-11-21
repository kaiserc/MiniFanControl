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
        public static readonly int[] CalibrationSteps = new int[] { 12, 15, 18, 20, 22, 24, 26, 28, 30, 32, 35, 40, 45, 50, 55, 60, 70, 80, 90, 100 };
        public const int CALIBRATION_THERMAL_ABORT_C = 88;

        public bool HasDualFans = true;
        public bool WasCanceled = false;
        public bool WasThermalAborted = false;
        public int ThermalAbortTempC = 0;
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
            bool apuCanStopToZero = false;
            bool sysCanStopToZero = false;

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

                    string passHeader = string.Format("--- PASS {0} OF {1} (Spin-Down to 0 RPM + {2}-Step Sweep) ---", pass, numPasses, CalibrationSteps.Length);
                    sb.AppendLine(passHeader);
                    EmitProgress(onProgress, pass, numPasses, 0, CalcPct(completedUnits, totalUnits),
                        string.Format("Pass {0}/{1} • Stopping fans (waiting for 0 RPM coast-down)...", pass, numPasses),
                        passHeader, new FanStatus());

                    // 1. Command 1% manual (0x81 = manual stop) and wait until rotors coast to 0 RPM.
                    // Reset tachometer debounce state so 0 RPM coast-down detection has zero filter lag on any EC/Super I/O.
                    HardwareBackend.ResetTachometerFilterState();
                    if (demoMode) { demo.SetFan(1, 1); demo.SetFan(2, 1); }
                    else { hw.SetFan(1, 1); hw.SetFan(2, 1); }

                    int maxSpinDownMs = stepDelayMs > 0
                        ? (demoMode && stepDelayMs < 500 ? stepDelayMs * 2 : Math.Max(stepDelayMs * 7, 35000))
                        : 0;

                    int elapsedSpinDown = 0;
                    int zeroConsecutive = 0;
                    int lastKeepAliveMs = 0;
                    List<int> recentF1 = new List<int>();
                    List<int> recentF2 = new List<int>();
                    FanStatus lastSt = demoMode ? demo.GetStatus() : hw.GetStatus();

                    while (elapsedSpinDown < maxSpinDownMs)
                    {
                        if (checkCanceled != null && checkCanceled()) { res.WasCanceled = true; break; }
                        Thread.Sleep(tickMs);
                        elapsedSpinDown += tickMs;

                        // Re-assert 1% manual stop every 2s so EC slew/watchdog never reverts during long coast-down
                        if (!demoMode && elapsedSpinDown - lastKeepAliveMs >= 2000)
                        {
                            hw.SetFan(1, 1);
                            hw.SetFan(2, 1);
                            lastKeepAliveMs = elapsedSpinDown;
                        }

                        lastSt = demoMode ? demo.GetStatus() : hw.GetStatus();
                        int coastHotTemp = Math.Max(lastSt.CpuTemp, lastSt.GpuTemp);
                        if (coastHotTemp >= CALIBRATION_THERMAL_ABORT_C && coastHotTemp <= 115 && coastHotTemp != 127)
                        {
                            res.WasThermalAborted = true;
                            res.ThermalAbortTempC = coastHotTemp;
                            if (demoMode) { demo.SetFan(1, 100); demo.SetFan(2, 100); }
                            else { hw.SetFan(1, 100); hw.SetFan(2, 100); }
                            break;
                        }
                        if (lastSt.Fan1Rpm == 0) apuCanStopToZero = true;
                        if (lastSt.Fan2Rpm == 0) sysCanStopToZero = true;

                        recentF1.Add(lastSt.Fan1Rpm);
                        recentF2.Add(lastSt.Fan2Rpm);
                        if (recentF1.Count > 8) recentF1.RemoveAt(0);
                        if (recentF2.Count > 8) recentF2.RemoveAt(0);

                        if (lastSt.Fan1Rpm == 0 && lastSt.Fan2Rpm == 0)
                        {
                            zeroConsecutive++;
                            if (zeroConsecutive >= 4) // Confirmed 0 RPM on both fans for ~1 second
                                break;
                        }
                        else
                        {
                            zeroConsecutive = 0;

                            // If hardware has a non-zero minimum floor at 1% PWM, only stop waiting after >= 15s
                            // if neither fan is still decelerating over a full 2-second (8-sample) window
                            // and any fan known to support 0 RPM has already reached 0 RPM.
                            if (!demoMode && elapsedSpinDown >= 15000 && recentF1.Count >= 8)
                            {
                                int dropF1 = recentF1[0] - lastSt.Fan1Rpm;
                                int dropF2 = recentF2[0] - lastSt.Fan2Rpm;
                                bool f1Settled = (lastSt.Fan1Rpm == 0) || (!apuCanStopToZero && Math.Abs(dropF1) <= 15);
                                bool f2Settled = (lastSt.Fan2Rpm == 0) || (!sysCanStopToZero && Math.Abs(dropF2) <= 15);
                                if (f1Settled && f2Settled)
                                    break;
                            }
                        }

                        EmitProgress(onProgress, pass, numPasses, 0, CalcPct(completedUnits + 1, totalUnits),
                            string.Format("Pass {0}/{1} • Coasting to 0 RPM (APU: {2} RPM, Sys: {3} RPM, {4:0.0}s)...",
                                pass, numPasses, lastSt.Fan1Rpm, lastSt.Fan2Rpm, elapsedSpinDown / 1000.0),
                            null, lastSt);
                    }

                    if (res.WasCanceled || res.WasThermalAborted) break;
                    completedUnits += 2;

                    string spinDownMsg = string.Format("  [Pass {0}] Baseline Stop (1% PWM) -> APU: {1,4} RPM | Sys: {2,4} RPM (after {3:0.0}s)",
                        pass, lastSt.Fan1Rpm, lastSt.Fan2Rpm, elapsedSpinDown / 1000.0);
                    sb.AppendLine(spinDownMsg);
                    EmitProgress(onProgress, pass, numPasses, 1, CalcPct(completedUnits, totalUnits),
                        string.Format("Pass {0}/{1} • 0 RPM baseline ready", pass, numPasses),
                        spinDownMsg, lastSt);

                    int passApuMin = 0;
                    int passSysMin = 0;
                    int prevStepApuRpm = lastSt.Fan1Rpm;
                    int prevStepSysRpm = lastSt.Fan2Rpm;

                    // 2. Step through CalibrationSteps (20%..100%) with stabilization polling
                    for (int sIdx = 0; sIdx < CalibrationSteps.Length; sIdx++)
                    {
                        if (checkCanceled != null && checkCanceled()) { res.WasCanceled = true; break; }

                        int pct = CalibrationSteps[sIdx];
                        if (demoMode) { demo.SetFan(1, pct); demo.SetFan(2, pct); }
                        else { hw.SetFan(1, pct); hw.SetFan(2, pct); }

                        int elapsedStep = 0;
                        List<int> f1Samples = new List<int>();
                        List<int> f2Samples = new List<int>();

                        if (stepDelayMs <= 0)
                        {
                            lastSt = demoMode ? demo.GetStatus() : hw.GetStatus();
                            f1Samples.Add(lastSt.Fan1Rpm);
                            f2Samples.Add(lastSt.Fan2Rpm);
                        }
                        else
                        {
                            int targetStepMs = stepDelayMs;
                            // Allow longer settle on 100% (up to +6s) and intermediate steps (+3s) to accommodate
                            // both fast Super I/O blowers and slow-slewing laptop/mini-PC EC firmware.
                            int maxExtraSettleMs = (!demoMode && stepDelayMs >= 1000) ? (pct >= 100 ? 6000 : 3000) : 0;
                            Queue<int> f1SlewWindow = new Queue<int>();
                            Queue<int> f2SlewWindow = new Queue<int>();
                            f1SlewWindow.Enqueue(lastSt.Fan1Rpm);
                            f2SlewWindow.Enqueue(lastSt.Fan2Rpm);

                            while (elapsedStep < targetStepMs)
                            {
                                if (checkCanceled != null && checkCanceled()) { res.WasCanceled = true; break; }
                                int sleepChunk = Math.Min(tickMs, targetStepMs - elapsedStep);
                                Thread.Sleep(sleepChunk);
                                elapsedStep += sleepChunk;

                                lastSt = demoMode ? demo.GetStatus() : hw.GetStatus();
                                int stepHotTemp = Math.Max(lastSt.CpuTemp, lastSt.GpuTemp);
                                if (stepHotTemp >= CALIBRATION_THERMAL_ABORT_C && stepHotTemp <= 115 && stepHotTemp != 127)
                                {
                                    res.WasThermalAborted = true;
                                    res.ThermalAbortTempC = stepHotTemp;
                                    if (demoMode) { demo.SetFan(1, 100); demo.SetFan(2, 100); }
                                    else { hw.SetFan(1, 100); hw.SetFan(2, 100); }
                                    break;
                                }

                                f1SlewWindow.Enqueue(lastSt.Fan1Rpm);
                                f2SlewWindow.Enqueue(lastSt.Fan2Rpm);
                                while (f1SlewWindow.Count > 5) f1SlewWindow.Dequeue(); // ~1.0s window at 250ms tick
                                while (f2SlewWindow.Count > 5) f2SlewWindow.Dequeue();

                                // If at the end of the nominal step window the rotor is still accelerating/decelerating
                                // (> 40 RPM change over the last ~1.0 second), extend stabilization for true steady-state RPM.
                                if (maxExtraSettleMs > 0 && elapsedStep >= targetStepMs && targetStepMs < stepDelayMs + maxExtraSettleMs)
                                {
                                    bool f1Moving = (f1SlewWindow.Count >= 4 && Math.Abs(lastSt.Fan1Rpm - f1SlewWindow.Peek()) > 40);
                                    bool f2Moving = (f2SlewWindow.Count >= 4 && Math.Abs(lastSt.Fan2Rpm - f2SlewWindow.Peek()) > 40);
                                    if (f1Moving || f2Moving)
                                    {
                                        targetStepMs += tickMs * 2;
                                        f1Samples.Clear();
                                        f2Samples.Clear();
                                    }
                                }

                                // Collect readings during the final 35% of the step window after rotor stabilizes
                                if (elapsedStep >= (targetStepMs * 65) / 100 || elapsedStep >= targetStepMs)
                                {
                                    f1Samples.Add(lastSt.Fan1Rpm);
                                    f2Samples.Add(lastSt.Fan2Rpm);
                                }

                                double remSec = Math.Max(0.0, (targetStepMs - elapsedStep) / 1000.0);
                                EmitProgress(onProgress, pass, numPasses, pct, CalcPct(completedUnits, totalUnits),
                                    string.Format("Pass {0}/{1} • Testing {2}% PWM (stabilizing {3:0.0}s left — APU: {4} RPM)",
                                        pass, numPasses, pct, remSec, lastSt.Fan1Rpm),
                                    null, lastSt);
                            }
                        }

                        if (res.WasCanceled || res.WasThermalAborted) break;
                        completedUnits++;

                        int avgF1 = ComputeRobustStepRpm(f1Samples, lastSt.Fan1Rpm);
                        int avgF2 = ComputeRobustStepRpm(f2Samples, lastSt.Fan2Rpm);

                        // Do not falsely mark a fan as "Spin-Up" if it never stopped at baseline AND is still decelerating
                        bool f1CoastingDown = (prevStepApuRpm >= 250 && avgF1 < prevStepApuRpm - 50);
                        bool f2CoastingDown = (prevStepSysRpm >= 250 && avgF2 < prevStepSysRpm - 50);

                        bool apuJustSpun = (avgF1 >= 250 && passApuMin == 0 && !f1CoastingDown);
                        bool sysJustSpun = (avgF2 >= 250 && passSysMin == 0 && !f2CoastingDown);

                        prevStepApuRpm = avgF1;
                        prevStepSysRpm = avgF2;

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

                    if (res.WasCanceled || res.WasThermalAborted) break;

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
            res.ApuMinSpinPct = worstApuBare > 0 ? Math.Max(14, Math.Min(45, worstApuBare + 2)) : 30;
            res.SysMinSpinPct = worstSysBare > 0 ? Math.Max(14, Math.Min(45, worstSysBare + 2)) : 30;
            res.ApuMaxRpm = maxApu > 0 ? maxApu : 3150;
            res.SysMaxRpm = maxSys > 0 ? maxSys : (res.HasDualFans ? 1940 : 0);
            res.CalibratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            sb.AppendLine("----------------------------------------------------------------------");
            if (res.WasThermalAborted)
            {
                sb.AppendFormat("  STATUS                : THERMAL SAFETY ABORT (APU hit {0}°C >= {1}°C — Fans restored to Auto)\r\n",
                    res.ThermalAbortTempC, CALIBRATION_THERMAL_ABORT_C);
                sb.AppendLine("  ACTION                : Close background CPU/GPU workloads and re-run calibration at idle.");
            }
            else if (res.WasCanceled)
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

        public static int ComputeRobustStepRpm(List<int> samples, int fallbackRpm = 0)
        {
            if (samples == null || samples.Count == 0)
                return (fallbackRpm > 0 && fallbackRpm < 7500) ? fallbackRpm : 0;

            List<int> valid = new List<int>(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                int v = samples[i];
                if (v >= 0 && v < 7500)
                    valid.Add(v);
            }
            if (valid.Count == 0)
                return 0;

            valid.Sort();

            // When 4+ samples exist, discard the min and max extremes (trimmed mean) to reject single-sample tach spikes
            int start = valid.Count >= 4 ? 1 : 0;
            int end = valid.Count >= 4 ? valid.Count - 1 : valid.Count;
            int sum = 0;
            int count = 0;
            for (int i = start; i < end; i++)
            {
                sum += valid[i];
                count++;
            }
            return count > 0 ? (sum / count) : valid[valid.Count / 2];
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
