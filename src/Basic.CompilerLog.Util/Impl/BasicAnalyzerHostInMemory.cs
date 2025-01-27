﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

#if NET
using System.Runtime.Loader;
#endif

namespace Basic.CompilerLog.Util.Impl;

/// <summary>
/// Loads analyzers in memory
/// </summary>
internal sealed class BasicAnalyzerHostInMemory : BasicAnalyzerHost
{
    internal InMemoryLoader Loader { get; }
    protected override ImmutableArray<AnalyzerReference> AnalyzerReferencesCore => Loader.AnalyzerReferences;

    internal BasicAnalyzerHostInMemory(IBasicAnalyzerHostDataProvider provider, List<AnalyzerData> analyzers)
        :base(BasicAnalyzerKind.InMemory)
    {
        var name = $"{nameof(BasicAnalyzerHostInMemory)} - {Guid.NewGuid().ToString("N")}";
        Loader = new InMemoryLoader(name, provider, analyzers, AddDiagnostic);
    }

    protected override void DisposeCore()
    {
        Loader.Dispose();
    }
}

#if NET

internal sealed class InMemoryLoader : AssemblyLoadContext
{
    private readonly Dictionary<string, byte[]> _map = new();
    internal ImmutableArray<AnalyzerReference> AnalyzerReferences { get; }
    internal AssemblyLoadContext CompilerLoadContext { get; }

    internal InMemoryLoader(string name, IBasicAnalyzerHostDataProvider provider, List<AnalyzerData> analyzers, Action<Diagnostic> onDiagnostic)
        :base(name, isCollectible: true)
    {
        CompilerLoadContext = provider.LogReaderState.CompilerLoadContext;
        var builder = ImmutableArray.CreateBuilder<AnalyzerReference>(analyzers.Count);
        foreach (var analyzer in analyzers)
        {
            var simpleName = Path.GetFileNameWithoutExtension(analyzer.FileName);
            _map[simpleName] = provider.GetAssemblyBytes(analyzer.AssemblyData);
            builder.Add(new BasicAnalyzerReference(new AssemblyName(simpleName), this, onDiagnostic));
        }

        AnalyzerReferences = builder.MoveToImmutable();
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        try
        {
            if (CompilerLoadContext.LoadFromAssemblyName(assemblyName) is { } compilerAssembly)
            {
                return compilerAssembly;
            }
        }
        catch
        {
            // Expected to happen when the assembly cannot be resolved in the compiler / host
            // AssemblyLoadContext.
        }

        // Prefer registered dependencies in the same directory first.
        if (_map.TryGetValue(assemblyName.Name!, out var bytes))
        {
            return LoadFromStream(bytes.AsSimpleMemoryStream());
        }

        return null;
    }

    public void Dispose()
    {
        Unload();
    }
}    

#else

internal sealed class InMemoryLoader
{
    internal ImmutableArray<AnalyzerReference> AnalyzerReferences { get; }

    internal InMemoryLoader(string name, IBasicAnalyzerHostDataProvider provider, List<AnalyzerData> analyzers, Action<Diagnostic> onDiagnostic)
    {
        var builder = ImmutableArray.CreateBuilder<AnalyzerReference>(analyzers.Count);
        foreach (var analyzer in analyzers)
        {
            builder.Add(new BasicAnalyzerReference(new AssemblyName(analyzer.AssemblyIdentityData.AssemblyName), this, onDiagnostic));
        }

        AnalyzerReferences = builder.MoveToImmutable();
    }

    public Assembly LoadFromAssemblyName(AssemblyName assemblyName)
    {
        throw new PlatformNotSupportedException();
    }

    public void Dispose()
    {
    }

    /*
     * 
        rough sketch of how this could work
        private readonly Dictionary<string, (byte[], Assembly?)> _map = new();
        internal ImmutableArray<AnalyzerReference> AnalyzerReferences { get; }

        internal InMemoryLoader(string name, BasicAnalyzersOptions options, CompilerLogReader reader, List<RawAnalyzerData> analyzers)
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;    
            var builder = ImmutableArray.CreateBuilder<AnalyzerReference>(analyzers.Count);
            foreach (var analyzer in analyzers)
            {
                var simpleName = Path.GetFileNameWithoutExtension(analyzer.FileName);
                _map[simpleName] = (reader.GetAssemblyBytes(analyzer.Mvid), null);
                builder.Add(new BasicAnalyzerReference(new AssemblyName(simpleName), this));
            }

            AnalyzerReferences = builder.MoveToImmutable();
        }


        private readonly HashSet<string> _loadingSet = new();

        private Assembly? LoadCore(string assemblyName)
        {
            lock (_map)
            {
                if (!_map.TryGetValue(assemblyName, out var value))
                {
                    return null;
                }

                var (bytes, assembly) = value;
                if (assembly is null)
                {
                    assembly = Assembly.Load(bytes);
                    _map[assemblyName] = (bytes, assembly);
                }

                return assembly;
            }
        }

        private Assembly? OnAssemblyResolve(object sender, ResolveEventArgs e)
        {
            var name = new AssemblyName(e.Name);
            if (LoadCore(name.Name) is { } assembly)
            {
                return assembly;
            }

            lock (_loadingSet)
            {
                if (!_loadingSet.Add(e.Name))
                {
                    return null;
                }
            }

            try
            {
                return AppDomain.CurrentDomain.Load(name);
            }
            finally
            {
                lock (_loadingSet)
                {
                    _loadingSet.Remove(e.Name);
                }
            }
        }

        public Assembly LoadFromAssemblyName(AssemblyName assemblyName) =>
            LoadCore(assemblyName.Name) ?? throw new Exception($"Cannot find assembly with name {assemblyName.Name}");

        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;    
        }

        */
}

