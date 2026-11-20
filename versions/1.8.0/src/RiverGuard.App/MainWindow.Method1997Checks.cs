using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using RiverGuard.Core;

namespace RiverGuard;

public partial class MainWindow
{
    private async Task VerifyMethod1997Async(string directory)
    {
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new InvalidDataException(text);checks.Add("PASS "+text);}
        var p=Method1997Examples.Control();string path=ProjectStorage.Create(p);SetProject(p,path);Navigation.SelectedIndex=1;UpdateLayout();await Task.Delay(350);
        Check(Method1997Panel.Visibility==Visibility.Visible&&CalculationModeBox.SelectedValue?.ToString()==CalculationModes.Method1997,"Method1997 separate input panel and selected mode");
        Check(RgComputedVolume.Visibility==Visibility.Collapsed,"Method1997 never displays RG linear volume as methodological volume");
        Check(!OpeningWidthFields.IsEnabled&&!BreachFractionFields.IsEnabled&&ModelBadge.Text.Contains("1997"),"Method1997 disables incompatible length fraction and identifies active model");
        Check(SectionsGrid.Columns.OfType<DataGridBoundColumn>().Any(c=>(c.Binding as System.Windows.Data.Binding)?.Path.Path==nameof(RiverSection.MethodCoefficientSource)),"Method1997 coefficient provenance editable in section grid");
        Screenshot(Path.Combine(directory,"method1997-input.png"));await RunAsync();var output=result??throw new InvalidDataException("No method result");
        Check(output.ModelVersion==Method1997Solver.Version&&!output.HasMassBalance&&vm.BalanceMetric=="н/п","Method1997 UI does not claim zero mass error");
        Check(vm.Summary.Contains("не рассчитываются")&&!Status.Text.Contains("невязка 0"),"Method1997 overview discloses missing dynamic diagnostics");
        Check(!PlayButton.IsEnabled&&!TimeSlider.IsEnabled&&ChartTypeBox.SelectedIndex==3,"Method1997 defaults to event times and disables playback");
        ResultSectionBox.ItemsSource=new[]{new SectionResult{Name="Предыдущий результат"}};ResultSectionBox.SelectedIndex=0;UpdateAnalysis();
        Check(AnalysisChart.Lines.Count>0,"Method1997 tolerates stale section selection during result replacement");ResultSectionBox.ItemsSource=output.Sections;ResultSectionBox.SelectedIndex=0;
        Check(output.Sections.All(s=>s.DepthHistory.Count==0)&&output.Isolines.Count==0,"Method1997 UI does not synthesize time histories");
        ChartTypeBox.SelectedIndex=0;UpdateAnalysis();Check(AnalysisChart.EmptyMessage.Contains("не вычисляет")&&AnalysisChart.Lines.All(l=>l.Points.Count==0),"Method1997 unsupported chart explains absence");
        ChartTypeBox.SelectedIndex=6;UpdateAnalysis();Navigation.SelectedIndex=3;UpdateLayout();await Task.Delay(350);Screenshot(Path.Combine(directory,"method1997-plan.png"));
        Check(ZonePlan.Visibility==Visibility.Visible&&output.FloodArea>=0,"Method1997 geometric postprocessing plan remains available");
        Check(p.CalculationHistory.Count==1&&ProjectStorage.Load(path).CalculationMode==CalculationModes.Method1997&&p.CalculationHistory[0].BalanceErrorPercent==null,"Method1997 project history persists mode without zero balance");
        var images=ReportImages();Check(images.Count==2&&images.All(i=>!i.Name.Contains("Гидрограф")),"Method1997 report images contain only event times and schematic");
        string word=Path.Combine(directory,"method1997-report.docx");ExportService.Word(output,word,images);
        File.WriteAllText(Path.Combine(directory,"method1997-results.json"),JsonSerializer.Serialize(output,ProjectData.Json));
        using(var document=WordprocessingDocument.Open(word,false))
        {
            Check(!new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document).Any(),"Method1997 DOCX validates");
            string text=document.MainDocumentPart!.Document!.InnerText;
            Check(text.Contains("9. Источники")&&text.Contains(SourceCatalog.ScanSha256)&&text.Contains("Nр вводится вручную"),"Method1997 DOCX carries bibliography scan identity and unresolved coefficient table");
            Check(text.Contains("доля площади")&&!text.Contains("Eр · доля длины"),"Method1997 report never relabels length fraction as area fraction");
            Check(text.Contains("Баланс массы, сетка и временные ряды не вычисляются")&&text.Contains("формула (51)"),"Method1997 report distinguishes unavailable balance from method width");
        }
        string book=Path.Combine(directory,"method1997-report.xlsx");ExportService.Excel(output,book);
        using(var document=SpreadsheetDocument.Open(book,false))Check(!new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document).Any(),"Method1997 XLSX validates with sources and coefficient tables");
        string html=Path.Combine(directory,"method1997-report.html");ExportService.Html(output,html,images);Check(File.ReadAllText(html).Contains(SourceCatalog.ScanSha256),"Method1997 HTML contains traceable sources");
        string textPath=Path.Combine(directory,"method1997-report.txt");ExportService.TextReport(output,textPath,"txt");Check(File.ReadAllText(textPath).Contains("Параметры методики")&&File.ReadAllText(textPath).Contains("Источники"),"Method1997 text report contains extra parameters and citations");
        string csv=Path.Combine(directory,"method1997-report.csv");ExportService.Csv(output,csv);Check(File.ReadAllText(csv).Contains("ИСТОЧНИКИ")&&!File.ReadAllText(csv).Contains("Невязка массы: 0,000%"),"Method1997 CSV never exposes fabricated balance");
        Navigation.SelectedIndex=8;UpdateLayout();await Task.Delay(350);
        Check(SourcesPage.Visibility==Visibility.Visible&&Navigation.Items.Count==9,"Sources is a standalone application tab");
        Check(Descendants<TextBlock>(SourcesPage).Any(t=>t.Text.Contains("не является сертификатом")),"Sources tab explicitly distinguishes citations from certification");
        Check(Descendants<Button>(SourcesPage).Count(b=>b.Content?.ToString()=="Открыть источник ↗")==SourceCatalog.Entries.Count(s=>s.Url.Length>0),"Sources links are opt-in buttons only");
        Screenshot(Path.Combine(directory,"sources.png"));
        p.Sections[0].MethodConnection=1e-8;bool failed=false;try{await RunAsync();}catch(InvalidOperationException){failed=true;}finally{p.Sections[0].MethodConnection=1;}
        Check(failed&&ReferenceEquals(result,output)&&p.CalculationHistory.Last().Status=="Ошибка"&&ProjectStorage.Load(path).CalculationHistory.Last().Status=="Ошибка","Method1997 runtime failure preserves prior results and persists error record");
        string json=JsonSerializer.Serialize(p,ProjectData.Json);var restored=JsonSerializer.Deserialize<ProjectData>(json,ProjectData.Json)!;
        Check(restored.Method1997.AreaFraction==.5&&restored.Sections[0].MethodConnection==1&&restored.Sections[0].MethodCoefficientSource.Length>0,"Method1997 serialized project retains all explicit coefficients");
        restored.CalculationMode=CalculationModes.Rg1D;SetProject(restored);Check(Method1997Panel.Visibility==Visibility.Collapsed,"Returning to RG1D hides methodological inputs without deleting them");
        File.WriteAllLines(Path.Combine(directory,"method1997-checks.txt"),checks);
    }
}
