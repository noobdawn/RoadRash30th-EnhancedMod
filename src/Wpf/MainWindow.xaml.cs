using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace RoadRash.Workbench
{
    public partial class MainWindow : Window
    {
        internal WorkbenchViewModel ViewModel { get; }
        private readonly bool testMode;
        private bool launchFinished;
        public MainWindow(WorkbenchViewModel vm, bool offline = false)
        {
            InitializeComponent(); ViewModel = vm; DataContext = vm; testMode = offline;
            vm.Settings.Strings.AddWindowResources(Resources);
            vm.LaunchCompleted += () => { launchFinished = true; Close(); };
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(vm.Page)) PageScroll.ScrollToTop(); };
            if (!offline) Loaded += async (s, e) => await vm.Inspect();
            Closing += OnClosing;
        }
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (testMode || launchFinished) return;
            if (ViewModel.Busy) { e.Cancel = true; return; }
            if (!ViewModel.Settings.Valid && ViewModel.Dirty)
            {
                if (MessageBox.Show(this, "有无效输入，无法保存本次草稿。放弃本次修改并关闭？", "尚未保存", MessageBoxButton.OKCancel) != MessageBoxResult.OK) e.Cancel = true;
                return;
            }
            try { ViewModel.SaveDraft(); }
            catch (Exception ex) { e.Cancel = true; ViewModel.Log("草稿保存失败：" + ex.Message); ViewModel.LogVisible = true; }
        }
        private async void ChooseDirectory(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择 RoadRash95.exe 所在目录", InitialDirectory = ViewModel.GameDirectory };
            if (dialog.ShowDialog(this) != true) return;
            try { await ViewModel.ChangeDirectory(dialog.FolderName); }
            catch (Exception ex) { ViewModel.Log(ex.Message); ViewModel.LogVisible = true; }
        }
        private void OpenDirectory(object sender, RoutedEventArgs e) => Open(ViewModel.GameDirectory);
        private void OpenReadme(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Tools", "LAUNCHER_README.txt");
            if (!File.Exists(path)) path = Path.Combine(ViewModel.GameDirectory, "Tools", "MOD_README_CN.txt");
            Open(path);
        }
        private void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { ViewModel.Log(ex.Message); ViewModel.LogVisible = true; }
        }
        private async void RestoreOriginal(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(this, "将游戏文件还原为官方英文原版。设置草稿会保留。\n\n确认还原？", "还原原版文件", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK) await ViewModel.RestoreOriginal();
        }
    }
}
