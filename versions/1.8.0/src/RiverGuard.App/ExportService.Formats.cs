using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S=DocumentFormat.OpenXml.Spreadsheet;
using RiverGuard.Core;

namespace RiverGuard;

public static partial class ExportService
{
    // Единые таблицы отчёта
    private sealed record ReportTable(string Name,string[] Headers,List<object?[]> Rows);
    private static List<ReportTable> Tables(SimulationResult r)
    {
        object?[] Row(params object?[] cells)=>cells;
        var tables=new List<ReportTable>{
            new("О проекте",new[]{"Поле","Значение"},new(){Row("Проект",r.Source.Name),Row("Автор приложения","Apdulium-beep"),Row("Дата",r.Created.ToString("O")),Row("Модель",r.ModelVersion),Row("Примечания",r.Source.Notes),Row("Ограничение","Исследовательская модель. Не является основанием для реальной эвакуации.")}),
            new("Исходные данные",new[]{"Параметр","Значение"},ParameterCells(r.Source).Select(row=>Row(row.Label,row.Value)).ToList()),
            new("Геометрия",new[]{"Створ","L, м","Дно, м","Hб, м","Левый, м","Правый, м","Vб, м/с","n","K"},r.Source.Sections.OrderBy(s=>s.Distance).Select(s=>Row(s.Name,s.Distance,s.Bed,s.NormalDepth,s.LeftWidth,s.RightWidth,s.InitialVelocity,s.Manning,s.DrainageFactor)).ToList()),
            new("Горизонтали",new[]{"Створ","Берег","От оси, м","Отметка, м"},r.Source.Sections.SelectMany(s=>s.LeftBank.Select(p=>Row(s.Name,"Левый",p.Distance,p.Elevation)).Concat(s.RightBank.Select(p=>Row(s.Name,"Правый",p.Distance,p.Elevation)))).ToList()),
            new("Результаты",ResultHeaders(r),r.Sections.Select(s=>Row(s.Name,s.Distance,s.MaxLevel,s.MaxDepth,s.WaveHeight,s.MaxDischarge,s.MaxVelocity,s.ArrivalHours,s.ArrivalHours.HasValue?(double?)s.PeakHours:null,s.RecessionHours)).ToList()),
            new("Зоны",ZoneHeaders,r.Sections.Select(s=>Row(s.Name,s.FloodLeft,s.FloodRight,s.DangerLeft,s.DangerRight,s.DestructionLeft,s.DestructionRight,s.Evacuation)).ToList()),
            new("Изодаты",new[]{"Створ","Берег","h, м","R, м","v, м/с","Осушение, ч"},r.Isolines.Select(s=>Row(s.Section,s.Bank,s.Depth,s.Distance,s.Velocity,s.DryingHours)).ToList()),
            new("Гидрограф",new[]{"Время, ч","Расход, м³/с"},r.BreachFlow.Select(p=>Row(p.X,p.Y)).ToList()),
            new("Уровень водохранилища",new[]{"Время, ч","Отметка, м"},r.ReservoirLevel.Select(p=>Row(p.X,p.Y)).ToList()),
            new("Истории створов",new[]{"Створ","Величина","Время, ч","Значение"},r.Sections.SelectMany(s=>new[]{("Глубина, м",s.DepthHistory),("Безаварийная глубина, м",s.BaselineDepthHistory),("Скорость, м/с",s.VelocityHistory),("Расход, м³/с",s.FlowHistory)}.SelectMany(series=>series.Item2.Select(point=>Row(s.Name,series.Item1,point.X,point.Y)))).ToList()),
            new("Диагностика",new[]{"Показатель","Значение"},new(){Row("Невязка массы, %",r.BalanceErrorPercent),Row("Выпущено, м³",r.ReleasedVolume),Row("Начальный объём реки, м³",r.InitialRiverVolume),Row("Конечный объём реки, м³",r.FinalRiverVolume),Row("Выход с нижней границы, м³",r.OutflowVolume),Row("Бытовой приток, м³/с",r.BaseDischarge),Row("Объём бытового притока, м³",r.BaseInflowVolume),Row("Установление режима, ч",r.InitializationHours),Row("Остаток установления, %",r.InitializationResidualPercent),Row("Площадь затопления, м²",r.FloodArea),Row("Опасная площадь, м²",r.DangerArea),Row("Шагов",r.Steps),Row("Вычисление, с",r.ElapsedSeconds)}),
            new("Предупреждения",new[]{"Сообщение"},r.Warnings.Select(w=>Row(w)).ToList()),
            new("Метод",new[]{"Описание"},new(){Row(MethodFor(r.Source)),Row(r.Source.Zones.Description),Row("Не определено означает отсутствие события в расчётном интервале, а не нулевое время.")})
        };
        tables.Add(new("Источники",new[]{"Название","Библиография","Применение","Ограничения","Ссылка"},SourceCatalog.Entries.Select(s=>Row(s.Title,s.Citation,s.Application,s.Limitations,s.Url)).ToList()));
        if(!r.HasMassBalance)
        {
            tables.RemoveAll(t=>t.Name is "Диагностика" or "Гидрограф" or "Уровень водохранилища" or "Истории створов" or "Изодаты");
            tables.Add(new("Диагностика",new[]{"Показатель","Значение"},new(){Row("Невязка массы, %",null),Row("Методическая диагностика",ReportDiagnostics(r))}));
            tables.Add(new("Параметры методики",new[]{"Створ","B (51), м","Vг (52), м/с","Cф, м/с","K","Nр","Источник K и Nр"},r.Sections.Select(s=>{var input=r.Source.Sections.Single(i=>i.Name==s.Name);return Row(s.Name,s.MethodFloodWidth,s.MethodCrestVelocity,s.MethodFrontSpeed,input.MethodShape,input.MethodConnection,input.MethodCoefficientSource);}).ToList()));
        }
        return tables;
    }
    private static bool Numeric(object? v)=>v is double or int or float or long or decimal;
    private static string Value(object? v)=>v==null?"не определено":Convert.ToString(v,CultureInfo.InvariantCulture)??"";
    private static string DisplayValue(object? v)=>v is double d?F(d):v is float f?F(f):v is decimal m?m.ToString("F3",CultureInfo.GetCultureInfo("ru-RU")):Value(v);

