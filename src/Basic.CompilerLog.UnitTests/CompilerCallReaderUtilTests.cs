using Basic.CompilerLog.Util;
using Xunit;
using Xunit.Abstractions;

namespace Basic.CompilerLog.UnitTests;

[Collection(CompilerLogCollection.Name)]
public sealed class CompilerCallReaderUtilTests : TestBase
{
    public CompilerLogFixture Fixture { get; }

    public CompilerCallReaderUtilTests(ITestOutputHelper testOutputHelper, CompilerLogFixture fixture)
        : base(testOutputHelper, nameof(CompilerLogReaderTests))
    {
        Fixture = fixture;
    }

    [Fact]
    public void CreateBadExtension()
    {
        Assert.Throws<ArgumentException>(() => CompilerCallReaderUtil.Create("file.bad"));
    }

    [Theory]
    [CombinatorialData]
    public void GetAllAnalyzerKinds(BasicAnalyzerKind basicAnalyzerKind)
    {
        using var reader = CompilerCallReaderUtil.Create(Fixture.Console.Value.CompilerLogPath!, basicAnalyzerKind);
        Assert.Equal(basicAnalyzerKind, reader.BasicAnalyzerKind);
    }
}