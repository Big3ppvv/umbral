using System;

namespace Umbra.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                ProgressionChecks.Run();
                SimulationChecks.Run();
                Console.WriteLine("UMBRA Unity: all deterministic core and progression checks passed.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
    }
}
