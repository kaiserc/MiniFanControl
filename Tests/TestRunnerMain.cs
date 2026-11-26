#if STANDALONE_TEST_RUNNER
using System;

namespace MiniFanControl
{
    internal static class TestRunnerMain
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null && args.Length > 0 &&
                (args[0].Equals("hw-matrix", StringComparison.OrdinalIgnoreCase) ||
                 args[0].Equals("--hw-matrix", StringComparison.OrdinalIgnoreCase)))
            {
                return SelfTestSuite.RunHardwareMatrix();
            }
            return SelfTestSuite.RunAll();
        }
    }
}
#endif
