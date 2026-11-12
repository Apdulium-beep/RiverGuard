using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using RiverGuard.Updates;

namespace RiverGuard;

public partial class MainWindow
{
    // Проверка обновлений при запуске и вручную
    private readonly UpdateService updateService=new();
    private readonly CancellationTokenSource updateLifetime=new();
    private bool checkingUpdate,autoUpdatePromptShown;
    private ReleaseUpdate? availableUpdate;
    private PreparedUpdate? preparedUpdate;
    private bool restartForUpdate;
    private async void CheckUpdates_Click(object s,RoutedEventArgs e)=>await CheckForUpdatesAsync(true);
    private void OfferPendingUpdateIfIdle()
    {
        if(availableUpdate!=null&&!autoUpdatePromptShown&&!checkingUpdate&&IsActive&&cancellation==null&&RenameOverlay.Visibility!=Visibility.Visible&&!(StartScreen.Visibility==Visibility.Visible&&CreateProjectPanel.Visibility==Visibility.Visible)&&OwnedWindows.Count==0)
            _=CheckForUpdatesAsync(false);
    }
    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if(checkingUpdate||updateLifetime.IsCancellationRequested)return;
        if(preparedUpdate!=null){if(interactive)RestartWithUpdate();return;}
        checkingUpdate=true;CheckUpdatesButton.IsEnabled=false;
        try
        {
            if(interactive||availableUpdate==null)availableUpdate=await updateService.CheckAsync(updateLifetime.Token);
            if(!IsVisible||updateLifetime.IsCancellationRequested)return;
            if(availableUpdate==null){if(interactive)Notify("Установлена актуальная стабильная версия RiverGuard "+UpdateService.CurrentVersionText+".");return;}
            if(cancellation!=null||RenameOverlay.Visibility==Visibility.Visible||StartScreen.Visibility==Visibility.Visible&&CreateProjectPanel.Visibility==Visibility.Visible||OwnedWindows.Count>0||!IsActive)
            {if(interactive)Notify("Доступна версия "+availableUpdate.Version+". Завершите текущую операцию и снова нажмите «Проверить обновления».");return;}
            autoUpdatePromptShown=true;
            var dialog=new UpdateWindow(updateService,availableUpdate){Owner=this};dialog.ShowDialog();
            if(dialog.Prepared!=null){preparedUpdate=dialog.Prepared;RestartWithUpdate();}
        }
        catch(OperationCanceledException){if(interactive&&IsVisible&&!updateLifetime.IsCancellationRequested)Notify("Проверка обновлений не завершилась вовремя. Программа продолжает работать без обновления.");}
        catch(Exception ex){if(interactive&&IsVisible&&!updateLifetime.IsCancellationRequested)Notify("Не удалось проверить обновления. Проверьте интернет и доступность публичного репозитория GitHub.\n\n"+ex.Message);}
        finally
        {
            checkingUpdate=false;CheckUpdatesButton.IsEnabled=true;
            CheckUpdatesButton.Content=preparedUpdate!=null?"Перезапустить и обновить":availableUpdate!=null?"Доступна версия "+availableUpdate.Version:"Проверить обновления";
        }
    }
    private void RestartWithUpdate()
    {
        if(preparedUpdate==null||cancellation!=null)return;
        restartForUpdate=true;Close();
        if(IsVisible){restartForUpdate=false;Notify("Обновление уже загружено, но закрытие проекта отменено. Можно сохранить данные и позже нажать «Перезапустить и обновить».");}
    }
    // Перезапуск только после принятого закрытия и сохранения проекта
    private void UpdateWindow_Closed(object? sender,EventArgs e)
    {
        updateLifetime.Cancel();updateService.Dispose();
        if(!restartForUpdate||preparedUpdate==null)return;
        try
        {
            var start=new ProcessStartInfo(preparedUpdate.ExecutablePath){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(preparedUpdate.ExecutablePath)!};
            if(projectPath!=null&&File.Exists(projectPath))start.ArgumentList.Add(projectPath);
            using var process=Process.Start(start)??throw new IOException("Windows не запустила новую версию.");
            UpdateShortcuts(preparedUpdate.ExecutablePath);
        }
        catch(Exception ex){MessageBox.Show("Не удалось запустить обновление. Предыдущая версия сохранена и может быть запущена снова.\n\n"+ex.Message,"RiverGuard",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    private static void UpdateShortcuts(string executable)
    {
        // Ярлыки приложения
        try
        {
            string old=Path.Combine(AppContext.BaseDirectory,"RiverGuard.exe");
            string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string registry=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RiverGuard","Updates","shortcuts.json");
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Path.Combine(desktop,"RiverGuard.lnk"),Path.Combine(desktop,"Запустить RiverGuard.lnk"),Path.Combine(desktop,"RiverGuard","Запустить RiverGuard.lnk")};
            if(File.Exists(registry)&&new FileInfo(registry).Length<16384)
            {
                try{foreach(string path in JsonSerializer.Deserialize<string[]>(File.ReadAllText(registry))??Array.Empty<string>())if(Path.IsPathFullyQualified(path)&&!path.StartsWith(@"\\")&&Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase))paths.Add(path);}catch(JsonException){}
            }
            DirectoryInfo? parent=new(AppContext.BaseDirectory);
            for(int i=0;i<5&&parent!=null;i++,parent=parent.Parent)paths.Add(Path.Combine(parent.FullName,"Запустить RiverGuard.lnk"));
            var type=Type.GetTypeFromProgID("WScript.Shell");if(type==null)return;
            dynamic shell=Activator.CreateInstance(type)!;
            var updated=new List<string>();
            foreach(string path in paths)
            {
                if(!File.Exists(path)&&path!=Path.Combine(desktop,"RiverGuard.lnk"))continue;
                dynamic link=shell.CreateShortcut(path);
                if(File.Exists(path)&&!string.Equals((string)link.TargetPath,old,StringComparison.OrdinalIgnoreCase))continue;
                link.TargetPath=executable;link.WorkingDirectory=Path.GetDirectoryName(executable);link.IconLocation=executable+",0";link.Description="RiverGuard · Apdulium-beep";link.Save();
                updated.Add(path);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(registry)!);string temporary=registry+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(updated));File.Move(temporary,registry,true);
        }
        catch(Exception){ }
    }
}
