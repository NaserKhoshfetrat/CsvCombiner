using ConsoleMonthlyCsvCombiner.Utils;
using Serilog;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ConsoleMonthlyCsvCombiner
{
    public class CsvCombiner : IDisposable
    {
        private readonly string _sourceFolder;
        private readonly string _outputFolder;
        public string _pattern { init; get; }
        private string _filePrefix;
        public Regex _fileNameRegex { init; get; }
        private readonly ILogger _logger;
        private bool _disposed = false;
        private readonly CsvCombinerHelper _helper;
        // For cross‑process locking on output files
        private static readonly Dictionary<string, Mutex> FileMutexes = new Dictionary<string, Mutex>();
        private static readonly object MutexDictLock = new object();

        public CsvCombiner(ILogger logger, string sourceFolder, string outputFolder = "Monthly_Combined", string filePattern = "dl_*.csv")
        {
            _logger = logger;
            _sourceFolder = sourceFolder;
            _outputFolder = outputFolder;
            _helper = new CsvCombinerHelper(logger);

            #region Setup
            _pattern = filePattern;

            _filePrefix = _helper.ExtractPrefix(_pattern);
            if (string.IsNullOrEmpty(_filePrefix))
            {
                _logger.Error("Invalid file pattern: cannot extract prefix from '{Pattern}'", _pattern);
                throw new ArgumentException("Invalid file pattern: cannot extract prefix from '{Pattern}'", _pattern);
            }

            // Build regexes dynamically using the prefix
            _fileNameRegex = new Regex($@"{Regex.Escape(_filePrefix)}(\d{{4}})_(0[1-9]|1[0-2])_(0[1-9]|[12]\d|3[01])\.csv$", RegexOptions.Compiled);
            #endregion Setup
        }

        // IDisposable implementation to clean up mutexes
        public void Dispose()
        {
            if (!_disposed)
            {
                lock (MutexDictLock)
                {
                    foreach (var mutex in FileMutexes.Values)
                    {
                        mutex.Dispose();
                    }
                    FileMutexes.Clear();
                }
                _disposed = true;
            }
        }

        public void Run()
        {
            _logger.Information("Monthly CSV Combiner started");
            _logger.Information("Source folder: {SourceFolder}", _sourceFolder);
            _logger.Information("Output folder: {OutputFolder}", _outputFolder);

            try
            {
                // *** CHECK THAT SOURCE FOLDER EXISTS ***
                if (!Directory.Exists(_sourceFolder))
                {
                    _logger.Error($"ERROR: Source folder does not exist: {_sourceFolder}");
                    return;
                }

                var files = _helper.GetCsvFiles(_sourceFolder, _pattern, _fileNameRegex);
                if (files == null || files.Length == 0)
                    return;

                // 1. Setup output folder
                _helper.CreateOutputFolder(_outputFolder);

                // 2. Check disk space
                var diskInfo = _helper.CheckDiskSpace(files, _outputFolder);
                if (diskInfo == null) // cancellation or failure
                    return;

                // 3. Group files by year-month
                var groups = _helper.GroupFilesByMonth(files, _fileNameRegex);
                if (groups.Count == 0)
                {
                    _logger.Error("No valid monthly groups found!");
                    return;
                }

                // 4. Show file groups
                _helper.ShowFileGroups(groups);

                // 5. Process each month
                int monthCounter = 0;
                double lastMonthDurationSec = 0;

                foreach (var group in groups.OrderBy(g => g.Key))
                {
                    monthCounter++;
                    string yearMonth = group.Key;
                    var monthFiles = group.Value;

                    double percentComplete = (monthCounter - 1) * 100.0 / groups.Count;
                    _logger.Information("[{MonthCounter}/{TotalMonths}] {YearMonth} ({FileCount} files) - Progress: {Percent:F1}% complete",
                        monthCounter, groups.Count, yearMonth.Replace('_', '-'), monthFiles.Count, percentComplete);

                    string outputFile = Path.Combine(_outputFolder, $"monthly_{yearMonth}.csv");

                    // Overwrite handling (requires user input)
                    string overwriteAction = _helper.ConfirmOverwrite(outputFile);
                    if (overwriteAction == "Cancel") return;
                    if (overwriteAction == "Skip") continue;

                    // Sort files by day
                    var sortedFiles = _helper.SortFilesByDay(monthFiles, _fileNameRegex);
                    _helper.ShowDayList(sortedFiles, _fileNameRegex);

                    // Month stats (input size)
                    long monthInputBytes = monthFiles.Sum(f => new FileInfo(f).Length);
                    double monthInputMB = Math.Round(monthInputBytes / (1024.0 * 1024), 2);
                    _logger.Information("Month data size: {MonthInputMB} MB", monthInputMB);

                    // Combine files (thread‑safe & cross‑process safe)
                    var combineResult = CombineMonthlyFiles(sortedFiles, outputFile, yearMonth, _fileNameRegex);

                    // Show summary
                    _helper.ShowMonthSummary(combineResult.FileCount, combineResult.TotalRows,
                        combineResult.OutputRows, combineResult.OutputSizeMB,
                        combineResult.DurationSec, outputFile);

                    // Estimate remaining time
                    if (monthCounter < groups.Count)
                    {
                        lastMonthDurationSec = combineResult.DurationSec;
                        _helper.EstimateRemainingTime(monthCounter, groups.Count, lastMonthDurationSec);
                    }
                }

                // 6. Final summary
                _helper.ShowFinalSummary(groups.Count, files.Length, _outputFolder, diskInfo);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error during processing");
                throw;
            }
        }

        public CsvCombinerHelper.CombineResult CombineMonthlyFiles(List<string> sortedFiles, string outputFile, string yearMonth, Regex dayRegex)
        {
            var startTime = DateTime.Now;
            int totalDataRows = 0;
            int fileCounter = 0;

            // Cross‑process lock for this output file
            Mutex fileMutex = GetMutexForPath(outputFile);
            bool hasLock = false;
            try
            {
                hasLock = fileMutex.WaitOne(TimeSpan.FromSeconds(30));
                if (!hasLock)
                {
                    throw new TimeoutException($"Could not acquire lock for file {outputFile}");
                }

                using (var writer = new StreamWriter(outputFile))
                {
                    for (int i = 0; i < sortedFiles.Count; i++)
                    {
                        fileCounter++;
                        string file = sortedFiles[i];
                        string fileName = Path.GetFileName(file);
                        var dayMatch = _fileNameRegex.Match(fileName);
                        string dayNum = dayMatch.Success ? dayMatch.Groups[3].Value : "??";

                        long fileSizeMB = new FileInfo(file).Length / (1024 * 1024);
                        int fileRows = 0;
                        int dataRowsAdded = 0;

                        using (var reader = new StreamReader(file))
                        {
                            bool isFirstLine = true;
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                fileRows++;
                                if (i == 0 || !isFirstLine)
                                {
                                    writer.WriteLine(line);
                                    if (!(i == 0 && isFirstLine))
                                        dataRowsAdded++;
                                }
                                isFirstLine = false;
                            }
                        }

                        totalDataRows += dataRowsAdded;
                        double dayPercent = (fileCounter * 100.0) / sortedFiles.Count;
                        _logger.Debug("  [{FileCounter}/{TotalFiles}] Day {DayNum} - {FileName} ({FileRows} rows, {FileSizeMB} MB) -> Processed {DataRows} data rows - {Percent:F1}% of month",
                            fileCounter, sortedFiles.Count, dayNum, fileName, fileRows, fileSizeMB, dataRowsAdded, dayPercent);
                    }
                }

                // After writing, get final output stats
                long outputBytes = new FileInfo(outputFile).Length;
                double outputSizeMB = Math.Round(outputBytes / (1024.0 * 1024), 2);
                int finalOutputRows = File.ReadLines(outputFile).Count();

                var endTime = DateTime.Now;
                double durationSec = Math.Round((endTime - startTime).TotalSeconds, 2);

                return new CsvCombinerHelper.CombineResult
                {
                    StartTime = startTime,
                    TotalRows = totalDataRows,
                    FileCount = sortedFiles.Count,
                    OutputRows = finalOutputRows,
                    OutputSizeMB = outputSizeMB,
                    DurationSec = durationSec
                };
            }
            finally
            {
                if (hasLock)
                    fileMutex.ReleaseMutex();
            }
        }

        private static Mutex GetMutexForPath(string path)
        {
            // Cross‑platform safe name: replace directory separators and colon with underscore
            string safeName = "Global_" + path.Replace(Path.DirectorySeparatorChar, '_').Replace(':', '_');
            lock (MutexDictLock)
            {
                if (!FileMutexes.TryGetValue(safeName, out var mutex))
                {
                    mutex = new Mutex(false, safeName);
                    FileMutexes[safeName] = mutex;
                }
                return mutex;
            }
        }

    }
}