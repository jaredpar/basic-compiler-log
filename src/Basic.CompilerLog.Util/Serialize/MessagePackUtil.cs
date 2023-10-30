
using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MessagePack.Formatters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basic.CompilerLog.Util.Serialize;

internal static class MessagePackUtil
{
    private static readonly Lazy<PropertyInfo?> s_debugPlusPropertyInfo = new(() => GetDebugPlusPropertyInfo());

    private static PropertyInfo? GetDebugPlusPropertyInfo()
    {
        try
        {
            var type = typeof(CompilationOptions);
            return type.GetProperty("DebugPlusMode", BindingFlags.NonPublic | BindingFlags.Instance);
        }
        catch
        {
            return null;
        }
    }

    private static void SetDebugPlusMode(CompilationOptions options, bool debugPlusMode)
    {
        if (s_debugPlusPropertyInfo.Value is { } info)
        {
            info.SetValue(options, debugPlusMode);
        }
    }

    internal static EmitOptions CreateEmitOptions(EmitOptionsPack pack)
    {
        return new EmitOptions
        (
            metadataOnly: pack.EmitMetadataOnly,
            includePrivateMembers: pack.IncludePrivateMembers,
            debugInformationFormat: pack.DebugInformationFormat,
            pdbFilePath: pack.PdbFilePath,
            outputNameOverride: pack.OutputNameOverride,
            baseAddress: pack.BaseAddress,
            highEntropyVirtualAddressSpace: pack.HighEntropyVirtualAddressSpace,
            fileAlignment: pack.FileAlignment,
            subsystemVersion: SubsystemVersion.Create(pack.SubsystemVersion.Item1, pack.SubsystemVersion.Item2),
            runtimeMetadataVersion: pack.RuntimeMetadataVersion,
            instrumentationKinds: pack.InstrumentationKinds,
            pdbChecksumAlgorithm: new HashAlgorithmName(pack.PdbChecksumAlgorithm),
            defaultSourceFileEncoding: pack.DefaultSourceFileEncoding is int cp ? Encoding.GetEncoding(cp) : null
        );
    }

    internal static EmitOptionsPack CreateEmitOptionsPack(EmitOptions options)
    {
        return new EmitOptionsPack()
        {
            EmitMetadataOnly = options.EmitMetadataOnly,
            TolerateErrors = options.TolerateErrors,
            IncludePrivateMembers = options.IncludePrivateMembers,
            InstrumentationKinds = options.InstrumentationKinds,
            SubsystemVersion = (options.SubsystemVersion.Major, options.SubsystemVersion.Minor),
            FileAlignment = options.FileAlignment,
            HighEntropyVirtualAddressSpace = options.HighEntropyVirtualAddressSpace,
            BaseAddress = options.BaseAddress,
            DebugInformationFormat = options.DebugInformationFormat,
            OutputNameOverride = options.OutputNameOverride,
            PdbFilePath = options.PdbFilePath,
            PdbChecksumAlgorithm = options.PdbChecksumAlgorithm.Name,
            RuntimeMetadataVersion = options.RuntimeMetadataVersion,
            DefaultSourceFileEncoding = options.DefaultSourceFileEncoding?.CodePage,
            FallbackSourceFileEncoding = null,
        };
    }

