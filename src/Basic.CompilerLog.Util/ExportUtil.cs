﻿using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace Basic.CompilerLog.Util;

/// <summary>
/// Tool to export compilations to disk for other uses
/// </summary>
public sealed partial class ExportUtil
{
    private sealed class ContentBuilder
    {
        internal string DestinationDirectory { get; }
        internal string SourceDirectory { get; }
        internal string EmbeddedResourceDirectory { get; }
        internal string OriginalProjectFilePath { get; }
        internal string OriginalProjectDirectory { get; }
        internal ResilientDirectory MiscDirectory { get; }
        internal ResilientDirectory AnalyzerDirectory { get; }
        internal ResilientDirectory GeneratedCodeDirectory { get; }
        internal ResilientDirectory BuildOutput { get; }

        internal ContentBuilder(string destinationDirectory, string originalProjectFilePath)
        {
            DestinationDirectory = destinationDirectory;
            OriginalProjectFilePath = originalProjectFilePath;
            OriginalProjectDirectory = Path.GetDirectoryName(OriginalProjectFilePath)!;
            SourceDirectory = Path.Combine(destinationDirectory, "src");
            EmbeddedResourceDirectory = Path.Combine(destinationDirectory, "resources");
            MiscDirectory = new(Path.Combine(destinationDirectory, "misc"));
            GeneratedCodeDirectory = new(Path.Combine(destinationDirectory, "generated"));
            AnalyzerDirectory = new(Path.Combine(destinationDirectory, "analyzers"));
            BuildOutput = new(Path.Combine(destinationDirectory, "output"), flatten: true);
            Directory.CreateDirectory(SourceDirectory);
            Directory.CreateDirectory(EmbeddedResourceDirectory);
        }

        internal string GetNewSourcePath(string originalFilePath)
        {
            string filePath;
            if (originalFilePath.StartsWith(OriginalProjectDirectory, PathUtil.Comparison))
            {
                filePath = PathUtil.ReplacePathStart(originalFilePath, OriginalProjectDirectory, SourceDirectory);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            }
            else
            {
                return MiscDirectory.GetNewFilePath(originalFilePath);
            }

            return filePath;
        }

        internal string WriteContent(string originalFilePath, string contents)
        {
            var newFilePath = GetNewSourcePath(originalFilePath);
            File.WriteAllText(newFilePath, contents);
            return newFilePath;
        }

        /// <summary>
        /// Writes the content to the new directory structure and returns the full path of the 
        /// file that was written.
        /// </summary>
        internal string WriteContent(string originalFilePath, Stream stream)
        {
            var newFilePath = GetNewSourcePath(originalFilePath);
            using var fileStream = new FileStream(newFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            stream.CopyTo(fileStream);
            return newFilePath;
        }
    }

    internal static Regex OptionsRegex { get; } = GetOptionRegex();

    public CompilerLogReader Reader { get; }
    public bool IncludeAnalyzers { get; }
    internal PathNormalizationUtil PathNormalizationUtil => Reader.PathNormalizationUtil;

    public ExportUtil(CompilerLogReader reader, bool includeAnalyzers = true)
    {
        Reader = reader;
        IncludeAnalyzers = includeAnalyzers;
    }

    public void ExportAll(string destinationDir, IEnumerable<string> sdkDirectories, Func<CompilerCall, bool>? predicate = null)
    {
        predicate ??= static _ => true;
        for (int  i = 0; i < Reader.Count ; i++)
        {
            var compilerCall = Reader.ReadCompilerCall(i);
            if (predicate(compilerCall))
            {
                var dir = Path.Combine(destinationDir, i.ToString());
                Directory.CreateDirectory(dir);
                Export(compilerCall, dir, sdkDirectories);
            }
        }
    }

