using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("暴力摩托增强修改器兼容入口")]
[assembly: AssemblyProduct("暴力摩托增强修改器")]

internal static class LegacyEntry
{
    [STAThread]
    private static int Main(string[] args)
    {
        string target = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "暴力摩托增强修改器.exe");
        // Keep the original Windows argument tail, including empty and quoted arguments.
        // Only the executable token changes when an existing shortcut is used.
        string command = Environment.CommandLine;
        int end = 0;
        bool quoted = false;
        for (; end < command.Length; end++)
        {
            char c = command[end];
            if (c == '"') quoted = !quoted;
            else if (!quoted && (c == ' ' || c == '\t')) break;
        }
        var start = new ProcessStartInfo(target, command.Substring(end)) { UseShellExecute = false };
        try
        {
            using (Process mod = Process.Start(start))
            {
                mod.WaitForExit();
                return mod.ExitCode;
            }
        }
        catch (Win32Exception ex)
        {
            string message = "无法打开同目录的「暴力摩托增强修改器.exe」。请完整解压玩家包。\n" + ex.Message;
            if (args.Length == 0) MessageBox.Show(message, "暴力摩托增强修改器");
            else Console.Error.WriteLine(message);
            return 1;
        }
    }
}
