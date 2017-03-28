using System.IO;
using Console = Colorful.Console;

namespace ApiDump
{
    static class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Please provide a file for dumping");
                return;
            }

            string assemblyPath = args[0];
            if (!File.Exists(assemblyPath))
            {
                Console.Error.WriteLine($"File provided, '{assemblyPath}', does not exist");
                return;
            }

            AssemblyPrinter.Dump(assemblyPath);
        }
    }
}