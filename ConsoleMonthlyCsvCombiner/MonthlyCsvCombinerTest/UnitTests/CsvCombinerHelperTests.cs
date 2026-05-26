using ConsoleMonthlyCsvCombiner.Utils;
using Moq;
using Serilog;
using System.Text.RegularExpressions;

namespace MonthlyCsvCombinerTest.UnitTests
{
    public class CsvCombinerHelperTests
    {
        private const string _Prefix = "dl_";
        private readonly Regex _fileNameRegex;
        private readonly CsvCombinerHelper _sut;

        public CsvCombinerHelperTests()
        {
            _fileNameRegex = new Regex($@"{Regex.Escape(_Prefix)}(\d{{4}})_(0[1-9]|1[0-2])_(0[1-9]|[12]\d|3[01])\.csv$", RegexOptions.Compiled);

            var loggerMock = new Mock<ILogger>();

            ILogger dummyLogger = loggerMock.Object;

            _sut = new CsvCombinerHelper(dummyLogger);
        }

        [Theory]
        [InlineData("dl_*.csv", "dl_")]
        [InlineData("data-*.log", "data-")]
        [InlineData("*.csv", null)]      // no prefix
        [InlineData("prefix", null)]      // no asterisk
        public void ExtractPrefix_ReturnsExpected(string pattern, string expectedPrefix)
        {
            var result = _sut.ExtractPrefix(pattern);

            Assert.True(expectedPrefix == result,
                $"Found prefix: {(result == null ? "null" : $"\"{result}\"")} " +
                $"for pattern: {pattern}, when expecting prefix {(expectedPrefix == null ? "null" : $"\"{expectedPrefix}\"")}");
        }

        [Fact]
        public void GroupFilesByMonth_GroupsCorrectly_WhenMultipleMonths()
        {
            // Arrange: simulate a list of files
            var files = new[]
            {
                "dl_2025_01_05.csv",
                "dl_2025_01_10.csv",
                "dl_2025_02_01.csv",
                "dl_2025_02_28.csv",
                "invalid.csv",
                "dl_2025_03_15.csv"
            };

            var groups = _sut.GroupFilesByMonth(files, _fileNameRegex);

            Assert.Equal(3, groups.Count);
            Assert.Equal(2, groups["2025_01"].Count);
            Assert.Equal(2, groups["2025_02"].Count);
            Assert.Single(groups["2025_03"]);
            Assert.DoesNotContain("invalid.csv", groups.SelectMany(kvp => kvp.Value));
        }

        [Fact]
        public void SortFilesByDay_OrdersByDayNumber()
        {
            var unsorted = new List<string>
            {
                "dl_2025_01_15.csv",
                "dl_2025_01_01.csv",
                "dl_2025_01_09.csv",
                "dl_2025_01_22.csv"
            };

            var expected = new List<string>
            {
                "dl_2025_01_01.csv",
                "dl_2025_01_09.csv",
                "dl_2025_01_15.csv",
                "dl_2025_01_22.csv"
            };

            var sorted = _sut.SortFilesByDay(unsorted, _fileNameRegex); // internal method
            Assert.Equal(expected, sorted);
        }

        [Fact]
        public void GroupFilesByMonth_GroupsCorrectly()
        {
            var files = new[]
               {
                    "dl_2025_03_01.csv",
                    "dl_2025_03_15.csv",
                    "dl_2025_04_10.csv"
                };

            var groups = _sut.GroupFilesByMonth(files, _fileNameRegex);
            Assert.Equal(2, groups.Count);
            Assert.Equal(2, groups["2025_03"].Count);
            Assert.Single(groups["2025_04"]);
        }
    }
}