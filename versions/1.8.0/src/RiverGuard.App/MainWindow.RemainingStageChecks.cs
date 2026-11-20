using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RiverGuard.Core;
using RiverGuard.Controls;
using W=DocumentFormat.OpenXml.Wordprocessing;
using DW=DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace RiverGuard;

public partial class MainWindow
{
    // Сквозные проверки отчёта, схемы, истории, ввода и настроек
    private async Task VerifyRemainingStagesAsync(string directory)
    {
        var checks=new List<string>();void Check(bool condition,string name){if(!condition)throw new InvalidDataException(name);checks.Add("PASS "+name);}
        Check(LaunchGuideWindow.Guide.Contains("self-contained")&&LaunchGuideWindow.Guide.Contains("Ctrl+S")&&LaunchGuideWindow.Guide.Contains("не выполнены"),"Built-in launch guide documents prerequisites, shortcuts and unverified clean-machine coverage");
        Check(!preferences.AutomaticUpdates&&AutomaticUpdatesBox.IsChecked==false,"Automatic update checks are disabled by default");
        string prefFolder=Path.Combine(directory,"PreferencesFixture");
        Check(!ApplicationPreferences.Load(prefFolder).AutomaticUpdates,"Missing preferences never enable networking");
        var pref=new ApplicationPreferences{AutomaticUpdates=true};pref.Save(prefFolder);Check(ApplicationPreferences.Load(prefFolder).AutomaticUpdates,"Explicit automatic-update preference persists");
        File.WriteAllText(Path.Combine(prefFolder,"preferences.json"),"invalid");pref.AutomaticUpdates=false;pref.Save(prefFolder);
        Check(!ApplicationPreferences.Load(prefFolder).AutomaticUpdates&&Directory.EnumerateFiles(prefFolder,"preferences.json.backup-*").Any(),"Malformed preferences are backed up before replacement");
        int updateRequests=0;UpdateCheckOverride=token=>{updateRequests++;return Task.FromResult<Updates.ReleaseUpdate?>(null);};
        await CheckForUpdatesAsync(false);Check(updateRequests==0,"Disabled startup check makes zero backend requests");
        AutomaticUpdatesBox.IsChecked=true;AutomaticUpdates_Click(this,new());await CheckForUpdatesAsync(false);
        Check(updateRequests==1,"Opted-in automatic check reaches isolated backend");
        AutomaticUpdatesBox.IsChecked=false;AutomaticUpdates_Click(this,new());await CheckForUpdatesAsync(true);
        Check(updateRequests==2&&testNotice.Contains("актуальная")&&CheckUpdatesButton.IsEnabled,"Manual update check remains usable when automatic checks are off");UpdateCheckOverride=null;

        var p=Examples.Valley();p.ReservoirLevel=130;p.FormationMinutes=0;p.BreachWidth=100;p.Cells=40;p.DurationHours=.2;p.Executor="Исполнитель испытания";p.HydroUnitNumber="TEST-01";p.DischargeCoefficient=.3851234567;
        string path=ProjectStorage.Create(p);SetProject(p,path);Navigation.SelectedIndex=1;Activate();UpdateLayout();await Task.Delay(100);
        TextBox Field(string name)=>LogicalDescendants<TextBox>(ScenarioPage).Single(t=>BindingOperations.GetBinding(t,TextBox.TextProperty)?.Path.Path=="Project."+name);
        var level=Field(nameof(ProjectData.ReservoirLevel));var bed=Field(nameof(ProjectData.DamBed));
        Check(level.Text=="130,000"&&bed.Text=="100,000","Input numbers display exactly three decimal places");
        Check(Field(nameof(ProjectData.DischargeCoefficient)).Text=="0,385"&&Commit(false)&&vm.Project.DischargeCoefficient==.3851234567,"Committing formatted fields never rounds unchanged source values");
        level.Focus();await Task.Delay(80);Check(level.SelectionLength==level.Text.Length,"Keyboard focus selects complete field contents");
        var enter=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent};level.RaiseEvent(enter);await Task.Delay(80);
        Check(enter.Handled&&bed.IsKeyboardFocusWithin,"Enter routes to next input field");
        MoveInputFocus(bed,true);await Task.Delay(80);Check(level.IsKeyboardFocusWithin,"Backward input traversal returns to preceding field");
        Check(InputBindings.OfType<KeyBinding>().Any(k=>k.Key==Key.F5)&&InputBindings.OfType<KeyBinding>().Any(k=>k.Key==Key.S&&k.Modifiers==ModifierKeys.Control),"F5 and Ctrl+S bindings remain available");
        await RunAsync();var first=result??throw new InvalidDataException("Missing first stage two result");
        Check(vm.Project.CalculationHistory.Count==1&&ProjectStorage.Load(path).CalculationHistory.Count==1,"Completed run is immediately persisted in existing project history");
        Check(first.Source.CalculationHistory.Count==0&&vm.Project.CalculationHistory[0].Input.CalculationHistory.Count==0,"UI result and history snapshots omit recursive history");
        Check(NeedResult(),"History-only changes never make current numerical results stale");
        Check(vm.FlowMetric.Split(',').Last().Length==3&&vm.HeightMetric.Split(',').Last().Length==3&&vm.AreaMetric.Split(',').Last().Length==3,"Overview metrics use three decimal places");
        Check(first.FloodArea>0,"Plan fixture has real nonzero scenario flood extent");
        Navigation.SelectedIndex=3;ChartTypeBox.SelectedIndex=6;UpdateAnalysis();UpdateLayout();await Task.Delay(350);
        Check(ZonePlan.Visibility==Visibility.Visible&&AnalysisChart.Visibility==Visibility.Collapsed&&!PlayButton.IsEnabled,"Plan mode renders independently of the time chart");
        byte[] plan=ZonePlanView.Png(first);File.WriteAllBytes(Path.Combine(directory,"zone-plan.png"),plan);
        using(var memory=new MemoryStream(plan))
        {
            var bitmap=BitmapDecoder.Create(memory,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0];
            Check(bitmap.PixelWidth==1200&&bitmap.PixelHeight==900,"Plan PNG has expected printable dimensions");
        }
        Screenshot(Path.Combine(directory,"zone-plan-workspace.png"));
        var images=ReportImages();Check(images.Count==2+4*first.Sections.Count&&images.Count(i=>i.Name.StartsWith("Схема зон"))==1,"Report includes all section graphs and exactly one plan");
        string word=Path.Combine(directory,"stage23-report.docx");ExportService.Word(first,word,images);
        using(var document=DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(word,false))
        {
            Check(!new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document).Any(),"Portrait structured DOCX passes OpenXML validation");
            var body=document.MainDocumentPart?.Document?.Body??throw new InvalidDataException("DOCX body missing");string text=body.InnerText;var page=body.Descendants<W.PageSize>().Single();
            Check(page.Orient?.Value==W.PageOrientationValues.Portrait&&page.Width?.Value<page.Height?.Value,"DOCX uses portrait A4 layout");
            Check(text.Contains("Исполнитель: Исполнитель испытания")&&text.Contains("TEST-01")&&text.Contains("RiverGuard "+first.ApplicationVersion),"DOCX title identifies executor, hydro unit, date and application version");
            Check(new[]{"Wв","Hв","Sв","Bв","Hбо","Bбо","Vбо","Hр","Eр","P ·"}.All(text.Contains),"DOCX records all requested hydro-unit fields");
            Check(text.Contains("Изодаты по берегам")&&text.Contains("Средняя скорость спада")&&text.Contains("Площадь КР")&&text.Contains("не ГИС-карта"),"DOCX includes results, drying rates, isolines, zones and schematic disclaimer");
            Check(body.Descendants<W.Paragraph>().Count(q=>q.InnerText.StartsWith("Рисунок "))==images.Count&&!text.Contains("___"),"DOCX figure captions are sequential and have no placeholders");
            Check(body.Descendants<DW.Extent>().All(e=>e.Cx!.Value<=5900000&&e.Cy!.Value<=8000000),"DOCX images fit portrait content area");
            Check(body.Descendants<W.Table>().All(t=>t.Elements<W.TableRow>().All(row=>row.Elements<W.TableCell>().Count()<=6)),"DOCX tables remain narrow enough for portrait pages");
            Check(body.Elements<W.Paragraph>().First().InnerText=="РАСЧЁТ ВОЛНЫ ПРОРЫВА"&&text.Contains("Автор приложения RiverGuard: Apdulium-beep"),"Neutral report heading preserves author attribution in method section");
        }
        string html=Path.Combine(directory,"stage23-report.html");ExportService.Html(first,html,images);Check(File.ReadAllText(html).Contains("Рисунок "+images.Count),"HTML includes matching numbered figures and plan");
        string txt=Path.Combine(directory,"stage23-report.txt");ExportService.TextReport(first,txt,"txt");Check(File.ReadAllText(txt).Contains("0,385")&&!File.ReadAllText(txt).Contains("0.3851234567"),"Human-readable text displays three decimals without exposing rounded storage");
        string xlsx=Path.Combine(directory,"stage23-report.xlsx");ExportService.Excel(first,xlsx);
        using(var book=DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(xlsx,false))
        {
            Check(!new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(book).Any(),"Three-decimal XLSX still passes schema validation");
            var workbook=book.WorkbookPart??throw new InvalidDataException("Workbook missing");
            Check(workbook.WorkbookStylesPart?.Stylesheet?.NumberingFormats?.Elements<DocumentFormat.OpenXml.Spreadsheet.NumberingFormat>().Any(f=>f.FormatCode=="0.000")==true,"XLSX uses display precision rather than rounding numeric cells");
            Check(workbook.WorksheetParts.SelectMany(s=>s.Worksheet?.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>()??Enumerable.Empty<DocumentFormat.OpenXml.Spreadsheet.Cell>()).Any(c=>c.StyleIndex?.Value==2&&c.CellValue?.Text==p.DischargeCoefficient.ToString("R",CultureInfo.InvariantCulture)),"XLSX numeric storage preserves unrounded coefficient");
        }
        p.ReservoirLevel+=1;await RunAsync();var second=result??throw new InvalidDataException("Missing second result");
        Check(vm.Project.CalculationHistory.Count==2&&ProjectStorage.Load(path).CalculationHistory.Count==2,"Second run appends instead of replacing history");
        var history=new HistoryWindow(vm.Project){Owner=this};history.FirstRun.SelectedItem=vm.Project.CalculationHistory[0];history.SecondRun.SelectedItem=vm.Project.CalculationHistory[1];history.RefreshComparison();history.Show();await Task.Delay(120);
        Check(history.ComparisonGrid.Items.Cast<HistoryComparisonRow>().Any(r=>r.Quantity=="Уровень водохранилища, м"&&r.Difference=="1,000"),"History window compares two runs with three-decimal difference");
        Check(history.FirstRun.SelectionBoxItem.ToString()!.Contains("130,000")&&!history.FirstRun.SelectionBoxItem.ToString()!.Contains("CalculationRecord"),"History selector displays readable dated run labels");
        var historyBitmap=new RenderTargetBitmap((int)history.ActualWidth,(int)history.ActualHeight,96,96,PixelFormats.Pbgra32);historyBitmap.Render(history);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(historyBitmap));using(var stream=File.Create(Path.Combine(directory,"history-window.png")))encoder.Save(stream);
        history.TakeInputButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(history.ChosenInput!=null&&history.ChosenInput.CalculationHistory.Count==0&&history.ChosenInput.ReservoirLevel==130&&vm.Project.ReservoirLevel==131,"Taking history input creates independent new-calculation data without overwriting project");Activate();

        Navigation.SelectedIndex=2;vm.SelectedSection=p.Sections[0];UpdateLayout();
        var preciseSection=p.Sections[0];preciseSection.Manning=.0351234567;
        var manningColumn=SectionsGrid.Columns.OfType<DataGridBoundColumn>().Single(c=>(c.Binding as Binding)?.Path.Path==nameof(RiverSection.Manning));
        SectionsGrid.ScrollIntoView(preciseSection,manningColumn);SectionsGrid.CurrentCell=new DataGridCellInfo(preciseSection,manningColumn);SectionsGrid.Focus();SectionsGrid.UpdateLayout();
        if(manningColumn.GetCellContent(preciseSection)?.Parent is DataGridCell precisionCell)precisionCell.Focus();SectionsGrid.BeginEdit();
        Check(manningColumn.GetCellContent(preciseSection) is TextBox numericEditor&&numericEditor.Text=="0,0351234567","Table editor reveals unrounded value instead of formatted preview");
        Check(AdvanceGrid(SectionsGrid,false)&&preciseSection.Manning==.0351234567,"Committing numeric table editor preserves all input digits");
        SectionsGrid.CommitEdit(DataGridEditingUnit.Cell,true);SectionsGrid.CommitEdit(DataGridEditingUnit.Row,true);
        int rowCount=p.Sections.Count;var last=p.Sections.Last();SectionsGrid.ScrollIntoView(last,SectionsGrid.Columns.Last());SectionsGrid.CurrentCell=new DataGridCellInfo(last,SectionsGrid.Columns.Last());
        Check(AdvanceGrid(SectionsGrid,false)&&p.Sections.Count==rowCount+1&&SectionsGrid.CurrentCell.Column==SectionsGrid.Columns.First(),"Enter at final section cell creates a new row and returns to its first column");
        SectionsGrid.CommitEdit(DataGridEditingUnit.Cell,true);SectionsGrid.CommitEdit(DataGridEditingUnit.Row,true);p.Sections.RemoveAt(p.Sections.Count-1);vm.SelectedSection=p.Sections[0];UpdateLayout();
        var section=p.Sections[0];int points=section.LeftBank.Count;var lastPoint=section.LeftBank.Last();LeftGrid.ScrollIntoView(lastPoint,LeftGrid.Columns.Last());LeftGrid.CurrentCell=new DataGridCellInfo(lastPoint,LeftGrid.Columns.Last());
        Check(AdvanceGrid(LeftGrid,false)&&section.LeftBank.Count==points+1&&LeftGrid.CurrentCell.Column==LeftGrid.Columns.First(),"Enter at final bank cell creates a new bank row");
        Check(AdvanceGrid(LeftGrid,true)&&LeftGrid.CurrentCell.Item==lastPoint&&LeftGrid.CurrentCell.Column==LeftGrid.Columns.Last(),"Backward table traversal crosses row boundary without creating rows");
        LeftGrid.CommitEdit(DataGridEditingUnit.Cell,true);LeftGrid.CommitEdit(DataGridEditingUnit.Row,true);section.LeftBank.RemoveAt(section.LeftBank.Count-1);
        Check(SectionsGrid.Columns.OfType<PrecisionTextColumn>().Any()&&LeftGrid.Columns.All(c=>c is PrecisionTextColumn),"Numeric table editors preserve full precision while display uses three decimals");
        var previous=result;p.DurationHours=48;p.Cells=400;var cancelledRun=RunAsync();await Task.Delay(10);cancellation?.Cancel();await cancelledRun;
        Check(ReferenceEquals(result,previous)&&p.CalculationHistory.Last().Status=="Отменён"&&ProjectStorage.Load(path).CalculationHistory.Last().Status=="Отменён","Cancellation preserves previous results and persists cancelled-attempt record");
        p.DurationHours=.2;p.Cells=40;ChartTypeBox.SelectedIndex=0;
        File.WriteAllLines(Path.Combine(directory,"stage23-checks.txt"),checks);
    }
}
