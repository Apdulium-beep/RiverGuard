using System.IO;
using System.Windows;
using System.Windows.Controls;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using S=DocumentFormat.OpenXml.Spreadsheet;
using RiverGuard.Core;

namespace RiverGuard;

public partial class MainWindow
{
    // Проверки новых механизмов в рабочем процессе
    private async Task VerifyMechanismFlowAsync(string directory)
    {
        var checks=new List<string>();
        void Check(bool condition,string name){if(!condition)throw new InvalidDataException(name);checks.Add("PASS "+name);}
        SetProject(Examples.Valley());
        Check(new[]{BreachScenarioButton,OverflowScenarioButton,GateScenarioButton,CircularScenarioButton}.All(b=>b.IsEnabled),"Creation wizard offers four selectable mechanisms");
        foreach(var (button,mode,id) in new[]{(GateScenarioButton,ScenarioMode.Gate,"gate"),(CircularScenarioButton,ScenarioMode.Circular,"circular")})
        {
            BeginNewProject();NewProjectName.Text="Проверка · "+mode;ChooseScenario_Click(button,new());button.BringIntoView();await Task.Delay(150);
            Check(newProjectScenario==mode&&WizardScenarioTitle.Text==mode,"Scenario button selects "+id);
            Screenshot(Path.Combine(directory,"create-"+id+".png"));FinishCreate_Click(this,new());
            Check(vm.Project.Scenario==mode&&projectPath!=null&&File.Exists(projectPath),"Wizard creates and saves "+id);
            bool gate=ScenarioMode.IsGate(mode);
            Check(GateHeightFields.Visibility==(gate?Visibility.Visible:Visibility.Collapsed)&&CircularFields.Visibility==(gate?Visibility.Collapsed:Visibility.Visible),"Mechanism-specific geometry inputs: "+id);
            Check(FormationFields.Visibility==(gate?Visibility.Visible:Visibility.Collapsed)&&OpeningWidthFields.Visibility==(gate?Visibility.Visible:Visibility.Collapsed),"Inapplicable width and formation inputs are hidden: "+id);
            Check(vm.Project.DischargeCoefficient==0.62,"New orifice defaults use an orifice coefficient: "+id);
            vm.Project.DurationHours=0.03;vm.Project.Cells=40;if(gate)vm.Project.FormationMinutes=0.4;
            Check(SaveProject(),"Save new mechanism parameters: "+id);await RunAsync();
            Check(result!=null&&result.ReleasedVolume>0&&result.BalanceErrorPercent<0.001,"New mechanism calculates with mass conservation: "+id);
            Check(OverviewFlowTitle.Text==ScenarioMode.FlowTitle(mode),"Hydrograph uses mechanism-specific title: "+id);
            Navigation.SelectedIndex=1;await Task.Delay(250);Screenshot(Path.Combine(directory,id+"-workspace.png"));
            string html=Path.Combine(directory,id+"-report.html");ExportService.Html(result!,html,ReportImages());string text=File.ReadAllText(html);
            Check(text.Contains(gate?"Максимальная высота открытия":"Диаметр отверстия")&&!text.Contains("Формирование, мин"),"HTML contains correct mechanism parameters: "+id);
            if(!gate)Check(!text.Contains("Ширина прорана, м")&&!text.Contains("Время подъёма затвора, мин"),"Circular report excludes gate and breach inputs");
            foreach(string format in new[]{"xlsx","ods","xml","md","txt","rtf","zip"})WriteExtendedReport(format,Path.Combine(directory,id+"-report."+format));
            using(var book=SpreadsheetDocument.Open(Path.Combine(directory,id+"-report.xlsx"),false))
            {
                Check(!new OpenXmlValidator().Validate(book).Any(),"New mechanism XLSX schema valid: "+id);
                var labels=book.WorkbookPart!.WorksheetParts.SelectMany(p=>p.Worksheet!.Descendants<S.Text>()).Select(t=>t.Text).ToList();
                Check(labels.Contains(gate?"Максимальная высота открытия, м":"Диаметр отверстия, м")&&!labels.Contains("Формирование, мин"),"XLSX uses correct numeric parameter mapping: "+id);
            }
            string docx=Path.Combine(directory,id+"-report.docx");ExportService.Word(result!,docx,ReportImages());
            using(var word=WordprocessingDocument.Open(docx,false))Check(!new OpenXmlValidator().Validate(word).Any(),"New mechanism DOCX schema valid: "+id);
            string path=projectPath!;LoadProject(path);
            Check(vm.Project.Scenario==mode&&vm.Project.GateHeight==2&&vm.Project.OutletDiameter==2,"Reopening preserves new mechanism and dimensions: "+id);
        }
        File.WriteAllLines(Path.Combine(directory,"mechanism-checks.txt"),checks);
    }
}
