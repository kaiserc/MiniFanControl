using System;
using System.Threading;

namespace MiniFanControl
{
    /// <summary>
    /// Thread-safe hardware polling, fan curve regulation, thermal tripwire, and failsafe state machine.
    /// Decouples hardware I/O and control state from WinForms UI threads.
    /// </summary>
    public class FanControllerService : IDisposable
    {
        private readonly HardwareBackend _hw;
        private readonly DemoBackend _demo;
        private readonly FanCurveProfile[] _builtInProfiles;
        private readonly object _stateLock = new object();

        private bool _demoMode;
        private FanCurveProfile _apuProfile;
        private FanCurveProfile _sysProfile;
        private bool _hasDualFans = true;
        private bool _apuManualOverride = false;
        private bool _sysManualOverride = false;
        private int _apuManualPct = 0;
        private int _sysManualPct = 0;
        private int _lastAppliedApuPct = -1;
        private int _lastAppliedSysPct = -1;
        private int _lastAppliedEcMode = -1;
        private int _biosPowerModeOverride = -1;
        private int _lastCustomEvalTemp = -100;
        private bool _emergencyThermalActive = false;

        private string _lastHardwareWriteError = null;
        private int _consecutiveWriteFailures = 0;
        private int _fan2ConsecutiveWriteFailures = 0;
        private bool _readOnlyFailsafeLatched = false;
        private bool _latchedAutoRestored = false;
        private DateTime _lastLatchedAutoRetryUtc = DateTime.MinValue;
        public static readonly TimeSpan LatchedAutoRetryInterval = TimeSpan.FromSeconds(5);

        private FanStatus _lastStatus;
        private DateTime _rampUntil = DateTime.MinValue;
        private int _refreshIntervalMs = 1000;
        private long _lastWorkerSampleUtcTicks = DateTime.UtcNow.Ticks;

        private Thread _hwThread;
        private volatile bool _hwThreadRunning;
        private volatile bool _hwWorkerPaused;
        private readonly AutoResetEvent _hwWakeEvent = new AutoResetEvent(false);

        public event Action<FanStatus> SampleReceived;
        public event Action<Exception> WorkerError;
        public event Action DualFanAutoAdapted;
        public event Action<string, int> WriteErrorStateChanged;
        public event Action<int> EscalatedFailsafeTriggered;
        public event Action WorkerIterationCompleted;

        public FanControllerService(HardwareBackend hw, DemoBackend demo, FanCurveProfile[] builtInProfiles, bool demoMode)
        {
            _hw = hw;
            _demo = demo;
            _builtInProfiles = builtInProfiles;
            _demoMode = demoMode;
            if (_builtInProfiles != null && _builtInProfiles.Length > 0)
            {
                _apuProfile = _builtInProfiles[0];
                _sysProfile = _builtInProfiles[0];
            }
            if (_demoMode && _demo != null)
            {
                try { _lastStatus = _demo.GetStatus(); }
                catch (Exception ex) { WatchdogSupervisor.LogWatchdogEvent("Demo initial status read failed: " + ex.Message); }
            }
        }

        public bool DemoMode
        {
            get { lock (_stateLock) return _demoMode; }
            set { lock (_stateLock) _demoMode = value; }
        }

        public FanCurveProfile ApuProfile
        {
            get { lock (_stateLock) return _apuProfile; }
            set { lock (_stateLock) _apuProfile = value; }
        }

        public FanCurveProfile SysProfile
        {
            get { lock (_stateLock) return _sysProfile; }
            set { lock (_stateLock) _sysProfile = value; }
        }

        public bool HasDualFans
        {
            get { lock (_stateLock) return _hasDualFans; }
            set { lock (_stateLock) _hasDualFans = value; }
        }