    public void Export(CompilerCall compilerCall, string destinationDir, IEnumerable<string> sdkDirectories)
    {
        if (!Path.IsPathRooted(destinationDir))
        {
            throw new ArgumentException("Need a full path", nameof(destinationDir));
        }

        var builder = new ContentBuilder(destinationDir, compilerCall.ProjectFilePath);

        var commandLineList = new List<string>();
        var data = Reader.ReadRawCompilationData(compilerCall);
        Directory.CreateDirectory(destinationDir);
        WriteGeneratedFiles();
        WriteEmbedLines();
        WriteContent();
        WriteAnalyzers();
        WriteReferences();
        WriteResources();

        var rspFilePath = Path.Combine(destinationDir, "build.rsp");
        File.WriteAllLines(rspFilePath, ProcessRsp());

        // Need to create a few directories so that the builds will actually function
        foreach (var sdkDir in sdkDirectories)
        {
            var cmdFileName = $"build-{Path.GetFileName(sdkDir)}";
            WriteBuildCmd(sdkDir, cmdFileName);
        }

        string? bestSdkDir = sdkDirectories.OrderByDescending(x => x, PathUtil.Comparer).FirstOrDefault();
        if (bestSdkDir is not null)
        {
            WriteBuildCmd(bestSdkDir, "build");
        }

        void WriteBuildCmd(string sdkDir, string cmdFileName)
        {
            var lines = new List<string>();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                cmdFileName += ".cmd";
            }
            else
            {
                cmdFileName += ".sh";
                lines.Add(@"#!/bin/sh");
            }

            var execPath = Path.Combine(sdkDir, "Roslyn", "bincore");
            execPath = compilerCall.IsCSharp
                ? Path.Combine(execPath, "csc.dll")
                : Path.Combine(execPath, "vbc.dll");

            lines.Add($@"dotnet exec ""{execPath}"" @build.rsp");
            var cmdFilePath = Path.Combine(destinationDir, cmdFileName);
            File.WriteAllLines(cmdFilePath, lines);

#if NET

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var info = new FileInfo(cmdFilePath);
                info.UnixFileMode |= UnixFileMode.UserExecute;
            }

#endif
        }

        List<string> ProcessRsp()
        {
            var lines = new List<string>();

            // compiler options aren't case sensitive
            var comparison = StringComparison.OrdinalIgnoreCase;

            foreach (var line in compilerCall.GetArguments())
            {
                // The only non-options are source files and those are rewritten by other 
                // methods and added to commandLineList
                if (!IsOption(line.AsSpan()))
                {
                    continue;
                }

                var span = line.AsSpan().Slice(1);

                // These options are all rewritten below
                if (span.StartsWith("reference", comparison) ||
                    span.StartsWith("analyzer", comparison) ||
                    span.StartsWith("additionalfile", comparison) ||
                    span.StartsWith("analyzerconfig", comparison) ||
                    span.StartsWith("embed", comparison) ||
                    span.StartsWith("resource", comparison) ||
                    span.StartsWith("linkresource", comparison) ||
                    span.StartsWith("ruleset", comparison) ||
                    span.StartsWith("keyfile", comparison))
                {
                    continue;
                }

                // Map all of the output items to the build output directory
                if (span.StartsWith("out", comparison) ||
                    span.StartsWith("refout", comparison) ||
                    span.StartsWith("doc", comparison) ||
                    span.StartsWith("generatedfilesout", comparison) ||
                    span.StartsWith("errorlog", comparison))
                {
                    var index = span.IndexOf(':');
                    var argName = span.Slice(0, index).ToString();
                    var originalValue = PathNormalizationUtil.NormalizePath(span.Slice(index + 1).ToString());
                    var newValue = builder.BuildOutput.GetNewFilePath(originalValue);
                    commandLineList.Add($@"/{argName}:{FormatPathArgument(newValue)}");

                    if (span.StartsWith("generatedfilesout", comparison))
                    {
                        Directory.CreateDirectory(newValue);
                    }

                    continue;
                }

                lines.Add(line);
            }

            lines.AddRange(commandLineList);
            return lines;
        }

        void WriteReferences()
        {
            var refDir = Path.Combine(destinationDir, "ref");
            Directory.CreateDirectory(refDir);

            foreach (var tuple in data.References)
            {
                var mvid = tuple.Mvid;
                var filePath = Path.Combine(refDir, Reader.GetMetadataReferenceFileName(mvid));

                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                Reader.CopyAssemblyBytes(mvid, fileStream);

                if (tuple.Aliases.Length > 0)
                {
                    foreach (var alias in tuple.Aliases)
                    {
                        var arg = $@"/reference:{alias}=""{PathUtil.RemovePathStart(filePath, destinationDir)}""";
                        commandLineList.Add(arg);
                    }
                }
                else
                {
                    var arg = $@"/reference:""{PathUtil.RemovePathStart(filePath, destinationDir)}""";
                    commandLineList.Add(arg);
                }
            }
        }

        void WriteAnalyzers()
        {
            if (!IncludeAnalyzers)
            {
                return;
            }

            foreach (var analyzer in data.Analyzers)
            {
                using var analyzerStream = Reader.GetAssemblyStream(analyzer.Mvid);
                var filePath = builder.AnalyzerDirectory.WriteContent(analyzer.FilePath, analyzerStream);
                var arg = $@"/analyzer:""{PathUtil.RemovePathStart(filePath, builder.DestinationDirectory)}""";
                commandLineList.Add(arg);
            }
        }

