using ConsoleMonthlyCsvCombiner;
using ConsoleMonthlyCsvCombiner.Utils;
using Moq;
using Serilog;

namespace MonthlyCsvCombinerTest.UnitTests
{
    public class CsvCombinerTest
    {
        private readonly CsvCombiner _sut;

        public CsvCombinerTest()
        {
            var loggerMock = new Mock<ILogger>();
            ILogger dummyLogger = loggerMock.Object;

            _sut = new CsvCombiner(dummyLogger, "C:\\DummySource", "C:\\DummyOutput", "dl_*.csv");
        }

        [Theory]
        [InlineData("dl_2025_03_01.csv", "2025", "03")]
        [InlineData("dl_1999_12_31.csv", "1999", "12")]
        public void FileNameRegex_MatchesValidFile_ExtractsYearMonth(string filename, string expectedYear, string expectedMonth)
        {
            var match = _sut._fileNameRegex.Match(filename);
            Assert.True(match.Success);
            Assert.Equal(expectedYear, match.Groups[1].Value);
            Assert.Equal(expectedMonth, match.Groups[2].Value);
        }

        [Theory]
        [InlineData("dl_2025_3_01.csv")]      // missing leading zero in month
        [InlineData("dl_2025_03_1.csv")]      // missing leading zero in day
        [InlineData("dl_2025_13_01.csv")]     // invalid month
        [InlineData("prefix_2025_03_01.csv")] // wrong prefix
        public void FileNameRegex_RejectsInvalidFormat_ShouldSucceed(string filename)
        {
            bool isMatch = _sut._fileNameRegex.IsMatch(filename);
            Assert.False(isMatch, $"Failed for fileName: {filename}");
        }

        [Theory]
        [InlineData("dl_2025_03_01.csv", "01")]
        [InlineData("dl_2025_03_15.csv", "15")]
        public void DayRegex_ExtractsDay_ShouldSucceed(string filename, string expectedDay)
        {
            var match = _sut._fileNameRegex.Match(filename);
            Assert.True(match.Success, userMessage: $"in file name {filename}, day regex did not matched");
            Assert.True(expectedDay == match.Groups[3].Value, $"in file name {filename}, expected Day was {expectedDay} but found {match.Groups[1].Value}");
        }

        [Theory]
        [InlineData("dl_2025_03_9.csv", "09")] // Ensure leading zero preserved
        public void DayRegex_ExtractsDay_ShouldFail(string filename, string expectedDay)
        {
            var match = _sut._fileNameRegex.Match(filename);
            Assert.False(match.Success, userMessage: $"in file name {filename}, day regex did not matched");
            Assert.False(expectedDay == match.Groups[3].Value, $"in file name {filename}, expected Day was {expectedDay} but found {match.Groups[1].Value}");
        }
    }
}