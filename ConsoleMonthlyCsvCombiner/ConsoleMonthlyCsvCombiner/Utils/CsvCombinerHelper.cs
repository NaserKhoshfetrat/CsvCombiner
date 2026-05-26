using Serilog;
using System.Text.RegularExpressions;

namespace ConsoleMonthlyCsvCombiner.Utils
{
    public class CsvCombinerHelper
    {

        private readonly ILogger _logger;

        public CsvCombinerHelper(ILogger logger)
        {
            _logger = logger;
        }

        #region Helper methods
        public void CreateOutputFolder(string outputFolder)
        {
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
                _logger.Information("Created output folder: {OutputFolder}", outputFolder);
            }
            else
            {
                _logger.Information("Output folder already exists: {OutputFolder}", outputFolder);
            }
        }

        public string[] GetCsvFiles(string folder, string pattern, Regex fileNameRegex)
        {
            if (!Directory.Exists(folder))
            {
                _logger.Error("Source folder does not exist: {Folder}", folder);
                return null;
            }

            var files = Directory.GetFiles(folder, pattern);
            var validFiles = files.Where(f => fileNameRegex.IsMatch(Path.GetFileName(f))).ToArray();

            if (validFiles.Length == 0)
            {
                _logger.Error("No files found matching pattern '{Pattern}' in '{Folder}'!", pattern, folder);

                return null;
            }

            _logger.Information("Found {Count} CSV files in {Folder}", validFiles.Length, folder);
            return validFiles;
        }

        public class DiskSpaceInfo
        {
            public string Drive { get; set; }
            public double FreeSpaceGB { get; set; }
            public double RequiredSpaceGB { get; set; }
        }

        public DiskSpaceInfo CheckDiskSpace(string[] files, string outputFolder)
        {
            string rootPath = Path.GetPathRoot(Path.GetFullPath(outputFolder));
            if (string.IsNullOrEmpty(rootPath))
                rootPath = Directory.GetDirectoryRoot(Directory.GetCurrentDirectory());

            try
            {
                DriveInfo drive = new DriveInfo(rootPath);
                double freeSpaceGB = Math.Round(drive.AvailableFreeSpace / (1024.0 * 1024 * 1024), 2);
                double totalSpaceGB = Math.Round(drive.TotalSize / (1024.0 * 1024 * 1024), 2);
                double freePercent = Math.Round((drive.AvailableFreeSpace / (double)drive.TotalSize) * 100, 1);

                _logger.Information("Disk Space Check: Drive {RootPath} - Free {FreeSpaceGB} GB ({FreePercent}%), Total {TotalSpaceGB} GB",
                    rootPath, freeSpaceGB, freePercent, totalSpaceGB);

                long totalInputBytes = files.Sum(f => new FileInfo(f).Length);
                double totalInputGB = Math.Round(totalInputBytes / (1024.0 * 1024 * 1024), 2);
                double estimatedOutputGB = Math.Round(totalInputGB * 0.8, 2);
                double requiredSpaceGB = Math.Round(estimatedOutputGB * 1.5, 2);

                _logger.Information("Space Requirements: Input {TotalInputGB} GB, Estimated Output {EstimatedOutputGB} GB, Recommended {RequiredSpaceGB} GB",
                    totalInputGB, estimatedOutputGB, requiredSpaceGB);

                if (freeSpaceGB < requiredSpaceGB)
                {
                    _logger.Warning("Insufficient disk space! Have {FreeSpaceGB} GB, need ~{RequiredSpaceGB} GB", freeSpaceGB, requiredSpaceGB);
                    Console.Write("Continue anyway? (Y/N): ");
                    string response = Console.ReadLine();
                    if (response?.ToUpper() != "Y")
                        return null;
                }
                else
                {
                    _logger.Information("Disk space check: PASSED");
                }

                return new DiskSpaceInfo
                {
                    Drive = rootPath,
                    FreeSpaceGB = freeSpaceGB,
                    RequiredSpaceGB = requiredSpaceGB
                };
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not check disk space. Continuing anyway...");
                return new DiskSpaceInfo(); // allow continue
            }
        }

        public Dictionary<string, List<string>> GroupFilesByMonth(string[] files, Regex fileNameRegex)
        {
            var groups = new Dictionary<string, List<string>>();

            foreach (var file in files)
            {
                string name = Path.GetFileName(file);
                var match = fileNameRegex.Match(name);
                if (match.Success)
                {
                    string yearMonth = $"{match.Groups[1].Value}_{match.Groups[2].Value}";
                    if (!groups.ContainsKey(yearMonth))
                        groups[yearMonth] = new List<string>();
                    groups[yearMonth].Add(file);
                }
            }

            return groups;
        }

