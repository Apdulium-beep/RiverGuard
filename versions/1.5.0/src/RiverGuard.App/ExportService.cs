using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using RiverGuard.Core;
using W=DocumentFormat.OpenXml.Wordprocessing;
using A=DocumentFormat.OpenXml.Drawing;
using DW=DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC=DocumentFormat.OpenXml.Drawing.Pictures;

namespace RiverGuard;

// Подготовка отчётных данных
public static partial class ExportService
{
    private static string F(double x)=>x.ToString("F3",CultureInfo.GetCultureInfo("ru-RU"));
    private static string F(double? x)=>x.HasValue?F(x.Value):"не определено";
    private static string[] Headers=>new[]{"Створ","L, м","Zmax, м","Hmax, м","Волна, м","Qmax, м³/с","Vmax, м/с","Фронт, ч","Гребень, ч","Спад, ч"};
    private static IEnumerable<string[]> Results(SimulationResult r)=>r.Sections.Select(s=>new[]{s.Name,F(s.Distance),F(s.MaxLevel),F(s.MaxDepth),F(s.WaveHeight),F(s.MaxDischarge),F(s.MaxVelocity),F(s.ArrivalHours),F(s.PeakHours),F(s.RecessionHours)});
    private static List<(string Label,object Value)> ParameterCells(ProjectData p)
    {
        bool overflow=ScenarioMode.IsOverflow(p.Scenario),gate=ScenarioMode.IsGate(p.Scenario),circle=ScenarioMode.IsCircular(p.Scenario);
        var rows=new List<(string,object)>{("Сценарий",p.Scenario),("Уровень водохранилища, м",p.ReservoirLevel),("Дно у плотины, м",p.DamBed),("Объём, млн м³",p.ReservoirVolume),("Площадь зеркала, км²",p.ReservoirArea)};
        rows.Add(circle?("Диаметр отверстия, м",p.OutletDiameter):(overflow?"Длина гребня, м":gate?"Ширина затвора, м":"Ширина прорана, м",p.BreachWidth));
        rows.Add((overflow?"Отметка гребня, м":circle?"Отметка низа круглого отверстия, м":gate?"Отметка порога затвора, м":"Отметка порога прорана, м",p.SillElevation));
        if(gate)rows.Add(("Максимальная высота открытия, м",p.GateHeight));
        if(ScenarioMode.HasFormation(p.Scenario))rows.Add((gate?"Время подъёма затвора, мин":"Формирование, мин",p.FormationMinutes));
        rows.AddRange(new (string,object)[]{("Коэффициент расхода",p.DischargeCoefficient),("Нижний бьеф: глубина, м",p.LowerDepth),("Нижний бьеф: ширина, м",p.LowerWidth),("Нижний бьеф: скорость, м/с",p.LowerVelocity),("Расчётное время, ч",p.DurationHours),("Количество ячеек",p.Cells),("Подъём для критерия прихода, м",p.ArrivalThreshold),("Экстренная зона, ч",p.EvacuationHours)});
        return rows;
    }
    private static IEnumerable<string[]> Parameters(ProjectData p)=>ParameterCells(p).Select(row=>new[]{row.Label,row.Value is double n?F(n):row.Value.ToString()??""});
    private static IEnumerable<string[]> Geometry(ProjectData p)=>p.Sections.OrderBy(s=>s.Distance).Select(s=>new[]{s.Name,F(s.Distance),F(s.Bed),F(s.NormalDepth),F(s.LeftWidth),F(s.RightWidth),F(s.InitialVelocity),F(s.Manning),F(s.DrainageFactor)});
    private static IEnumerable<string[]> Isolines(SimulationResult r)=>r.Isolines.Select(s=>new[]{s.Section,s.Bank,F(s.Depth),F(s.Distance),F(s.Velocity),F(s.DryingHours)});
    private static IEnumerable<string[]> Zones(SimulationResult r)=>r.Sections.Select(s=>new[]{s.Name,F(s.FloodLeft),F(s.FloodRight),F(s.DangerLeft),F(s.DangerRight),F(s.DestructionLeft),F(s.DestructionRight),s.Evacuation});
    private static string Diagnostics(SimulationResult r)=>$"Модель {r.ModelVersion}. Сетка: {r.Source.Cells}; шагов: {r.Steps}; время вычислений: {F(r.ElapsedSeconds)} с. Выпуск: {F(r.ReleasedVolume)} м³; начальный объём реки: {F(r.InitialRiverVolume)} м³; конечный: {F(r.FinalRiverVolume)} м³; выход с нижней границы (со знаком): {F(r.OutflowVolume)} м³. Невязка массы: {r.BalanceErrorPercent:F6}%. Площадь пойменного затопления между заданными створами: {F(r.FloodArea)} м². Площадь с глубиной не менее 1,5 м: {F(r.DangerArea)} м².";
    private const string Method="Независимая исследовательская модель RG-1D: одномерные уравнения мелкой воды, конечные объёмы первого порядка, HLL, гидростатическая реконструкция, CFL≤0,35, полунеявное трение Маннинга. Начальное течение не устанавливается отдельным разгоночным расчётом. Приход и высота волны определены сравнением с отдельным безаварийным расчётом с закрытым прораном; максимальные уровни относятся к полному потоку. Водохранилище имеет постоянную площадь зеркала; расход прорана Cd·b·√(2g)·max(hu^(3/2)−hd^(3/2),0). Формула подпора является принятой аппроксимацией. Параболическое русло аппроксимировано отрезками; берега — горизонталями. Скорость изодат при максимальном уровне распределена по h^(2/3) с нормировкой по расходу. Фронт — первое превышение безаварийной глубины на заданный порог. Спад — последнее превышение этого критерия, только при его завершении в пределах моделируемого времени. Осушение от гребня дополнительно умножается на пользовательский K. Зоны по глубинам 0/1,5/3 м — сценарные контуры, не нормативная оценка риска. Результаты не являются GIS-картой или сертифицированным расчётом аварии ГТС. Необходимы проверка сходимости и профессиональная верификация.";
    private static string MethodFor(ProjectData p)
    {
        if(ScenarioMode.IsGate(p.Scenario)||ScenarioMode.IsCircular(p.Scenario))
            return Method.Replace("расход прорана Cd·b·√(2g)·max(hu^(3/2)−hd^(3/2),0). Формула подпора является принятой аппроксимацией.","расход отверстия Q=Cd·√(2g)·∫√max(max(Zв−z,0)−max(Zн−z,0),0) dA. Постоянный коэффициент Cd; обратный поток не моделируется.")+(ScenarioMode.IsGate(p.Scenario)?" Прямоугольный затвор: ширина постоянна, высота открытия линейно растёт до заданного максимума. Интеграл по высоте вычисляется аналитически.":" Круглое отверстие: диаметр постоянен, нижняя отметка задаёт низ окружности. Затопленная площадь вычисляется аналитически; свободная часть интегрируется квадратурой Симпсона по углу с 64 интервалами и заменой переменной у свободной поверхности.")+" Прочность, размыв, рост дефекта и потери в длинных водоводах не рассчитываются; входной импульс приближён по напору над нижней отметкой.";
        return Method+(ScenarioMode.IsOverflow(p.Scenario)?" В этом сценарии b означает постоянную длину гребня. Время формирования прорана не применяется.":" В этом сценарии b(t) — ширина прорана: линейное раскрытие за заданное время либо мгновенное открытие при нулевом времени.");
    }