    internal static CompilationOptionsPack CreateCompilationOptionsPack(CompilationOptions options)
    {
        var debugPlusMode = false;
        if (s_debugPlusPropertyInfo.Value is { } info)
        {
            debugPlusMode = (bool)info.GetValue(options)!;
        }

        return new CompilationOptionsPack()
        {
            OutputKind = options.OutputKind,
            ModuleName = options.ModuleName,
            ScriptClassName = options.ScriptClassName,
            MainTypeName = options.MainTypeName,
            CryptoPublicKey = options.CryptoPublicKey,
            CryptoKeyFile = options.CryptoKeyFile,
            CryptoKeyContainer = options.CryptoKeyContainer,
            DelaySign = options.DelaySign,
            PublicSign = options.PublicSign,
            CheckOverflow = options.CheckOverflow,
            Platform = options.Platform,
            OptimizationLevel = options.OptimizationLevel,
            GeneralDiagnosticOption = options.GeneralDiagnosticOption,
            WarningLevel = options.WarningLevel,
            ConcurrentBuild = options.ConcurrentBuild,
            Deterministic = options.Deterministic,
            MetadataImportOptions =  options.MetadataImportOptions,
            SpecificDiagnosticOptions = options.SpecificDiagnosticOptions,
            ReportSuppressedDiagnostics = options.ReportSuppressedDiagnostics,
            DebugPlusMode = debugPlusMode,
        };
    }

    internal static ParseOptionsPack CreateParseOptionsPack(ParseOptions options)
    {
        return new ParseOptionsPack()
        {
            Kind = options.Kind,
            SpecifiedKind = options.SpecifiedKind,
            DocumentationMode = options.DocumentationMode,
        };
    }

    internal static CSharpParseOptions CreateCSharpParseOptions(ParseOptionsPack optionsPack, CSharpParseOptionsPack csharpPack)
    {
        return new CSharpParseOptions
        (
            languageVersion: csharpPack.SpecifiedLanguageVersion,
            documentationMode: optionsPack.DocumentationMode,
            kind: optionsPack.Kind,
            preprocessorSymbols: csharpPack.PreprocessorSymbols
        ).WithFeatures(csharpPack.Features);
    }

    internal static (ParseOptionsPack, CSharpParseOptionsPack) CreateCSharpParseOptionsPack(CSharpParseOptions options)
    {
        var pack = new CSharpParseOptionsPack()
        {
            SpecifiedLanguageVersion = options.SpecifiedLanguageVersion,
            PreprocessorSymbols = options.PreprocessorSymbolNames,
            Features = options.Features,
        };

        return (CreateParseOptionsPack(options), pack);
    }

    internal static CSharpCompilationOptions CreateCSharpCompilationOptions(CompilationOptionsPack optionsPack, CSharpCompilationOptionsPack csharpPack)
    {
        var options = new CSharpCompilationOptions
        (
            outputKind: optionsPack.OutputKind,
            moduleName: optionsPack.ModuleName,
            mainTypeName: optionsPack.MainTypeName,
            scriptClassName: WellKnownMemberNames.DefaultScriptClassName,
            usings: csharpPack.Usings,
            optimizationLevel: optionsPack.OptimizationLevel,
            checkOverflow: optionsPack.CheckOverflow,
            nullableContextOptions: csharpPack.NullableContextOptions,
            allowUnsafe: csharpPack.AllowUnsafe,
            deterministic: optionsPack.Deterministic,
            concurrentBuild: optionsPack.Deterministic,
            cryptoKeyContainer: optionsPack.CryptoKeyContainer,
            cryptoKeyFile: optionsPack.CryptoKeyFile,
            delaySign: optionsPack.DelaySign,
            platform: optionsPack.Platform,
            generalDiagnosticOption: optionsPack.GeneralDiagnosticOption,
            warningLevel: optionsPack.WarningLevel,
            specificDiagnosticOptions: optionsPack.SpecificDiagnosticOptions,
            reportSuppressedDiagnostics: optionsPack.ReportSuppressedDiagnostics,
            publicSign: optionsPack.PublicSign
        );
        SetDebugPlusMode(options, optionsPack.DebugPlusMode);
        return options;
    }

    internal static (CompilationOptionsPack, CSharpCompilationOptionsPack) CreateCSharpCompilationOptionsPack(CSharpCompilationOptions options)
    {
        var pack = new CSharpCompilationOptionsPack()
        {
            AllowUnsafe = options.AllowUnsafe,
            Usings = options.Usings,
            NullableContextOptions = options.NullableContextOptions,
        };

        return (CreateCompilationOptionsPack(options), pack);
    }

