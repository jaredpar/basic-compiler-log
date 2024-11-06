using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Basic.CompilerLog.Util;

public interface ICompilerCallReader : IDisposable
{
    public BasicAnalyzerKind BasicAnalyzerKind { get; }
    public LogReaderState LogReaderState { get; }
    public bool OwnsLogReaderState { get; }
    public CompilerCall ReadCompilerCall(int index);
    public List<CompilerCall> ReadAllCompilerCalls(Func<CompilerCall, bool>? predicate = null);
    public List<CompilationData> ReadAllCompilationData(Func<CompilerCall, bool>? predicate = null);
    public CompilationData ReadCompilationData(CompilerCall compilerCall);
    public CompilerCallData ReadCompilerCallData(CompilerCall compilerCall);
    public SourceText ReadSourceText(SourceTextData sourceTextData);

    /// <summary>
    /// Read all of the <see cref="SourceTextData"/> for documents passed to the compilation
    /// </summary>
    public List<SourceTextData> ReadAllSourceTextData(CompilerCall compilerCall);

    /// <summary>
    /// Read all of the <see cref="AssemblyData"/> for references passed to the compilation
    /// </summary>
    public List<ReferenceData> ReadAllReferenceData(CompilerCall compilerCall);

    /// <summary>
    /// Read all of the <see cref="AssemblyData"/> for analyzers passed to the compilation
    /// </summary>
    public List<AnalyzerData> ReadAllAnalyzerData(CompilerCall compilerCall);

    /// <summary>
    /// Read all of the compilers used in this build.
    /// </summary>
    public List<CompilerAssemblyData> ReadAllCompilerAssemblies();

    public BasicAnalyzerHost CreateBasicAnalyzerHost(CompilerCall compilerCall);

    public bool TryGetCompilerCallIndex(Guid mvid, out int compilerCallIndex);

    /// <summary>
    /// Copy the bytes of the <paramref name="referenceData"/> to the provided <paramref name="stream"/>
    /// </summary>
    public void CopyAssemblyBytes(AssemblyData referenceData, Stream stream);

    public MetadataReference ReadMetadataReference(ReferenceData referenceData);
}