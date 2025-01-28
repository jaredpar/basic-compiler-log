using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using Basic.CompilerLog;
using Basic.CompilerLog.Util;
using BenchmarkDotNet.Environments;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging.StructuredLogger;

//using Microsoft.Build.Logging.StructuredLogger;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Diagnostics.Tracing.Parsers.JScript;
using Scratch;
using TraceReloggerLib;

#pragma warning disable 8321

//var zipFilePath = @"C:\Users\jaredpar\Downloads\msbuild_logs.zip";
//using var reader = CompilerLogReader.Create(zipFilePath);
RunComplog($@"replay --compiler C:\Users\jaredpar\Downloads\dotnet-sdk-9.0.200-win-x64\sdk\9.0.200\Roslyn\bincore -p WinStore.DataContracts2.csproj C:\Users\jaredpar\Downloads\msbuild_logs\msbuild.complog");



/*
using var reader = CompilerCallReaderUtil.Create("/home/jaredpar/code/msbuild/artifacts/log/Debug/Build.binlog", BasicAnalyzerKind.None);
var compilerCall = reader
    .ReadAllCompilerCalls()
    .Single(x => x.ProjectFileName == "StringTools.UnitTests.csproj");
var data = reader.ReadCompilationData(compilerCall);
var compilation = data.GetCompilationAfterGenerators();
Console.WriteLine(compilation.AssemblyName);
var diagnostics = compilation.GetDiagnostics();
*/

//Bing();
void Bing()
{
    var filePath = @"C:\Users\jaredpar\code\snrcode\msbuild.binlog";
    using var reader = BinaryLogReader.Create(filePath, BasicAnalyzerKind.None);
    foreach (var cc in reader.ReadAllCompilerCalls())
    {
        Console.WriteLine(cc.ToString());
    }
}

// PrintGeneratedFiles();

// var filePath = @"c:\users\jaredpar\temp\console\msbuild.binlog";
// var filePath = @"C:\Users\jaredpar\code\roslyn\artifacts\log\Debug\Build.complog";
// var filePath = @"C:\Users\jaredpar\code\vs-threading\msbuild.binlog";
// var filePath = @"c:\users\jaredpar\temp\Build.complog";
// var filePath = @"C:\Users\jaredpar\code\MudBlazor\src\msbuild.binlog";
// var filePath = @"C:\Users\jaredpar\code\roslyn\src\Compilers\Core\Portable\msbuild.binlog";
// var filePath = @"C:\Users\jaredpar\Downloads\Roslyn.complog";
// var filePath = @"C:\Users\jaredpar\code\wt\ros2\artifacts\log\Debug\Build.binlog";
// var filePath = @"C:\Users\jaredpar\code\roslyn\artifacts\log\Debug\Build.binlog";
//var filePath = @"C:\Users\jaredpar\code\roslyn\src\Compilers\CSharp\csc\msbuild.binlog";

//TestDiagnostics(filePath);
// RoundTrip(filePath);


// await SolutionScratchAsync(filePath);

// Profile();

// DarkArtOfBuild();
// ReadAttribute();
// ExportScratch();
// await WorkspaceScratch();
// RoslynScratch();
// Sarif();
// var timeSpan = DateTime.UtcNow;
// RunComplog($"rsp {filePath} --project Microsoft.VisualStudio.Threading.Tests");
// Console.WriteLine(DateTime.UtcNow - timeSpan);

/*
using var reader = CompilerLogReader.Create(filePath);

VerifyAll(filePath);
Console.WriteLine("Done");
*/

/*
var reader = SolutionReader.Create(filePath);
var info = reader.ReadSolutionInfo();
var workspace = new AdhocWorkspace();
workspace.AddSolution(info);
*/


/*
var b = new CompilerBenchmark()
{
    Options = BasicAnalyzersOptions.InMemory
};
b.GenerateLog();
b.LoadAnalyzers();
*/
//_ = BenchmarkDotNet.Running.BenchmarkRunner.Run<CompilerBenchmark>();

/*
var binlogPath = @"c:\users\jaredpar\temp\console\msbuild.binlog";
var complogPath = Path.Combine(Path.GetDirectoryName(binlogPath)!, "msbuild.complog");
if (File.Exists(complogPath))
{
    File.Delete(complogPath);
}
CompilerLogUtil.ConvertBinaryLog(binlogPath, complogPath);
var reader = CompilerLogReader.Create(complogPath);
var analyzers = reader.ReadBasicAnalyzers(reader.ReadRawCompilationData(0).Item2.Analyzers, BasicAnalyzersOptions.InMemory);
foreach (var analyzer in analyzers.AnalyzerReferences)
{
    _ = analyzer.GetAnalyzersForAllLanguages();
    _ = analyzer.GetGeneratorsForAllLanguages();
}

*/