#endif

file sealed class BasicAnalyzerReference : AnalyzerReference
{
    public static readonly DiagnosticDescriptor CannotLoadTypes =
        new DiagnosticDescriptor(
            "BCLA0002",
            "Failed to load types from assembly",
            "Failed to load types from {0}: {1}",
            "BasicCompilerLog",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

    internal AssemblyName AssemblyName { get; }
    internal InMemoryLoader Loader { get; }
    internal Action<Diagnostic> OnDiagnostic { get; }
    public override object Id { get; } = Guid.NewGuid();
    public override string? FullPath => null;

    internal BasicAnalyzerReference(AssemblyName assemblyName, InMemoryLoader loader, Action<Diagnostic> onDiagnostic)
    {
        AssemblyName = assemblyName;
        Loader = loader;
        OnDiagnostic = onDiagnostic;
    }

    public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language)
    {
        var builder = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();
        GetAnalyzers(builder, language);
        return builder.ToImmutable();
    }

    public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages()
    {
        var builder = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();
        GetAnalyzers(builder, languageName: null);
        return builder.ToImmutable();
    }

    public override ImmutableArray<ISourceGenerator> GetGenerators(string language)
    {
        var builder = ImmutableArray.CreateBuilder<ISourceGenerator>();
        GetGenerators(builder, language);
        return builder.ToImmutable();
    }

    public override ImmutableArray<ISourceGenerator> GetGeneratorsForAllLanguages()
    {
        var builder = ImmutableArray.CreateBuilder<ISourceGenerator>();
        GetGenerators(builder, languageName: null);
        return builder.ToImmutable();
    }

    internal Type[] GetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (Exception ex)
        {
            var diagnostic = Diagnostic.Create(CannotLoadTypes, Location.None, assembly.FullName, ex.Message);
            OnDiagnostic(diagnostic);
            return Array.Empty<Type>();
        }
    }

    internal void GetAnalyzers(ImmutableArray<DiagnosticAnalyzer>.Builder builder, string? languageName)
    {
        var assembly = Loader.LoadFromAssemblyName(AssemblyName);
        foreach (var type in GetTypes(assembly))
        {
            if (type.GetCustomAttributes(inherit: false) is { Length: > 0 } attributes)
            {
                foreach (var attribute in attributes)
                {
                    if (attribute is DiagnosticAnalyzerAttribute d &&
                        IsLanguageMatch(d.Languages))
                    {
                        builder.Add((DiagnosticAnalyzer)CreateInstance(type));
                        break;
                    }
                }
            }
        }

        bool IsLanguageMatch(string[] languages) =>
            languageName is null || languages.Contains(languageName);
    }

    internal void GetGenerators(ImmutableArray<ISourceGenerator>.Builder builder, string? languageName)
    {
        var assembly = Loader.LoadFromAssemblyName(AssemblyName);
        foreach (var type in GetTypes(assembly))
        {
            if (type.GetCustomAttributes(inherit: false) is { Length: > 0 } attributes)
            {
                foreach (var attribute in attributes)
                {
                    if (attribute is GeneratorAttribute g &&
                        IsLanguageMatch(g.Languages))
                    {
                        var generator = CreateInstance(type);
                        if (generator is ISourceGenerator sg)
                        {
                            builder.Add(sg);
                        }
                        else
                        {
                            IIncrementalGenerator ig = (IIncrementalGenerator)generator;
                            builder.Add(ig.AsSourceGenerator());
                        }

                        break;
                    }
                }
            }
        }

        bool IsLanguageMatch(string[] languages) =>
            languageName is null || languages.Contains(languageName);
    }

    private static object CreateInstance(Type type)
    {
        var ctor = type.GetConstructors()
            .Where(c => c.GetParameters().Length == 0)
            .Single();
        return ctor.Invoke(null);
    }

    public override string ToString() => $"In Memory {AssemblyName}";
}

