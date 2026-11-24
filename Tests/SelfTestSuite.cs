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

    public static partial class SelfTestSuite

    {

        private static int _passed = 0;

        private static int _failed = 0;



        private static void Check(bool condition, string name, string detail)

        {

            if (condition)

            {

                _passed++;

                Console.WriteLine("  [PASS] " + name);

            }

            else

            {

                _failed++;

                Console.WriteLine("  [FAIL] " + name + " -> " + detail);

            }

        }



        public static int RunAll()

        {

            _passed = 0;

            _failed = 0;

            Console.WriteLine("======================================================================");

            Console.WriteLine("  MiniFanControl - Automated Hardware Safety & Curve Test Suite");

            Console.WriteLine("======================================================================");



            RunCoreAndSafetyTests();
            RunSensorsAndUiTests();
            RunSecurityAndEtwTests();
            RunRoadmapTests();

            Console.WriteLine("----------------------------------------------------------------------");

            Console.WriteLine(string.Format("  Result: {0} passed, {1} failed", _passed, _failed));

            Console.WriteLine("======================================================================");

            return _failed == 0 ? 0 : 1;

        }

        public static int RunHardwareMatrix()
        {
            Console.WriteLine("======================================================================");
            Console.WriteLine("  MiniFanControl - Hardware Validation Matrix (6 Supported Profiles)");
            Console.WriteLine("======================================================================");
            int passed = 0;
            int failed = 0;

            foreach (DeviceProfile prof in DeviceProfile.KnownProfiles)
            {
                bool ok = true;
                Console.WriteLine(string.Format("• Profile: {0} ({1})", prof.DisplayName, prof.Id));
                Console.WriteLine(string.Format("  EC Base : 0x{0:X8} (Size: 0x{1:X2})", prof.EcBaseAddress, prof.EcSize));
                Console.WriteLine(string.Format("  Offsets : Cmd=0x{0:X2}, Status=0x{1:X2}, Fan1=0x{2:X2}, Fan2=0x{3:X2}, Temp=0x{4:X2}",
                    prof.RegModeCmd, prof.RegModeStatus, prof.RegFan1Ctrl, prof.RegFan2Ctrl, prof.RegCpuTemp));
                Console.WriteLine(string.Format("  Fans    : {0} | Verified: {1} | ReadOnly: {2}",
                    prof.HasDualFans ? "Dual Fans (APU + Sys)" : "Single Fan", prof.IsVerified, prof.IsReadOnly));

                HardwareBackend hw = new HardwareBackend();
                hw.EnableSimulationMode();
                hw.Profile = prof;

                if (prof.Id == "ryzen-hx370-ec" || prof.Id == "adreamer-ariesbox10")
                {
                    hw.SuperIoBaseAddr = 0x0A30;
                    bool autoOk = hw.SetAuto();
                    if (autoOk)
                        Console.WriteLine("  Restore : Safe Super I/O Fallback & EC Auto [OK]");
                    else
                        ok = false;
                }
                else if (prof.IsReadOnly)
                {
                    bool wFan = hw.SetFan(1, 50);
                    bool wAuto = hw.SetAuto();
                    if (!wFan && !wAuto)
                        Console.WriteLine("  Safety  : Read-Only Fail-Closed Protection Confirmed [OK]");
                    else
                        ok = false;
                }
                else
                {
                    bool autoOk = hw.SetAuto();
                    if (autoOk)
                        Console.WriteLine("  Restore : Native BIOS EC Auto (0x00 to 0x32..0x34) [OK]");
                    else
                        ok = false;
                }

                if (ok) passed++; else failed++;
                Console.WriteLine(string.Format("  Result  : {0}", ok ? "PASS" : "FAIL"));
                Console.WriteLine();
            }

            Console.WriteLine(string.Format("Matrix Result: {0} passed, {1} failed", passed, failed));
            Console.WriteLine("======================================================================");
            return failed == 0 ? 0 : 1;
        }
    }
}