// Sample from README used to validate in still compiles
void ReadMeSample1(string logFilePath)
{
    using var reader = CompilerCallReaderUtil.Create(logFilePath);
    foreach (var compilationData in reader.ReadAllCompilationData())
    {
        var compilation = compilationData.GetCompilationAfterGenerators();
    }
}

// Sample from README used to validate in still compiles
void ReadMeSample2(string logFilePath)
{
    var reader = SolutionReader.Create(logFilePath);
    var workspace = new AdhocWorkspace();
    var solution = workspace.AddSolution(reader.ReadSolutionInfo());
}


void CountReferences()
{
    var filePath = @"C:\Users\jaredpar\code\roslyn\artifacts\log\Debug\Build.binlog";
    var reader = BinaryLogReader.Create(filePath);
    var refCount = 0;
    var satRefCount = 0;


    foreach (var cc in reader.ReadAllCompilerCalls())
    {
        var references = reader.ReadAllReferenceData(cc);
        refCount += references.Count;
        if (cc.Kind == CompilerCallKind.Satellite)
        {
            satRefCount += references.Count;
        }
    }

    Console.WriteLine($"Reference count: {refCount}");
    Console.WriteLine($"Satellite Reference count: {satRefCount}");
}

void DarkArtOfBuild()
{
    var filePath = @"C:\Users\jaredpar\Downloads\out_no_analyzers.binlog";
    const string targetProjectFile = @"D:\a\_work\1\s\Source\Provider\Provider\MiddleTier.Provider.csproj";
    var map = new Dictionary<int, List<string>>();
    var targetMap = new Dictionary<(int, int), string>();
    var set = new HashSet<int>();
    using var stream = File.OpenRead(filePath);
    var records = BinaryLog.ReadRecords(stream);
    foreach (var record in records)
    {
        if (record.Args is not { BuildEventContext: { } context })
        {
            continue;
        }

        switch (record.Args)
        {
            case ProjectStartedEventArgs { ProjectFile: targetProjectFile } e:
            {
                _ = set.Add(context.ProjectContextId);
                break;
            }
            case TaskStartedEventArgs { ProjectFile: targetProjectFile } e:
            {
                if (e.TaskName != "Csc")
                {
                    break;
                }

                var key = (context.ProjectContextId, context.TargetId);
                if (!targetMap.TryGetValue(key, out var targetName))
                {
                    targetName = "<Unknown>";
                    Console.WriteLine($"Missing target {e.TaskName}");
                    break;
                }

                var list = GetOrCreate(context.ProjectContextId);
                list.Add($"{targetName},{context.TargetId},{context.TaskId}");
                break;
            }
            case TargetStartedEventArgs { ProjectFile: targetProjectFile } e:
            {
                var key = (context.ProjectContextId, context.TargetId);
                if (targetMap.TryGetValue(key, out var target))
                {
                    Console.WriteLine($"Duplicate target {e.TargetName}");
                }

                targetMap[key] = e.TargetName;
                break;
            }
        }
    }

    foreach (var id in set)
    {
        if (!map.TryGetValue(id, out var list))
        {
            continue;
        }

        Console.WriteLine(id);
        foreach (var item in list)
        {
            Console.WriteLine($"  {item}");
        }
    }

    List<string> GetOrCreate(int projectContextId)
    {
        if (!map.TryGetValue(projectContextId, out var list))
        {
            list = new List<string>();
            map[projectContextId] = list;
        }

        return list;
    }
}

void ReadAttribute()
{
    var assemblyPath = @"c:\Program Files\dotnet\sdk\8.0.204\Roslyn\bincore\csc.dll";
    using (var stream = File.OpenRead(assemblyPath))
    using (var peReader = new PEReader(stream))
    {
        var metadataReader = peReader.GetMetadataReader();
        var attributes = metadataReader.GetAssemblyDefinition().GetCustomAttributes();
        foreach (var attributeHandle in attributes)
        {
            var attribute = metadataReader.GetCustomAttribute(attributeHandle);
            if (attribute.Constructor.Kind is HandleKind.MemberReference)
            {
                var ctor = metadataReader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (ctor.Parent.Kind is HandleKind.TypeReference)
                {
                    var typeNameHandle = metadataReader.GetTypeReference((TypeReferenceHandle)ctor.Parent).Name;
                    var typeName = metadataReader.GetString(typeNameHandle);
                    if (typeName.EndsWith("CommitHashAttribute"))
                    {
                        var value = metadataReader.GetBlobReader(attribute.Value);
                        _ = value.ReadBytes(2); // prolog
                        var str = value.ReadSerializedString();
                        Console.WriteLine("here");
                    }
                }
            }
        }
    }
}

