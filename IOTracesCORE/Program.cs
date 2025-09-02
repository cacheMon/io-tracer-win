// Program.cs
namespace IOTracesCORE
{
    class Program
    {
        static void Main(string[] args)
        {
            string outputPath = "./output";
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o":
                    case "--output":
                        if (i + 1 < args.Length)
                        {
                            outputPath = args[++i]; 
                        }
                        break;
                    case "-h":
                    case "--help":
                        PrintHelp();
                        return;
                }
            }

            Tracer trc = new(outputPath);
            trc.Trace();
        }

        static void PrintHelp()
        {
            Console.WriteLine("Options:");
            Console.WriteLine("  -o, --output <file>   Path to output directory (default: ./output)");
            Console.WriteLine("  -h, --help            Show help");
        }
    }
}
