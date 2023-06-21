﻿using Basic.CompilerLog.Util.Impl;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static Basic.CompilerLog.Util.CommonUtil;

namespace Basic.CompilerLog.Util;

internal sealed class CompilerLogBuilder : IDisposable
{
    // GUIDs specified in https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#document-table-0x30
    public static readonly Guid HashAlgorithmSha1 = unchecked(new Guid((int)0xff1816ec, (short)0xaa5e, 0x4d10, 0x87, 0xf7, 0x6f, 0x49, 0x63, 0x83, 0x34, 0x60));
    public static readonly Guid HashAlgorithmSha256 = unchecked(new Guid((int)0x8829d00f, 0x11b8, 0x4213, 0x87, 0x8b, 0x77, 0x0e, 0x85, 0x97, 0xac, 0x16));

    // https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#embedded-source-c-and-vb-compilers
    public static readonly Guid EmbeddedSourceGuid = new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

    private readonly Dictionary<Guid, (string FileName, AssemblyName AssemblyName)> _mvidToRefInfoMap = new();
    private readonly Dictionary<string, Guid> _assemblyPathToMvidMap = new(PathUtil.Comparer);
    private readonly HashSet<string> _sourceHashMap = new(PathUtil.Comparer);

    private int _compilationCount;
    private bool _closed;

    internal List<string> Diagnostics { get; }
    internal ZipArchive ZipArchive { get; set; }

    internal bool IsOpen => !_closed;
    internal bool IsClosed => _closed;

    internal CompilerLogBuilder(Stream stream, List<string> diagnostics)
    {
        ZipArchive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        Diagnostics = diagnostics;
    }

    internal bool Add(CompilerCall compilerCall)
    {
        var memoryStream = new MemoryStream();
        using var compilationWriter = Polyfill.NewStreamWriter(memoryStream, ContentEncoding, leaveOpen: true);
        compilationWriter.WriteLine(compilerCall.ProjectFilePath);
        compilationWriter.WriteLine(compilerCall.IsCSharp ? "C#" : "VB");
        compilationWriter.WriteLine(compilerCall.TargetFramework);
        compilationWriter.WriteLine(compilerCall.Kind);

        var arguments = compilerCall.Arguments;
        compilationWriter.WriteLine(arguments.Length);
        foreach (var arg in arguments)
        {
            compilationWriter.WriteLine(arg);
        }

        var baseDirectory = Path.GetDirectoryName(compilerCall.ProjectFilePath);
        CommandLineArguments commandLineArguments = compilerCall.IsCSharp
            ? CSharpCommandLineParser.Default.Parse(arguments, baseDirectory, sdkDirectory: null, additionalReferenceDirectories: null)
            : VisualBasicCommandLineParser.Default.Parse(arguments, baseDirectory, sdkDirectory: null, additionalReferenceDirectories: null);

        try
        {
            AddReferences(compilationWriter, commandLineArguments);
            AddAnalyzers(compilationWriter, commandLineArguments);
            AddAnalyzerConfigs(compilationWriter, commandLineArguments);
            AddGeneratedFiles(compilationWriter, commandLineArguments, compilerCall);
            AddSources(compilationWriter, commandLineArguments);
            AddAdditionalTexts(compilationWriter, commandLineArguments);
            AddResources(compilationWriter, commandLineArguments);
            AddedEmbeds(compilationWriter, commandLineArguments);
            AddContentIf("link", commandLineArguments.SourceLink);
            AddContentIf("ruleset", commandLineArguments.RuleSetPath);
            AddContentIf("appconfig", commandLineArguments.AppConfigPath);
            AddContentIf("win32resource", commandLineArguments.Win32ResourceFile);
            AddContentIf("win32icon", commandLineArguments.Win32Icon);
            AddContentIf("win32manifest", commandLineArguments.Win32Manifest);
            AddContentIf("cryptokeyfile", commandLineArguments.CompilationOptions.CryptoKeyFile);

            compilationWriter.Flush();

            CompressionLevel level;
#if NETCOREAPP
            level = CompressionLevel.SmallestSize;
#else
            level = CompressionLevel.Optimal;
#endif
            var entry = ZipArchive.CreateEntry(GetCompilerEntryName(_compilationCount), level);
            using var entryStream = entry.Open();
            memoryStream.Position = 0;
            memoryStream.CopyTo(entryStream);
            entryStream.Close();

            _compilationCount++;
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Add($"Error adding {compilerCall.ProjectFilePath}: {ex.Message}");
            return false;
        }

        void AddContentIf(string key, string? filePath)
        {
            if (TryResolve(filePath) is { } resolvedFilePath)
            {
                AddContentCore(compilationWriter, key, resolvedFilePath);
            }
        }

        string? TryResolve(string? filePath)
        {
            if (filePath is null)
            {
                return null;
            }

            if (Path.IsPathRooted(filePath))
            {
                return filePath;
            }

            var resolved = Path.Combine(compilerCall.ProjectDirectory, filePath);
            if (File.Exists(resolved))
            {
                return resolved;
            }

            return null;
        }
    }