void TestBinaryLogReader()
{
    var binlogPath = @"e:\temp\console\build.binlog";
    var reader = BinaryLogReader.Create(binlogPath);
    var all = reader.ReadAllCompilationData();
    foreach (var data in all)
    {
        var compilation = data.GetCompilationAfterGenerators();
        var diagnostics = compilation.GetDiagnostics();
        foreach (var d in diagnostics)
        {
            Console.WriteLine(d.GetMessage());
        }
    }
}

void PrintCompilers(string filePath)
{
    using var reader = CompilerLogReader.Create(filePath);
    foreach (var info in reader.ReadAllCompilerAssemblies())
    {
        Console.WriteLine(info.FilePath);
        Console.WriteLine(info.AssemblyName);
    }
}

static void Test(ITypeSymbol symbol)
{
    if (symbol is not INamedTypeSymbol { IsGenericType: true } nt)
    {
        return;
    }

    Console.WriteLine(nt.Name);
}

static void PrintGeneratedFiles()
{
    var binlogPath = @"C:\Users\jaredpar\code\roslyn\msbuild.binlog";
    using var reader = CompilerLogReader.Create(binlogPath);
    foreach (var cc in reader.ReadAllCompilerCalls())
    {
        var generatedFiles = reader.ReadAllRawContent(cc, RawContentKind.GeneratedText);
        if (!generatedFiles.Any())
        {
            continue;
        }

        Console.WriteLine(cc.GetDiagnosticName());
        foreach (var file in generatedFiles)
        {
            Console.WriteLine($"  {file.OriginalFilePath}");
        }
    }
}

/*static async Task WorkspaceScratch()
{
    var filePath = @"/mnt/c/Users/jaredpar/temp/console/msbuild.complog";
    using var reader = SolutionReader.Create(filePath, BasicAnalyzerKind.None);
    using var workspace = new AdhocWorkspace();
    workspace.AddSolution(reader.ReadSolutionInfo());
    foreach (var project in workspace.CurrentSolution.Projects)
    {
        foreach (var document in project.Documents)
        {
            var text = await document.GetTextAsync();
            var textSpan = new TextSpan(0, text.Length);
            _ = await Classifier.GetClassifiedSpansAsync(document, textSpan);
        }
    }
}
*/

static void ExportScratch()
{
    var filePath = @"/mnt/c/Users/jaredpar/temp/console/msbuild.complog";
    var dest = "/home/jaredpar/temp/export";
    if (Directory.Exists(dest))
    {
        Directory.Delete(dest, recursive: true);
    }

    using var reader = CompilerLogReader.Create(filePath, BasicAnalyzerKind.None);
    var exportUtil = new ExportUtil(reader);
    exportUtil.ExportAll(dest, SdkUtil.GetSdkDirectories());
}

static void RoslynScratch()
{
    var code = """
        using System;
        public class C {
            public void M() {
        #line (7,2)-(7,6) 24 "C:\SomeProject\SomeRazorFile.razor"
        Console.WriteLine("hello world");
            }
        }

        """;

    var syntaxTree = CSharpSyntaxTree.ParseText(code);
    var compilation = CSharpCompilation.Create(
        "scratch",
        new[] { syntaxTree },
        Basic.Reference.Assemblies.Net60.References.All);

    var context = compilation.GetSemanticModel(syntaxTree);
    var token = syntaxTree
        .GetRoot()
        .DescendantTokens()
        .First(x => x.Text == "Console");

    /*
    var node = syntaxTree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>().Single();
    var cType = context.GetDeclaredSymbol(node);
    var enumerableType = compilation.GetSpecialType(SpecialType.System_Collections_IEnumerable);
    var conversion = compilation.ClassifyConversion(cType!, enumerableType);
    Console.WriteLine(conversion.Exists);
    */


    /*
    var node = syntaxTree.GetRoot().DescendantNodes().OfType<ParameterSyntax>().Single();
    var format = SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);
    if (context.GetDeclaredSymbol(node) is IParameterSymbol { Type: var type })
    {
        Console.WriteLine(type.ToDisplayString(format));
    }
    else
    {
        // error resolving parameter, possible errors in the compilation
    }
    */

}