    // Книга Excel
    public static void Excel(SimulationResult r,string path)
    {
        using var doc=SpreadsheetDocument.Create(path,SpreadsheetDocumentType.Workbook);
        var book=doc.AddWorkbookPart();book.Workbook=new S.Workbook();var sheets=book.Workbook.AppendChild(new S.Sheets());uint id=1;
        var styles=book.AddNewPart<WorkbookStylesPart>();
        var regularFont=new S.Font();regularFont.AddChild(new S.FontSize{Val=11},true);regularFont.AddChild(new S.FontName{Val="Calibri"},true);
        var headingFont=new S.Font();headingFont.AddChild(new S.Bold(),true);headingFont.AddChild(new S.Color{Rgb="FFFFFFFF"},true);headingFont.AddChild(new S.FontSize{Val=11},true);headingFont.AddChild(new S.FontName{Val="Calibri"},true);
        styles.Stylesheet=new S.Stylesheet(
            new S.NumberingFormats(new S.NumberingFormat{NumberFormatId=164U,FormatCode="0.000"}),
            new S.Fonts(regularFont,headingFont),
            new S.Fills(new S.Fill(new S.PatternFill{PatternType=S.PatternValues.None}),new S.Fill(new S.PatternFill{PatternType=S.PatternValues.Gray125}),new S.Fill(new S.PatternFill(new S.ForegroundColor{Rgb="FF176E75"},new S.BackgroundColor{Indexed=64U}){PatternType=S.PatternValues.Solid})),
            new S.Borders(new S.Border()),new S.CellStyleFormats(new S.CellFormat()),
            new S.CellFormats(new S.CellFormat(),new S.CellFormat(new S.Alignment{WrapText=true}){FontId=1U,FillId=2U,ApplyFont=true,ApplyFill=true},new S.CellFormat{NumberFormatId=164U,ApplyNumberFormat=true}),
            new S.CellStyles(new S.CellStyle{Name="Normal",FormatId=0U,BuiltinId=0U}));
        styles.Stylesheet.Save();
        foreach(var table in Tables(r))
        {
            var part=book.AddNewPart<WorksheetPart>();var data=new S.SheetData();
            part.Worksheet=new S.Worksheet(new S.SheetViews(new S.SheetView(new S.Pane{VerticalSplit=1,TopLeftCell="A2",ActivePane=S.PaneValues.BottomLeft,State=S.PaneStateValues.Frozen}){WorkbookViewId=0U}),new S.Columns(Enumerable.Range(1,table.Headers.Length).Select(i=>new S.Column{Min=(uint)i,Max=(uint)i,Width=i==1?32:24,CustomWidth=true})),data);
            bool header=true;
            foreach(var cells in new[]{table.Headers.Cast<object?>().ToArray()}.Concat(table.Rows))
            {
                var row=new S.Row();foreach(var v in cells)
                {
                    var cell=new S.Cell{StyleIndex=header?1U:v is double or float or decimal?2U:0U};
                    if(Numeric(v)){cell.DataType=S.CellValues.Number;cell.CellValue=new S.CellValue(Value(v));}
                    else{cell.DataType=S.CellValues.InlineString;cell.InlineString=new S.InlineString(new S.Text(Value(v)){Space=SpaceProcessingModeValues.Preserve});}
                    row.Append(cell);
                }
                data.Append(row);header=false;
            }
            sheets.Append(new S.Sheet{Id=book.GetIdOfPart(part),SheetId=id++,Name=table.Name});part.Worksheet.Save();
        }
        book.Workbook.Save();
    }

