﻿using Basic.CompilerLog.Util;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Basic.CompilerLog.UnitTests;

public readonly struct LogData(string compilerLogPath, string? binaryLogPath)
{
    public string CompilerLogPath { get; } = compilerLogPath;
    public string? BinaryLogPath { get; } = binaryLogPath;

    public override string ToString() => $"{Path.GetFileName(CompilerLogPath)}";
}

public sealed class FileLockHold(List<Stream> streams) : IDisposable
{
    public List<Stream> Streams { get; } = streams;

    public void Dispose()
    {
        foreach (var stream in Streams)
        {
            stream.Dispose();
        }
        Streams.Clear();
    }
}

public sealed class CompilerLogFixture : FixtureBase, IDisposable
{
    private readonly ImmutableArray<Lazy<LogData>> _allLogs;

    /// <summary>
    /// Storage directory for all the generated artifacts and scatch directories
    /// </summary>
    internal string StorageDirectory { get; }

    internal string ScratchDirecectory { get; }

    /// <summary>
    /// Directory that holds the log files
    /// </summary>
    internal string ComplogDirectory { get; }

    internal Lazy<LogData> Console { get; }

    internal Lazy<LogData> ConsoleNoGenerator { get; }

    /// <summary>
    /// This is a console project that has every nasty feature that can be thought of
    /// like resources, line directives, embeds, etc ... Rather than running a 
    /// `dotnet build` for every one of these individually (which is expensive) in 
    /// unit tests try to create a single project that has all of them.
    /// </summary>
    internal Lazy<LogData> ConsoleComplex { get; }

    internal Lazy<LogData> ClassLib { get; }

    internal Lazy<LogData> ClassLibRefOnly { get; }

    /// <summary>
    /// A multi-targeted class library
    /// </summary>
    internal Lazy<LogData> ClassLibMulti { get; }

    internal Lazy<LogData> ConsoleVisualBasic { get; }

    internal Lazy<LogData>? WpfApp { get; }

    /// <summary>
    /// Named complog value that makes intent of getting signed one clear
    /// </summary>
    internal Lazy<LogData> ConsoleSigned => ConsoleComplex;

