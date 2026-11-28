using System;
using System.Collections.Generic;

namespace MiniFanControl
{
    /// <summary>
    /// Shared Digital Foundry-style performance graph math used by both
    /// CapFrameXOverlayForm (in-game OSD) and LivePerformanceCanvas (Live Perf tab):
    /// p90 dynamic FPS/frametime ceiling ladders, hysteresis hold counters,
    /// clean axis tick generation, rolling FPS smoothing, and timeline labels.
    /// </summary>
    public static class DfGraphMath
    {
        public static int CalculateFpsCeiling(int curLiveFps, IList<int> fpsHistory)
        {
            int total = (fpsHistory != null) ? fpsHistory.Count : 0;
            int lookback = Math.Min(total, 20);
            int activeMaxFps = curLiveFps > 0 ? curLiveFps : 60;
            if (lookback > 0)
            {
                List<int> recent = new List<int>(lookback);
                for (int i = 0; i < lookback; i++)
                {
                    int f = fpsHistory[total - lookback + i];
                    if (f > 0) recent.Add(f);
                }
                if (recent.Count > 0)
                {
                    recent.Sort();
                    int p90Idx = Math.Min(recent.Count - 1, (int)Math.Ceiling(recent.Count * 0.90) - 1);
                    activeMaxFps = Math.Max(recent[p90Idx], curLiveFps);
                }
            }

            if (activeMaxFps > 235)
                return (int)(Math.Ceiling(activeMaxFps / 60.0) * 60);
            if (activeMaxFps > 170) return 240;
            if (activeMaxFps > 115) return 180;
            if (activeMaxFps > 75) return 120;
            if (activeMaxFps > 38) return 80; // 58-62 FPS stays rock solid at 80 ceiling with 20 FPS headroom above 60 line
            return 60;
        }

        public static int CalculateFrametimeCeiling(double curFtMs, IList<int> ftHistory)
        {
            double activeMaxFt = curFtMs > 0 ? curFtMs : 16.6;
            if (ftHistory != null && ftHistory.Count > 0)
            {
                int total = ftHistory.Count;
                int lookback = Math.Min(total, 30);
                List<double> recent = new List<double>(lookback);
                for (int i = 0; i < lookback; i++)
                {
                    int raw = ftHistory[total - lookback + i];
                    if (raw > 0) recent.Add(raw / 10.0);
                }
                if (recent.Count > 0)
                {
                    recent.Sort();
                    int p90 = Math.Min(recent.Count - 1, (int)Math.Ceiling(recent.Count * 0.90) - 1);
                    activeMaxFt = Math.Max(recent[p90], curFtMs);
                }
            }

            if (activeMaxFt > 95.0)
                return Math.Min(200, (int)(Math.Ceiling(activeMaxFt / 50.0) * 50));
            if (activeMaxFt > 65.0) return 100;
            if (activeMaxFt > 38.0) return 75;
            return 50; // Zoomed in default for clean pacing (16.6ms / 19.3ms gameplay)
        }

        /// <summary>
        /// Updates a dynamic chart ceiling with immediate scale-out (zero clipping on spikes)
        /// and a smooth hold window before stepping back down.
        /// </summary>
        public static int UpdateCeilingWithHysteresis(int targetCeiling, ref int currentCeiling, ref int holdCounter, int holdUpdates = 12)
        {
            if (targetCeiling > currentCeiling)
            {
                currentCeiling = targetCeiling;
                holdCounter = 0;
            }
            else if (targetCeiling < currentCeiling)
            {
                holdCounter++;
                if (holdCounter >= holdUpdates)
                {
                    currentCeiling = targetCeiling;
                    holdCounter = 0;
                }
            }
            else
            {
                holdCounter = 0;
            }
            return currentCeiling;
        }

        public static int[] GetFpsAxisLevels(int fpsCeiling)
        {
            if (fpsCeiling == 60) return new int[] { 60, 40, 20, 0 };
            if (fpsCeiling == 80) return new int[] { 80, 60, 40, 20, 0 };
            if (fpsCeiling == 90) return new int[] { 90, 60, 30, 0 };
            if (fpsCeiling == 120) return new int[] { 120, 80, 40, 0 };
            if (fpsCeiling == 180) return new int[] { 180, 120, 60, 0 };
            if (fpsCeiling == 240) return new int[] { 240, 160, 80, 0 };
            int step = Math.Max(1, fpsCeiling / 3);
            return new int[] { fpsCeiling, step * 2, step, 0 };
        }

        /// <summary>
        /// 3-point weighted rolling average (0.2, 0.6, 0.2) keeping the leading edge exact to live FPS.
        /// </summary>
        public static float SampleSmoothedFps3Pt(IList<int> history, int start, int take, int i, int curLiveFps)
        {
            if (history == null || history.Count == 0) return 0f;
            int idx = start + i;
            if (idx < 0 || idx >= history.Count) return 0f;
            if (take <= 2) return Math.Max(0, history[idx]);
            if (i == 0)
            {
                float f0 = Math.Max(0, history[idx]);
                float f1 = (idx + 1 < history.Count) ? Math.Max(0, history[idx + 1]) : f0;
                return f0 * 0.75f + f1 * 0.25f;
            }
            if (i == take - 1)
                return (curLiveFps > 0) ? curLiveFps : Math.Max(0, history[idx]);

            float fPrev = Math.Max(0, history[idx - 1]);
            float fCurr = Math.Max(0, history[idx]);
            float fNext = (idx + 1 < history.Count) ? Math.Max(0, history[idx + 1]) : fCurr;
            return fPrev * 0.2f + fCurr * 0.6f + fNext * 0.2f;
        }

        /// <summary>
        /// 5-point Gaussian-weighted rolling average (0.1, 0.2, 0.4, 0.2, 0.1) keeping the leading edge exact to live FPS.
        /// </summary>
        public static float SampleSmoothedFpsGaussian5Pt(IList<int> history, int start, int take, int i, int curLiveFps)
        {
            if (history == null || history.Count == 0) return 0f;
            int idx = start + i;
            if (idx < 0 || idx >= history.Count) return 0f;
            if (take <= 2) return Math.Max(0, history[idx]);
            if (i == take - 1) return (curLiveFps > 0) ? curLiveFps : Math.Max(0, history[idx]);

            float[] weights = new float[] { 0.10f, 0.20f, 0.40f, 0.20f, 0.10f };
            float sum = 0f;
            float wSum = 0f;
            for (int k = -2; k <= 2; k++)
            {
                int sIdx = idx + k;
                if (sIdx >= start && sIdx < start + take && sIdx < history.Count)
                {
                    float w = weights[k + 2];
                    sum += Math.Max(0, history[sIdx]) * w;
                    wSum += w;
                }
            }
            return wSum > 0f ? (sum / wSum) : Math.Max(0, history[idx]);
        }

        public static string FormatSampleTimeAgo(int samplesAgo, int sampleIntervalMs)
        {
            if (samplesAgo <= 0) return "Now";
            double secPerSample = Math.Max(0.1, sampleIntervalMs / 1000.0);
            double secs = samplesAgo * secPerSample;
            if (secPerSample < 0.95)
                return string.Format("-{0:0.0}s", secs);
            return string.Format("-{0:0}s", secs);
        }
    }
}
