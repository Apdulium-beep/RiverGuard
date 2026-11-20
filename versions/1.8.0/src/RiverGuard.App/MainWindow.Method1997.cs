using System.Windows;
using System.Windows.Controls;
using RiverGuard.Core;
using RiverGuard.Controls;

namespace RiverGuard;

public partial class MainWindow
{
    private void MethodMode_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(Method1997Panel==null)return;
        AdaptScenario();UpdateMethodPresentation();
        Dispatcher.InvokeAsync(()=>{AdaptScenario();UpdateMethodPresentation();ApplyPresentation(presentation);});
    }
    private void UpdateMethodPresentation()
    {
        if(Method1997Panel==null)return;
        bool method=vm.Project.CalculationMode==CalculationModes.Method1997;
        Method1997Panel.Visibility=method?Visibility.Visible:Visibility.Collapsed;
        if(RgStorageHint!=null)RgStorageHint.Visibility=RgComputedVolume.Visibility=method?Visibility.Collapsed:Visibility.Visible;
        if(ModelBadge!=null)ModelBadge.Text=method?"МЕТОДИКА 1997 / EXPERIMENTAL":"RG-1D / 1.3";
        OpeningWidthFields.IsEnabled=BreachFractionFields.IsEnabled=!method;
        if(ScenarioPageTitle!=null&&method){ScenarioPageTitle.Text="Методика ВНИИ ГОЧС · 1997";ScenarioPageHint.Text="Отдельная экспериментальная транскрипция. Мгновенный прорыв, явные коэффициенты; источники и интерпретации открыты.";}
        if(SharedInputHint!=null)SharedInputHint.Text=method?"В этом режиме общий объём и площадь относятся к НПУ, уровень — к моменту аварии. Дно нулевого створа = дно у плотины. Ниже задайте мгновенное разрушение (0 мин), отключите долю длины плотины. Cd, ширина прорана RG-1D, сетка, горизонт времени и порог прихода не используются. K и Nр вводятся в таблице створов с обоснованием.":"RG-1D: численная модель; новые методические коэффициенты в этом режиме не участвуют.";
    }
    private void Sources_Click(object sender,RoutedEventArgs e)
    {
        var window=new Window{Title="RiverGuard · Источники",Owner=this,Width=1000,Height=760,MinWidth=700,MinHeight=500,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=Background,Foreground=Foreground,FontFamily=FontFamily,Content=new SourcesView{Margin=new Thickness(28)}};
        window.ShowDialog();
    }
    private void MethodExample_Click(object sender,RoutedEventArgs e)
    {
        if(ConfirmReplace()){SetProject(Method1997Examples.Control());Notify("Загружен синтетический контроль формул с K=Nр=1. Это не эталон результата «Волны»; значения коэффициентов демонстрационные.");}
    }
}
