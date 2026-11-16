// Copyright (C) MiniFanControl and Contributors.
// Licensed under the MIT License.

using System;
using System.Diagnostics;

namespace MiniFanControl
{
    /// <summary>
    /// Limits how often a slow hardware sensor source (e.g. JEDEC SPD5118 hub reads over SMBus,
    /// NVMe SMART attributes over IOCTL/ATA) is accessed, independently of the high-speed polling timer.
    /// Sensors keep their last sampled value in between.
    /// The first call reads immediately. Subsequent reads follow a fixed grid of Interval, shifted by
    /// the phase: poll jitter does not stretch the interval, and devices with different phases do not
    /// read in the same poll, preventing SMBus and PCIe bus contention.
    /// A gap longer than one interval (e.g. paused or suspended polling) restarts the grid from now.
    /// </summary>
    public sealed class RefreshThrottle
    {
        private static readonly double MillisecondsPerTick = 1000.0 / Stopwatch.Frequency;

        private readonly Func<long> _clockMilliseconds;
        private readonly long _phaseMilliseconds;
        private bool _started;
        private long _nextDueMilliseconds;

        /// <summary>
        /// Default high-resolution monotonic clock in milliseconds.
        /// Guaranteed not to wrap or overflow across .NET Framework 4.8 and modern .NET.
        /// </summary>
        public static long GetMonotonicMilliseconds()
        {
            return (long)(Stopwatch.GetTimestamp() * MillisecondsPerTick);
        }

        /// <param name="interval">Minimum time between two reads.</param>
        /// <param name="phase">Delays the second read and thereby subsequent ones to interleave bus access.</param>
        /// <param name="clockMilliseconds">Monotonic clock provider in milliseconds; defaults to high-resolution Stopwatch clock.</param>
        public RefreshThrottle(TimeSpan interval, TimeSpan phase = default(TimeSpan), Func<long> clockMilliseconds = null)
        {
            Interval = interval;
            _phaseMilliseconds = Math.Max(0L, (long)phase.TotalMilliseconds);
            _clockMilliseconds = clockMilliseconds ?? GetMonotonicMilliseconds;
        }

        /// <summary>
        /// Gets or sets the interval between reads.
        /// </summary>
        public TimeSpan Interval { get; set; }

        /// <summary>
        /// Returns true when the source should be read now and schedules the next read.
        /// </summary>
        public bool TryBegin()
        {
            long now = _clockMilliseconds();
            long interval = (long)Interval.TotalMilliseconds;

            if (!_started)
            {
                _started = true;
                _nextDueMilliseconds = now + interval + _phaseMilliseconds;
                return true;
            }

            if (interval <= 0)
                return true;

            if (now < _nextDueMilliseconds)
                return false;

            // On time: advance strictly along the grid.
            // After a gap longer than one interval: restart the grid from now.
            _nextDueMilliseconds = (now - _nextDueMilliseconds < interval)
                ? _nextDueMilliseconds + interval
                : now + interval;

            return true;
        }

        /// <summary>
        /// Resets the throttle state, causing the next TryBegin call to return true immediately.
        /// </summary>
        public void Reset()
        {
            _started = false;
            _nextDueMilliseconds = 0;
        }
    }
}
