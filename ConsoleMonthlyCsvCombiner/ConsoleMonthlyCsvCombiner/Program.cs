using Microsoft.Extensions.Configuration;
using Serilog;

namespace ConsoleMonthlyCsvCombiner
{
    internal class Program
    {
        static void Main(string[] args)
        {

            Log.Logger = new LoggerConfiguration()
             .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
             .WriteTo.File("log.txt", rollingInterval: RollingInterval.Minute)
             .CreateLogger();

            // Build configuration from:
            // 1. appsettings.json (if exists)
            // 2. Command line arguments (override)
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddCommandLine(args)   // <-- allows --pattern "*.csv" --output "MyFolder"
                .Build();

            // Read values with fallbacks
            // Read source folder – fallback to exe location if missing
            string sourceFolder = config["source"] ?? config["SourceFolder"];
            if (string.IsNullOrEmpty(sourceFolder))
            {
                sourceFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SourceFolder");
                Console.WriteLine($"No input folder provided. Using default: {sourceFolder}," +
                    System.Environment.NewLine +
                    $"Use --source \"C:\\YourPath\" or set 'SourceFolder' in appsettings.json");
            }

            // Read output folder – fallback to exe location if missing
            string outputFolder = config["output"] ?? config["OutputFolder"];
            if (string.IsNullOrEmpty(outputFolder))
            {
                outputFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Monthly_Combined");
                Console.WriteLine($"No output folder provided. Using default: {outputFolder}," +
                    System.Environment.NewLine +
                    $"Use --output \"C:\\YourPath\" or set 'OutputFolder' in appsettings.json");
                
            }

            string filePattern = config["pattern"] ?? config["FilePattern"] ?? "dl_*.csv";

            //Run program logic
            using (var combiner = new CsvCombiner(Log.Logger, sourceFolder: sourceFolder, outputFolder: outputFolder, filePattern: filePattern))
            {
                combiner.Run();
            }

            Console.WriteLine("Press any key to exit");
            Console.ReadLine();

            Log.CloseAndFlush();
        }
    }
}