        void WriteContent()
        {
            foreach (var tuple in data.Contents)
            {
                var prefix = tuple.Kind switch
                {
                    RawContentKind.SourceText => "",
                    RawContentKind.GeneratedText => null,
                    RawContentKind.AdditionalText => "/additionalfile:",
                    RawContentKind.AnalyzerConfig => "/analyzerconfig:",
                    RawContentKind.Embed => "/embed:",
                    RawContentKind.EmbedLine => null,
                    RawContentKind.SourceLink => "/sourcelink:",
                    RawContentKind.RuleSet => "/ruleset:",
                    RawContentKind.AppConfig => "/appconfig:",
                    RawContentKind.Win32Manifest => "/win32manifest:",
                    RawContentKind.Win32Resource => "/win32res:",
                    RawContentKind.Win32Icon => "/win32icon:",
                    RawContentKind.CryptoKeyFile => "/keyfile:",
                    _ => throw new Exception(),
                };

                if (prefix is null)
                {
                    continue;
                }

                string? filePath = null;
                if (tuple.Kind == RawContentKind.AnalyzerConfig)
                {
                    var sourceText = Reader.GetSourceText(tuple.ContentHash, data.ChecksumAlgorithm);
                    if (RoslynUtil.IsGlobalEditorConfigWithSection(sourceText))
                    {
                        var content = RoslynUtil.RewriteGlobalEditorConfigSections(sourceText, x => builder.GetNewSourcePath(x));
                        filePath = builder.WriteContent(tuple.FilePath, content);
                    }
                }

                if (filePath is null)
                {
                    using var contentStream = Reader.GetContentStream(tuple.ContentHash);
                    filePath = builder.WriteContent(tuple.FilePath, contentStream);
                }

                commandLineList.Add($@"{prefix}{FormatPathArgument(filePath)}");
            }
        }

        void WriteGeneratedFiles()
        {
            foreach (var tuple in data.Contents.Where(x => x.Kind == RawContentKind.GeneratedText))
            {
                using var contentStream = Reader.GetContentStream(tuple.ContentHash);
                var filePath = builder.GeneratedCodeDirectory.WriteContent(tuple.FilePath, contentStream);

                if (!IncludeAnalyzers)
                {
                    commandLineList.Add(FormatPathArgument(filePath));
                }
            }
        }

        void WriteEmbedLines()
        {
            foreach (var tuple in data.Contents.Where(x => x.Kind == RawContentKind.EmbedLine))
            {
                using var contentStream = Reader.GetContentStream(tuple.ContentHash);
                var newPath = builder.WriteContent(tuple.FilePath, contentStream);
            }
        }

        void WriteResources()
        {
            foreach (var resourceData in data.Resources)
            {
                // The name of file resources isn't that important. It doesn't contribute to the compilation 
                // output. What is important is all the other parts of the string. Just need to create a
                // unique name inside the embedded resource folder
                var d = Reader.ReadResourceDescription(resourceData);
                var originalFileName = d.GetFileName();
                var resourceName = d.GetResourceName();
                var filePath = Path.Combine(builder.EmbeddedResourceDirectory, resourceData.ContentHash, originalFileName ?? resourceName);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllBytes(filePath, Reader.GetContentBytes(resourceData.ContentHash));

                var accessibility = d.IsPublic() ? "public" : "private";
                var kind = originalFileName is null ? "/resource:" : "/linkresource";
                var arg = $"{kind}{PathUtil.RemovePathStart(filePath, builder.DestinationDirectory)},{resourceName},{accessibility}";
                commandLineList.Add(arg);
            }
        }

        string FormatPathArgument(string filePath)
        {
            filePath = PathUtil.RemovePathStart(filePath, destinationDir);
            return MaybeQuoteArgument(filePath);
        }
    }

    public static void ExportRsp(CompilerCall compilerCall, TextWriter writer, bool singleLine = false) =>
        ExportRsp(compilerCall.GetArguments(), writer, singleLine);

    public static void ExportRsp(IEnumerable<string> arguments, TextWriter writer, bool singleLine = false)
    {
        bool isFirst = true;
        foreach (var line in arguments)
        {
            var str = MaybeQuoteArgument(line);
            if (singleLine)
            {
                if (!isFirst)
                {
                    writer.Write(' ');
                }
                writer.Write(str);
            }
            else
            {
                writer.WriteLine(str);
            }

            isFirst = false;
        }
    }

    private static string MaybeQuoteArgument(string arg)
    {
        if (IsOption(arg.AsSpan()))
        {
            return arg;
        }

        if (arg.Contains(' ') || arg.Contains('=') || arg.Contains(','))
        {
            var str = $@"""{arg}""";
            return str;
        }

        return arg;
    }

    private static bool IsOption(ReadOnlySpan<char> str) => OptionsRegex.IsMatch(str);

    private const string OptionRegexContent = @"^/[a-z0-9]+:";

#if NET
    [GeneratedRegex(OptionRegexContent, RegexOptions.IgnoreCase)]
    private static partial Regex GetOptionRegex();
#else
    private static Regex GetOptionRegex() => new Regex(OptionRegexContent, RegexOptions.IgnoreCase);
#endif
}
