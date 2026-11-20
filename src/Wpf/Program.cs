using System;
using System.IO;
using System.Windows;

namespace RoadRash.Workbench
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--export-localization")
                {
                    if (args.Length != 2) throw new ArgumentException("请指定导出的 JSON 文件路径。");
                    LauncherStrings.ExportDefaults(args[1]);
                    return 0;
                }
                string directory = Launcher.DetectGameDir();
                if (File.Exists(Path.Combine(Environment.CurrentDirectory, "RoadRash95.exe"))) directory = Environment.CurrentDirectory;
                Launcher.GameDir = directory; Launcher.ToolPath = Launcher.FindTool();
                if (args.Length > 0 && args[0] == "--inspect-game")
                {
                    GameService.Bridge("inspect", args[1], null, _ => { }).GetAwaiter().GetResult().Save(args[2]);
                    return 0;
                }
                if (args.Length > 0 && args[0] != "--self-test" && args[0] != "--render-ui")
                {
                    if (args[0] == "--status") GameService.Bridge("inspect", directory, null, _ => { }).GetAwaiter().GetResult().Save(Path.Combine(directory, "launcher_status.xml"));
                    else if (args[0] == "--apply" || args[0] == "--restore")
                    {
                        var settings = new SettingsStore();
                        // Preserve the historical --apply profile; UI apply uses the INI draft.
                        settings.Put("racers", 100); settings.Put("aggression", 3); settings.Put("police", 2); settings.Put("traffic", 2);
                        GameService.Bridge(args[0] == "--apply" ? "apply" : "restore", directory, GameService.Options(settings), _ => { }).GetAwaiter().GetResult().Save(Path.Combine(directory, "launcher_cli.xml"));
                    }
                    else Launcher.RunCli(args);
                    return Environment.ExitCode;
                }
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/RoadRash30thLauncher;component/Theme.xaml", UriKind.Relative) });
                if (args.Length > 0) return OfflineVerification.Run(args[0], args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "verification"));
                app.DispatcherUnhandledException += (s, e) =>
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "RoadRashLauncher-error.log"), e.Exception.ToString());
                    MessageBox.Show("修改器遇到错误：\n" + e.Exception.Message, "RoadRash 修改器", MessageBoxButton.OK, MessageBoxImage.Error); e.Handled = true;
                };
                return app.Run(new MainWindow(new WorkbenchViewModel(directory)));
            }
            catch (Exception ex)
            {
                string report = Path.Combine(Path.GetTempPath(), "RoadRashLauncher-error.log");
                File.WriteAllText(report, ex.ToString());
                if (args.Length == 0) MessageBox.Show("启动失败：" + ex.Message + "\n日志：" + report, "RoadRash 修改器");
                return 1;
            }
        }
    }
}