    /// <summary>
    /// Constructor for the primary fixture. To get actual diagnostic messages into the output 
    /// Add the following to xunit.runner.json to enable "diagnosticMessages": true
    /// </summary>
    public CompilerLogFixture(IMessageSink messageSink)
        : base(messageSink)
    {
        StorageDirectory = Path.Combine(Path.GetTempPath(), nameof(CompilerLogFixture), Guid.NewGuid().ToString("N"));
        ComplogDirectory = Path.Combine(StorageDirectory, "logs");
        ScratchDirecectory = Path.Combine(StorageDirectory, "scratch dir");
        Directory.CreateDirectory(ComplogDirectory);
        Directory.CreateDirectory(ScratchDirecectory);

        var testArtifactsDir = Environment.GetEnvironmentVariable("TEST_ARTIFACTS_PATH");
        if (testArtifactsDir is not null)
        {
            testArtifactsDir = Path.Combine(testArtifactsDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testArtifactsDir);
        }

        var builder = ImmutableArray.CreateBuilder<Lazy<LogData>>();
        Console = WithBuild("console.complog", void (string scratchPath) =>
        {
            RunDotnetCommand($"new console --name console --output .", scratchPath);
            var projectFileContent = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """;
            File.WriteAllText(Path.Combine(scratchPath, "console.csproj"), projectFileContent, TestBase.DefaultEncoding);
            var program = """
                using System;
                using System.Text.RegularExpressions;
                // This is an amazing resource
                var r = Util.GetRegex();
                Console.WriteLine(r);

                partial class Util {
                    [GeneratedRegex("abc|def", RegexOptions.IgnoreCase, "en-US")]
                    internal static partial Regex GetRegex();
                }
                """;
            File.WriteAllText(Path.Combine(scratchPath, "Program.cs"), program, TestBase.DefaultEncoding);
            RunDotnetCommand("build -bl -nr:false", scratchPath);
        });

        ClassLibMulti = WithBuild("classlibmulti.complog", void (string scratchPath) =>
        {
            RunDotnetCommand($"new classlib --name classlibmulti --output .", scratchPath);
            var projectFileContent = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net6.0;net8.0</TargetFrameworks>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """;
            File.WriteAllText(Path.Combine(scratchPath, "classlibmulti.csproj"), projectFileContent, TestBase.DefaultEncoding);
            var program = """
                using System;
                using System.Text.RegularExpressions;

                partial class Util {
                    internal static Regex GetRegex() => null!;
                }
                """;
            File.WriteAllText(Path.Combine(scratchPath, "Class1.cs"), program, TestBase.DefaultEncoding);
            RunDotnetCommand("build -bl -nr:false", scratchPath);
        });

        ClassLibRefOnly = WithBuild("classlibrefonly.complog", void (string scratchPath) =>
        {
            RunDotnetCommand($"new classlib --name classlibrefonly --output .", scratchPath);
            var projectFileContent = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net6.0;net8.0</TargetFrameworks>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <ProduceOnlyReferenceAssembly>true</ProduceOnlyReferenceAssembly>
                  </PropertyGroup>
                </Project>
                """;
            File.WriteAllText(Path.Combine(scratchPath, "classlibrefonly.csproj"), projectFileContent, TestBase.DefaultEncoding);
            var program = """
                public class Util {
                    public void M() { }
                }
                """;
            File.WriteAllText(Path.Combine(scratchPath, "Util.cs"), program, TestBase.DefaultEncoding);
            RunDotnetCommand("build -bl -nr:false", scratchPath);
        }, expectDiagnosticMessages: true);

        ConsoleNoGenerator = WithBuild("console-no-generator.complog", void (string scratchPath) =>
        {
            RunDotnetCommand($"new console --name example-no-generator --output .", scratchPath);
            RunDotnetCommand("build -bl -nr:false", scratchPath);
        });

        ConsoleVisualBasic = WithBuild("console-vb.complog", void (string scratchPath) =>
        {
            RunDotnetCommand($"new console --name console-vb --language VB --output .", scratchPath);
            File.WriteAllText(Path.Combine(scratchPath, "Extra.vb"), """
                Module M

                #ExternalSource("line.txt", 0)
                    Sub G()

                    End Sub
                #End ExternalSource
                End Module
                """, TestBase.DefaultEncoding);
            File.WriteAllText(Path.Combine(scratchPath, "line.txt"), "this is content", TestBase.DefaultEncoding);
            File.WriteAllText(Path.Combine(scratchPath, "console-vb.vbproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                        <OutputType>Exe</OutputType>
                        <RootNamespace>vbconsole</RootNamespace>
                        <TargetFramework>net8.0</TargetFramework>
                        <EmbedAllSources>true</EmbedAllSources>
                    </PropertyGroup>
                </Project>
                """, TestBase.DefaultEncoding);
            RunDotnetCommand("build -bl -nr:false", scratchPath);
        });
        
