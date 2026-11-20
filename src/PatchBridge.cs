// The original enhancement tool requires Framework's System.Web.Extensions.
// Keep that dependency in a small process; never load it into the WPF runtime.
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

internal static class PatchBridge
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 4) return 2;
        XElement reply = new XElement("reply", new XAttribute("ok", false));
        try
        {
            Launcher.GameDir = Path.GetFullPath(args[1]);
            Launcher.ToolPath = Launcher.FindTool();
            Launcher.LoadTool();
            if (args[0] == "inspect")
            {
                bool recovery;
                object options = Launcher.Inspect(out recovery);
                reply.Add(new XAttribute("recovery", recovery));
                if (options != null)
                    foreach (PropertyInfo p in Launcher.OptionsType.GetProperties())
                        reply.Add(new XElement("option", new XAttribute("name", p.Name), Convert.ToString(p.GetValue(options, null), CultureInfo.InvariantCulture)));
            }
            else if (args[0] == "apply" || args[0] == "restore")
            {
                if (Launcher.GameRunning()) throw new InvalidOperationException("请先退出游戏，再应用设置。");
                object options = Launcher.NewOptions();
                if (args[0] == "apply")
                    foreach (XElement item in XElement.Load(args[2]).Elements("option"))
                    {
                        PropertyInfo p = Launcher.OptionsType.GetProperty((string)item.Attribute("name"));
                        if (p == null) throw new InvalidDataException("不支持的补丁选项。");
                        p.SetValue(options, Convert.ChangeType(item.Value, p.PropertyType, CultureInfo.InvariantCulture), null);
                    }
                Launcher.ApplyOptions(options, delegate(string text) { reply.Add(new XElement("log", text)); }, true);
            }
            else throw new ArgumentException("未知操作。");
            reply.SetAttributeValue("ok", true);
        }
        catch (Exception ex)
        {
            reply.Add(new XElement("error", Launcher.Unwrap(ex).Message));
            reply.Add(new XElement("details", Launcher.Unwrap(ex).ToString()));
            // Recovery writes are only authorized as part of an explicit apply.
            if (args[0] == "apply" && Launcher.TryResetStateIfVanilla())
                reply.Add(new XElement("error", "检测到 Steam 已还原原版，已重置过期补丁状态。请再次应用。"));
        }
        reply.Save(args[3]);
        return (bool)reply.Attribute("ok") ? 0 : 1;
    }
}
