using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace RiverGuard;

public partial class MainWindow
{
    // Удаление проектов
    private void DeleteProject_Click(object s,RoutedEventArgs e)
    {
        e.Handled=true;
        if((s as Button)?.Tag is not ProjectEntry entry||cancellation!=null)return;
        bool active=projectPath!=null&&string.Equals(Path.GetFullPath(projectPath),Path.GetFullPath(entry.Path),StringComparison.OrdinalIgnoreCase);
        string message=File.Exists(entry.Path)?"Файл будет перемещён в корзину Windows. Для импортированного проекта удаляется файл на его исходном месте.":"Файл отсутствует. Запись будет удалена из списка.";
        if(active)message+="\nАктивный проект будет закрыт. Несохранённые изменения будут потеряны.";
        if(MessageBox.Show(this,$"Удалить проект «{entry.Name}»?\n\n{entry.Path}\n\n{message}","Удаление проекта",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        try{DeleteLibraryEntry(entry);}
        catch(Exception ex){Notify("Не удалось удалить проект: "+ex.Message);RefreshLibrary();}
    }
    private void DeleteLibraryEntry(ProjectEntry entry)
    {
        bool active=projectPath!=null&&string.Equals(Path.GetFullPath(projectPath),Path.GetFullPath(entry.Path),StringComparison.OrdinalIgnoreCase);
        ProjectStorage.Delete(entry);
        if(active){playback.Stop();hasActiveProject=false;projectPath=null;result=null;savedState="";}
        ShowHome();
    }

    // Дополнительные форматы отчётов
    private void ExtendedExport_Click(object s,RoutedEventArgs e)
    {
        if(!NeedResult()||(s as Button)?.Tag is not string format)return;
        var path=Output($"Отчёт {format.ToUpperInvariant()}|*.{format}","");if(path==null)return;
        ExportAction(()=>WriteExtendedReport(format,path),path);
    }
    private void WriteExtendedReport(string format,string path)
    {
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            switch(format)
            {
                case "xlsx":ExportService.Excel(result!,temporary);break;
                case "ods":ExportService.Ods(result!,temporary);break;
                case "xml":ExportService.Xml(result!,temporary);break;
                case "zip":ExportService.Bundle(result!,temporary,ReportImages());break;
                case "txt":case "md":case "rtf":ExportService.TextReport(result!,temporary,format);break;
                default:throw new InvalidOperationException("Неизвестный формат отчёта.");
            }
            File.Move(temporary,path,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
