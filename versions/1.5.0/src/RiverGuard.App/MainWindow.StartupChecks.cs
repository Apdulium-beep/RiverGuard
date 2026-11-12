using System.IO;
using System.Windows;
using System.Windows.Controls;
using RiverGuard.Core;

namespace RiverGuard;

public partial class MainWindow
{
    // Проверки создания и открытия проектов
    private async Task VerifyStartupFlowAsync(string directory)
    {
        var checks=new List<string>();
        void Check(bool condition,string name){if(!condition)throw new InvalidDataException(name);checks.Add("PASS "+name);}
        Check(!hasActiveProject&&StartScreen.Visibility==Visibility.Visible,"Startup opens project hub, not a demonstration project");
        Check(!Pages.IsEnabled&&ImportProjectButton.IsEnabled&&CreateNewProjectButton.IsEnabled,"Workspace is inactive until a project is opened");
        var close=new System.ComponentModel.CancelEventArgs();closingTest=false;try{Window_Closing(null,close);}finally{closingTest=true;}
        Check(!close.Cancel&&!File.Exists(Path.Combine(ProjectStorage.Folder,"last-session.riverguard")),"Closing the initial hub does not create or overwrite a recovery session");
        await Task.Delay(350);Screenshot(Path.Combine(directory,"startup-empty.png"));
        BeginNewProject();Check(CreateProjectPanel.Visibility==Visibility.Visible,"New project opens scenario selection");
        NewProjectName.Text="Пустой проран";UseDemoGeometry.IsChecked=false;
        SetNewScenario(ScenarioMode.Breach);
        await Task.Delay(100);Screenshot(Path.Combine(directory,"create-breach.png"));
        FinishCreate_Click(this,new());
        string first=projectPath??throw new InvalidDataException("New project was not assigned a file");
        Check(File.Exists(first)&&vm.Project.Sections.Count==0,"Project without demonstration geometry is saved with empty sections");
        Check(hasActiveProject&&StartScreen.Visibility==Visibility.Collapsed&&FormationFields.Visibility==Visibility.Visible,"Breach workspace displays formation inputs");
        Check(EditorProfile.Lines.Count==0,"Empty geometry does not display a previous profile");
        ShowHome();var firstEntry=library.Single(p=>p.Path==first);
        Check(firstEntry.IsAvailable&&firstEntry.Name=="Пустой проран","Created project is listed with metadata");
        BeginNewProject();NewProjectName.Text="Отменённое создание";WizardBack_Click(this,new());
        Check(projectPath==first&&vm.Project.Name=="Пустой проран","Leaving the wizard preserves the active project");
        LibraryProject_Click(new Button{Tag=firstEntry},new());
        Check(projectPath==first&&StartScreen.Visibility==Visibility.Collapsed,"Single project-card click opens the project");
        ShowHome();BeginNewProject();NewProjectName.Text="Перелив — проверка";SetNewScenario(ScenarioMode.Overflow);
        await Task.Delay(100);Screenshot(Path.Combine(directory,"create-overflow.png"));
        FinishCreate_Click(this,new());
        string second=projectPath??throw new InvalidDataException("Overflow project was not saved");
        Check(second!=first&&File.Exists(first),"Creating another project does not overwrite existing files");
        Check(vm.Project.Scenario==ScenarioMode.Overflow&&FormationFields.Visibility==Visibility.Collapsed,"Overflow hides inapplicable breach-formation fields");
        Check(OpeningWidthLabel.Text.Contains("гребня")&&SillLabel.Text.Contains("гребня"),"Overflow uses crest-length and crest-elevation labels");
        Check(!Descendants<TextBox>(ScenarioPage).Any(t=>t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path=="Project.Name")&&!Descendants<ComboBox>(ScenarioPage).Any(),"Scenario form has no project-name editor or mechanism selector");
        vm.Project.DurationHours=0.03;vm.Project.Cells=40;vm.Project.FormationMinutes=60;
        Check(SaveProject(),"Changes are saved into the created project without another file dialog");
        await RunAsync();
        Check(result!=null&&result.Source.Scenario==ScenarioMode.Overflow&&result.ReleasedVolume>0,"Created overflow project calculates successfully");
        var overflowResult=result!;
        string overflowReport=Path.Combine(directory,"overflow-report.html");ExportService.Html(overflowResult,overflowReport,ReportImages());
        string html=File.ReadAllText(overflowReport);
        Check(!html.Contains("Формирование, мин")&&html.Contains("Длина гребня"),"Overflow report excludes breach-formation parameter");
        Navigation.SelectedIndex=1;await Task.Delay(350);Screenshot(Path.Combine(directory,"overflow-workspace.png"));
        string import=Path.Combine(ProjectStorage.Folder,"external.riverguard");
        var imported=Examples.Valley();imported.Name="Импортированный проект";ProjectStorage.Save(imported,import);LoadProject(import);
        Check(projectPath==import&&vm.Project.Name==imported.Name,"Import loads existing project without replacing its data");
        Check(ProjectStorage.Library().Any(p=>p.Path==import),"Imported project is remembered in the library");
        RenameProject_Click(this,new());RenameText.Text="Отменённое переименование";CancelRename_Click(this,new());
        Check(vm.Project.Name==imported.Name,"Cancelling project rename preserves the name");
        RenameProject_Click(this,new());RenameText.Text="   ";ApplyRename_Click(this,new());
        Check(RenameOverlay.Visibility==Visibility.Visible&&RenameError.Visibility==Visibility.Visible,"Blank project name is rejected by the rename dialog");
        RenameText.Text="  Переименованный импорт  ";await Task.Delay(100);Screenshot(Path.Combine(directory,"rename-project.png"));ApplyRename_Click(this,new());
        Check(vm.Project.Name=="Переименованный импорт"&&vm.Project.Scenario==imported.Scenario,"Rename changes metadata without changing the mechanism");
        Check(SaveProject()&&ProjectStorage.Load(import).Name==vm.Project.Name,"Renamed project is persisted in its existing file");
        ProjectSearch.Text="Пустой проран";ShowHome();
        Check(ProjectLibraryList.Items.Count==1,"Project search filters names and paths");ProjectSearch.Text="";
        string missing=Path.Combine(ProjectStorage.Folder,"missing.riverguard");ProjectStorage.Remember(missing);
        string corrupt=Path.Combine(ProjectStorage.Folder,"corrupt.riverguard");File.WriteAllText(corrupt,"broken JSON");ProjectStorage.Remember(corrupt);
        RefreshLibrary();Check(library.Where(p=>p.Path==missing||p.Path==corrupt).All(p=>!p.IsAvailable),"Missing and malformed projects are shown as unavailable");
        var reopened=ProjectStorage.Load(second);
        Check(reopened.Scenario==ScenarioMode.Overflow&&reopened.FormationMinutes==60,"Saved project round trip preserves the selected scenario and stored data");
        string duplicate=ProjectStorage.Create(reopened);
        Check(duplicate!=second&&File.Exists(second),"Same-name projects receive independent file paths");
        string recovery=Path.Combine(ProjectStorage.Folder,"last-session.riverguard");ProjectStorage.Save(reopened,recovery);RefreshLibrary();
        var recoveryEntry=library.Single(p=>p.IsRecovery);
        LibraryProject_Click(new Button{Tag=recoveryEntry},new());
        Check(projectPath==null&&savedState=="","Recovery is opened as unsaved data, not as the recovery-file destination");
        Check(File.Exists(recovery),"Opening recovery does not overwrite the recovery file");
        ShowHome();await Task.Delay(300);Screenshot(Path.Combine(directory,"startup-projects.png"));
        File.WriteAllLines(Path.Combine(directory,"startup-checks.txt"),checks);
    }
}
