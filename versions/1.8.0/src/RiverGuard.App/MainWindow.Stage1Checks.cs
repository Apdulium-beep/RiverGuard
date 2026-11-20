using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RiverGuard.Core;

namespace RiverGuard;

// Проверки новых расчётных параметров в рабочем пространстве
public partial class MainWindow
{
    private async Task VerifyStage1FlowAsync(string directory)
    {
        var checks=new List<string>();
        void Check(bool condition,string name){if(!condition)throw new InvalidDataException(name);checks.Add("PASS "+name);}
        SetProject(Examples.ThreeSections());Navigation.SelectedIndex=1;UpdateLayout();
        Check(BreachFractionFields.Visibility==Visibility.Visible,"Breach exposes fraction input");
        TextBox Field(string name)=>Descendants<TextBox>(ScenarioPage).Single(t=>BindingOperations.GetBinding(t,TextBox.TextProperty)?.Path.Path=="Project."+name);
        void Enter(string name,string text)
        {
            var field=Field(name);field.SetCurrentValue(TextBox.TextProperty,text);field.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            if(System.Windows.Controls.Validation.GetHasError(field))throw new InvalidDataException("Invalid binding: "+name+"; language="+field.Language.IetfLanguageTag+"; "+string.Join(" | ",System.Windows.Controls.Validation.GetErrors(field).Select(e=>e.ErrorContent+" "+e.Exception)));
        }
        Enter(nameof(ProjectData.ReservoirReferenceLevel),"71,3");Enter(nameof(ProjectData.ReservoirLevel),"72,3");
        Check(vm.Project.ReservoirReferenceLevel==71.3&&Math.Abs(vm.Project.EffectiveReservoirVolume-13.92)<1e-10,"Russian nullable reference level updates effective volume");
        Enter(nameof(ProjectData.LowerBedElevation),"60,2");
        Check(vm.Project.LowerBedElevation==60.2&&vm.Project.DamBed==65.3,"Downstream bed edits independently of reservoir bed");
        vm.Project.UseDestructionFraction=true;Enter(nameof(ProjectData.DamLength),"120");Enter(nameof(ProjectData.DestructionFraction),"0,1");Enter(nameof(ProjectData.BreachWidth),"999");
        Check(vm.Project.EffectiveBreachWidth==12,"Fraction input overrides stored explicit width");
        Check(Descendants<TextBlock>(ScenarioPage).Any(t=>t.Text.Contains("13,920")),"Effective volume is displayed with three decimal places");
        await RunAsync();
        Check(result!=null&&result.Source.EffectiveBreachWidth==12&&Math.Abs(result.Source.EffectiveReservoirVolume-13.92)<1e-10,"UI calculation uses effective width and volume");
        Check(result!.BalanceErrorPercent<0.001&&result.BaseInflowVolume>0,"UI calculation balances domestic and accident inflows");
        Check(result.Sections.All(s=>!s.RecessionHours.HasValue||s.RecessionHours.Value>=s.PeakHours),"UI results never show recession before wave crest");
        string path=Path.Combine(directory,"stage1-input.riverguard");ProjectStorage.Save(vm.Project,path);var restored=ProjectStorage.Load(path);
        Check(restored.SchemaVersion==1&&restored.LowerBedElevation==60.2&&restored.ReservoirReferenceLevel==71.3&&restored.UseDestructionFraction&&restored.EffectiveBreachWidth==12,"Optional inputs persist without changing project schema");
        string html=Path.Combine(directory,"stage1-report.html");ExportService.Html(result,html,ReportImages());string text=File.ReadAllText(html);
        Check(text.Contains("13,920")&&text.Contains("12,000")&&text.Contains("60,200")&&text.Contains("Доля длины плотины"),"Report uses computed parameters and records fraction and downstream bed");
        Check(text.Contains("бытовой приток")&&text.Contains("установления")&&!text.Contains("стационарный разгон не выполнялся"),"Report describes new initialization and mass balance");
        string word=Path.Combine(directory,"stage1-report.docx");ExportService.Word(result,word,ReportImages());
        using(var document=DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(word,false))
            Check(!new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document).Any(),"Stage one DOCX remains schema valid");
        Enter(nameof(ProjectData.ReservoirReferenceLevel),"");
        Check(vm.Project.ReservoirReferenceLevel==null&&vm.Project.EffectiveReservoirVolume==vm.Project.ReservoirVolume,"Clearing nullable reference level restores independent volume");
        Enter(nameof(ProjectData.LowerBedElevation),"");Check(vm.Project.LowerBedElevation==null,"Clearing downstream bed restores legacy fallback");
        Enter(nameof(ProjectData.ReservoirReferenceLevel),"71.3");Check(vm.Project.ReservoirReferenceLevel==71.3,"Optional reference level accepts decimal point without grouping ambiguity");
        var invalid=Field(nameof(ProjectData.ReservoirReferenceLevel));invalid.SetCurrentValue(TextBox.TextProperty,"не число");invalid.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Check(System.Windows.Controls.Validation.GetHasError(invalid)&&vm.Project.ReservoirReferenceLevel==71.3,"Invalid optional number is marked and does not overwrite source");
        Enter(nameof(ProjectData.ReservoirReferenceLevel),"");
        foreach(string scenario in new[]{ScenarioMode.Gate,ScenarioMode.Circular,ScenarioMode.Overflow})
        {
            var p=Examples.Valley();p.Scenario=scenario;SetProject(p);AdaptScenario();
            Check(BreachFractionFields.Visibility==Visibility.Collapsed,"Fraction controls hidden for mechanism: "+scenario);
        }
        File.WriteAllLines(Path.Combine(directory,"stage1-checks.txt"),checks);
    }
}