    internal static VisualBasicParseOptions CreateVisualBasicParseOptions(ParseOptionsPack optionsPack, VisualBasicParseOptionsPack basicPack)
    {
        return new VisualBasicParseOptions
        (
            languageVersion: basicPack.LanguageVersion,
            documentationMode: optionsPack.DocumentationMode,
            kind: optionsPack.Kind,
            preprocessorSymbols: basicPack.PreprocessorSymbols
        ).WithFeatures(basicPack.Features);
    }

    internal static (ParseOptionsPack, VisualBasicParseOptionsPack) CreateVisualBasicParseOptionsPack(VisualBasicParseOptions options)
    {
        var pack = new VisualBasicParseOptionsPack()
        {
            LanguageVersion = options.LanguageVersion,
            SpecifiedLanguageVersion = options.SpecifiedLanguageVersion,
            PreprocessorSymbols = options.PreprocessorSymbols,
            Features = options.Features,
        };

        return (CreateParseOptionsPack(options), pack);
    }

    internal static VisualBasicCompilationOptions CreateVisualBasicCompilationOptions(CompilationOptionsPack optionsPack, VisualBasicCompilationOptionsPack basicPack, ParseOptionsPack parsePack, VisualBasicParseOptionsPack parseBasicPack)
    {
        var options = new VisualBasicCompilationOptions
        (
            outputKind: optionsPack.OutputKind,
            reportSuppressedDiagnostics: optionsPack.ReportSuppressedDiagnostics,
            moduleName: optionsPack.ModuleName,
            mainTypeName: optionsPack.MainTypeName,
            scriptClassName: WellKnownMemberNames.DefaultScriptClassName,
            globalImports: basicPack.GlobalImports.Select(x => GlobalImport.Parse(x)).ToImmutableArray(),
            rootNamespace: basicPack.RootNamespace,
            optionStrict: basicPack.OptionStrict,
            optionInfer: basicPack.OptionInfer,
            optionExplicit: basicPack.OptionExplicit,
            optionCompareText: basicPack.OptionCompareText,
            embedVbCoreRuntime: basicPack.EmbedVbCoreRuntime,
            checkOverflow: optionsPack.CheckOverflow,
            deterministic: optionsPack.Deterministic,
            concurrentBuild: optionsPack.Deterministic,
            cryptoKeyContainer: optionsPack.CryptoKeyContainer,
            cryptoKeyFile: optionsPack.CryptoKeyFile,
            delaySign: optionsPack.DelaySign,
            platform: optionsPack.Platform,
            generalDiagnosticOption: optionsPack.GeneralDiagnosticOption,
            specificDiagnosticOptions: optionsPack.SpecificDiagnosticOptions,
            optimizationLevel: optionsPack.OptimizationLevel,
            publicSign: optionsPack.PublicSign,
            parseOptions: CreateVisualBasicParseOptions(parsePack, parseBasicPack)
        );
        SetDebugPlusMode(options, optionsPack.DebugPlusMode);
        return options;
    }

    internal static (CompilationOptionsPack, VisualBasicCompilationOptionsPack, ParseOptionsPack, VisualBasicParseOptionsPack) CreateVisualBasicCompilationOptionsPack(VisualBasicCompilationOptions options)
    {
        var tuple = CreateVisualBasicParseOptionsPack(options.ParseOptions);
        var pack = new VisualBasicCompilationOptionsPack()
        {
            GlobalImports = options.GlobalImports.Select(x => x.Name).ToArray(),
            RootNamespace = options.RootNamespace,
            OptionStrict = options.OptionStrict,
            OptionInfer = options.OptionInfer,
            OptionExplicit = options.OptionExplicit,
            OptionCompareText = options.OptionCompareText,
            EmbedVbCoreRuntime = options.EmbedVbCoreRuntime,
        };

        return (CreateCompilationOptionsPack(options), pack, tuple.Item1, tuple.Item2);
    }
}