        public bool ApuManualOverride { get { lock (_stateLock) return _apuManualOverride; } }
        public bool SysManualOverride { get { lock (_stateLock) return _sysManualOverride; } }
        public int ApuManualPct { get { lock (_stateLock) return _apuManualPct; } }
        public int SysManualPct { get { lock (_stateLock) return _sysManualPct; } }
        public int LastAppliedApuPct { get { lock (_stateLock) return _lastAppliedApuPct; } }
        public int LastAppliedSysPct { get { lock (_stateLock) return _lastAppliedSysPct; } }
        public int LastAppliedEcMode { get { lock (_stateLock) return _lastAppliedEcMode; } }
        public int BiosPowerModeOverride
        {
            get { lock (_stateLock) return _biosPowerModeOverride; }
            set { lock (_stateLock) { _biosPowerModeOverride = (value >= 0 && value <= 2) ? value : -1; _lastAppliedEcMode = -1; } }
        }
        public string LastHardwareWriteError { get { lock (_stateLock) return _lastHardwareWriteError; } }
        public int ConsecutiveWriteFailures { get { lock (_stateLock) return _consecutiveWriteFailures; } }
        public bool ReadOnlyFailsafeLatched { get { lock (_stateLock) return _readOnlyFailsafeLatched; } }
        public bool LatchedAutoRestored { get { lock (_stateLock) return _latchedAutoRestored; } }
        public bool EmergencyThermalActive { get { lock (_stateLock) return _emergencyThermalActive; } }
        public FanStatus LastStatus { get { lock (_stateLock) return _lastStatus; } }
        public bool IsWorkerPaused { get { return _hwWorkerPaused; } }

        public int RefreshIntervalMs
        {
            get { lock (_stateLock) return _refreshIntervalMs; }
            set { lock (_stateLock) _refreshIntervalMs = Math.Max(100, Math.Min(10000, value)); }
        }

        public DateTime LastWorkerSampleUtc
        {
            get { return new DateTime(Interlocked.Read(ref _lastWorkerSampleUtcTicks), DateTimeKind.Utc); }
        }

        public void SetInitialStatus(FanStatus status)
        {
            lock (_stateLock)
            {
                _lastStatus = status;
            }
        }

        public void SetRampUntil(DateTime utc)
        {
            lock (_stateLock)
            {
                _rampUntil = utc;
            }
        }

        public void InvalidateAppliedCache()
        {
            lock (_stateLock)
            {
                _lastAppliedApuPct = -1;
                _lastAppliedSysPct = -1;
                _lastAppliedEcMode = -1;
            }
        }

        public void Start()
        {
            if (_hwThreadRunning) return;
            _hwThreadRunning = true;
            _hwThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "MiniFanControl.HardwareWorker",
                Priority = ThreadPriority.AboveNormal
            };
            _hwThread.Start();
            _hwWakeEvent.Set();
        }

        public void Stop(int joinTimeoutMs = 500)
        {
            _hwThreadRunning = false;
            try { _hwWakeEvent.Set(); } catch { }
            if (_hwThread != null && _hwThread.IsAlive)
            {
                _hwThread.Join(joinTimeoutMs);
            }
        }

        public void RequestImmediateRefresh()
        {
            try { _hwWakeEvent.Set(); } catch { }
        }

        public void PauseForSuspend()
        {
            _hwWorkerPaused = true;
            lock (_stateLock)
            {
                _emergencyThermalActive = false;
            }
        }

        public void ResumeFromSuspend(bool reinitHardware)
        {
            if (reinitHardware && _hw != null)
            {
                _hw.ReinitializeHardwareAfterResume();
            }
            lock (_stateLock)
            {
                _lastAppliedApuPct = -1;
                _lastAppliedSysPct = -1;
                _lastCustomEvalTemp = 0;
                _emergencyThermalActive = false;
            }
            Interlocked.Exchange(ref _lastWorkerSampleUtcTicks, DateTime.UtcNow.Ticks);
            _hwWorkerPaused = false;
            RequestImmediateRefresh();
        }

