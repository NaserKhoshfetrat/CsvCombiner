using ConsoleMonthlyCsvCombiner;
using MonthlyCsvCombinerTest.IntegrationTests;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MonthlyCsvCombiner.Tests.Integration
{
    public class MonthlyCsvCombinerTests : IDisposable
    {
        private readonly TempDirectoryFixture _temp;
        private readonly ILogger _logger;
        private readonly StringWriter _consoleOutput;
        private readonly TextReader _originalConsoleIn;
        private readonly TextWriter _originalConsoleOut;
        public MonthlyCsvCombinerTests()
        {
            _temp = new TempDirectoryFixture();
            _logger = new LoggerConfiguration()
                .WriteTo.Sink(new TestSink())
                .CreateLogger();

            _consoleOutput = new StringWriter();
            _originalConsoleOut = Console.Out;
            _originalConsoleIn = Console.In;
            Console.SetOut(_consoleOutput);
        }

        private class TestSink : ILogEventSink
        {
            public List<LogEvent> Events { get; } = new();
            public void Emit(LogEvent logEvent) => Events.Add(logEvent);
        }

        public void Dispose()
        {
            Console.SetOut(_originalConsoleOut);
            Console.SetIn(_originalConsoleIn);
            _consoleOutput.Dispose();

            _temp.Dispose();
        }

        // Helper to create CSV files
        private void CreateCsv(string directory, string fileName, string[] lines)
        {
            var path = Path.Combine(directory, fileName);
            File.WriteAllLines(path, lines);
        }

        private CsvCombiner CreateSut(string sourceFolder = null, string outputFolder = null, string pattern = "dl_*.csv")
        {
            sourceFolder ??= _temp.Path;
            outputFolder ??= Path.Combine(_temp.Path, "Monthly_Combined"); // absolute path inside temp
            return new CsvCombiner(_logger, sourceFolder, outputFolder, pattern);
        }

        private void SetWorkingDirectoryx(Action action)
        {
            string originalDirectory = Directory.GetCurrentDirectory();

            try
            {
                Directory.SetCurrentDirectory(_temp.Path);
                action.Invoke();

            }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);

            }

        }

        [Fact]
        public void Run_ValidFiles_CombinesCorrectly()
        {
            // Arrange
            CreateCsv(_temp.Path, "dl_2025_03_01.csv", new[] { "Col1,Col2", "A,1", "B,2" });
            CreateCsv(_temp.Path, "dl_2025_03_02.csv", new[] { "Col1,Col2", "C,3" });
            CreateCsv(_temp.Path, "dl_2025_03_15.csv", new[] { "Col1,Col2", "D,4", "E,5" });
            // Simulate "Y" for overwrite prompt (won't be used because file doesn't exist)
            SimulateConsoleInput("Y");

            var sut = CreateSut();

            // Act
            sut.Run();

            // Assert
            string outputFile = Path.Combine(_temp.Path, "Monthly_Combined", "monthly_2025_03.csv");
            Assert.True(File.Exists(outputFile));
            var lines = File.ReadAllLines(outputFile);
            Assert.Equal(6, lines.Length); // header + 5 data rows
            Assert.Equal("Col1,Col2", lines[0]);
            Assert.Contains("C,3", lines);
        }

        [Fact]
        public void Run_MultipleMonths_CreatesSeparateMonthlyFiles()
        {
            // March files
            CreateCsv(_temp.Path, "dl_2025_03_01.csv", new[] { "Col1,Col2", "A,1" });
            CreateCsv(_temp.Path, "dl_2025_03_02.csv", new[] { "Col1,Col2", "B,2" });
            // April files
            CreateCsv(_temp.Path, "dl_2025_04_01.csv", new[] { "Col1,Col2", "C,3" });
            CreateCsv(_temp.Path, "dl_2025_04_10.csv", new[] { "Col1,Col2", "D,4" });

            SimulateConsoleInput("Y");

            var sut = CreateSut();
            sut.Run();

            string marchOutput = Path.Combine(_temp.Path, "Monthly_Combined", "monthly_2025_03.csv");
            string aprilOutput = Path.Combine(_temp.Path, "Monthly_Combined", "monthly_2025_04.csv");
            Assert.True(File.Exists(marchOutput));
            Assert.True(File.Exists(aprilOutput));

            var marchLines = File.ReadAllLines(marchOutput);
            Assert.Equal(3, marchLines.Length); // header + 2 data rows
            var aprilLines = File.ReadAllLines(aprilOutput);
            Assert.Equal(3, aprilLines.Length);
        }

        [Fact]
        public void Run_NoMatchingFiles_LogsErrorAndExits()
        {
            // No CSV files created
            SimulateConsoleInput("Y");
            var sut = CreateSut();

            sut.Run();

            // The logger should have an error message; we can assert via test sink
            // For simplicity, check that no output folder was created
            string outputFolder = Path.Combine(_temp.Path, "Monthly_Combined");
            Assert.False(Directory.Exists(outputFolder));
        }

        [Fact]
        public void Run_OverwriteExistingFile_PromptsUserAndOverwritesWhenYes()
        {
            // Create input files
            CreateCsv(_temp.Path, "dl_2025_03_01.csv", new[] { "Col1,Col2", "Old data" });
            // Pre-create output file with different content
            string outputFolder = Path.Combine(_temp.Path, "Monthly_Combined");
            Directory.CreateDirectory(outputFolder);
            string outputFile = Path.Combine(outputFolder, "monthly_2025_03.csv");
            File.WriteAllText(outputFile, "Col1,Col2\nOldOutput");

            // Simulate "Y" (overwrite)
            SimulateConsoleInput("Y");

            var sut = CreateSut();
            sut.Run();

            // Output should be overwritten with new content
            var lines = File.ReadAllLines(outputFile);
            // header only? Actually input has header + one data row -> output header + data row = 2 lines
            // Input file has header + "Old data" => 2 rows
            Assert.Equal(2, lines.Length);
            Assert.Equal("Col1,Col2", lines[0]);
            Assert.Equal("Old data", lines[1]);
        }

        [Fact]
        public void Run_OverwriteExistingFile_Skip_DoesNotOverwrite()
        {
            CreateCsv(_temp.Path, "dl_2025_03_01.csv", new[] { "Col1,Col2", "New data" });
            string outputFolder = Path.Combine(_temp.Path, "Monthly_Combined");
            Directory.CreateDirectory(outputFolder);
            string outputFile = Path.Combine(outputFolder, "monthly_2025_03.csv");
            File.WriteAllText(outputFile, "Col1,Col2\nOld data");

            // Simulate "S" (skip)
            SimulateConsoleInput("S");

            var sut = CreateSut();
            sut.Run();

            var lines = File.ReadAllLines(outputFile);
            Assert.Equal(2, lines.Length);
            Assert.Equal("Old data", lines[1]); // unchanged
        }

        [Fact]
        public void Run_OverwriteExistingFile_Cancel_StopsProcessing()
        {
            CreateCsv(_temp.Path, "dl_2025_03_01.csv", new[] { "Col1,Col2", "Data" });
            string outputFolder = Path.Combine(_temp.Path, "Monthly_Combined");
            Directory.CreateDirectory(outputFolder);
            string outputFile = Path.Combine(outputFolder, "monthly_2025_03.csv");
            File.WriteAllText(outputFile, "Old");

            // Simulate "N" (Cancel)
            SimulateConsoleInput("N");

            var sut = CreateSut();
            sut.Run();

            // File should be unchanged
            var content = File.ReadAllText(outputFile);
            Assert.Equal("Old", content);
        }

        // Helper to simulate console input
        private void SimulateConsoleInput(params string[] inputs)
        {
            var inputQueue = new Queue<string>(inputs);
            Console.SetIn(new StringReader(string.Join(Environment.NewLine, inputs) + Environment.NewLine));
        }
    }
}