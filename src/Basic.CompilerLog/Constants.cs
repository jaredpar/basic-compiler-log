using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Basic.CompilerLog.Util;

internal static partial class Constants
{
    internal const int ExitFailure = 1;
    internal const int ExitSuccess = 0;

    internal static string CurrentDirectory { get; set; } = Environment.CurrentDirectory;
    internal static string LocalAppDataDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Basic.CompilerLog");

    internal static TextWriter Out { get; set; } = Console.Out;

    internal static Action<ICompilerCallReader> OnCompilerCallReader = _ => { };
}
