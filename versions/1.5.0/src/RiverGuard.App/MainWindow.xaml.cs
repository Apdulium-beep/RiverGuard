using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RiverGuard.Core;
using RiverGuard.Controls;

namespace RiverGuard;

public partial class MainWindow : Window
{
    // Рабочее состояние
    private readonly Workspace vm=new();
    private SimulationResult? result;
    private CancellationTokenSource? cancellation;
    private string? projectPath;
    private string savedState="";
    private bool closingTest;
    private readonly List<Action> detachWatchers=new();
    private readonly System.Windows.Threading.DispatcherTimer playback=new(){Interval=TimeSpan.FromMilliseconds(40)};
    private static readonly Color Teal=Color.FromRgb(76,224,193),Blue=Color.FromRgb(113,163,255),Purple=Color.FromRgb(175,150,255),Amber=Color.FromRgb(244,191,111),Red=Color.FromRgb(255,122,142);
    public MainWindow()
    {
        InitializeComponent();DataContext=vm;vm.SelectedSection=vm.Project.Sections.FirstOrDefault();
        Title="RiverGuard "+Updates.UpdateService.CurrentVersionText+" — Apdulium-beep";
        Closed+=UpdateWindow_Closed;Activated+=(s,e)=>OfferPendingUpdateIfIdle();
        WatchProject();
        InitializeCatalog();
        playback.Tick+=(s,e)=>{if(result!=null)TimeSlider.Value=TimeSlider.Value>=TimeSlider.Maximum?0:Math.Min(TimeSlider.Maximum,TimeSlider.Value+TimeSlider.Maximum/250);};
        savedState=JsonSerializer.Serialize(vm.Project,ProjectData.Json);
        CommandBindings.Add(new(ApplicationCommands.Save,Save_Command));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save,Key.S,ModifierKeys.Control));
        CommandBindings.Add(new(ApplicationCommands.Open,(s,e)=>Open_Click(this,new())));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Open,Key.O,ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new ActionCommand(()=>Run_Click(this,new())),Key.F5,ModifierKeys.None));
        AdaptScenario();WorkspaceSidebar.IsEnabled=WorkspaceHeader.IsEnabled=Pages.IsEnabled=false;
    }
    // Запуск и восстановление
    private async void Window_Loaded(object sender,RoutedEventArgs e)
    {
        ProfilePreview();
        var args=Environment.GetCommandLineArgs();
        if(args.Contains("--smoke-test"))
        {
            closingTest=true;
            try
            {
                string errorPath=Path.Combine(AppContext.BaseDirectory,"smoke-error.txt");if(File.Exists(errorPath))File.Delete(errorPath);
                string dir=Path.Combine(AppContext.BaseDirectory,"SmokeTest");Directory.CreateDirectory(dir);
                ProjectStorage.TestFolderOverride=Path.Combine(dir,"Storage",Guid.NewGuid().ToString("N"));
                ShowHome();await VerifyStartupFlowAsync(dir);
                await VerifyMechanismFlowAsync(dir);
                await VerifyDeletionAsync(dir);
                await VerifyUpdaterAsync(dir);
                SetProject(Examples.Valley());
                vm.Project.DurationHours=0.12;vm.Project.Cells=40;
                await RunAsync();
                if(result==null)throw new InvalidOperationException("Расчёт не завершился");
                ProjectStorage.Save(vm.Project,Path.Combine(dir,"demo.riverguard"));
                ExportService.Csv(result,Path.Combine(dir,"data.csv"));
                ExportService.Html(result,Path.Combine(dir,"report.html"),ReportImages());
                ExportService.Word(result,Path.Combine(dir,"report.docx"),ReportImages());
                VerifyExtendedExports(dir);
                var authorChecks=new List<string>();
                void AuthorCheck(bool condition,string name){if(!condition)throw new InvalidDataException(name);authorChecks.Add("PASS "+name);}
                AuthorCheck(File.ReadAllText(Path.Combine(dir,"report.html")).Contains("Apdulium-beep"),"HTML identifies application author");
                AuthorCheck(File.ReadAllText(Path.Combine(dir,"data.csv")).Contains("Apdulium-beep"),"CSV identifies application author");
                AuthorCheck(result.ApplicationAuthor=="Apdulium-beep","JSON result metadata identifies application author");
                Navigation.SelectedIndex=6;UpdateLayout();
                AuthorCheck(Descendants<TextBlock>(AboutPage).Any(t=>t.Text.Contains("Apdulium-beep")),"About page identifies application author");
                Screenshot(Path.Combine(dir,"application-author.png"));Navigation.SelectedIndex=0;
                using(var authorWord=DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(Path.Combine(dir,"report.docx"),false))AuthorCheck(authorWord.MainDocumentPart!.Document!.InnerText.Contains("Apdulium-beep"),"DOCX identifies application author");
                File.WriteAllLines(Path.Combine(dir,"author-checks.txt"),authorChecks);
                using(var word=DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(Path.Combine(dir,"report.docx"),false))
                {
                    var issues=new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(word).ToList();
                    if(issues.Count>0)throw new InvalidDataException(string.Join("\n",issues.Select(issue=>issue.Description)));
                }
                await Task.Delay(400);Screenshot(Path.Combine(dir,"overview.png"));
                Navigation.SelectedIndex=3;await Task.Delay(400);UpdateLayout();
                for(int mode=0;mode<6;mode++){ChartTypeBox.SelectedIndex=mode;UpdateAnalysis();}
                ChartTypeBox.SelectedIndex=0;
                TimeSlider.Value=TimeSlider.Maximum/2;UpdateAnalysis();
                double before=TimeSlider.Value;Play_Click(this,new());await Task.Delay(160);Play_Click(this,new());
                if(TimeSlider.Value<=before)throw new InvalidOperationException("Воспроизведение времени не работает.");
                Screenshot(Path.Combine(dir,"analysis.png"));
                Navigation.SelectedIndex=2;await Task.Delay(400);UpdateLayout();Screenshot(Path.Combine(dir,"sections.png"));
                Navigation.SelectedIndex=1;await Task.Delay(400);UpdateLayout();Screenshot(Path.Combine(dir,"scenario.png"));
                Navigation.SelectedIndex=7;await Task.Delay(400);UpdateLayout();Screenshot(Path.Combine(dir,"catalog.png"));
                VerifyCatalogControls();
                Width=1150;Height=760;Navigation.SelectedIndex=0;await Task.Delay(400);UpdateLayout();Screenshot(Path.Combine(dir,"compact.png"));
                Navigation.SelectedIndex=5;await Task.Delay(300);Screenshot(Path.Combine(dir,"export-formats.png"));
                File.WriteAllText(Path.Combine(dir,"success.txt"),$"UTC: {DateTime.UtcNow:O}; Balance: {result.BalanceErrorPercent}; Steps: {result.Steps}; Sections: {result.Sections.Count}; DOCX schema: valid; All chart modes: passed; Catalog: {ScenarioCatalog.All.Count} presets, filters and previews passed; Startup, creation, import, library, scenario forms: passed; Deletion and extended exports: passed; Gate and circular mechanisms: passed; Update checks: {File.ReadAllLines(Path.Combine(dir,"update-checks.txt")).Length} passed");
            }
            catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"smoke-error.txt"),ex.ToString());Environment.ExitCode=1;}
            Close();return;
        }
        ShowHome();
        if(args.Skip(1).FirstOrDefault(File.Exists) is string path)
        {try{LoadProject(path);}catch(Exception ex){Notify(ex.Message);}}
        _=CheckForUpdatesAsync(false);
    }
    private void Screenshot(string path)
    {
        UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var f=File.Create(path);encoder.Save(f);
    }
    // Навигация и анимации
    private void Navigation_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(Pages==null)return;
        if(Navigation.SelectedIndex!=3){playback.Stop();if(PlayButton!=null)PlayButton.Content="▶";}
        var pages=new FrameworkElement[]{Overview,ScenarioPage,SectionsPage,ChartsPage,IsoPage,ReportPage,AboutPage,CatalogPage};
        for(int i=0;i<pages.Length;i++)pages[i].Visibility=i==Navigation.SelectedIndex?Visibility.Visible:Visibility.Collapsed;
        if(Navigation.SelectedIndex<0)return;
        var page=pages[Navigation.SelectedIndex];page.RenderTransform=new TranslateTransform();
        page.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(260)));
        page.RenderTransform.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(10,0,TimeSpan.FromMilliseconds(320)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
        if(Navigation.SelectedIndex==2)ProfilePreview();
        if(Navigation.SelectedIndex==3)UpdateAnalysis();
    }
    private void Scenario_Click(object s,RoutedEventArgs e)=>Navigation.SelectedIndex=1;
    private void Title_Drag(object s,MouseButtonEventArgs e){if(e.ClickCount==2)Maximize_Click(s,e);else if(e.LeftButton==MouseButtonState.Pressed)DragMove();}
    private void Minimize_Click(object s,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void Maximize_Click(object s,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void Close_Click(object s,RoutedEventArgs e)=>Close();
    private void OpenFolder_Click(object s,RoutedEventArgs e)=>Process.Start(new ProcessStartInfo("explorer.exe",$"\"{(projectPath!=null?Path.GetDirectoryName(projectPath):AppContext.BaseDirectory)}\""){UseShellExecute=true});
    // Ввод и проекты
    private bool Commit(bool notify=true)
    {
        foreach(var grid in new[]{SectionsGrid,LeftGrid,RightGrid})if(!grid.CommitEdit(DataGridEditingUnit.Cell,true)||!grid.CommitEdit(DataGridEditingUnit.Row,true)){if(notify)Notify("Завершите редактирование таблицы и исправьте недопустимые значения.");return false;}
        Keyboard.ClearFocus();FocusManager.SetFocusedElement(this,this);
        foreach(var box in Descendants<TextBox>(this))box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if(Descendants<DependencyObject>(this).Any(System.Windows.Controls.Validation.GetHasError)){if(notify)Notify("В одном из полей введено недопустимое число. Исправьте поле с красной рамкой.");return false;}
        return true;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T t)yield return t;foreach(var nested in Descendants<T>(child))yield return nested;}
    }
    private bool ConfirmReplace()
    {
        if(cancellation!=null)return false;
        if(RenameOverlay.Visibility==Visibility.Visible)return false;
        if(!hasActiveProject)return true;
        bool valid=Commit(false);
        if(valid&&JsonSerializer.Serialize(vm.Project,ProjectData.Json)==savedState)return true;
        var choice=MessageBox.Show("Сохранить изменения текущего проекта?","RiverGuard",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
        return choice==MessageBoxResult.No||(choice==MessageBoxResult.Yes&&SaveProject());
    }
    private void SetProject(ProjectData p,string? path=null)
    {
        vm.Project=p;vm.SelectedSection=p.Sections.FirstOrDefault();projectPath=path;result=null;
        playback.Stop();PlayButton.Content="▶";TimeSlider.Value=0;
        WatchProject();
        vm.Results=new();vm.FilteredIsolines=new();vm.FlowMetric=vm.HeightMetric=vm.AreaMetric=vm.BalanceMetric="—";
        vm.Warnings=new(){"Результаты отсутствуют. Выполните расчёт текущего сценария."};vm.Summary="Расчёт ещё не выполнялся.";
        foreach(var chart in new[]{OverviewChart,AnalysisChart,ResultProfile}){chart.Lines.Clear();chart.Refresh();}
        ResultSectionBox.ItemsSource=null;IsoSectionBox.ItemsSource=null;
        AdaptScenario();ShowWorkspace();Navigation.SelectedIndex=0;
        savedState=JsonSerializer.Serialize(p,ProjectData.Json);SavedBadge.Text=path==null?" •  Новый проект":" •  "+Path.GetFileName(path);
        ProfilePreview();Status.Text="Проект открыт · F5 — расчёт · Ctrl+S — сохранить";
    }
    // Отслеживание изменений исходных данных
    private void WatchProject()
    {
        foreach(var detach in detachWatchers)detach();detachWatchers.Clear();
        void Observe(INotifyPropertyChanged item)
        {PropertyChangedEventHandler handler=(s,e)=>MarkChanged();item.PropertyChanged+=handler;detachWatchers.Add(()=>item.PropertyChanged-=handler);}
        void Collection(System.Collections.Specialized.INotifyCollectionChanged collection)
        {System.Collections.Specialized.NotifyCollectionChangedEventHandler handler=(s,e)=>{WatchProject();MarkChanged();};collection.CollectionChanged+=handler;detachWatchers.Add(()=>collection.CollectionChanged-=handler);}
        Observe(vm.Project);Collection(vm.Project.Sections);
        foreach(var section in vm.Project.Sections)
        {Observe(section);Collection(section.LeftBank);Collection(section.RightBank);foreach(var point in section.LeftBank.Concat(section.RightBank))Observe(point);}
    }
    private void MarkChanged()
    {
        if(SavedBadge!=null&&!SavedBadge.Text.EndsWith(" *"))SavedBadge.Text+=" *";
        if(result!=null){Status.Text="Исходные данные изменены · показан предыдущий расчёт · F5 — пересчитать";Status.Foreground=new SolidColorBrush(Amber);}
    }
    private void New_Click(object s,RoutedEventArgs e)=>BeginNewProject();
    private void Demo_Click(object s,RoutedEventArgs e){if(ConfirmReplace())SetProject(Examples.Valley());}
    private void ThreeSections_Click(object s,RoutedEventArgs e){if(ConfirmReplace()){SetProject(Examples.ThreeSections());Notify("Загружен пример параметров для трёх створов. Проверьте исходные данные перед расчётом.");}}
    private void Open_Click(object s,RoutedEventArgs e)
    {if(!ConfirmReplace())return;var dialog=new OpenFileDialog{Filter="Проект RiverGuard|*.riverguard;*.json|Все файлы|*.*"};if(dialog.ShowDialog()==true)try{LoadProject(dialog.FileName);}catch(Exception ex){Notify("Не удалось открыть проект: "+ex.Message);}}
    private void LoadProject(string path){var p=ProjectStorage.Load(path);SetProject(p,path);ProjectStorage.Remember(path);RefreshLibrary();}
    private void Recent_Open(object s,MouseButtonEventArgs e){if(RecentList.SelectedItem is string path&&ConfirmReplace())try{LoadProject(path);}catch(Exception ex){Notify(ex.Message);}}
    private void Save_Command(object sender,ExecutedRoutedEventArgs e)=>SaveProject();
    private void Save_Click(object s,RoutedEventArgs e)=>SaveProject();
    private bool SaveProject()
    {
        if(!hasActiveProject){if(CreateProjectPanel.Visibility!=Visibility.Visible)BeginNewProject();return false;}
        if(!Commit())return false;
        if(projectPath==null){var dialog=new SaveFileDialog{Filter="Проект RiverGuard|*.riverguard",FileName=SafeName(vm.Project.Name)};if(dialog.ShowDialog()!=true)return false;projectPath=dialog.FileName;}
        try{ProjectStorage.Save(vm.Project,projectPath);savedState=JsonSerializer.Serialize(vm.Project,ProjectData.Json);ProjectStorage.Remember(projectPath);RefreshLibrary();SavedBadge.Text=" •  "+Path.GetFileName(projectPath);Status.Text="Проект сохранён: "+projectPath;return true;}catch(Exception ex){Notify(ex.Message);return false;}
    }
    private void Window_Closing(object? s,CancelEventArgs e)
    {
        playback.Stop();
        if(closingTest)return;
        if(!hasActiveProject)return;
        if(cancellation!=null){cancellation.Cancel();e.Cancel=true;Status.Text="Остановка расчёта. После завершения можно закрыть окно.";return;}
        if(!ConfirmReplace()){e.Cancel=true;return;}
        try{Directory.CreateDirectory(ProjectStorage.Folder);ProjectStorage.Save(vm.Project,Path.Combine(ProjectStorage.Folder,"last-session.riverguard"));}catch{}
    }
    // Створы и горизонтали
    private void AddSection_Click(object s,RoutedEventArgs e)
    {if(!Commit())return;double x=vm.Project.Sections.Count>0?vm.Project.Sections.Max(v=>v.Distance)+500:500;var section=new RiverSection{Name=$"Створ {vm.Project.Sections.Count+1}",Distance=x};vm.Project.Sections.Add(section);vm.SelectedSection=section;}
    private void CopySection_Click(object s,RoutedEventArgs e)
    {if(!Commit()||vm.SelectedSection==null)return;var copy=JsonSerializer.Deserialize<RiverSection>(JsonSerializer.Serialize(vm.SelectedSection))!;copy.Name+=" · копия";copy.Distance+=100;vm.Project.Sections.Add(copy);vm.SelectedSection=copy;}
    private void DeleteSection_Click(object s,RoutedEventArgs e)
    {if(vm.SelectedSection==null)return;var item=vm.SelectedSection;if(MessageBox.Show($"Удалить «{item.Name}»?","RiverGuard",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes){vm.Project.Sections.Remove(item);vm.SelectedSection=vm.Project.Sections.FirstOrDefault();}}
    private void SortSections_Click(object s,RoutedEventArgs e){if(!Commit())return;var sorted=vm.Project.Sections.OrderBy(v=>v.Distance).ToArray();vm.Project.Sections.Clear();foreach(var item in sorted)vm.Project.Sections.Add(item);vm.SelectedSection=sorted.FirstOrDefault();}
    private void AddBank_Click(object s,RoutedEventArgs e)
    {
        if(!Commit()||vm.SelectedSection is not RiverSection section)return;bool left=(s as Button)?.Tag?.ToString()=="left";var bank=left?section.LeftBank:section.RightBank;
        bank.Add(new(){Distance=bank.Count>0?bank[^1].Distance+25:(left?section.LeftWidth:section.RightWidth)+25,Elevation=bank.Count>0?bank[^1].Elevation+2:section.Bed+section.NormalDepth+2});ProfilePreview();
    }
    private void DeleteBank_Click(object s,RoutedEventArgs e)
    {if(vm.SelectedSection==null)return;bool left=(s as Button)?.Tag?.ToString()=="left";var grid=left?LeftGrid:RightGrid;var bank=left?vm.SelectedSection.LeftBank:vm.SelectedSection.RightBank;if(grid.SelectedItem is BankPoint point)bank.Remove(point);ProfilePreview();}
    private void Section_Changed(object s,SelectionChangedEventArgs e)=>ProfilePreview();
    private void Section_Edit(object? s,DataGridCellEditEndingEventArgs e)=>Dispatcher.BeginInvoke(new Action(ProfilePreview));
    private void ProfilePreview()
    {
        if(EditorProfile==null)return;
        if(vm.SelectedSection==null){EditorProfile.Lines.Clear();EditorProfile.Refresh();return;}
        try{var g=new SectionGeometry(vm.SelectedSection);DrawProfile(EditorProfile,g,vm.SelectedSection.Bed+vm.SelectedSection.NormalDepth,null);}catch{EditorProfile.Lines.Clear();EditorProfile.Refresh();}
    }
    // Выполнение расчёта
    private void Validate_Click(object s,RoutedEventArgs e)
    {if(!Commit())return;var errors=Core.Validation.Errors(vm.Project);Notify(errors.Count==0?"Исходные данные прошли проверку диапазонов и структуры.":string.Join("\n",errors));}
    private async void Run_Click(object s,RoutedEventArgs e)=>await RunAsync();
    private async Task RunAsync()
    {
        if(RenameOverlay.Visibility==Visibility.Visible)return;
        if(!hasActiveProject){Notify("Сначала создайте или импортируйте проект.");return;}
        if(StartScreen.Visibility==Visibility.Visible)ShowWorkspace();
        if(cancellation!=null||!Commit())return;
        var errors=Core.Validation.Errors(vm.Project);if(errors.Count>0){Notify(string.Join("\n",errors));return;}
        var input=vm.Project.Copy();cancellation=new();BusyOverlay.Visibility=Visibility.Visible;CalculationProgress.Value=0;
        var progress=new Progress<double>(value=>{CalculationProgress.Value=value*100;ProgressText.Text=$"{value*100:F0}% · расчёт неустановившегося потока";});
        try
        {
            result=await Task.Run(()=>new Solver().Run(input,progress,cancellation.Token));
            AdaptScenario();
            playback.Stop();PlayButton.Content="▶";TimeSlider.Maximum=result.Source.DurationHours;TimeSlider.Value=0;
            vm.Results=result.Sections;vm.Warnings=result.Warnings;vm.FilteredIsolines=result.Isolines;
            vm.FlowMetric=result.Sections.Max(r=>r.MaxDischarge).ToString("N1");vm.HeightMetric=result.Sections.Max(r=>r.WaveHeight).ToString("N2");
            vm.AreaMetric=(result.FloodArea/10000).ToString("N2");vm.BalanceMetric=result.BalanceErrorPercent.ToString("F5");
            vm.Summary=$"{result.Created:dd.MM.yyyy HH:mm} · {result.Source.Cells} ячеек · {result.Steps:N0} шагов · {result.ElapsedSeconds:F1} с\nВыход из водохранилища: {result.ReleasedVolume/1e6:F4} млн м³. Невязка: {result.BalanceErrorPercent:F6}%.";
            ResultSectionBox.ItemsSource=result.Sections;ResultSectionBox.SelectedIndex=0;
            IsoSectionBox.ItemsSource=new[]{"Все створы"}.Concat(result.Sections.Select(r=>r.Name)).ToArray();IsoSectionBox.SelectedIndex=0;
            OverviewChart.Lines.Clear();OverviewChart.XTitle="Время, ч";OverviewChart.YTitle="Расход, м³/с";OverviewChart.Lines.Add(new(){Name=ScenarioMode.FlowTitle(result.Source.Scenario),Color=Teal,Points=result.BreachFlow,Fill=true});OverviewChart.Refresh();
            UpdateAnalysis();Status.Text=$"Готово · {result.Sections.Count} створов · невязка {result.BalanceErrorPercent:F6}% · результаты относятся к снимку входных данных";
            Status.Foreground=new SolidColorBrush(Teal);
        }
        catch(OperationCanceledException){Status.Text="Расчёт отменён. Предыдущие результаты сохранены.";}
        catch(Exception ex){Status.Text="Расчёт не завершён";if(closingTest)throw;Notify(ex.Message);}
        finally{cancellation.Dispose();cancellation=null;BusyOverlay.Visibility=Visibility.Collapsed;OfferPendingUpdateIfIdle();}
    }
    private void Cancel_Click(object s,RoutedEventArgs e)=>cancellation?.Cancel();
    // Графики и фильтрация
    private void ResultSelection_Changed(object s,SelectionChangedEventArgs e)=>UpdateAnalysis();
    private void ChartType_Changed(object s,SelectionChangedEventArgs e)=>UpdateAnalysis();
    private void UpdateAnalysis()
    {
        if(result==null||AnalysisChart==null)return;
        var r=ResultSectionBox.SelectedItem as SectionResult??result.Sections.First();
        var section=result.Source.Sections.OrderBy(v=>v.Distance).ElementAt(result.Sections.IndexOf(r));
        double position=TimeSlider?.Value??0;
        double current=section.Bed+Interpolate(r.DepthHistory,position);
        DrawProfile(ResultProfile,new SectionGeometry(section),section.Bed+section.NormalDepth,r.MaxLevel,current);
        AnalysisChart.Lines.Clear();AnalysisChart.XTitle="Время, ч";
        switch(ChartTypeBox.SelectedIndex)
        {
            case 1:AnalysisChart.YTitle="Расход, м³/с";AnalysisChart.Lines.Add(new(){Name=r.Name,Color=Blue,Points=r.FlowHistory,Fill=true});break;
            case 2:AnalysisChart.YTitle="Скорость, м/с";AnalysisChart.Lines.Add(new(){Name=r.Name,Color=Purple,Points=r.VelocityHistory,Fill=true});break;
            case 3:
                AnalysisChart.XTitle="Удаление, км";AnalysisChart.YTitle="Время, ч";
                AnalysisChart.Lines.Add(new(){Name="Фронт",Color=Teal,Points=result.Sections.Where(v=>v.ArrivalHours.HasValue).Select(v=>new Sample(v.Distance/1000,v.ArrivalHours!.Value)).ToList()});
                AnalysisChart.Lines.Add(new(){Name="Гребень",Color=Red,Points=result.Sections.Select(v=>new Sample(v.Distance/1000,v.PeakHours)).ToList()});
                AnalysisChart.Lines.Add(new(){Name="Спад",Color=Purple,Points=result.Sections.Where(v=>v.RecessionHours.HasValue).Select(v=>new Sample(v.Distance/1000,v.RecessionHours!.Value)).ToList()});break;
            case 4:
                AnalysisChart.XTitle="Удаление, км";AnalysisChart.YTitle="За бытовым берегом, м";
                AnalysisChart.Lines.Add(new(){Name="ВЗМ · левый",Color=Teal,Points=result.Sections.Select(v=>new Sample(v.Distance/1000,v.FloodLeft)).ToList()});
                AnalysisChart.Lines.Add(new(){Name="ВЗМ · правый",Color=Teal,Dashed=true,Points=result.Sections.Select(v=>new Sample(v.Distance/1000,-v.FloodRight)).ToList()});
                AnalysisChart.Lines.Add(new(){Name="h ≥ 1,5 м",Color=Red,Points=result.Sections.Select(v=>new Sample(v.Distance/1000,v.DangerLeft)).ToList()});
                AnalysisChart.Lines.Add(new(){Name="h ≥ 3 м",Color=Amber,Points=result.Sections.Select(v=>new Sample(v.Distance/1000,v.DestructionLeft)).ToList()});break;
            case 5:AnalysisChart.YTitle="Уровень, м";AnalysisChart.Lines.Add(new(){Name="Водохранилище",Color=Blue,Points=result.ReservoirLevel,Fill=true});break;
            default:AnalysisChart.YTitle="Глубина, м";AnalysisChart.Lines.Add(new(){Name=r.Name,Color=Teal,Points=r.DepthHistory,Fill=true});AnalysisChart.Lines.Add(new(){Name="Бытовая глубина",Color=Blue,Dashed=true,Points=new(){new(0,section.NormalDepth),new(result.Source.DurationHours,section.NormalDepth)}});break;
        }
        AnalysisChart.MarkerX=ChartTypeBox.SelectedIndex is 0 or 1 or 2 or 5?position:null;
        AnalysisChart.Refresh();
    }
    private static void DrawProfile(ChartView chart,SectionGeometry g,double initial,double? peak,double? current=null)
    {
        chart.Lines.Clear();chart.XTitle="От фарватера, м";chart.YTitle="Отметка, м";
        var s=g.Source;double left=s.LeftBank.Count>0?s.LeftBank[^1].Distance:s.LeftWidth+80,right=s.RightBank.Count>0?s.RightBank[^1].Distance:s.RightWidth+80;
        var extent=g.Extent(peak??initial);left=Math.Max(left,extent.Left+5);right=Math.Max(right,extent.Right+5);
        chart.Lines.Add(new(){Name="Рельеф",Color=Amber,Points=new[]{new Sample(-left,g.BedAt(-left))}.Concat(g.Profile.Where(p=>p.X>-left&&p.X<right)).Append(new Sample(right,g.BedAt(right))).ToList()});
        var edge=g.Extent(initial);chart.Lines.Add(new(){Name="Бытовой",Color=Blue,Dashed=true,Points=new(){new(-edge.Left,initial),new(edge.Right,initial)}});
        if(peak is double level){var max=g.Extent(level);chart.Lines.Add(new(){Name="Максимум",Color=Teal,Points=new(){new(-max.Left,level),new(max.Right,level)}});}
        if(current is double now){var live=g.Extent(now);chart.Lines.Add(new(){Name="Сейчас",Color=Purple,Points=new(){new(-live.Left,now),new(live.Right,now)}});}
        chart.Refresh();
    }
    // Воспроизведение временной истории
    private static double Interpolate(List<Sample> values,double t)
    {
        if(values.Count==0)return 0;
        if(t<=values[0].X)return values[0].Y;if(t>=values[^1].X)return values[^1].Y;
        int lo=0,hi=values.Count-1;while(hi-lo>1){int mid=(lo+hi)/2;if(values[mid].X<t)lo=mid;else hi=mid;}
        return values[lo].Y+(values[hi].Y-values[lo].Y)*(t-values[lo].X)/(values[hi].X-values[lo].X);
    }
    private void Play_Click(object s,RoutedEventArgs e)
    {if(result==null){Notify("Сначала выполните расчёт.");return;}if(playback.IsEnabled){playback.Stop();PlayButton.Content="▶";}else{playback.Start();PlayButton.Content="Ⅱ";}}
    private void Time_Changed(object s,RoutedPropertyChangedEventArgs<double> e)
    {if(TimeLabel!=null)TimeLabel.Text=$"{e.NewValue:F3} ч";if(result!=null)UpdateAnalysis();}
    private void IsoFilter_Changed(object s,SelectionChangedEventArgs e)
    {
        if(result==null||IsoSectionBox==null||IsoBankBox==null)return;
        string? name=IsoSectionBox.SelectedItem as string;int bank=IsoBankBox.SelectedIndex;
        vm.FilteredIsolines=result.Isolines.Where(v=>(name==null||name=="Все створы"||v.Section==name)&&(bank<=0||v.Bank==(bank==1?"Левый":"Правый"))).ToList();
    }
    // Экспорт
    private bool NeedResult()
    {
        if(result==null){Notify("Сначала выполните расчёт.");return false;}
        if(!Commit())return false;
        if(JsonSerializer.Serialize(vm.Project,ProjectData.Json)!=JsonSerializer.Serialize(result.Source,ProjectData.Json))
            return MessageBox.Show("Исходные данные изменены после расчёта. Экспортировать предыдущий расчёт с его исходными данными?","RiverGuard",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
        return true;
    }
    private static string SafeName(string name)=>string.Join("_",name.Split(Path.GetInvalidFileNameChars()));
    private string? Output(string filter,string suffix)
    {var dialog=new SaveFileDialog{Filter=filter,FileName=SafeName(result?.Source.Name??vm.Project.Name)+suffix};return dialog.ShowDialog()==true?dialog.FileName:null;}
    private List<(string Name,byte[] Png)> ReportImages()
    {return new(){(ScenarioMode.FlowTitle(result?.Source.Scenario),OverviewChart.Png()),("Анализ выбранного створа",AnalysisChart.Png()),("Морфоствор",ResultProfile.Png())};}
    private void Word_Click(object s,RoutedEventArgs e){if(!NeedResult())return;var path=Output("Документ Word|*.docx","");if(path!=null)ExportAction(()=>ExportService.Word(result!,path,ReportImages()),path);}
    private void Html_Click(object s,RoutedEventArgs e){if(!NeedResult())return;var path=Output("Автономный HTML|*.html","");if(path!=null)ExportAction(()=>ExportService.Html(result!,path,ReportImages()),path);}
    private void Csv_Click(object s,RoutedEventArgs e){if(!NeedResult())return;var path=Output("Таблицы CSV для Excel|*.csv","");if(path!=null)ExportAction(()=>ExportService.Csv(result!,path),path);}
    private void ResultJson_Click(object s,RoutedEventArgs e){if(!NeedResult())return;var path=Output("Полные результаты JSON|*.json","_результаты");if(path!=null)ExportAction(()=>File.WriteAllText(path,JsonSerializer.Serialize(result,ProjectData.Json)),path);}
    private void SaveChart_Click(object s,RoutedEventArgs e){if(!NeedResult())return;var path=Output("Изображение PNG|*.png","_график");if(path!=null)ExportAction(()=>File.WriteAllBytes(path,AnalysisChart.Png(1600,650)),path);}
    private void ExportAction(Action action,string path){try{action();Status.Text="Сохранено: "+path;Notify("Файл сохранён:\n"+path);}catch(Exception ex){Notify("Не удалось сохранить файл: "+ex.Message);}}
    private void Notify(string text)=>MessageBox.Show(this,text,"RiverGuard",MessageBoxButton.OK,MessageBoxImage.Information);
}

// Команды клавиатуры
public sealed class ActionCommand(Action action) : ICommand
{
    public event EventHandler? CanExecuteChanged {add{}remove{}}
    public bool CanExecute(object? parameter)=>true;
    public void Execute(object? parameter)=>action();
}
