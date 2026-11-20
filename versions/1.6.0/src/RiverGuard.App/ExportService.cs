using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using RiverGuard.Core;
using RiverGuard.Controls;
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
    private static IEnumerable<string[]> Results(SimulationResult r)=>r.Sections.Select(s=>new[]{s.Name,F(s.Distance),F(s.MaxLevel),F(s.MaxDepth),F(s.WaveHeight),F(s.MaxDischarge),F(s.MaxVelocity),F(s.ArrivalHours),F(s.ArrivalHours.HasValue?(double?)s.PeakHours:null),F(s.RecessionHours)});
    private static List<(string Label,object Value)> ParameterCells(ProjectData p)
    {
        bool overflow=ScenarioMode.IsOverflow(p.Scenario),gate=ScenarioMode.IsGate(p.Scenario),circle=ScenarioMode.IsCircular(p.Scenario);
        var rows=new List<(string,object)>{("Сценарий",p.Scenario),("Уровень водохранилища, м",p.ReservoirLevel),("Дно у плотины, м",p.DamBed),("Объём, млн м³",p.EffectiveReservoirVolume),("Площадь зеркала, км²",p.ReservoirArea)};
        rows.Add(circle?("Диаметр отверстия, м",p.OutletDiameter):(overflow?"Длина гребня, м":gate?"Ширина затвора, м":"Ширина прорана, м",p.EffectiveBreachWidth));
        rows.Add((overflow?"Отметка гребня, м":circle?"Отметка низа круглого отверстия, м":gate?"Отметка порога затвора, м":"Отметка порога прорана, м",p.SillElevation));
        if(gate)rows.Add(("Максимальная высота открытия, м",p.GateHeight));
        if(ScenarioMode.HasFormation(p.Scenario))rows.Add((gate?"Время подъёма затвора, мин":"Формирование, мин",p.FormationMinutes));
        rows.AddRange(new (string,object)[]{("Коэффициент расхода",p.DischargeCoefficient),("Нижний бьеф: глубина, м",p.LowerDepth),("Нижний бьеф: ширина, м",p.LowerWidth),("Нижний бьеф: скорость, м/с",p.LowerVelocity),("Расчётное время, ч",p.DurationHours),("Количество ячеек",p.Cells),("Подъём для критерия прихода, м",p.ArrivalThreshold),("Экстренная зона, ч",p.EvacuationHours)});
        if(p.ReservoirReferenceLevel is double reference){rows.Add(("Опорный уровень объёма, м",reference));rows.Add(("Объём при опорном уровне, млн м³",p.ReservoirVolume));}
        if(p.LowerBedElevation is double lowerBed)rows.Add(("Дно нижнего бьефа у плотины, м",lowerBed));
        if(p.UseDestructionFraction&&p.Scenario==ScenarioMode.Breach){rows.Add(("Длина плотины, м",p.DamLength));rows.Add(("Доля длины плотины",p.DestructionFraction));}
        if(!string.IsNullOrWhiteSpace(p.Executor))rows.Add(("Исполнитель",p.Executor));
        if(!string.IsNullOrWhiteSpace(p.HydroUnitNumber))rows.Add(("Номер гидроузла N",p.HydroUnitNumber));
        return rows;
    }
    private static IEnumerable<string[]> Parameters(ProjectData p)=>ParameterCells(p).Select(row=>new[]{row.Label,row.Value is double n?F(n):row.Value.ToString()??""});
    private static IEnumerable<string[]> Geometry(ProjectData p)=>p.Sections.OrderBy(s=>s.Distance).Select(s=>new[]{s.Name,F(s.Distance),F(s.Bed),F(s.NormalDepth),F(s.LeftWidth),F(s.RightWidth),F(s.InitialVelocity),F(s.Manning),F(s.DrainageFactor)});
    private static IEnumerable<string[]> Isolines(SimulationResult r)=>r.Isolines.Select(s=>new[]{s.Section,s.Bank,F(s.Depth),F(s.Distance),F(s.Velocity),F(s.DryingHours)});
    private static IEnumerable<string[]> Zones(SimulationResult r)=>r.Sections.Select(s=>new[]{s.Name,F(s.FloodLeft),F(s.FloodRight),F(s.DangerLeft),F(s.DangerRight),F(s.DestructionLeft),F(s.DestructionRight),s.Evacuation});
    private static string[] ZoneHeaders=>new[]{"Створ","ВЗМ Л, м","ВЗМ П, м","ВКЗМ Л, м","ВКЗМ П, м","КР Л, м","КР П, м","Эвакуация"};
    private static List<(string Name,byte[] Png)> WithPlan(SimulationResult r,List<(string Name,byte[] Png)> images)
    {var list=images.ToList();if(!list.Any(i=>i.Name.StartsWith("Схема зон")))list.Add(("Схема зон затопления · схема, не ГИС-карта",ZonePlanView.Png(r)));return list;}
    private static string Diagnostics(SimulationResult r)=>$"Модель {r.ModelVersion}. Сетка: {r.Source.Cells}; шагов: {r.Steps}; время вычислений: {F(r.ElapsedSeconds)} с. Установление бытового режима: {F(r.InitializationHours)} ч, {r.InitializationSteps} шагов; остаток {F(r.InitializationResidualPercent)}%. Выпуск: {F(r.ReleasedVolume)} м³; бытовой приток: {F(r.BaseDischarge)} м³/с, {F(r.BaseInflowVolume)} м³; начальный объём реки после установления: {F(r.InitialRiverVolume)} м³; конечный: {F(r.FinalRiverVolume)} м³; выход с нижней границы (со знаком): {F(r.OutflowVolume)} м³. Невязка массы: {F(r.BalanceErrorPercent)}%. Площадь ВЗМ за бытовыми берегами: {F(r.FloodArea)} м²; ВКЗМ: {F(r.DangerArea)} м²; КР: {F(r.DestructionArea)} м². "+r.Source.Zones.Description;
    private const string Method="Независимая исследовательская модель RG-1D: одномерные уравнения мелкой воды, конечные объёмы первого порядка, HLL, гидростатическая реконструкция, CFL≤0,35, полунеявное трение Маннинга. До аварии устанавливается безаварийный режим с постоянным притоком Qб=A(Hб)·Vб на верхней границе и открытой нижней границей; заданные глубины и скорости служат начальным приближением. Два последовательных окна по 300 с проверяют относительные изменения площадей/расходов (<0,01%) и постоянство межъячеечного расхода (<0,1%); предел установления 7 суток, при неуспехе аварийный расчёт не выполняется. При Qб=0 выбран гидростатический режим с общим горизонтом воды, нулевыми скоростями и сохранением исходного объёма реки; это не моделирование естественного опорожнения. Один установившийся снимок используется для аварийного и безаварийного расчётов. Приход, гребень и высота волны определены превышением безаварийной глубины; максимум полного уровня и расход в этот момент хранятся отдельно. Максимальные уровни, расходы и скорости относятся к полному потоку после установления. Водохранилище имеет постоянную площадь зеркала; расход прорана Cd·b·√(2g)·max(hu^(3/2)−hd^(3/2),0). Формула подпора является принятой аппроксимацией. При заданном опорном уровне W=Wоп+S·(Z−Zоп), иначе объём независим от уровня. Параболическое русло аппроксимировано отрезками; берега — горизонталями. Скорость изодат при максимальном полном уровне распределена по h^(2/3) с нормировкой по расходу. Фронт — первое превышение безаварийной глубины на заданный порог. Гребень — максимум превышения при достижении критерия прихода. Спад — последнее превышение критерия после гребня, только при завершении в пределах расчётного времени, иначе не определён. Осушение изодат отсчитывается от максимума полного уровня и умножается на пользовательский K. Зоны по глубинам 0/1,5/3 м — сценарные контуры, не нормативная оценка риска. Методика «Волны» не воспроизводится. Результаты не являются GIS-картой или сертифицированным расчётом аварии ГТС. Необходимы проверка сходимости и профессиональная верификация.";
    private static string MethodFor(ProjectData p)
    {
        string method=Method.Replace("Зоны по глубинам 0/1,5/3 м","Зоны по настраиваемым критериям глубины, скорости и прихода")+" "+p.Zones.Description+" Средняя скорость спада добавки = Hг / длительность спада; в изодатах h/t — отношение глубины в точке к расчётному времени осушения. Это производные сценарные оценки, не формулы методики «Волны».";
        if(ScenarioMode.IsGate(p.Scenario)||ScenarioMode.IsCircular(p.Scenario))
            return method.Replace("расход прорана Cd·b·√(2g)·max(hu^(3/2)−hd^(3/2),0). Формула подпора является принятой аппроксимацией.","расход отверстия Q=Cd·√(2g)·∫√max(max(Zв−z,0)−max(Zн−z,0),0) dA. Постоянный коэффициент Cd; обратный поток не моделируется.")+(ScenarioMode.IsGate(p.Scenario)?" Прямоугольный затвор: ширина постоянна, высота открытия линейно растёт до заданного максимума. Интеграл по высоте вычисляется аналитически.":" Круглое отверстие: диаметр постоянен, нижняя отметка задаёт низ окружности. Затопленная площадь вычисляется аналитически; свободная часть интегрируется квадратурой Симпсона по углу с 64 интервалами и заменой переменной у свободной поверхности.")+" Прочность, размыв, рост дефекта и потери в длинных водоводах не рассчитываются; входной импульс приближён по напору над нижней отметкой.";
        return method+(ScenarioMode.IsOverflow(p.Scenario)?" В этом сценарии b означает постоянную длину гребня. Время формирования прорана не применяется.":" В этом сценарии b(t) — ширина прорана: линейное раскрытие за заданное время либо мгновенное открытие при нулевом времени.");
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
        Heading("4. Сценарные зоны за бытовыми берегами");Table(ZoneHeaders,Zones(r));text.Append("<p>"+E(r.Source.Zones.Description)+"</p>");
        Heading("5. Графические результаты");int figure=0;foreach(var image in WithPlan(r,images))text.Append("<h3>Рисунок "+(++figure)+". "+E(image.Name)+"</h3><img alt='"+E(image.Name)+"' src='data:image/png;base64,"+Convert.ToBase64String(image.Png)+"'>");
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
        {var props=new W.RunProperties();if(heading)props.Append(new W.Bold());props.Append(new W.Color{Val=heading?"176E75":"25364B"},new W.FontSize{Val=heading?"28":"20"});var run=new W.Run(props,new W.Text(value){Space=SpaceProcessingModeValues.Preserve});var paragraphProperties=new W.ParagraphProperties();if(heading||value.StartsWith("Рисунок "))paragraphProperties.Append(new W.KeepNext());paragraphProperties.Append(new W.SpacingBetweenLines{After=heading?"180":"100"});body.Append(new W.Paragraph(paragraphProperties,run));}
        void Table(string[] headers,IEnumerable<string[]> rows)
        {
            var table=new W.Table(new W.TableProperties(new W.TableWidth{Width="5000",Type=W.TableWidthUnitValues.Pct},new W.TableBorders(new W.TopBorder{Val=W.BorderValues.Single,Size=4,Color="D5DFE9"},new W.BottomBorder{Val=W.BorderValues.Single,Size=4,Color="D5DFE9"},new W.InsideHorizontalBorder{Val=W.BorderValues.Single,Size=4,Color="D5DFE9"},new W.InsideVerticalBorder{Val=W.BorderValues.Single,Size=4,Color="D5DFE9"})));
            table.Append(new W.TableGrid(headers.Select(_=>new W.GridColumn{Width=(9600/headers.Length).ToString()})));
            foreach(var (row,index) in new[]{headers}.Concat(rows).Select((row,index)=>(row,index)))
            {
                var tr=new W.TableRow();if(index==0)tr.Append(new W.TableRowProperties(new W.TableHeader()));
                foreach(var value in row)
                {var cp=new W.TableCellProperties();if(index==0)cp.Append(new W.Shading{Val=W.ShadingPatternValues.Clear,Fill="E4F3F2"});var props=new W.RunProperties();if(index==0)props.Append(new W.Bold());props.Append(new W.FontSize{Val="17"});var run=new W.Run(props,new W.Text(value){Space=SpaceProcessingModeValues.Preserve});tr.Append(new W.TableCell(cp,new W.Paragraph(run)));}
                table.Append(tr);
            }
            body.Append(table);body.Append(new W.Paragraph());
        }
        Paragraph("РАСЧЁТ ВОЛНЫ ПРОРЫВА",true);Paragraph(r.Source.Name,true);Paragraph($"Дата: {r.Created:dd.MM.yyyy HH:mm:ss}. RiverGuard {(string.IsNullOrWhiteSpace(r.ApplicationVersion)?Updates.UpdateService.CurrentVersionText:r.ApplicationVersion)}. Модель: {r.ModelVersion}.");
        Paragraph("Исполнитель: "+(string.IsNullOrWhiteSpace(r.Source.Executor)?"не указан":r.Source.Executor));
        Paragraph("Исследовательская модель. Не является сертифицированным расчётом последствий аварии ГТС и основанием для эвакуации.");
        body.Append(new W.Paragraph(new W.Run(new W.Break{Type=W.BreakValues.Page})));
        Paragraph("1. Исходные данные по гидроузлу",true);
        var p=r.Source;
        Table(new[]{"Обозначение / параметр","Значение"},new[]{
            new[]{"N · номер гидроузла",string.IsNullOrWhiteSpace(p.HydroUnitNumber)?"не задан":p.HydroUnitNumber},
            new[]{"Wв · исходный / опорный объём, млн м³",F(p.ReservoirVolume)},
            new[]{"Hв · глубина при опорном уровне (без него — начальная), м",F((p.ReservoirReferenceLevel??p.ReservoirLevel)-p.DamBed)},
            new[]{"Sв · площадь зеркала, км²",F(p.ReservoirArea)},new[]{"Bв · длина плотины, м",F(p.DamLength)},
            new[]{"Hбо · заданная глубина НБ, м",F(p.LowerDepth)},new[]{"Bбо · заданная ширина НБ, м",F(p.LowerWidth)},new[]{"Vбо · скорость для бытового притока, м/с",F(p.LowerVelocity)},
            new[]{"Hр · глубина водохранилища при аварии, м",F(p.ReservoirDepth)},new[]{"W · объём при аварии, млн м³",F(p.EffectiveReservoirVolume)},
            new[]{"Eр · доля длины плотины",p.Scenario!=ScenarioMode.Breach?"не применяется к этому механизму":p.UseDestructionFraction?F(p.DestructionFraction):"не задана; ширина введена напрямую"},new[]{"P · превышение порога над дном водохранилища, м",F(p.SillElevation-p.DamBed)}});
        Table(new[]{"Дополнительный параметр","Значение"},Parameters(p));Paragraph(p.Notes);
        Paragraph("Длина плотины, отдельное дно НБ и опорные параметры должны быть подтверждены исходными материалами. Демонстрационные значения не являются обмерами реального объекта.");
        Paragraph("2. Исходные данные по створам",true);
        foreach(var section in r.Source.Sections)
        {
            Paragraph(section.Name,true);Table(new[]{"Параметр","Значение"},new[]{new[]{"Удаление L, м",F(section.Distance)},new[]{"Дно, м",F(section.Bed)},new[]{"Заданная бытовая глубина, м",F(section.NormalDepth)},new[]{"Заданная скорость, м/с",F(section.InitialVelocity)},new[]{"Левый / правый бытовой берег от оси, м",F(section.LeftWidth)+" / "+F(section.RightWidth)},new[]{"n Маннинга",F(section.Manning)},new[]{"K осушения",F(section.DrainageFactor)}});
            Table(new[]{"Берег","От оси, м","Отметка, м"},section.LeftBank.Select(pt=>new[]{"Левый",F(pt.Distance),F(pt.Elevation)}).Concat(section.RightBank.Select(pt=>new[]{"Правый",F(pt.Distance),F(pt.Elevation)})));
        }
        Paragraph("3. Результаты по створам",true);
        foreach(var s in r.Sections)
        {
            Paragraph(s.Name,true);Table(new[]{"Показатель","Значение"},new[]{new[]{"Qmax, м³/с",F(s.MaxDischarge)},new[]{"Vmax, м/с",F(s.MaxVelocity)},new[]{"H · максимальная полная глубина, м",F(s.MaxDepth)},new[]{"Hг · подъём от аварии, м",F(s.WaveHeight)},new[]{"Zmax · абсолютный уровень, м",F(s.MaxLevel)},new[]{"Tф · фронт, ч",F(s.ArrivalHours)},new[]{"Tг · гребень добавки, ч",F(s.WavePeakHours)},new[]{"Tх · спад, ч",F(s.RecessionHours)},new[]{"Средняя скорость спада добавки, м/ч",F(s.DryingRate)},new[]{"Установившаяся начальная глубина, м",F(s.InitialDepth)},new[]{"Установившийся начальный расход, м³/с",F(s.InitialDischarge)},new[]{"ВЗМ за левым / правым бытовым берегом, м",F(s.FloodLeft)+" / "+F(s.FloodRight)},new[]{"ВКЗМ слева / справа, м",F(s.DangerLeft)+" / "+F(s.DangerRight)},new[]{"КР слева / справа, м",F(s.DestructionLeft)+" / "+F(s.DestructionRight)},new[]{"Эвакуация",s.Evacuation}});
        }
        Paragraph("Прочерк / «не определено» означает отсутствие события в расчётном интервале, а не нулевое время. Максимумы Q, V и H могут наступать в разные моменты.");
        Paragraph("4. Зоны и итог расчёта",true);Paragraph(p.Zones.Description);
        Table(new[]{"Итоговый показатель","Значение"},new[]{new[]{"Площадь ВЗМ за бытовыми берегами, м²",F(r.FloodArea)},new[]{"Площадь ВКЗМ, м²",F(r.DangerArea)},new[]{"Площадь КР с условием времени прихода, м²",F(r.DestructionArea)},new[]{"Зона эвакуации I",$"фронт ≤ {F(p.EvacuationHours)} ч"},new[]{"Зона эвакуации II",$"{F(p.EvacuationHours)} < фронт ≤ 4,000 ч"},new[]{"Зона эвакуации III","фронт > 4,000 ч"}});
        Table(new[]{"Створ","Фронт, ч","Эвакуация"},r.Sections.Select(s=>new[]{s.Name,F(s.ArrivalHours),s.Evacuation}));
        Paragraph("5. Графические результаты",true);uint imageId=1;
        int figureNumber=0;foreach(var image in WithPlan(r,images))
        {
            Paragraph($"Рисунок {++figureNumber}. {image.Name}");var part=main.AddImagePart(ImagePartType.Png);using(var ms=new MemoryStream(image.Png))part.FeedData(ms);
            string id=main.GetIdOfPart(part);long cx=5900000,cy=2360000;
            if(image.Png.Length>=24)
            {
                int iw=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(image.Png.AsSpan(16,4)),ih=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(image.Png.AsSpan(20,4));
                if(iw>0&&ih>0){cy=(long)(cx*(double)ih/iw);if(cy>8000000){cx=(long)(cx*8000000d/cy);cy=8000000;}}
            }
            var drawing=new W.Drawing(new DW.Inline(new DW.Extent{Cx=cx,Cy=cy},new DW.DocProperties{Id=imageId++,Name=image.Name},new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks{NoChangeAspect=true}),new A.Graphic(new A.GraphicData(new PIC.Picture(new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties{Id=0U,Name=image.Name},new PIC.NonVisualPictureDrawingProperties()),new PIC.BlipFill(new A.Blip{Embed=id},new A.Stretch(new A.FillRectangle())),new PIC.ShapeProperties(new A.Transform2D(new A.Offset{X=0,Y=0},new A.Extents{Cx=cx,Cy=cy}),new A.PresetGeometry(new A.AdjustValueList()){Preset=A.ShapeTypeValues.Rectangle}))){Uri="http://schemas.openxmlformats.org/drawingml/2006/picture"})){DistanceFromTop=0U,DistanceFromBottom=0U,DistanceFromLeft=0U,DistanceFromRight=0U});
            body.Append(new W.Paragraph(new W.Run(drawing)));
        }
        Paragraph("6. Изодаты по берегам",true);
        foreach(var section in r.Sections){Paragraph(section.Name,true);Table(new[]{"Берег","h, м","R, м","v, м/с","Осушение, ч","h / t, м/ч"},r.Isolines.Where(i=>i.Section==section.Name).Select(i=>new[]{i.Bank,F(i.Depth),F(i.Distance),F(i.Velocity),F(i.DryingHours),F(i.DryingRate)}));}
        Paragraph("7. Баланс и диагностика",true);Paragraph(Diagnostics(r));foreach(var warning in r.Warnings)Paragraph("• "+warning);
        Paragraph("8. Метод и ограничения",true);Paragraph(MethodFor(r.Source));Paragraph("Автор приложения RiverGuard: Apdulium-beep.");
        body.Append(new W.SectionProperties(new W.PageSize{Width=11906U,Height=16838U,Orient=W.PageOrientationValues.Portrait},new W.PageMargin{Top=1134,Bottom=1134,Left=1134U,Right=1134U}));
        main.Document.Save();
    }
}