    private void EnsureOpen()
    {
        if (IsClosed)
            throw new InvalidOperationException();
    }

    public void Close()
    {
        try
        {
            EnsureOpen();
            WriteMetadata();
            WriteAssemblyInfo();
            WriteSourceInfo();
            ZipArchive.Dispose();
            ZipArchive = null!;
        }
        finally
        {
            _closed = true;
        }

        void WriteMetadata()
        {
            var entry = ZipArchive.CreateEntry(MetadataFileName, CompressionLevel.Optimal);
            using var writer = Polyfill.NewStreamWriter(entry.Open(), ContentEncoding, leaveOpen: false);
            writer.WriteLine($"count:{_compilationCount}");
        }

        void WriteAssemblyInfo()
        {
            var entry = ZipArchive.CreateEntry(AssemblyInfoFileName, CompressionLevel.Optimal);
            using var writer = Polyfill.NewStreamWriter(entry.Open(), ContentEncoding, leaveOpen: false);
            foreach (var kvp in _mvidToRefInfoMap.OrderBy(x => x.Value.FileName).ThenBy(x => x.Key))
            {
                writer.WriteLine($"{kvp.Value.FileName}:{kvp.Key:N}:{kvp.Value.AssemblyName}");
            }
        }

        void WriteSourceInfo()
        {
            var entry = ZipArchive.CreateEntry(SourceInfoFileName, CompressionLevel.Optimal);
            using var writer = Polyfill.NewStreamWriter(entry.Open(), ContentEncoding, leaveOpen: false);
            foreach (var value in _sourceHashMap.OrderBy(x => x))
            {
                writer.WriteLine(value);
            }
        }
    }

    private void AddContentCore(StreamWriter compilationWriter, string key, string filePath)
    {
        var contentHash = AddContent(filePath);
        compilationWriter.WriteLine($"{key}:{contentHash}:{filePath}");
    }

    private void AddAnalyzerConfigs(StreamWriter compilationWriter, CommandLineArguments args)
    {
        foreach (var filePath in args.AnalyzerConfigPaths)
        {
            AddContentCore(compilationWriter, "config", filePath);
        }
    }

    private void AddSources(StreamWriter compilationWriter, CommandLineArguments args)
    {
        foreach (var commandLineFile in args.SourceFiles)
        {
            AddContentCore(compilationWriter, "source", commandLineFile.Path);
        }
    }