        public void SetProfileTarget(FanCurveProfile profile, bool editSys)
        {
            if (profile == null) return;
            lock (_stateLock)
            {
                if (editSys)
                {
                    _sysProfile = profile;
                    _sysManualOverride = false;
                    _lastAppliedSysPct = -1;
                }
                else
                {
                    _apuProfile = profile;
                    _apuManualOverride = false;
                    _lastAppliedApuPct = -1;
                }
                if (profile.IsBuiltIn)
                {
                    _biosPowerModeOverride = (profile.BuiltInCode == 3) ? 1 : profile.BuiltInCode;
                }
                _lastAppliedEcMode = -1;
            }
        }

        /// <summary>
        /// Changes the live BIOS/EC TDP Power Mode (0=Balanced, 1=Performance, 2=Silent via EC 0x32)
        /// independently without replacing active Custom Fan Curves or manual fan overrides.
        /// </summary>
        public bool SetBiosPowerMode(int modeCode)
        {
            if (modeCode < 0 || modeCode > 2) return false;
            bool demo;
            lock (_stateLock)
            {
                if (_readOnlyFailsafeLatched) return false;
                demo = _demoMode;
            }

            // Validate platform capability BEFORE mutating controller state
            if (!demo && _hw != null && _hw.SuperIoBaseAddr != 0)
            {
                return false;
            }

            lock (_stateLock)
            {
                _biosPowerModeOverride = modeCode;
                _lastAppliedEcMode = -1;
            }

            bool ok = demo ? _demo.SetMode(modeCode) : _hw.SetMode(modeCode);
            if (ok)
            {
                lock (_stateLock) { _lastAppliedEcMode = modeCode; }
                ClearHardwareWriteError();
                RequestImmediateRefresh();
            }
            else
            {
                RecordHardwareWriteError(string.Format("BIOS Power Mode change ({0}) verification failed", modeCode));
            }
            return ok;
        }

        public void OnProfileRemoved(FanCurveProfile removed)
        {
            if (removed == null || _builtInProfiles == null || _builtInProfiles.Length == 0) return;
            lock (_stateLock)
            {
                if (_apuProfile == removed)
                {
                    _apuProfile = _builtInProfiles[0];
                    _lastAppliedApuPct = -1;
                }
                if (_sysProfile == removed)
                {
                    _sysProfile = _builtInProfiles[0];
                    _lastAppliedSysPct = -1;
                }
            }
        }

        internal void ExpireLatchedAutoRetryTimerForTest()
        {
            lock (_stateLock) { _lastLatchedAutoRetryUtc = DateTime.MinValue; }
        }

        public bool ApplyManualFan(int fanId, int pct)
        {
            bool demo;
            lock (_stateLock)
            {
                if (_readOnlyFailsafeLatched) return false;
                demo = _demoMode;
                if (fanId == 1)
                {
                    _apuManualPct = Math.Max(0, Math.Min(100, pct));
                    _apuManualOverride = (_apuManualPct > 0);
                    _lastAppliedApuPct = -1;
                }
                else
                {
                    _sysManualPct = Math.Max(0, Math.Min(100, pct));
                    _sysManualOverride = (_sysManualPct > 0);
                    _lastAppliedSysPct = -1;
                }
                _lastAppliedEcMode = -1;
            }

            bool ok = demo ? _demo.SetFan(fanId, pct) : _hw.SetFan(fanId, pct);
            if (ok)
            {
                lock (_stateLock)
                {
                    if (fanId == 1) _lastAppliedApuPct = pct;
                    else _lastAppliedSysPct = pct;
                }
                if (fanId == 1 || (_hw == null || _hw.Profile == null || _hw.Profile.HasDualFans))
                {
                    ClearHardwareWriteError();
                }
            }
            else
            {
                RecordHardwareWriteError(string.Format("Manual {0} write ({1}%) verification failed", fanId == 1 ? "APU Fan" : "Sys Fan", pct));
            }
            return ok;
        }