        ConsoleComplex = WithBuild("console-complex.complog", void (string scratchPath) =>
        {
            RunDotnetCommand($"new console --name console-complex --output .", scratchPath);
            var keyFilePath = Path.Combine(scratchPath, "Key.snk");
            File.WriteAllText(Path.Combine(scratchPath, "console-complex.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <EmbedAllSources>true</EmbedAllSources>
                    <CodeAnalysisRuleset>{scratchPath}\example.ruleset</CodeAnalysisRuleset>
                    <Win32Manifest>resource.txt</Win32Manifest>
                    <KeyOriginatorFile>{keyFilePath}</KeyOriginatorFile>
                    <DocumentationFile>console-complex.xml</DocumentationFile>
                    <SourceLink>example.sourcelink</SourceLink>
                  </PropertyGroup>
                  <ItemGroup>
                    <EmbeddedResource Include="resource.txt" />
                    <AdditionalFiles Include="additional.txt" FixtureKey="true" />
                    <CompilerVisibleItemMetadata Include="AdditionalFiles" MetadataName="FixtureKey" />
                  </ItemGroup>
                </Project>
                """, TestBase.DefaultEncoding);

            File.WriteAllBytes(keyFilePath, ResourceLoader.GetResourceBlob("Key.snk"));

            File.WriteAllText(Path.Combine(scratchPath, "Extra.cs"), """
                using System;
                using System.Text.RegularExpressions;

                // File that does exsit
                #line 42 "line.txt"
                class C { }
                """, TestBase.DefaultEncoding);
            File.WriteAllText(Path.Combine(scratchPath, "line.txt"), "this is content", TestBase.DefaultEncoding);

            File.WriteAllText(Path.Combine(scratchPath, "additional.txt"), """
                This is an additional file. 
                It just has some text in it
                """, TestBase.DefaultEncoding);

            // File with a space in the name to make sure we quote correctly in RSP
            File.WriteAllText(Path.Combine(scratchPath, "Code With Space In The Name.cs"), """
                class D { }
                """, TestBase.DefaultEncoding);

            File.WriteAllText(Path.Combine(scratchPath, "resource.txt"), """
                This is an awesome resource
                """);

            File.WriteAllText(Path.Combine(scratchPath, "example.ruleset"), """
                <RuleSet Name="Rules for Hello World project" Description="These rules focus on critical issues for the Hello World app." ToolsVersion="10.0">
                    <Localization ResourceAssembly="Microsoft.VisualStudio.CodeAnalysis.RuleSets.Strings.dll" ResourceBaseName="Microsoft.VisualStudio.CodeAnalysis.RuleSets.Strings.Localized">
                        <Name Resource="HelloWorldRules_Name" />
                        <Description Resource="HelloWorldRules_Description" />
                    </Localization>
                    <Rules AnalyzerId="Microsoft.Analyzers.ManagedCodeAnalysis" RuleNamespace="Microsoft.Rules.Managed">
                        <Rule Id="CA1001" Action="Warning" />
                        <Rule Id="CA1009" Action="Warning" />
                        <Rule Id="CA1016" Action="Warning" />
                        <Rule Id="CA1033" Action="Warning" />
                    </Rules>
                    <Rules AnalyzerId="Microsoft.CodeQuality.Analyzers" RuleNamespace="Microsoft.CodeQuality.Analyzers">
                        <Rule Id="CA1802" Action="Error" />
                        <Rule Id="CA1814" Action="Info" />
                        <Rule Id="CA1823" Action="None" />
                        <Rule Id="CA2217" Action="Warning" />
                    </Rules>
                </RuleSet>
                """);

            File.WriteAllText(Path.Combine(scratchPath, "example.sourcelink"), """
                {
                    "documents": {
                        "C:\\src\\complog\\*": "https://raw.githubusercontent.com/dotnet/complog/bcc51178e1a82fb2edaf47285f6e577989a7333f/*"
                    }
                }
                """);

            File.WriteAllText(Path.Combine(scratchPath, ".editorconfig"), """
                # This file is the top-most EditorConfig file
                root = true

                # All Files
                [*]
                charset = utf-8
                indent_style = space
                indent_size = 4
                insert_final_newline = true
                trim_trailing_whitespace = true

                [*.{cs,vb,}]
                # Default Severity for all .NET Code Style rules below
                dotnet_analyzer_diagnostic.severity = warning

                dotnet_style_qualification_for_field = true:warning
                dotnet_style_qualification_for_property = true:warning
                dotnet_style_qualification_for_method = true:warning
                dotnet_style_qualification_for_event = true:warning
                """);
            RunDotnetCommand("build -bl -nr:false", scratchPath);
        });

        ClassLib = WithBuild("classlib.complog", void (string scratchPath) =>
        {
            RunDotnetCommand($"new classlib --name classlib --output . --framework net8.0", scratchPath);
            var program = """
                using System;
                using System.Text.RegularExpressions;

                partial class Util {
                    [GeneratedRegex("abc|def", RegexOptions.IgnoreCase, "en-US")]
                    internal static partial Regex GetRegex();
                }
                """;
            File.WriteAllText(Path.Combine(scratchPath, "Class1.cs"), program, TestBase.DefaultEncoding);
            RunDotnetCommand("build -bl -nr:false", scratchPath);
        });

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WpfApp = WithBuild("wpfapp.complog", void (string scratchPath) =>
            {
                RunDotnetCommand("new wpf --name wpfapp --output .", scratchPath);
                RunDotnetCommand("build -bl -nr:false", scratchPath);
            });
        }

        WithResource("linux-console.complog");
        WithResource("windows-console.complog");

        _allLogs = builder.ToImmutable();
        Lazy<LogData> WithBuild(string name, Action<string> action, bool expectDiagnosticMessages = false)
        {
            var lazy = new Lazy<LogData>(() =>
            {
                var start = DateTime.UtcNow;
                try
                {
                    var scratchPath = Path.Combine(ScratchDirecectory, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(scratchPath);
                    messageSink.OnDiagnosticMessage($"Starting {name} in {scratchPath}");
                    RunDotnetCommand("new globaljson --sdk-version 8.0.300", scratchPath);
                    action(scratchPath);
                    var binlogFilePath = Path.Combine(scratchPath, "msbuild.binlog");
                    Assert.True(File.Exists(binlogFilePath));
                    var complogFilePath = Path.Combine(ComplogDirectory, name);
                    var compilerCalls = new List<CompilerCall>();
                    var diagnostics = CompilerLogUtil.ConvertBinaryLog(binlogFilePath, complogFilePath, cc => 
                    {
                        compilerCalls.Add(cc);
                        return true;
                    });

                    if (testArtifactsDir is not null)
                    {
                        File.Copy(binlogFilePath, Path.Combine(testArtifactsDir, Path.ChangeExtension(name, ".binlog")));
                    }

                    if (!expectDiagnosticMessages)
                    {
                        Assert.Empty(diagnostics);
                    }

                    return new LogData(complogFilePath, binlogFilePath);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Cannot generate compiler log {name}", ex);
                }
                finally
                {
                    messageSink.OnDiagnosticMessage($"Finished {name} {(DateTime.UtcNow - start).TotalSeconds:F2}s");
                }
            });

            builder.Add(lazy);
            return lazy;
        }

        void WithResource(string name)
        {
            var filePath = Path.Combine(ComplogDirectory, name);
            File.WriteAllBytes(filePath, ResourceLoader.GetResourceBlob(name));
            builder.Add(new Lazy<LogData>(() => new LogData(filePath, null)));
        }
    }

    public async IAsyncEnumerable<LogData> GetAllLogDatas(ITestOutputHelper testOutputHelper)
    {
        var start = DateTime.UtcNow;
        foreach (var logData in _allLogs)
        {
            if (logData.IsValueCreated)
            {
                testOutputHelper.WriteLine($"Using cached value");
                yield return logData.Value;
            }
            else
            {
                TaskCompletionSource<LogData> tcs = new();
                await Task.Factory.StartNew(() =>
                {
                    try
                    {
                        testOutputHelper.WriteLine($"Starting {nameof(GetAllCompilerLogs)}");
                        tcs.SetResult(logData.Value);
                        testOutputHelper.WriteLine($"Finished {nameof(GetAllCompilerLogs)} {(DateTime.UtcNow - start).TotalSeconds:F2}s");
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }, TaskCreationOptions.LongRunning);

                yield return await tcs.Task;
            }
        }
    } 

    /// <summary>
    /// This locks all the files on disk that are a part of this build. That allows us
    /// to validate operations involving the compiler log don't actually use the contents
    /// on disk
    /// </summary>
    public FileLockHold LockScratchDirectory()
    {
        var list = new List<Stream>();
        foreach (var filePath in Directory.EnumerateFiles(ScratchDirecectory, "*", SearchOption.AllDirectories))
        {
            // Don't lock the binlog or complogs as that is what the code is actually going to be reading
            if (Path.GetExtension(filePath) is ".binlog" or ".complog")
            {
                continue;
            }

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            list.Add(stream);
        }
        return new FileLockHold(list);
    }

    public async IAsyncEnumerable<string> GetAllLogs(ITestOutputHelper testOutputHelper)
    {
        await foreach (var logData in GetAllLogDatas(testOutputHelper))
        {
            yield return logData.CompilerLogPath;
            if (logData.BinaryLogPath is { } binaryLogPath)
            {
                yield return binaryLogPath;
            }
        }
    }

    public async IAsyncEnumerable<string> GetAllCompilerLogs(ITestOutputHelper testOutputHelper)
    {
        await foreach (var logData in GetAllLogDatas(testOutputHelper))
        {
            yield return logData.CompilerLogPath;
        }
    }

    public async IAsyncEnumerable<string> GetAllBinaryLogs(ITestOutputHelper testOutputHelper)
    {
        await foreach (var logData in GetAllLogDatas(testOutputHelper))
        {
            if (logData.BinaryLogPath is { } binaryLog)
            {
                yield return binaryLog;
            }
        }
    }

    public void Dispose()
    {
        Directory.Delete(StorageDirectory, recursive: true);
    }
}

[CollectionDefinition(Name)]
public sealed class CompilerLogCollection : ICollectionFixture<CompilerLogFixture>
{
    public const string Name = "Compiler Log Collection";
}
