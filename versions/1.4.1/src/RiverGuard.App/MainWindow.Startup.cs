using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RiverGuard.Core;

namespace RiverGuard;

public partial class MainWindow
{
    // Стартовый экран и библиотека
    private bool hasActiveProject;
    private string newProjectScenario=ScenarioMode.Breach;
    private List<ProjectEntry> library=new();
    private void ShowHome()
    {
        if(cancellation!=null)return;
        playback.Stop();PlayButton.Content="▶";
        WorkspaceSidebar.IsEnabled=WorkspaceHeader.IsEnabled=Pages.IsEnabled=false;
        StartScreen.Visibility=Visibility.Visible;StartHomePanel.Visibility=Visibility.Visible;CreateProjectPanel.Visibility=Visibility.Collapsed;
        ResumeProjectButton.Visibility=hasActiveProject?Visibility.Visible:Visibility.Collapsed;
        RefreshLibrary();
    }
    private void ShowWorkspace()
    {
        hasActiveProject=true;
        WorkspaceSidebar.IsEnabled=WorkspaceHeader.IsEnabled=Pages.IsEnabled=true;
        StartScreen.Visibility=Visibility.Collapsed;
    }
    private void Home_Click(object s,RoutedEventArgs e)=>ShowHome();
    private void Resume_Click(object s,RoutedEventArgs e){if(hasActiveProject)ShowWorkspace();}
    private void WizardBack_Click(object s,RoutedEventArgs e)=>ShowHome();
    private void RefreshLibrary_Click(object s,RoutedEventArgs e)=>RefreshLibrary();
    private void ProjectSearch_Changed(object s,TextChangedEventArgs e)=>FilterLibrary();
    private void RefreshLibrary()
    {
        if(ProjectLibraryList==null)return;
        try{library=ProjectStorage.Library();FilterLibrary();}
        catch(Exception ex){library=new();FilterLibrary();EmptyLibraryText.Text="Не удалось прочитать библиотеку";Status.Text=ex.Message;}
        RecentList.ItemsSource=ProjectStorage.Recent();
    }
    private void FilterLibrary()
    {
        if(ProjectLibraryList==null||EmptyLibraryPanel==null||ProjectSearch==null)return;
        string search=ProjectSearch.Text.Trim();
        var filtered=library.Where(p=>search.Length==0||$"{p.Name} {p.Scenario} {p.Path}".Contains(search,StringComparison.OrdinalIgnoreCase)).ToList();
        ProjectLibraryList.ItemsSource=filtered;
        EmptyLibraryPanel.Visibility=filtered.Count==0?Visibility.Visible:Visibility.Collapsed;
        EmptyLibraryText.Text=search.Length>0?"Проекты не найдены":"Здесь будут ваши проекты";
        LibraryCountText.Text=$"Показано: {filtered.Count} · всего в библиотеке: {library.Count}";
    }
    private void LibraryProject_Click(object s,RoutedEventArgs e)
    {
        if((s as Button)?.Tag is not ProjectEntry entry||!entry.IsAvailable||!ConfirmReplace())return;
        try
        {
            if(entry.IsRecovery)
            {
                SetProject(ProjectStorage.Load(entry.Path));savedState="";SavedBadge.Text=" •  Восстановленная сессия *";
                Status.Text="Восстановлены исходные данные. Сохраните их в отдельный проект через Ctrl+S.";
            }
            else LoadProject(entry.Path);
        }
        catch(Exception ex){Notify("Не удалось открыть проект: "+ex.Message);RefreshLibrary();}
    }
    private void ProjectsFolder_Click(object s,RoutedEventArgs e)
    {
        try{Directory.CreateDirectory(ProjectStorage.ProjectsFolder);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",$"\"{ProjectStorage.ProjectsFolder}\""){UseShellExecute=true});}
        catch(Exception ex){Notify("Не удалось открыть папку: "+ex.Message);}
    }
    // Создание проекта
    private void BeginNewProject()
    {
        if(cancellation!=null)return;
        ShowHome();StartHomePanel.Visibility=Visibility.Collapsed;CreateProjectPanel.Visibility=Visibility.Visible;
        NewProjectName.Text="Новый проект";UseDemoGeometry.IsChecked=true;SetNewScenario(ScenarioMode.Breach);
        NewProjectLocation.Text="Автоматическое сохранение: "+ProjectStorage.ProjectsFolder;
        NewProjectName.Focus();NewProjectName.SelectAll();
    }
    private void ChooseScenario_Click(object s,RoutedEventArgs e)=>SetNewScenario((s as Button)?.Tag?.ToString() switch{"overflow"=>ScenarioMode.Overflow,"gate"=>ScenarioMode.Gate,"circular"=>ScenarioMode.Circular,_=>ScenarioMode.Breach});
    private void SetNewScenario(string mode)
    {
        newProjectScenario=mode;bool overflow=ScenarioMode.IsOverflow(mode);
        foreach(var (button,value) in new[]{(BreachScenarioButton,ScenarioMode.Breach),(OverflowScenarioButton,ScenarioMode.Overflow),(GateScenarioButton,ScenarioMode.Gate),(CircularScenarioButton,ScenarioMode.Circular)})
        {bool selected=value==mode;button.Background=new SolidColorBrush(selected?Color.FromRgb(29,61,71):Color.FromRgb(23,39,61));button.BorderBrush=new SolidColorBrush(selected?Teal:Color.FromRgb(48,70,94));}
        WizardScenarioTitle.Text=overflow?"Перелив через гребень":"Заданный проран";
        WizardScenarioDescription.Text=overflow?"Вода проходит через фиксированный гребень. Его длина не изменяется во времени; поля формирования прорана не используются.":"Отверстие задано отметкой порога и конечной шириной. Ширина может раскрываться линейно или задаваться мгновенно. Эрозия не вычисляется.";
        WizardScenarioFields.Text=overflow?"• Длина участка гребня\n• Абсолютная отметка гребня\n• Коэффициент расхода водослива":"• Конечная ширина прорана\n• Абсолютная отметка порога\n• Время формирования\n• Коэффициент расхода прорана";
        if(ScenarioMode.IsGate(mode))
        {WizardScenarioTitle.Text=ScenarioMode.Gate;WizardScenarioDescription.Text="Прямоугольный затвор поднимается с заданной скоростью. Расход определяется интегралом напора по открытой площади, с учётом свободного и затопленного истечения. Причина отказа и прочность не рассчитываются.";WizardScenarioFields.Text="• Ширина затвора\n• Максимальная высота открытия\n• Абсолютная отметка порога\n• Время подъёма (0 — мгновенно)\n• Коэффициент расхода отверстия";}
        if(ScenarioMode.IsCircular(mode))
        {WizardScenarioTitle.Text=ScenarioMode.Circular;WizardScenarioDescription.Text="Истечение через круглое отверстие постоянного диаметра, в том числе при частичном погружении. Учитываются уровни воды с обеих сторон. Это заданный дефект, не модель развития суффозии или эрозии.";WizardScenarioFields.Text="• Диаметр отверстия\n• Абсолютная отметка низа окружности\n• Коэффициент расхода отверстия\n• Постоянная геометрия с начала расчёта";}
    }
    private static ProjectData BuildNewProject(string name,string mode,bool demonstration)
    {
        var p=demonstration?Examples.Valley():new ProjectData();
        p.Name=name.Trim();p.Scenario=mode;
        p.Notes=demonstration?"Начальные значения и геометрия синтетические. Замените их данными своего объекта перед интерпретацией результатов.":"Новый проект. Добавьте собственные створы и проверьте все начальные параметры.";
        if(ScenarioMode.IsOverflow(mode)){p.BreachWidth=45;p.SillElevation=107;p.FormationMinutes=0;}
        if(ScenarioMode.IsGate(mode)){p.BreachWidth=6;p.GateHeight=2;p.SillElevation=101;p.FormationMinutes=2;p.DischargeCoefficient=0.62;}
        if(ScenarioMode.IsCircular(mode)){p.OutletDiameter=2;p.SillElevation=101;p.FormationMinutes=0;p.DischargeCoefficient=0.62;}
        return p;
    }
    private void FinishCreate_Click(object s,RoutedEventArgs e)
    {
        if(string.IsNullOrWhiteSpace(NewProjectName.Text)){Notify("Введите название проекта.");NewProjectName.Focus();return;}
        if(!ConfirmReplace())return;
        try
        {
            var p=BuildNewProject(NewProjectName.Text,newProjectScenario,UseDemoGeometry.IsChecked==true);
            string path=ProjectStorage.Create(p);SetProject(p,path);RefreshLibrary();Navigation.SelectedIndex=1;
            Status.Text="Проект создан и сохранён: "+path;
        }
        catch(Exception ex){Notify("Не удалось создать проект: "+ex.Message);}
    }
    // Адаптация рабочего пространства
    private void AdaptScenario()
    {
        if(FormationFields==null)return;
        bool overflow=ScenarioMode.IsOverflow(vm.Project.Scenario),gate=ScenarioMode.IsGate(vm.Project.Scenario),circle=ScenarioMode.IsCircular(vm.Project.Scenario);
        ScenarioPageTitle.Text=overflow?"Перелив через гребень":"Заданный проран плотины";
        ScenarioPageHint.Text=overflow?"Фиксированный гребень: задайте его длину, отметку и коэффициент расхода. Формирование прорана в этом режиме не рассчитывается.":"Задайте порог, конечную ширину и время раскрытия. Все абсолютные отметки должны быть в одной системе высот.";
        OpeningBlockTitle.Text=overflow?"02  Фиксированный водослив":"02  Заданный проран";
        OpeningWidthLabel.Text=overflow?"Длина участка гребня, м":"Конечная ширина прорана, м";
        SillLabel.Text=overflow?"Абсолютная отметка гребня, м":"Абсолютная отметка порога прорана, м";
        FormationFields.Visibility=ScenarioMode.HasFormation(vm.Project.Scenario)?Visibility.Visible:Visibility.Collapsed;
        OpeningWidthFields.Visibility=circle?Visibility.Collapsed:Visibility.Visible;
        GateHeightFields.Visibility=gate?Visibility.Visible:Visibility.Collapsed;CircularFields.Visibility=circle?Visibility.Visible:Visibility.Collapsed;
        FormationLabel.Text=gate?"Время подъёма затвора, мин (0 — мгновенно)":"Время линейного раскрытия, мин (0 — мгновенно)";
        OpeningModeHint.Text=overflow?"Длина гребня постоянна с начала расчёта. Размыв и разрушение гребня не моделируются.":"При времени 0 отверстие открывается мгновенно. При положительном времени ширина растёт линейно.";
        if(gate){ScenarioPageTitle.Text=ScenarioMode.Gate;ScenarioPageHint.Text="Заданный подъём затвора: постоянная ширина и растущая высота. Учитываются уровни воды с обеих сторон отверстия.";OpeningBlockTitle.Text="02  Прямоугольный затвор";OpeningWidthLabel.Text="Ширина затвора, м";SillLabel.Text="Абсолютная отметка порога затвора, м";OpeningModeHint.Text="Высота растёт линейно до заданного максимума. Cd относится к отверстию, а не к водосливу. Прочность, обратный поток и длинный водовод не рассчитываются.";}
        if(circle){ScenarioPageTitle.Text=ScenarioMode.Circular;ScenarioPageHint.Text="Постоянный круглый дефект: задайте диаметр и отметку низа окружности. Напор интегрируется по смоченной площади.";OpeningBlockTitle.Text="02  Круглое отверстие";SillLabel.Text="Абсолютная отметка низа отверстия, м";OpeningModeHint.Text="Диаметр не изменяется. Время формирования и ширина прорана не используются. Это не расчёт суффозии или размыва; длинный водовод и обратный поток исключены.";}
        OverviewFlowTitle.Text=ScenarioMode.FlowTitle(result?.Source.Scenario??vm.Project.Scenario);
        OverviewFlowHint.Text="Расход из водохранилища во времени · "+(result?.Source.Scenario??vm.Project.Scenario);
    }
}