    // Книга OpenDocument
    public static void Ods(SimulationResult r,string path)
    {
        XNamespace office="urn:oasis:names:tc:opendocument:xmlns:office:1.0",table="urn:oasis:names:tc:opendocument:xmlns:table:1.0",text="urn:oasis:names:tc:opendocument:xmlns:text:1.0",manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0",style="urn:oasis:names:tc:opendocument:xmlns:style:1.0",number="urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0";
        const string mime="application/vnd.oasis.opendocument.spreadsheet";
        var content=new XElement(office+"document-content",new XAttribute(XNamespace.Xmlns+"office",office),new XAttribute(XNamespace.Xmlns+"table",table),new XAttribute(XNamespace.Xmlns+"text",text),new XAttribute(XNamespace.Xmlns+"style",style),new XAttribute(XNamespace.Xmlns+"number",number),new XAttribute(office+"version","1.2"),
            new XElement(office+"automatic-styles",new XElement(number+"number-style",new XAttribute(style+"name","N3"),new XElement(number+"number",new XAttribute(number+"decimal-places",3),new XAttribute(number+"min-integer-digits",1))),new XElement(style+"style",new XAttribute(style+"name","Cell3"),new XAttribute(style+"family","table-cell"),new XAttribute(style+"data-style-name","N3"))),
            new XElement(office+"body",new XElement(office+"spreadsheet",Tables(r).Select(t=>new XElement(table+"table",new XAttribute(table+"name",t.Name),new[]{t.Headers.Cast<object?>().ToArray()}.Concat(t.Rows).Select(row=>new XElement(table+"table-row",row.Select(v=>new XElement(table+"table-cell",new XAttribute(office+"value-type",Numeric(v)?"float":"string"),v is double or float or decimal?new XAttribute(table+"style-name","Cell3"):null,Numeric(v)?new XAttribute(office+"value",Value(v)):null,new XElement(text+"p",DisplayValue(v)))))))))));
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        void Entry(string name,string value,CompressionLevel compression=CompressionLevel.Optimal){var e=zip.CreateEntry(name,compression);using var writer=new StreamWriter(e.Open(),new UTF8Encoding(false));writer.Write(value);}
        Entry("mimetype",mime,CompressionLevel.NoCompression);Entry("content.xml",new XDocument(content).ToString());
        Entry("META-INF/manifest.xml",new XDocument(new XElement(manifest+"manifest",new XAttribute(XNamespace.Xmlns+"manifest",manifest),new XAttribute(manifest+"version","1.2"),new XElement(manifest+"file-entry",new XAttribute(manifest+"full-path","/"),new XAttribute(manifest+"media-type",mime)),new XElement(manifest+"file-entry",new XAttribute(manifest+"full-path","content.xml"),new XAttribute(manifest+"media-type","text/xml")))).ToString());
    }

