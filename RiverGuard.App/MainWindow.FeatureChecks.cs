using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using S=DocumentFormat.OpenXml.Spreadsheet;
using RiverGuard.Core;

namespace RiverGuard;

public partial class MainWindow
{
    // Проверки удаления и карточек
    private async Task VerifyDeletionAsync(string directory)
    {
        var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new InvalidDataException(name);checks.Add("PASS "+name);}
        string other=ProjectStorage.Create(Examples.Valley());
        string target=ProjectStorage.Create(Examples.Valley());LoadProject(target);ShowHome();await Task.Delay(100);UpdateLayout();
        var entry=library.Single(p=>p.Path==target);
        var card=Descendants<Grid>(ProjectLibraryList).First(g=>g.Name=="ProjectCard"&&g.DataContext is ProjectEntry p&&p.Path==target);
        var delete=Descendants<Button>(card).Single(b=>b.Name=="DeleteProjectButton");
        Check(delete.Visibility==Visibility.Hidden||delete.IsMouseOver,"Delete button is hidden on an unfocused card");
        Descendants<Button>(card).First(b=>b.Name!="DeleteProjectButton").Focus();await Task.Delay(100);
        Check(delete.Visibility==Visibility.Visible,"Delete button becomes available when the card receives keyboard focus");
        Screenshot(Path.Combine(directory,"project-delete.png"));
        DeleteLibraryEntry(entry);
        Check(!File.Exists(target)&&!ProjectStorage.Library().Any(p=>p.Path==target)&&!ProjectStorage.Recent().Contains(target),"Deletion removes the project file and both library indexes");
        Check(File.Exists(other),"Deleting one project preserves other project files");
        Check(!hasActiveProject&&projectPath==null&&result==null,"Deleting the active project closes its workspace");
        var closing=new System.ComponentModel.CancelEventArgs();closingTest=false;try{Window_Closing(null,closing);}finally{closingTest=true;}
        Check(!File.Exists(target),"Closing after deletion does not recreate the deleted project");
        var missing=library.First(p=>!File.Exists(p.Path));DeleteLibraryEntry(missing);
        Check(!ProjectStorage.Library().Any(p=>p.Path==missing.Path),"Missing-file entries can be removed from the library");
        string corrupt=Path.Combine(ProjectStorage.Folder,"delete-corrupt.riverguard");File.WriteAllText(corrupt,"invalid");ProjectStorage.Remember(corrupt);
        DeleteLibraryEntry(ProjectEntry.Read(corrupt));Check(!File.Exists(corrupt),"Corrupt project files can be deleted independently of opening them");
        File.WriteAllLines(Path.Combine(directory,"deletion-checks.txt"),checks);
    }

    // Проверки новых отчётных форматов
    private void VerifyExtendedExports(string directory)
    {
        var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new InvalidDataException(name);checks.Add("PASS "+name);}
        foreach(string format in new[]{"xlsx","ods","txt","md","rtf","xml","zip"})
        {
            string path=Path.Combine(directory,"extended-report."+format);WriteExtendedReport(format,path);WriteExtendedReport(format,path);
            Check(new FileInfo(path).Length>0,"Export and overwrite "+format);
        }
        using(var doc=SpreadsheetDocument.Open(Path.Combine(directory,"extended-report.xlsx"),false))
        {
            var issues=new OpenXmlValidator().Validate(doc).ToList();
            Check(issues.Count==0,"XLSX OpenXML schema: "+string.Join("; ",issues.Select(i=>i.Description)));
            var workbook=doc.WorkbookPart??throw new InvalidDataException("Missing workbook");
            Check(workbook.Workbook!.Descendants<S.Sheet>().Count()>=12,"XLSX contains separate report worksheets");
            Check(workbook.WorksheetParts.SelectMany(p=>p.Worksheet!.Descendants<S.Cell>()).Any(c=>c.DataType?.Value==S.CellValues.Number),"XLSX exports true numeric cells");
            Check(!workbook.WorksheetParts.SelectMany(p=>p.Worksheet!.Descendants<S.CellFormula>()).Any(),"XLSX user text never becomes an executable formula");
        }
        using(var zip=ZipFile.OpenRead(Path.Combine(directory,"extended-report.ods")))
        {
            using var reader=new StreamReader(zip.GetEntry("mimetype")!.Open());Check(reader.ReadToEnd()=="application/vnd.oasis.opendocument.spreadsheet","ODS media type");
            using var content=zip.GetEntry("content.xml")!.Open();var xml=XDocument.Load(content);XNamespace table="urn:oasis:names:tc:opendocument:xmlns:table:1.0";
            Check(xml.Descendants(table+"table").Count()>=12,"ODS contains all report worksheets");
            using var manifest=zip.GetEntry("META-INF/manifest.xml")!.Open();XDocument.Load(manifest);
        }
        var report=XDocument.Load(Path.Combine(directory,"extended-report.xml"));Check(report.Root?.Name=="RiverGuardReport","XML is well formed");
        Check(File.ReadAllText(Path.Combine(directory,"extended-report.rtf")).StartsWith(@"{\rtf1"),"RTF has a real RTF header");
        Check(File.ReadAllText(Path.Combine(directory,"extended-report.md")).Contains("## Результаты"),"Markdown contains result tables");
        using(var zip=ZipFile.OpenRead(Path.Combine(directory,"extended-report.zip")))
            Check(new[]{"report.xlsx","report.ods","report.docx","report.html","report.csv","results.json","report.xml","report.txt","report.md","report.rtf","chart-1.png"}.All(p=>zip.GetEntry(p)!=null),"ZIP contains all report formats and charts");
        File.WriteAllLines(Path.Combine(directory,"export-checks.txt"),checks);
    }
}
