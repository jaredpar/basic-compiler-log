namespace Basic.CompilerLog.Util;

public interface ICompilerCallReader
{
    public List<CompilerCall> ReadAllCompilerCalls(Func<CompilerCall, bool>? predicate = null);
    public List<CompilationData> ReadAllCompilationData(Func<CompilerCall, bool>? predicate = null);
}