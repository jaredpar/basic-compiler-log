
using Basic.CompilerLog.Util;
using Basic.CompilerLog.Util.Impl;
using Xunit;
using Xunit.Abstractions;

namespace Basic.CompilerLog.UnitTests;

[Collection(CompilerLogCollection.Name)]
public sealed class CompilationDataTests : TestBase
{
    public CompilerLogFixture Fixture { get; }

    public CompilationDataTests(ITestOutputHelper testOutputHelper, CompilerLogFixture fixture)
        : base(testOutputHelper, nameof(CompilationDataTests))
    {
        Fixture = fixture;
    }

    [Fact]
    public void EmitToMemoryCombinations()
    {
        using var reader = CompilerLogReader.Create(Fixture.ClassLib.Value.CompilerLogPath);
        var data = reader.ReadCompilationData(0);

        var emitResult = data.EmitToMemory();
        Assert.True(emitResult.Success);
        AssertEx.HasData(emitResult.AssemblyStream);
        AssertEx.HasData(emitResult.PdbStream);
        Assert.Null(emitResult.XmlStream);
        AssertEx.HasData(emitResult.MetadataStream);

        emitResult = data.EmitToMemory(EmitFlags.IncludePdbStream);
        Assert.True(emitResult.Success);
        AssertEx.HasData(emitResult.AssemblyStream);
        AssertEx.HasData(emitResult.PdbStream);
        Assert.Null(emitResult.XmlStream);
        Assert.Null(emitResult.MetadataStream);

        emitResult = data.EmitToMemory(EmitFlags.IncludePdbStream | EmitFlags.IncludeXmlStream);
        Assert.True(emitResult.Success);
        AssertEx.HasData(emitResult.AssemblyStream);
        AssertEx.HasData(emitResult.PdbStream);
        AssertEx.HasData(emitResult.XmlStream);
        Assert.Null(emitResult.MetadataStream);

        emitResult = data.EmitToMemory(EmitFlags.IncludePdbStream | EmitFlags.IncludeXmlStream | EmitFlags.IncludeMetadataStream);
        Assert.True(emitResult.Success);
        AssertEx.HasData(emitResult.AssemblyStream);
        AssertEx.HasData(emitResult.PdbStream);
        AssertEx.HasData(emitResult.XmlStream);
        AssertEx.HasData(emitResult.MetadataStream);

        emitResult = data.EmitToMemory(EmitFlags.MetadataOnly);
        Assert.True(emitResult.Success);
        AssertEx.HasData(emitResult.AssemblyStream);
        Assert.Null(emitResult.PdbStream);
        Assert.Null(emitResult.XmlStream);
        Assert.Null(emitResult.MetadataStream);
    }

    [Fact]
    public void EmitToMemoryRefOnly()
    {
        using var reader = CompilerLogReader.Create(Fixture.ClassLibRefOnly.Value.CompilerLogPath);
        var data = reader.ReadCompilationData(0);
        var result = data.EmitToMemory();
        Assert.True(result.Success);
    }

    [Fact]
    public void GetAnalyzersNormal()
    {
        using var reader = CompilerLogReader.Create(Fixture.ClassLib.Value.CompilerLogPath);
        var data = reader.ReadCompilationData(0);
        Assert.NotEmpty(data.GetAnalyzers());
    }

    [Fact]
    public void GetAnalyzersNoHosting()
    {
        using var reader = CompilerLogReader.Create(Fixture.ClassLib.Value.CompilerLogPath, BasicAnalyzerKind.None);
        var data = reader.ReadCompilationData(0);
        Assert.Empty(data.GetAnalyzers());
    }

    [Fact]
    public void GetDiagnostics()
    {
        using var reader = CompilerLogReader.Create(Fixture.ClassLib.Value.CompilerLogPath, BasicAnalyzerHost.DefaultKind);
        var data = reader.ReadCompilationData(0);
        Assert.NotEmpty(data.GetDiagnostics());
    }

    [Fact]
    public async Task GetAllDiagnostics()
    {
        using var reader = CompilerLogReader.Create(Fixture.ClassLib.Value.CompilerLogPath, BasicAnalyzerHost.DefaultKind);
        var data = reader.ReadCompilationData(0);
        Assert.NotEmpty(await data.GetAllDiagnosticsAsync());
    }

    [Fact]
    public void GetCompilationAfterGeneratorsDiagnostics()
    {
        using var reader = CompilerLogReader.Create(
            Fixture.Console.Value.CompilerLogPath,
            BasicAnalyzerKind.InMemory);
        var rawData = reader.ReadRawCompilationData(0).Item2;
        var analyzers = rawData.Analyzers
            .Where(x => x.FileName != "Microsoft.CodeAnalysis.NetAnalyzers.dll")
            .ToList();
        var host = new BasicAnalyzerHostInMemory(reader, analyzers);
        var data = (CSharpCompilationData)reader.ReadCompilationData(0);
        data = new CSharpCompilationData(
            data.CompilerCall,
            data.Compilation,
            data.ParseOptions,
            data.EmitOptions,
            data.EmitData,
            data.AdditionalTexts,
            host,
            data.AnalyzerConfigOptionsProvider);
        _ = data.GetCompilationAfterGenerators(out var diagnostics);
        Assert.NotEmpty(diagnostics);
    }

    [Theory]
    [CombinatorialData]
    public void GetGeneratedSyntaxTrees(BasicAnalyzerKind basicAnalyzerKind)
    {
        using var reader = CompilerLogReader.Create(Fixture.Console.Value.CompilerLogPath, basicAnalyzerKind);
        var data = reader.ReadAllCompilationData().Single();
        var trees = data.GetGeneratedSyntaxTrees();
        Assert.Single(trees);

        trees = data.GetGeneratedSyntaxTrees(out var diagnostics);
        Assert.Single(trees);
        Assert.Empty(diagnostics);
    }
}
