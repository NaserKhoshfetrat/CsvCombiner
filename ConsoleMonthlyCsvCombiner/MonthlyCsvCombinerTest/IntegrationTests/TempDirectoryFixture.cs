namespace MonthlyCsvCombinerTest.IntegrationTests
{
    public class TempDirectoryFixture : IDisposable
    {
        public string Path { get; private set; }
        public TempDirectoryFixture()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}