using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RiverGuard.Updates;

// Окно согласия и загрузки обновления
public partial class UpdateWindow : Window
{
    private readonly UpdateService service;
    private readonly ReleaseUpdate release;
    private CancellationTokenSource? download;
    private bool closeRequested;
    internal PreparedUpdate? Prepared {get;private set;}
    internal UpdateWindow(UpdateService service,ReleaseUpdate release)
    {
        InitializeComponent();this.service=service;this.release=release;
        VersionLabel.Text=$"Установлена {UpdateService.CurrentVersionText}   →   Доступна {release.Version}";
        ReleaseNotes.Text=string.IsNullOrWhiteSpace(release.Notes)?"Новая стабильная версия RiverGuard · Apdulium-beep.":release.Notes;
    }
    private void Release_Click(object s,RoutedEventArgs e)
    {
        try{Process.Start(new ProcessStartInfo(release.Page){UseShellExecute=true});}
        catch(Exception){ProgressText.Text="Не удалось открыть браузер. Можно обновиться, не открывая страницу.";}
    }
    private async void Install_Click(object s,RoutedEventArgs e)=>await DownloadAsync();
    internal async Task DownloadAsync()
    {
        if(download!=null)return;
        download=new();InstallButton.IsEnabled=false;LaterButton.Content="Отмена";DownloadProgress.Visibility=Visibility.Visible;ProgressText.Foreground=new SolidColorBrush(Color.FromRgb(175,193,216));
        try
        {
            var prepared=await service.PrepareAsync(release,new Progress<UpdateProgress>(p=>{if(download!=null&&!closeRequested){DownloadProgress.Value=p.Fraction;ProgressText.Text=p.Message;}}),download.Token);
            if(!closeRequested)Prepared=prepared;
            download.Dispose();download=null;Close();
        }
        catch(OperationCanceledException)
        {ProgressText.Text=closeRequested?"Загрузка отменена.":"Время загрузки истекло. Можно повторить попытку.";}
        catch(Exception ex)
        {ProgressText.Text="Обновление не установлено: "+ex.Message;ProgressText.Foreground=new SolidColorBrush(Color.FromRgb(255,154,165));}
        finally
        {
            download?.Dispose();download=null;InstallButton.IsEnabled=true;LaterButton.Content="Позже";
            if(closeRequested)Close();
        }
    }
    internal void Defer()
    {if(download!=null){closeRequested=true;download.Cancel();LaterButton.IsEnabled=false;}else Close();}
    private void Later_Click(object s,RoutedEventArgs e)=>Defer();
    private void Window_Closing(object? s,CancelEventArgs e)
    {if(download!=null){e.Cancel=true;closeRequested=true;download.Cancel();}}
    private void Window_KeyDown(object s,KeyEventArgs e)
    {if(e.Key==Key.Escape){Defer();e.Handled=true;}}
}