        public void ShowFileGroups(Dictionary<string, List<string>> groups)
        {
            _logger.Information("Monthly Groups (Detailed):");
            int totalFiles = 0;
            foreach (var group in groups.OrderBy(g => g.Key))
            {
                string[] parts = group.Key.Split('_');
                string yearMonth = $"{parts[0]}-{parts[1]}";
                totalFiles += group.Value.Count;
                _logger.Debug("{YearMonth} ({FileCount} files):", yearMonth, group.Value.Count);
                foreach (var file in group.Value.OrderBy(f => f))
                {
                    long sizeKB = new FileInfo(file).Length / 1024;
                    _logger.Debug("  - {FileName} ({SizeKB} KB)", Path.GetFileName(file), sizeKB);
                }
            }
            _logger.Information("Total files: {TotalFiles}", totalFiles);
        }

        public string ConfirmOverwrite(string filePath)
        {
            if (File.Exists(filePath))
            {
                _logger.Warning("File exists: {FilePath}", filePath);
                Console.Write("Overwrite? (Y=Yes, N=No, S=Skip): ");
                string response = Console.ReadLine()?.ToUpper();
                switch (response)
                {
                    case "Y": return "Overwrite";
                    case "S": return "Skip";
                    default: return "Cancel";
                }
            }
            return "Proceed";
        }

        public List<string> SortFilesByDay(List<string> files, Regex fullRegex)
        {
            return files.OrderBy(f =>
            {
                var match = fullRegex.Match(Path.GetFileName(f));
                return match.Success ? int.Parse(match.Groups[3].Value) : 0;
            }).ToList();
        }

        public void ShowDayList(List<string> sortedFiles, Regex fullRegex)
        {
            var days = sortedFiles.Select(f =>
            {
                var match = fullRegex.Match(Path.GetFileName(f));
                return match.Success ? match.Groups[3].Value : "?";
            });
            _logger.Debug("Days: {Days}", string.Join(" ", days));
        }

        public class CombineResult
        {
            public DateTime StartTime { get; set; }
            public int TotalRows { get; set; }       // data rows only (excluding headers)
            public int FileCount { get; set; }
            public int OutputRows { get; set; }      // total rows in output (including header)
            public double OutputSizeMB { get; set; }
            public double DurationSec { get; set; }
        }


        public void ShowMonthSummary(int fileCount, int totalRows, int outputRows, double outputSizeMB, double durationSec, string outputFile)
        {
            _logger.Information("-> Combined: {FileCount} files, {TotalRows} data rows", fileCount, totalRows);
            _logger.Information("-> Saved to: {OutputFile} ({OutputRows} rows total, {OutputSizeMB} MB)", outputFile, outputRows, outputSizeMB);
            _logger.Information("-> Month processing time: {DurationSec} seconds", durationSec);
        }

        public void EstimateRemainingTime(int currentMonth, int totalMonths, double lastMonthDurationSec)
        {
            int monthsRemaining = totalMonths - currentMonth;
            double estimatedRemainingSec = lastMonthDurationSec * monthsRemaining;
            if (estimatedRemainingSec > 60)
            {
                double minutes = Math.Round(estimatedRemainingSec / 60, 1);
                _logger.Information("-> Estimated time remaining: {Minutes} minutes", minutes);
            }
            else
            {
                _logger.Information("-> Estimated time remaining: {Seconds} seconds", Math.Round(estimatedRemainingSec, 0));
            }
        }

        public void ShowFinalSummary(int totalMonths, int totalFiles, string outputFolder, DiskSpaceInfo diskInfo)
        {
            _logger.Information("================== PROCESSING COMPLETE ==================");
            _logger.Information("Total months processed: {TotalMonths}", totalMonths);
            _logger.Information("Total files processed: {TotalFiles}", totalFiles);

            if (diskInfo != null && !string.IsNullOrEmpty(diskInfo.Drive))
            {
                try
                {
                    DriveInfo drive = new DriveInfo(diskInfo.Drive);
                    double finalFreeGB = Math.Round(drive.AvailableFreeSpace / (1024.0 * 1024 * 1024), 2);
                    double usedGB = Math.Round(diskInfo.FreeSpaceGB - finalFreeGB, 2);
                    _logger.Information("Final Disk Status: Space used by operation: {UsedGB} GB, Remaining free space: {FinalFreeGB} GB", usedGB, finalFreeGB);
                    if (finalFreeGB < 1)
                    {
                        _logger.Warning("Less than 1GB free space remaining!");
                    }
                }
                catch { }
            }

            _logger.Information("All files saved to: {FullPath}", Path.GetFullPath(outputFolder));
            _logger.Information("Done!");
        }

        public string? ExtractPrefix(string pattern)
        {
            // Pattern is like "prefix_*.csv" – get everything before the first '*'
            int starIndex = pattern.IndexOf('*');
            if (starIndex <= 0) return null;
            return pattern.Substring(0, starIndex);
        }

        #endregion Helper methods
    }
}