    // Текстовые и структурированные отчёты
    public static void TextReport(SimulationResult r,string path,string format)
    {
        string Escape(string s)=>System.Net.WebUtility.HtmlEncode(s).Replace("\\","\\\\").Replace("|","\\|").Replace("\r","").Replace("\n","<br>");
        var b=new StringBuilder();
        foreach(var t in Tables(r))
        {
            b.AppendLine(format=="md"?"## "+t.Name:t.Name);
            if(format=="md")
            {b.AppendLine("| "+string.Join(" | ",t.Headers.Select(Escape))+" |");b.AppendLine("| "+string.Join(" | ",t.Headers.Select(_=>"---"))+" |");}
            else b.AppendLine(string.Join("\t",t.Headers));
            foreach(var row in t.Rows)b.AppendLine(format=="md"?"| "+string.Join(" | ",row.Select(v=>Escape(DisplayValue(v))))+" |":string.Join("\t",row.Select(v=>DisplayValue(v).Replace("\t"," ").Replace("\r","").Replace("\n"," "))));
            b.AppendLine();
        }
        string output=b.ToString();
        if(format=="rtf")
        {
            var encoded=new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\uc1\f0\fs20 ");
            foreach(char c in output)
            {if(c=='\r')continue;if(c=='\n')encoded.Append(@"\par ");else if(c=='\t')encoded.Append(@"\tab ");else if(c is '\\' or '{' or '}')encoded.Append('\\').Append(c);else if(c>127)encoded.Append(@"\u").Append((short)c).Append('?');else encoded.Append(c);}
            output=encoded.Append('}').ToString();
        }
        File.WriteAllText(path,output,format=="rtf"?Encoding.ASCII:new UTF8Encoding(true));
    }
    public static void Xml(SimulationResult r,string path)
    {
        new XDocument(new XElement("RiverGuardReport",new XAttribute("version","1"),Tables(r).Select(t=>new XElement("Table",new XAttribute("name",t.Name),new XElement("Columns",t.Headers.Select(h=>new XElement("Column",h))),t.Rows.Select(row=>new XElement("Row",row.Select(v=>new XElement("Cell",new XAttribute("type",v==null?"undefined":Numeric(v)?"number":"text"),Value(v))))))))).Save(path);
    }

    // Комплект отчётов
    public static void Bundle(SimulationResult r,string path,List<(string Name,byte[] Png)> images)
    {
        images=WithPlan(r,images);
        string folder=Path.Combine(Path.GetTempPath(),"RiverGuard-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            Excel(r,Path.Combine(folder,"report.xlsx"));Ods(r,Path.Combine(folder,"report.ods"));Word(r,Path.Combine(folder,"report.docx"),images);Html(r,Path.Combine(folder,"report.html"),images);Csv(r,Path.Combine(folder,"report.csv"));Xml(r,Path.Combine(folder,"report.xml"));
            foreach(var format in new[]{"txt","md","rtf"})TextReport(r,Path.Combine(folder,"report."+format),format);
            File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(r,ProjectData.Json));
            for(int i=0;i<images.Count;i++)File.WriteAllBytes(Path.Combine(folder,$"chart-{i+1}.png"),images[i].Png);
            ZipFile.CreateFromDirectory(folder,path);
        }
        finally{Directory.Delete(folder,true);}
    }
}
