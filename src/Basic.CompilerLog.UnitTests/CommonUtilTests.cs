
using Basic.CompilerLog.Util;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
#if NET
using System.Runtime.Loader;
#endif

namespace Basic.CompilerLog.UnitTests;

public sealed class CommonUtilTests
{
#if NET
    [Fact]
    public void GetAssemblyLoadContext()
    {
        var alc = new AssemblyLoadContext("Custom", isCollectible: true);
        Assert.Same(alc, CommonUtil.GetAssemblyLoadContext(alc));
        alc.Unload();
    }
#endif

    [Fact]
    public void Defines()
    {
#if NET
        Assert.True(TestBase.IsNetCore);
        Assert.False(TestBase.IsNetFramework);
#else
        Assert.False(TestBase.IsNetCore);
        Assert.True(TestBase.IsNetFramework);
#endif
    }
}