        public bool ApplyAutoAll()
        {
            bool demo;
            lock (_stateLock)
            {
                demo = _demoMode;
                _readOnlyFailsafeLatched = false;
                _latchedAutoRestored = false;
                _lastLatchedAutoRetryUtc = DateTime.MinValue;
                _consecutiveWriteFailures = 0;
                _apuManualPct = 0;
                _sysManualPct = 0;
                _apuManualOverride = false;
                _sysManualOverride = false;
                _lastAppliedApuPct = -1;
                _lastAppliedSysPct = -1;
                _lastAppliedEcMode = -1;
                _biosPowerModeOverride = 0;
                if (_builtInProfiles != null && _builtInProfiles.Length > 0)
                {
                    _apuProfile = _builtInProfiles[0];
                    _sysProfile = _builtInProfiles[0];
                }
            }

            if (_hw != null && _hw.Profile != null && !_hw.Profile.Id.Equals("unknown-safe", StringComparison.OrdinalIgnoreCase))
            {
                _hw.Profile.IsReadOnly = false;
                _hw.ReinitializeHardwareAfterResume();
            }

            bool ok = demo ? _demo.SetAuto() : _hw.SetAuto();
            if (ok)
            {
                ClearHardwareWriteError();
            }
            else
            {
                RecordHardwareWriteError("Reset both fans to Auto verification failed");
            }
            return ok;
        }

        public void RestoreSafeAutoOnExit(bool restoreAutoOnExit)
        {
            if (!restoreAutoOnExit) return;

            bool demo;
            bool apuWasSoftwareOrManual;
            bool sysWasSoftwareOrManual;
            bool hasDual;
            lock (_stateLock)
            {
                demo = _demoMode;
                if (demo) return;
                apuWasSoftwareOrManual = _apuManualOverride || (_apuProfile != null && !_apuProfile.IsBuiltIn) || _lastAppliedApuPct > 0;
                sysWasSoftwareOrManual = _sysManualOverride || (_sysProfile != null && !_sysProfile.IsBuiltIn) || _lastAppliedSysPct > 0;
                hasDual = _hasDualFans;
            }

            try
            {
                bool isSuperIo = (_hw != null && (_hw.SuperIoBaseAddr != 0 || _hw.AccessMethod == HardwareBackend.EcAccessMethod.SuperIo2E || _hw.AccessMethod == HardwareBackend.EcAccessMethod.SuperIo4E));
                if (isSuperIo)
                {
                    _hw.SetAuto();
                }
                else
                {
                    if (apuWasSoftwareOrManual)
                        _hw.SetFan(1, 0);
                    if (hasDual && sysWasSoftwareOrManual)
                        _hw.SetFan(2, 0);
                    _hw.SetAuto();
                }
            }
            catch (Exception ex)
            {
                WatchdogSupervisor.LogWatchdogEvent("RestoreSafeAutoOnExit failed: " + ex.Message);
            }
        }

        private bool ExecuteSafetyWriteWhileLatched(bool demo, Func<bool> hwAction, Func<bool> demoAction)
        {
            if (demo)
            {
                return _demo != null && demoAction();
            }
            if (_hw == null || _hw.Profile == null) return false;
            // Never bypass read-only protection on inherently unverified hardware (unknown-safe)
            if (!_hw.Profile.IsVerified || _hw.Profile.Id.Equals("unknown-safe", StringComparison.OrdinalIgnoreCase))
                return false;

            bool prevReadOnly = _hw.Profile.IsReadOnly;
            try
            {
                _hw.Profile.IsReadOnly = false;
                return hwAction();
            }
            finally
            {
                _hw.Profile.IsReadOnly = prevReadOnly;
            }
        }

