using System.IO;
using System.Text.Json;
using System.Windows;
using RiverGuard.Core;

namespace RiverGuard;

public partial class MainWindow
{
    // Запись завершённых, отменённых и ошибочных попыток
    private void RecordCalculation(ProjectData input,SimulationResult? output,string? error=null,bool cancelled=false)
    {
        var record=CalculationRecord.Create(input,output,error,cancelled);record.ApplicationVersion=Updates.UpdateService.CurrentVersionText;vm.Project.CalculationHistory.Add(record);if(!SavedBadge.Text.EndsWith(" *"))SavedBadge.Text+=" *";
        if(projectPath==null){Status.Text+=" · История в памяти; Ctrl+S — сохранить проект";return;}
        try
        {
            ProjectStorage.Save(vm.Project,projectPath);savedState=JsonSerializer.Serialize(vm.Project,ProjectData.Json);SavedBadge.Text=" •  "+Path.GetFileName(projectPath);
            Status.Text+=" · запись истории сохранена";
        }
        catch(Exception ex){Status.Text+=" · история в памяти: файл проекта не сохранён ("+ex.Message+")";}
    }
    private void History_Click(object sender,RoutedEventArgs e)
    {
        if(!hasActiveProject||cancellation!=null||!Commit())return;
        var window=new HistoryWindow(vm.Project){Owner=this};window.ShowDialog();
        if(window.ChosenInput!=null&&ConfirmReplace())SetProject(window.ChosenInput);
    }
}
