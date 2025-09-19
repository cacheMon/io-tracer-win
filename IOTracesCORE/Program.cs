// Program.cs
namespace IOTracesCORE
{
    class Program
    {
        static void Main(string[] args)
        {
            string outputPath = "./output";
            bool anonymous = false;

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
                    case "-a":
                    case "--anonymous":
                        anonymous = true;
                        break;
                    case "-h":
                    case "--help":
                        PrintHelp();
                        return;
                }
            }

            while (true)
            {
                Console.Write($"Enter output path (default: {outputPath}): ");
                string inputPath = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(inputPath))
                {
                    break;
                }

                try
                {
                    if (inputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    {
                        Console.WriteLine("Invalid path: contains illegal characters. Please try again.");
                        continue;
                    }

                    if (!Directory.Exists(inputPath))
                    {
                        Console.Write($"Directory does not exist. Create it? (y/N): ");
                        string createDir = Console.ReadLine()?.Trim().ToLower();
                        if (createDir == "y")
                        {
                            Directory.CreateDirectory(inputPath);
                            outputPath = inputPath;
                            break;
                        }
                        else
                        {
                            Console.WriteLine("Please enter a valid existing path.");
                            continue;
                        }
                    }

                    outputPath = inputPath;
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Invalid path: {ex.Message}");
                }
            }

            while (true)
            {
                Console.Write("Enable anonymity? (y/N): ");
                string anonInput = Console.ReadLine()?.Trim().ToLower();

                if (string.IsNullOrWhiteSpace(anonInput) || anonInput == "n")
                {
                    anonymous = false;
                    break;
                }
                else if (anonInput == "y")
                {
                    anonymous = true;
                    break;
                }
                else
                {
                    Console.WriteLine("Invalid input. Please enter 'y' for yes or 'n' for no (default is 'n').");
                }
            }

            Tracer trc = new(anonymous, outputPath);
            trc.Trace();
        }

        static void PrintHelp()
        {
            Console.WriteLine("Usage: tracer [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  -o, --output <path>     Specify output path (default: ./output)");
            Console.WriteLine("  -a, --anonymous         Enable anonymity mode");
            Console.WriteLine("  -h, --help              Show this help message");
        }
    }
}