        public bool EvaluateAndApplyFanProfiles(int apuTemp)
        {
            FanCurveProfile apuProf;
            FanCurveProfile sysProf;
            FanStatus snapshot;
            bool demo;
            bool hasDual;
            bool apuManual;
            bool sysManual;
            bool emergState;
            bool latched;
            bool latchedAutoRestored;
            DateTime lastLatchedRetryUtc;
            int lastApuPct;
            int lastSysPct;
            int lastEcMode;
            int biosOverride;
            int lastEvalTemp;

            lock (_stateLock)
            {
                if (_apuProfile == null && _builtInProfiles != null && _builtInProfiles.Length > 0) _apuProfile = _builtInProfiles[0];
                if (_sysProfile == null && _builtInProfiles != null && _builtInProfiles.Length > 0) _sysProfile = _builtInProfiles[0];
                apuProf = _apuProfile;
                sysProf = _sysProfile;
                snapshot = _lastStatus;
                demo = _demoMode;
                hasDual = _hasDualFans;
                apuManual = _apuManualOverride;
                sysManual = _sysManualOverride;
                emergState = _emergencyThermalActive;
                latched = _readOnlyFailsafeLatched;
                latchedAutoRestored = _latchedAutoRestored;
                lastLatchedRetryUtc = _lastLatchedAutoRetryUtc;
                lastApuPct = _lastAppliedApuPct;
                lastSysPct = _lastAppliedSysPct;
                lastEcMode = _lastAppliedEcMode;
                biosOverride = _biosPowerModeOverride;
                lastEvalTemp = _lastCustomEvalTemp;
            }

            bool wroteHardware = false;

            // Emergency Thermal Safety Guard (TJMax Tripwire):
            // ALWAYS evaluated BEFORE the read-only failsafe latch return so a latched system
            // never loses thermal protection if the prior SetAuto() also failed.
            // If APU temperature hits >= 90°C, bypass manual overrides and curves, forcing 100% PWM.
            // Hysteresis holds until temperature recovers below 75°C.
            bool tripped = HardwareBackend.CheckEmergencyThermalTripwire(apuTemp, ref emergState);
            lock (_stateLock) { _emergencyThermalActive = emergState; }

            if (tripped)
            {
                if (lastApuPct != 100)
                {
                    bool ok = latched
                        ? ExecuteSafetyWriteWhileLatched(demo, () => _hw.SetFan(1, 100), () => _demo.SetFan(1, 100))
                        : (demo ? _demo.SetFan(1, 100) : _hw.SetFan(1, 100));
                    if (ok)
                    {
                        lock (_stateLock) { _lastAppliedApuPct = 100; }
                        wroteHardware = true;
                        if (!latched) ClearHardwareWriteError();
                    }
                    else
                    {
                        RecordHardwareWriteError("Emergency APU Fan 100% write rejected / read-back verification failed");
                    }
                }
                if (hasDual && lastSysPct != 100)
                {
                    bool ok = latched
                        ? ExecuteSafetyWriteWhileLatched(demo, () => _hw.SetFan(2, 100), () => _demo.SetFan(2, 100))
                        : (demo ? _demo.SetFan(2, 100) : _hw.SetFan(2, 100));
                    if (ok)
                    {
                        lock (_stateLock) { _lastAppliedSysPct = 100; }
                        wroteHardware = true;
                        if (!latched) ClearHardwareWriteError();
                    }
                    else
                    {
                        RecordHardwareWriteError("Emergency Sys Fan 100% write rejected / read-back verification failed");
                    }
                }
                return wroteHardware;
            }

            // If latched in failsafe read-only mode, block normal curve/manual writes,
            // but periodically retry SetAuto() until BIOS automatic fan control is confirmed restored.
            if (latched)
            {
                if (!latchedAutoRestored && (DateTime.UtcNow - lastLatchedRetryUtc) >= LatchedAutoRetryInterval)
                {
                    lock (_stateLock) { _lastLatchedAutoRetryUtc = DateTime.UtcNow; }
                    bool retryAutoOk = ExecuteSafetyWriteWhileLatched(demo, () => _hw.SetAuto(), () => _demo.SetAuto());
                    if (retryAutoOk)
                    {
                        lock (_stateLock)
                        {
                            _latchedAutoRestored = true;
                            _lastAppliedApuPct = 0;
                            _lastAppliedSysPct = 0;
                        }
                        WatchdogSupervisor.LogWatchdogEvent("Latched failsafe periodic SetAuto() retry succeeded; BIOS Auto restored.");
                        return true;
                    }
                }
                return false;
            }

            int desiredEcMode = -1;
            bool isSuperIo = (_hw != null && _hw.SuperIoBaseAddr != 0);
            bool apuUsesNativeEcAuto = false;
            bool sysUsesNativeEcAuto = false;

            if (!isSuperIo)
            {
                if (!apuManual && apuProf != null && apuProf.IsBuiltIn)
                {
                    desiredEcMode = apuProf.BuiltInCode == 3 ? 1 : apuProf.BuiltInCode;
                    apuUsesNativeEcAuto = (apuProf.BuiltInCode != 3);
                    if (hasDual && !sysManual && sysProf != null && sysProf.IsBuiltIn && sysProf.BuiltInCode == apuProf.BuiltInCode)
                    {
                        sysUsesNativeEcAuto = (sysProf.BuiltInCode != 3);
                    }
                }
                else if (hasDual && !sysManual && sysProf != null && sysProf.IsBuiltIn)
                {
                    desiredEcMode = sysProf.BuiltInCode == 3 ? 1 : sysProf.BuiltInCode;
                    sysUsesNativeEcAuto = (sysProf.BuiltInCode != 3);
                }

                if (sysUsesNativeEcAuto && (snapshot.NvmeTemp >= 65 || snapshot.NpuActivityPct >= 15))
                {
                    sysUsesNativeEcAuto = false;
                }

                // Explicit BIOS Power Mode override (0=Balanced, 1=Performance, 2=Silent) applies independently
                // even when driving fans via a Custom Curve or Auto-Tuned profile.
                if (biosOverride >= 0 && biosOverride <= 2)
                {
                    desiredEcMode = biosOverride;
                }
            }

            int npuBoost = 0;
            if (snapshot.NpuActivityPct >= 10)
            {
                npuBoost = (int)Math.Round(snapshot.NpuActivityPct * 0.35);
            }
            int effectiveApuTemp = apuTemp + npuBoost;

            // Asymmetric cooldown hysteresis (4°C down, 0°C up) prevents fan hunting on brief temperature dips
            bool withinCooldownHysteresis = (lastEvalTemp > 0 && effectiveApuTemp < lastEvalTemp && (lastEvalTemp - effectiveApuTemp) < 4);

            try
            {
                if (desiredEcMode >= 0 && lastEcMode != desiredEcMode)
                {
                    bool ok = demo ? _demo.SetMode(desiredEcMode) : _hw.SetMode(desiredEcMode);
                    if (ok)
                    {
                        lock (_stateLock) { _lastAppliedEcMode = desiredEcMode; }
                        wroteHardware = true;
                        ClearHardwareWriteError();
                    }
                    else
                    {
                        RecordHardwareWriteError(string.Format("EC Mode change (0x{0:X2}) verification failed", desiredEcMode));
                    }
                }

                if (!apuManual && apuProf != null)
                {
                    if (apuUsesNativeEcAuto)
                    {
                        if (lastApuPct != 0)
                        {
                            bool ok = demo ? _demo.SetFan(1, 0) : _hw.SetFan(1, 0);
                            if (ok)
                            {
                                lock (_stateLock) { _lastAppliedApuPct = 0; }
                                wroteHardware = true;
                                ClearHardwareWriteError();
                            }
                            else
                            {
                                RecordHardwareWriteError("APU Fan reset to Auto (0%) write verification failed");
                            }
                        }
                    }
                    else
                    {
                        int targetApu = apuProf.EvaluateApu(effectiveApuTemp);
                        int regPct = targetApu <= 0 ? (isSuperIo ? 0 : 1) : targetApu;
                        if (lastApuPct < 0 || (!withinCooldownHysteresis && Math.Abs(regPct - lastApuPct) >= 2) || regPct == 100)
                        {
                            bool ok = demo ? _demo.SetFan(1, regPct) : _hw.SetFan(1, regPct);
                            if (ok)
                            {
                                lock (_stateLock)
                                {
                                    _lastAppliedApuPct = regPct;
                                    _lastCustomEvalTemp = effectiveApuTemp;
                                }
                                wroteHardware = true;
                                ClearHardwareWriteError();
                            }
                            else
                            {
                                RecordHardwareWriteError(string.Format("APU Fan speed write ({0}%) verification failed", regPct));
                            }
                        }
                    }
                }

                if (hasDual && !sysManual && sysProf != null)
                {
                    if (sysUsesNativeEcAuto)
                    {
                        if (lastSysPct != 0)
                        {
                            bool ok = demo ? _demo.SetFan(2, 0) : _hw.SetFan(2, 0);
                            if (ok)
                            {
                                lock (_stateLock)
                                {
                                    _lastAppliedSysPct = 0;
                                    _fan2ConsecutiveWriteFailures = 0;
                                }
                                wroteHardware = true;
                                if (_hw == null || _hw.Profile == null || _hw.Profile.HasDualFans)
                                {
                                    ClearHardwareWriteError();
                                }
                            }
                            else
                            {
                                RecordHardwareWriteError("Sys Fan reset to Auto (0%) write verification failed");
                            }
                        }
                    }
                    else
                    {
                        int sysThermalSource = (snapshot.NvmeTemp > 0) ? Math.Max(effectiveApuTemp, snapshot.NvmeTemp) : effectiveApuTemp;
                        int targetSys = sysProf.EvaluateSys(sysThermalSource);
                        int regPct = targetSys <= 0 ? (isSuperIo ? 0 : 1) : targetSys;
                        if (lastSysPct < 0 || (!withinCooldownHysteresis && Math.Abs(regPct - lastSysPct) >= 2) || regPct == 100)
                        {
                            bool ok = demo ? _demo.SetFan(2, regPct) : _hw.SetFan(2, regPct);
                            if (ok)
                            {
                                lock (_stateLock)
                                {
                                    _lastAppliedSysPct = regPct;
                                    _lastCustomEvalTemp = effectiveApuTemp;
                                    _fan2ConsecutiveWriteFailures = 0;
                                }
                                wroteHardware = true;
                                if (_hw == null || _hw.Profile == null || _hw.Profile.HasDualFans)
                                {
                                    ClearHardwareWriteError();
                                }
                            }
                            else
                            {
                                bool adaptToSingle = false;
                                int f2Fails;
                                lock (_stateLock)
                                {
                                    _fan2ConsecutiveWriteFailures++;
                                    f2Fails = _fan2ConsecutiveWriteFailures;
                                    if (f2Fails >= 5 && regPct >= 30 && snapshot.Fan2Rpm == 0)
                                    {
                                        _hasDualFans = false;
                                        adaptToSingle = true;
                                    }
                                }
                                if (adaptToSingle)
                                {
                                    WatchdogSupervisor.LogWatchdogEvent(string.Format("Sys Fan (Fan 2) repeatedly unacknowledged ({0} failures) at {1}% duty and 0 RPM detected -> Auto-adapting to Single-Fan Mode.", f2Fails, regPct));
                                    if (DualFanAutoAdapted != null) DualFanAutoAdapted();
                                }
                                else
                                {
                                    RecordHardwareWriteError(string.Format("Sys Fan write ({0}%) verification failed", regPct));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RecordHardwareWriteError("Fan profile evaluation exception: " + ex.Message);
                WatchdogSupervisor.LogWatchdogEvent("EvaluateAndApplyFanProfiles exception: " + ex.ToString());
            }
            return wroteHardware;
        }

        private void RecordHardwareWriteError(string errorDetail)
        {
            int fails;
            bool escalate = false;
            lock (_stateLock)
            {
                _consecutiveWriteFailures++;
                fails = _consecutiveWriteFailures;
                _lastHardwareWriteError = errorDetail;
                if (fails >= 10 && !_readOnlyFailsafeLatched)
                {
                    _readOnlyFailsafeLatched = true;
                    escalate = true;
                }
            }

            if (escalate)
            {
                WatchdogSupervisor.LogWatchdogEvent(string.Format("PERSISTENT WRITE FAILURES ({0} consecutive): Escalating to emergency failsafe. Restoring BIOS Auto and latching read-only mode.", fails));
                bool autoRestored = false;
                try
                {
                    if (_demoMode && _demo != null)
                    {
                        autoRestored = _demo.SetAuto();
                    }
                    else if (_hw != null)
                    {
                        autoRestored = _hw.SetAuto();
                        if (_hw.Profile != null) _hw.Profile.IsReadOnly = true;
                    }
                }
                catch (Exception ex)
                {
                    WatchdogSupervisor.LogWatchdogEvent("Escalation SetAuto failed: " + ex.Message);
                }

                lock (_stateLock)
                {
                    _latchedAutoRestored = autoRestored;
                    _lastLatchedAutoRetryUtc = DateTime.UtcNow;
                }

                if (EscalatedFailsafeTriggered != null) EscalatedFailsafeTriggered(fails);
                return;
            }

            if (WriteErrorStateChanged != null) WriteErrorStateChanged(errorDetail, fails);
        }

        private void ClearHardwareWriteError()
        {
            bool hadError;
            lock (_stateLock)
            {
                _readOnlyFailsafeLatched = false;
                hadError = (_consecutiveWriteFailures > 0 || _lastHardwareWriteError != null);
                _consecutiveWriteFailures = 0;
                _lastHardwareWriteError = null;
            }
            if (_hw != null && _hw.Profile != null && !_hw.Profile.Id.Equals("unknown-safe", StringComparison.OrdinalIgnoreCase))
            {
                _hw.Profile.IsReadOnly = false;
            }
            if (hadError && WriteErrorStateChanged != null)
            {
                WriteErrorStateChanged(null, 0);
            }
        }

        private void WorkerLoop()
        {
            while (_hwThreadRunning)
            {
                if (!_hwWorkerPaused)
                {
                    try
                    {
                        bool demo;
                        lock (_stateLock) { demo = _demoMode; }

                        FanStatus st = demo ? _demo.GetStatus() : _hw.GetStatus();
                        lock (_stateLock) { _lastStatus = st; }

                        int apuTemp = Math.Max(st.CpuTemp, st.GpuTemp);
                        bool hwChanged = EvaluateAndApplyFanProfiles(apuTemp);
                        if (hwChanged)
                        {
                            st = demo ? _demo.GetStatus() : _hw.GetStatus();
                            lock (_stateLock) { _lastStatus = st; }
                        }

                        if (SampleReceived != null) SampleReceived(st);
                    }
                    catch (Exception ex)
                    {
                        WatchdogSupervisor.LogWatchdogEvent("Hardware worker loop exception: " + ex.ToString());
                        RecordHardwareWriteError("Hardware loop exception: " + ex.Message);
                        if (WorkerError != null) WorkerError(ex);
                    }
                }

                Interlocked.Exchange(ref _lastWorkerSampleUtcTicks, DateTime.UtcNow.Ticks);

                if (WorkerIterationCompleted != null) WorkerIterationCompleted();

                int waitMs;
                DateTime rampUntil;
                lock (_stateLock)
                {
                    waitMs = _refreshIntervalMs;
                    rampUntil = _rampUntil;
                }
                if (DateTime.UtcNow < rampUntil) waitMs = Math.Min(waitMs, 250);
                _hwWakeEvent.WaitOne(waitMs);
            }
        }

        public void Dispose()
        {
            Stop(500);
            try { _hwWakeEvent.Close(); } catch { }
        }
    }
}
