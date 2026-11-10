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
    public struct EcLockScope : IDisposable
    {
        public readonly bool Acquired;
        private readonly bool _holdsEc;
        private readonly bool _holdsIsa;

        internal EcLockScope(bool holdsEc, bool holdsIsa)
        {
            _holdsEc = holdsEc;
            _holdsIsa = holdsIsa;
            Acquired = holdsEc && holdsIsa;
        }

        public void Dispose()
        {
            if (_holdsIsa) EcBusMutex.ReleaseIsaInternal();
            if (_holdsEc) EcBusMutex.ReleaseEcInternal();
        }
    }

    /// <summary>
    /// Synchronizes Embedded Controller (EC) access with CapFrameX, LibreHardwareMonitor, and HWiNFO
    /// using the standard system-wide named mutex "Global\Access_EC".
    /// </summary>
    public static class EcBusMutex
    {
        private static Mutex _ecMutex;
        private static Mutex _isaBusMutex;
        private static Mutex _pciBusMutex;
        private static readonly object _initLock = new object();
        private static volatile bool _initDone;
        private static volatile bool _isSimulated;
        private static int _contentionCount;

        [ThreadStatic]
        private static bool _threadHoldsEc;
        [ThreadStatic]
        private static bool _threadHoldsIsa;
        [ThreadStatic]
        private static bool _threadHoldsPci;

        public static int ContentionCount
        {
            get { return _contentionCount; }
        }

        public static void EnsureCreated()
        {
            if (_isSimulated || _initDone) return;

            lock (_initLock)
            {
                if (_isSimulated || _initDone) return;

                if (_ecMutex == null) _ecMutex = CreateMutex(@"Global\Access_EC");
                if (_isaBusMutex == null) _isaBusMutex = CreateMutex(@"Global\Access_ISABUS.HTP.Method");
                if (_pciBusMutex == null) _pciBusMutex = CreateMutex(@"Global\Access_PCI");

                _initDone = true;
            }
        }

        private static Mutex CreateMutex(string name)
        {
            try
            {
                MutexSecurity sec = new MutexSecurity();
                SecurityIdentifier everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                sec.AddAccessRule(new MutexAccessRule(everyone, MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
                bool createdNew;
#if NET8_0_OR_GREATER
                return MutexAcl.Create(false, name, out createdNew, sec);
#else
                return new Mutex(false, name, out createdNew, sec);
#endif
            }
            catch
            {
                try { return Mutex.OpenExisting(name); }
                catch
                {
                    try
                    {
                        // Fallback to Local\ namespace if Global\ creation is denied (e.g. non-elevated CI runner)
                        string localName = name.StartsWith(@"Global\", StringComparison.OrdinalIgnoreCase)
                            ? @"Local\" + name.Substring(7)
                            : name;
                        bool createdNew;
                        return new Mutex(false, localName, out createdNew);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
        }

        /// <summary>
        /// Acquires exclusive EC bus access using a fail-closed pattern.
        /// Both Global\Access_EC and Global\Access_ISABUS.HTP.Method must be acquired.
        /// If either fails, any partial lock is immediately released.
        /// </summary>
        public static EcLockScope AcquireEc(int timeoutMs = 35)
        {
            EnsureCreated();
            bool gotEc = false;
            bool gotIsa = false;

            if (_ecMutex != null)
            {
                try { gotEc = _ecMutex.WaitOne(timeoutMs, false); }
                catch (AbandonedMutexException) { gotEc = true; }
                catch { gotEc = false; }
            }
            else
            {
                gotEc = false; // Fail-closed: missing EC mutex must NEVER allow unsynchronized bus access
            }

            if (!gotEc)
            {
                Interlocked.Increment(ref _contentionCount);
                return new EcLockScope(false, false);
            }

            if (_isaBusMutex != null)
            {
                try { gotIsa = _isaBusMutex.WaitOne(timeoutMs, false); }
                catch (AbandonedMutexException) { gotIsa = true; }
                catch { gotIsa = false; }
            }
            else
            {
                gotIsa = false; // Fail-closed: missing ISA mutex must NEVER allow unsynchronized bus access
            }

            if (!gotIsa)
            {
                if (gotEc && _ecMutex != null)
                {
                    try { _ecMutex.ReleaseMutex(); } catch { }
                }
                Interlocked.Increment(ref _contentionCount);
                return new EcLockScope(false, false);
            }

            return new EcLockScope(gotEc, gotIsa);
        }

        internal static void ReleaseEcInternal()
        {
            if (_ecMutex != null)
            {
                try { _ecMutex.ReleaseMutex(); } catch { }
            }
        }

        internal static void ReleaseIsaInternal()
        {
            if (_isaBusMutex != null)
            {
                try { _isaBusMutex.ReleaseMutex(); } catch { }
            }
        }

        public static bool WaitEc(int timeoutMs = 25)
        {
            var scope = AcquireEc(timeoutMs);
            if (scope.Acquired)
            {
                _threadHoldsEc = true;
                _threadHoldsIsa = true;
                return true;
            }
            return false;
        }

        public static void ReleaseEc()
        {
            if (_threadHoldsIsa)
            {
                _threadHoldsIsa = false;
                ReleaseIsaInternal();
            }
            if (_threadHoldsEc)
            {
                _threadHoldsEc = false;
                ReleaseEcInternal();
            }
        }

        internal static void SetSimulatedMutexes(Mutex ec, Mutex isa, Mutex pci)
        {
            lock (_initLock)
            {
                _ecMutex = ec;
                _isaBusMutex = isa;
                _pciBusMutex = pci;
                _isSimulated = true;
                _initDone = true;
            }
        }

        internal static void ResetMutexes()
        {
            lock (_initLock)
            {
                _isSimulated = false;
                _initDone = false;
                _ecMutex = null;
                _isaBusMutex = null;
                _pciBusMutex = null;
                EnsureCreated();
            }
        }

        public static bool WaitPci(int timeoutMs = 25)
        {
            EnsureCreated();
            if (_pciBusMutex == null) return false;
            try
            {
                bool got = _pciBusMutex.WaitOne(timeoutMs, false);
                _threadHoldsPci = got;
                return got;
            }
            catch (AbandonedMutexException)
            {
                _threadHoldsPci = true;
                return true;
            }
            catch
            {
                _threadHoldsPci = false;
                return false;
            }
        }

        public static void ReleasePci()
        {
            if (_pciBusMutex == null || !_threadHoldsPci) return;
            try
            {
                _threadHoldsPci = false;
                _pciBusMutex.ReleaseMutex();
            }
            catch { }
        }
    }

}