static void Sarif()
{
    var binlogPath = @"C:\Users\jaredpar\Downloads\BlazorWebApp\BlazorWebApp\msbuild.binlog";
    using var reader = CompilerLogReader.Create(binlogPath);
    var cc = reader.ReadAllCompilerCalls().Single(x => x.ProjectFilePath.Contains("BlazorWebApp.csproj"));
    var data = reader.ReadCompilationData(cc);
    var diagnostics = data.GetAllDiagnosticsAsync().GetAwaiter().GetResult();
    foreach (var d in diagnostics)
    {
        Console.WriteLine(d.Location.GetLineSpan());
        Console.WriteLine(d.Location.GetMappedLineSpan());
    }
}

void Profile()
{
    var binlogPath = @"c:\users\jaredpar\temp\console\msbuild.binlog";
    var complogPath = @"c:\users\jaredpar\temp\console\msbuild.complog";

    if (File.Exists(complogPath))
    {
        File.Delete(complogPath);
    }

    _ = CompilerLogUtil.ConvertBinaryLog(binlogPath, complogPath);

    using var reader = CompilerLogReader.Create(complogPath);
    foreach (var compilerCall in reader.ReadAllCompilerCalls())
    {
        _ = reader.ReadCompilationData(compilerCall);
    }
}

int RunComplog(string args)
{
    var assembly = typeof(FilterOptionSet).Assembly;
    var program = assembly.GetType("Program", throwOnError: true);
    var main = program!.GetMethod("<Main>$", BindingFlags.Static | BindingFlags.NonPublic);
    return (int)main!.Invoke(null, new[] { args.Split(' ', StringSplitOptions.RemoveEmptyEntries) })!;
}

void VerifyAll(string logPath)
{
    var exportDest = @"c:\users\jaredpar\temp\export";
    RunComplog($"replay {logPath} -o {exportDest} -export");
}

void ExportTest(CompilerLogReader reader)
{
    var dest = @"c:\users\jaredpar\temp\export";
    EmptyDirectory(dest);

    var d = SdkUtil.GetSdkDirectories();
    var util = new ExportUtil(reader);
    util.ExportAll(dest, d);
}

static void EmptyDirectory(string directory)
{
    if (Directory.Exists(directory))
    {
        var d = new DirectoryInfo(directory);
        foreach(System.IO.FileInfo file in d.GetFiles()) file.Delete();
        foreach(System.IO.DirectoryInfo subDirectory in d.GetDirectories()) subDirectory.Delete(true);
    }
}

static void RoundTrip(string binlogFilePath)
{
    var compilerLogFilePath = @"c:\users\jaredpar\temp\compiler.zip";
    using var stream = File.OpenRead(binlogFilePath);
    var diagnosticList = CompilerLogUtil.ConvertBinaryLog(binlogFilePath, compilerLogFilePath);
    foreach (var diagnostic in diagnosticList)
    {
        Console.WriteLine(diagnostic);
    }

    using var compilerLogStream = new FileStream(compilerLogFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var reader = CompilerLogReader.Create(compilerLogStream);
    for (int i = 0; i < reader.Count; i++)
    {
        var compilerCall = reader.ReadCompilerCall(i);
        Console.WriteLine($"{compilerCall.ProjectFilePath} ({compilerCall.TargetFramework})");

        var compilation = reader.ReadCompilationData(compilerCall);
    }
}

static async System.Threading.Tasks.Task SolutionScratchAsync(string binlogFilePath)
{
    using var stream = CompilerLogUtil.GetOrCreateCompilerLogStream(binlogFilePath);
    using var reader = SolutionReader.Create(stream, leaveOpen: false);
    var solution = reader.ReadSolutionInfo();
    var workspace = new AdhocWorkspace();
    workspace.AddSolution(solution);

    foreach (var project in workspace.CurrentSolution.Projects)
    {
        var compilation = await project.GetCompilationAsync();
        foreach (var syntaxTree in compilation!.SyntaxTrees)
        {
            Console.WriteLine(syntaxTree.ToString());
        }
    }
}

static void TestDiagnostics(string binlogFilePath)
{
    using var compilerLogStream = CompilerLogUtil.GetOrCreateCompilerLogStream(binlogFilePath); 
    using var reader = CompilerLogReader.Create(compilerLogStream);
    for (int i = 0; i < reader.Count; i++)
    {
        var compilationData = reader.ReadCompilationData(i);
        var compilation = compilationData.GetCompilationAfterGenerators();
        var diagnostics = compilation.GetDiagnostics().Where(x => x.Severity >= DiagnosticSeverity.Warning);
        foreach (var d in diagnostics)
        {
            Console.WriteLine(d.GetMessage());
        }
    }
}