    /// <summary>
    /// Attempt to add all the generated files from generators. When successful the generators
    /// don't need to be run when re-hydrating the compilation.
    /// </summary>
    private void AddGeneratedFiles(StreamWriter compilationWriter, CommandLineArguments args, CompilerCall compilerCall)
    {
        // This only works when using portable and embedded pdb formats. A full PDB can't store
        // generated files
        if (args.EmitOptions.DebugInformationFormat is not (DebugInformationFormat.Embedded or DebugInformationFormat.PortablePdb))
        {
            return;
        }

        var assemblyFileName = GetAssemblyFileName(args);
        var assemblyFilePath = Path.Combine(args.OutputDirectory, assemblyFileName);
        if (!File.Exists(assemblyFilePath))
        {
            Diagnostics.Add($"Can't find assembly file for {compilerCall.GetDiagnosticName()}");
            return;
        }

        MetadataReaderProvider? pdbReaderProvider = null;
        try
        {
            using var reader = OpenFileForRead(assemblyFilePath);
            using var peReader = new PEReader(reader);
            if (!peReader.TryOpenAssociatedPortablePdb(assemblyFilePath, OpenFileForRead, out pdbReaderProvider, out var pdbPath))
            {
                Diagnostics.Add($"Can't find portable pdb file for {compilerCall.GetDiagnosticName()}");
                return;
            }

            var pdbReader = pdbReaderProvider!.GetMetadataReader();
            foreach (var documentHandle in pdbReader.Documents.Skip(args.SourceFiles.Length))
            {
                if (GetContentStream(documentHandle) is {} tuple)
                {
                    var contentHash = AddContent(tuple.Stream);
                    compilationWriter.WriteLine($"generated:{contentHash}:{tuple.Name}");
                }
            }

            (string Name, MemoryStream Stream)? GetContentStream(DocumentHandle documentHandle)
            {
                var document = pdbReader.GetDocument(documentHandle);
                var name = pdbReader.GetString(document.Name);
                foreach (var cdiHandle in pdbReader.GetCustomDebugInformation(documentHandle))
                {
                    var cdi = pdbReader.GetCustomDebugInformation(cdiHandle);
                    if (pdbReader.GetGuid(cdi.Kind) != EmbeddedSourceGuid)
                    {
                        continue;
                    }

                    var hashAlgorithmGuid = pdbReader.GetGuid(document.HashAlgorithm);
                    var hashAlgorithm =
                        hashAlgorithmGuid == HashAlgorithmSha1 ? SourceHashAlgorithm.Sha1
                        : hashAlgorithmGuid == HashAlgorithmSha256 ? SourceHashAlgorithm.Sha256
                        : SourceHashAlgorithm.None;
                    if (hashAlgorithm == SourceHashAlgorithm.None)
                    {
                        continue;
                    }

                    var bytes = pdbReader.GetBlobBytes(cdi.Value);
                    if (bytes is null)
                    {
                        continue;
                    }

                    int uncompressedSize = BitConverter.ToInt32(bytes, 0);
                    var stream = new MemoryStream(bytes, sizeof(int), bytes.Length - sizeof(int));

                    if (uncompressedSize != 0)
                    {
                        var decompressed = new MemoryStream(uncompressedSize);
                        using (var deflateStream = new DeflateStream(stream, CompressionMode.Decompress))
                        {
                            deflateStream.CopyTo(decompressed);
                        }

                        if (decompressed.Length != uncompressedSize)
                        {
                            Diagnostics.Add($"Error decompressing embedded source file {compilerCall.GetDiagnosticName()}");
                            continue;
                        }

                        stream = decompressed;
                    }

                    return (name, stream);
                }

                return null;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Add($"Error embedding generated files {compilerCall.GetDiagnosticName()}): {ex.Message}");
            return;
        }
        finally
        {
            pdbReaderProvider?.Dispose();
        }
    }

    /// <summary>
    /// Add a source file to the storage and return the stored name of the content in our 
    /// storage. This will be a checksum of the content itself
    /// </summary>
    private string AddContent(string filePath)
    {
        using var fileStream = OpenFileForRead(filePath);
        return AddContent(fileStream);
    }

    /// <summary>
    /// Add a source file to the storage and return the stored name of the content in our 
    /// storage. This will be a checksum of the content itself
    /// </summary>
    private string AddContent(Stream stream)
    {
        var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        var hashText = GetHashText();

        if (_sourceHashMap.Add(hashText))
        {
            var entry = ZipArchive.CreateEntry(GetContentEntryName(hashText), CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            stream.Position = 0;
            stream.CopyTo(entryStream);
        }

        return hashText;

        string GetHashText()
        {
            var builder = new StringBuilder();
            builder.Length = 0;
            foreach (var b in hash)
            {
                builder.Append($"{b:X2}");
            }

            return builder.ToString();
        }
    }

    private void AddReferences(StreamWriter compilationWriter, CommandLineArguments args)
    {
        foreach (var reference in args.MetadataReferences)
        {
            var mvid = AddAssembly(reference.Reference);
            compilationWriter.Write($"m:{mvid}:");
            compilationWriter.Write((int)reference.Properties.Kind);
            compilationWriter.Write(":");
            compilationWriter.Write(reference.Properties.EmbedInteropTypes ? '1' : '0');
            compilationWriter.Write(":");

            var any = false;
            foreach (var alias in reference.Properties.Aliases)
            {
                if (any)
                    compilationWriter.Write(",");
                compilationWriter.Write(alias);
                any = true;
            }
            compilationWriter.WriteLine();
        }
    }

    private void AddAdditionalTexts(StreamWriter compilationWriter, CommandLineArguments args)
    {
        foreach (var additionalText in args.AdditionalFiles)
        {
            AddContentCore(compilationWriter, "text", additionalText.Path);
        }
    }

    private void AddResources(StreamWriter compilationWriter, CommandLineArguments args)
    {
        foreach (var r in args.ManifestResources)
        {
            var name = r.GetResourceName();
            var fileName = r.GetFileName();
            var isPublic = r.IsPublic();
            var dataProvider = r.GetDataProvider();

            using var stream = dataProvider();
            var contentHash = AddContent(stream);
            compilationWriter.WriteLine($"r:{contentHash}:{name}:{isPublic}:{fileName}");
        }
    }

    private void AddedEmbeds(StreamWriter compilationWriter, CommandLineArguments args)
    {
        foreach (var e in args.EmbeddedFiles)
        {
            AddContentCore(compilationWriter, "embed", e.Path);
        }
    }

    private void AddAnalyzers(StreamWriter compilationWriter, CommandLineArguments args)
    {
        foreach (var analyzer in args.AnalyzerReferences)
        {
            var mvid = AddAssembly(analyzer.FilePath);
            compilationWriter.WriteLine($"a:{mvid}:{analyzer.FilePath}");
        }
    }

    /// <summary>
    /// Add the assembly into the storage and return tis MVID
    /// </summary>
    private Guid AddAssembly(string filePath)
    {
        if (_assemblyPathToMvidMap.TryGetValue(filePath, out var mvid))
        {
            Debug.Assert(_mvidToRefInfoMap.ContainsKey(mvid));
            return mvid;
        }

        using var file = OpenFileForRead(filePath);
        using var reader = new PEReader(file);
        var mdReader = reader.GetMetadataReader();
        GuidHandle handle = mdReader.GetModuleDefinition().Mvid;
        mvid = mdReader.GetGuid(handle);

        _assemblyPathToMvidMap[filePath] = mvid;

        // If the assembly was already loaded from a different path then no more
        // work is needed here
        if (_mvidToRefInfoMap.ContainsKey(mvid))
        {
            return mvid;
        }

        var entry = ZipArchive.CreateEntry(GetAssemblyEntryName(mvid), CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        file.Position = 0;
        file.CopyTo(entryStream);

        // There are some assemblies for which MetadataReader will return an AssemblyName which 
        // fails ToString calls which is why we use AssemblyName.GetAssemblyName here.
        //
        // Example: .nuget\packages\microsoft.visualstudio.interop\17.2.32505.113\lib\net472\Microsoft.VisualStudio.Interop.dll
        var assemblyName = AssemblyName.GetAssemblyName(filePath);
        _mvidToRefInfoMap[mvid] = (Path.GetFileName(filePath), assemblyName);
        return mvid;
    }

    public void Dispose()
    {
        if (IsOpen)
        {
            Close();
        }
    }

    private static FileStream OpenFileForRead(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new Exception($"Missing file, either build did not happen on this machine or the environment has changed: {filePath}");
        }

        return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