    // Экспорт CSV
    public static void Csv(SimulationResult r,string path)
    {
        var text=new StringBuilder();
        string E(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
        void Row(IEnumerable<string> row)=>text.AppendLine(string.Join(";",row.Select(E)));
        Row(new[]{"RiverGuard",r.Source.Name,r.Created.ToString("s"),r.ModelVersion});Row(new[]{"Автор приложения","Apdulium-beep"});Row(new[]{"РЕЗУЛЬТАТЫ"});Row(Headers);foreach(var row in Results(r))Row(row);
        Row(new[]{""});Row(new[]{"ИЗОДАТЫ"});Row(new[]{"Створ","Берег","Глубина, м","От фарватера, м","Скорость, м/с","Осушение от гребня, ч"});foreach(var row in Isolines(r))Row(row);
        Row(new[]{""});Row(new[]{"ДИАГНОСТИКА",Diagnostics(r)});foreach(var warning in r.Warnings)Row(new[]{"Предупреждение",warning});
        File.WriteAllText(path,text.ToString(),new UTF8Encoding(true));
    }
    // Автономный HTML-отчёт
    public static void Html(SimulationResult r,string path,List<(string Name,byte[] Png)> images)
    {
        string E(string s)=>WebUtility.HtmlEncode(s);
        var text=new StringBuilder("<!doctype html><html lang='ru'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>RiverGuard — отчёт</title><style>body{font:14px/1.6 'Segoe UI',sans-serif;color:#203249;background:#eef3f8;margin:0}main{max-width:1150px;background:white;margin:35px auto;padding:45px;border-radius:20px}h1{font-size:34px;margin-bottom:0}h2{margin-top:35px}small{color:#62758c}table{border-collapse:collapse;width:100%;font-size:11px;margin:14px 0}td,th{padding:9px;border:1px solid #dde5ed;text-align:left}th{background:#12283d;color:white}.warning{padding:12px 16px;background:#fff6e6;border-left:3px solid #d5a044;margin:10px 0}img{width:100%;border-radius:12px}p{white-space:pre-wrap}@media print{body{background:white}main{margin:0;padding:0}h2{break-after:avoid}tr,img{break-inside:avoid}button{display:none}}</style><main><small>RIVERGUARD / RG-1D · RESEARCH EDITION</small>");
        text.Append("<h1>"+E(r.Source.Name)+"</h1><small>"+r.Created.ToString("dd.MM.yyyy HH:mm")+" · "+E(r.ModelVersion)+"</small><div class='warning'>Исследовательская модель. Не использовать как основание для реальных решений об эвакуации.</div>");
        void Heading(string s)=>text.Append("<h2>"+E(s)+"</h2>");
        void Table(string[] heads,IEnumerable<string[]> rows){text.Append("<table><thead><tr>");foreach(var h in heads)text.Append("<th>"+E(h)+"</th>");text.Append("</tr></thead><tbody>");foreach(var row in rows){text.Append("<tr>");foreach(var value in row)text.Append("<td>"+E(value)+"</td>");text.Append("</tr>");}text.Append("</tbody></table>");}
        Heading("1. Исходные данные");Table(new[]{"Параметр","Значение"},Parameters(r.Source));text.Append("<p>"+E(r.Source.Notes)+"</p>");
        Heading("2. Геометрия створов");Table(new[]{"Створ","L, м","Дно, м","Hб, м","Левый, м","Правый, м","Vб, м/с","n","K"},Geometry(r.Source));
        foreach(var s in r.Source.Sections){Table(new[]{s.Name,"Берег","От оси, м","Отметка, м"},s.LeftBank.Select(p=>new[]{"Горизонталь","Левый",F(p.Distance),F(p.Elevation)}).Concat(s.RightBank.Select(p=>new[]{"Горизонталь","Правый",F(p.Distance),F(p.Elevation)})));}
        Heading("3. Результаты");Table(Headers,Results(r));
        Heading("4. Сценарные зоны за бытовыми берегами");Table(new[]{"Створ","ВЗМ Л, м","ВЗМ П, м","h≥1,5 Л","h≥1,5 П","h≥3 Л","h≥3 П","Эвакуация"},Zones(r));
        Heading("5. Графические результаты");foreach(var image in images)text.Append("<h3>"+E(image.Name)+"</h3><img alt='"+E(image.Name)+"' src='data:image/png;base64,"+Convert.ToBase64String(image.Png)+"'>");
        Heading("6. Изодаты");Table(new[]{"Створ","Берег","h, м","R, м","v, м/с","Осушение, ч"},Isolines(r));
        Heading("7. Баланс и диагностика");text.Append("<p>"+E(Diagnostics(r))+"</p>");foreach(var w in r.Warnings)text.Append("<div class='warning'>"+E(w)+"</div>");
        Heading("8. Метод и ограничения");text.Append("<p>"+E(MethodFor(r.Source))+"</p><p>Автор приложения RiverGuard: Apdulium-beep.</p></main></html>");
        File.WriteAllText(path,text.ToString(),Encoding.UTF8);
    }
    // Документ Word
    public static void Word(SimulationResult r,string path,List<(string Name,byte[] Png)> images)
    {
        using var document=WordprocessingDocument.Create(path,WordprocessingDocumentType.Document);
        var main=document.AddMainDocumentPart();main.Document=new W.Document();var body=main.Document.AppendChild(new W.Body());
        var styles=main.AddNewPart<StyleDefinitionsPart>();styles.Styles=new W.Styles(new W.DocDefaults(new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(new W.RunFonts{Ascii="Calibri",HighAnsi="Calibri"},new W.FontSize{Val="20"}))),new W.Style{Type=W.StyleValues.Paragraph,StyleId="Normal",Default=true});
        void Paragraph(string value,bool heading=false)
        {var props=new W.RunProperties();if(heading)props.Append(new W.Bold());props.Append(new W.Color{Val=heading?"176E75":"25364B"},new W.FontSize{Val=heading?"28":"20"});var run=new W.Run(props,new W.Text(value){Space=SpaceProcessingModeValues.Preserve});body.Append(new W.Paragraph(new W.ParagraphProperties(new W.SpacingBetweenLines{After=heading?"180":"100"}),run));}
        void Table(string[] headers,IEnumerable<string[]> rows)
        {
            var table=new W.Table(new W.TableProperties(new W.TableWidth{Width="5000",Type=W.TableWidthUnitValues.Pct},new W.TableBorders(new W.TopBorder{Val=W.BorderValues.Single,Size=4,Color="D5DFE9"},new W.BottomBorder{Val=W.BorderValues.Single,Size=4,Color="D5DFE9"},new W.InsideHorizontalBorder{Val=W.BorderValues.Single,Size=4,Color="D5DFE9"},new W.InsideVerticalBorder{Val=W.BorderValues.Single,Size=4,Color="D5DFE9"})));
            table.Append(new W.TableGrid(headers.Select(_=>new W.GridColumn{Width=(15000/headers.Length).ToString()})));
            foreach(var (row,index) in new[]{headers}.Concat(rows).Select((row,index)=>(row,index)))
            {
                var tr=new W.TableRow();if(index==0)tr.Append(new W.TableRowProperties(new W.TableHeader()));
                foreach(var value in row)
                {var cp=new W.TableCellProperties();if(index==0)cp.Append(new W.Shading{Val=W.ShadingPatternValues.Clear,Fill="E4F3F2"});var props=new W.RunProperties();if(index==0)props.Append(new W.Bold());props.Append(new W.FontSize{Val="17"});var run=new W.Run(props,new W.Text(value){Space=SpaceProcessingModeValues.Preserve});tr.Append(new W.TableCell(cp,new W.Paragraph(run)));}
                table.Append(tr);
            }
            body.Append(table);body.Append(new W.Paragraph());
        }
        Paragraph("RIVERGUARD · РАСЧЁТ ВОЛНЫ ПРОРЫВА",true);Paragraph(r.Source.Name,true);Paragraph($"Дата: {r.Created:dd.MM.yyyy HH:mm}. Модель: {r.ModelVersion}.");
        Paragraph("Исследовательская модель. Не является сертифицированным расчётом последствий аварии ГТС и основанием для эвакуации.");
        Paragraph("1. Исходные данные",true);Table(new[]{"Параметр","Значение"},Parameters(r.Source));Paragraph(r.Source.Notes);
        Paragraph("2. Геометрия створов",true);Table(new[]{"Створ","L, м","Дно, м","Hб, м","Левый, м","Правый, м","Vб, м/с","n","K"},Geometry(r.Source));
        foreach(var section in r.Source.Sections)
        {Paragraph(section.Name);Table(new[]{"Берег","От оси, м","Отметка, м"},section.LeftBank.Select(p=>new[]{"Левый",F(p.Distance),F(p.Elevation)}).Concat(section.RightBank.Select(p=>new[]{"Правый",F(p.Distance),F(p.Elevation)})));}
        Paragraph("3. Результаты",true);Table(Headers,Results(r));Paragraph("Прочерк / «не определено» означает отсутствие события в расчётном интервале, а не нулевое время.");
        Paragraph("4. Сценарные границы за бытовыми берегами",true);Table(new[]{"Створ","ВЗМ Л, м","ВЗМ П, м","h≥1,5 Л","h≥1,5 П","h≥3 Л","h≥3 П","Эвакуация"},Zones(r));
        Paragraph("5. Графические результаты",true);uint imageId=1;
        foreach(var image in images)
        {
            Paragraph(image.Name);var part=main.AddImagePart(ImagePartType.Png);using(var ms=new MemoryStream(image.Png))part.FeedData(ms);
            string id=main.GetIdOfPart(part);const long cx=8500000,cy=3400000;
            var drawing=new W.Drawing(new DW.Inline(new DW.Extent{Cx=cx,Cy=cy},new DW.DocProperties{Id=imageId++,Name=image.Name},new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks{NoChangeAspect=true}),new A.Graphic(new A.GraphicData(new PIC.Picture(new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties{Id=0U,Name=image.Name},new PIC.NonVisualPictureDrawingProperties()),new PIC.BlipFill(new A.Blip{Embed=id},new A.Stretch(new A.FillRectangle())),new PIC.ShapeProperties(new A.Transform2D(new A.Offset{X=0,Y=0},new A.Extents{Cx=cx,Cy=cy}),new A.PresetGeometry(new A.AdjustValueList()){Preset=A.ShapeTypeValues.Rectangle}))){Uri="http://schemas.openxmlformats.org/drawingml/2006/picture"})){DistanceFromTop=0U,DistanceFromBottom=0U,DistanceFromLeft=0U,DistanceFromRight=0U});
            body.Append(new W.Paragraph(new W.Run(drawing)));
        }
        Paragraph("6. Изодаты",true);Table(new[]{"Створ","Берег","h, м","R, м","v, м/с","Осушение, ч"},Isolines(r));
        Paragraph("7. Баланс и диагностика",true);Paragraph(Diagnostics(r));foreach(var warning in r.Warnings)Paragraph("• "+warning);
        Paragraph("8. Метод и ограничения",true);Paragraph(MethodFor(r.Source));Paragraph("Автор приложения RiverGuard: Apdulium-beep.");
        body.Append(new W.SectionProperties(new W.PageSize{Width=16838U,Height=11906U,Orient=W.PageOrientationValues.Landscape},new W.PageMargin{Top=800,Bottom=800,Left=800U,Right=800U}));
        main.Document.Save();
    }
}
