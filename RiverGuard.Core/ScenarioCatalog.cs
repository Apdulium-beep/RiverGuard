namespace RiverGuard.Core;

// Шаблоны расчётных сценариев
public sealed class ScenarioPreset(string id,string category,string name,string description,string changes,Func<ProjectData> factory)
{
    public string Id { get; }=id;
    public string Category { get; }=category;
    public string Name { get; }=name;
    public string Description { get; }=description;
    public string Changes { get; }=changes;
    public override string ToString()=>Name;
    public ProjectData Create()
    {
        var p=factory();p.Name=Name;
        p.Notes+=$"\nШаблон каталога: {Id}. {Description}\nИзменения: {Changes}\nЭто вариант исходных условий RG-1D, а не отдельная подтверждённая методика аварии.";
        return p;
    }
}

// Каталог поддерживаемых исходных условий
public static class ScenarioCatalog
{
    public static IReadOnlyList<ScenarioPreset> All { get; }=Build().AsReadOnly();
    private static Func<ProjectData> Variant(Action<ProjectData> edit)=>()=>{var p=Examples.Valley();edit(p);return p;};
    private static List<ScenarioPreset> Build()
    {
        var list=new List<ScenarioPreset>();
        void Add(string id,string category,string name,string description,string changes,Action<ProjectData> edit)=>list.Add(new(id,category,name,description,changes,Variant(edit)));
        list.Add(new("base-valley","Базовые","Демонстрационная долина","Исходный синтетический пример с четырьмя створами.","Стандартные параметры: 2 ч, 100 ячеек.",Examples.Valley));
        list.Add(new("base-oreshko","Базовые","Орешко Волга · исходные данные","Параметры из предоставленных отчётов «Волны» 25.0. Результаты рассчитываются независимо.","Три створа: 100, 800 и 1600 м; без подстановки результатов старой программы.",Examples.Oreshko));
        Add("base-closed","Базовые","Без аварии · закрытый проран","Контроль начальной перестройки руслового потока без выпуска водохранилища.","Ширина прорана 0 м.",p=>p.BreachWidth=0);
        Add("breach-instant","Проран","Мгновенное раскрытие","Условное образование отверстия сразу на конечную ширину.","Раскрытие 0 мин; ширина 18 м.",p=>p.FormationMinutes=0);
        Add("breach-fast","Проран","Быстрое раскрытие","Быстрое, но не мгновенное увеличение ширины отверстия.","Раскрытие 2 мин.",p=>p.FormationMinutes=2);
        Add("breach-slow","Проран","Постепенное раскрытие","Плавное формирование ширины прорана в течение часа. Эрозия не моделируется.","Раскрытие 60 мин; расчёт 4 ч.",p=>{p.FormationMinutes=60;p.DurationHours=4;});
        Add("breach-narrow","Проран","Узкий проран","Небольшое отверстие при тех же отметках и водохранилище.","Ширина 4 м.",p=>p.BreachWidth=4);
        Add("breach-wide","Проран","Широкий проран","Увеличенная ширина отверстия для исследования чувствительности расхода.","Ширина 45 м.",p=>p.BreachWidth=45);
        Add("breach-full-width","Проран","Раскрытие на ширину русла","Условный предельный проран, открытый до дна. Не является расчётом механического разрушения плотины.","Ширина 80 м; порог 100 м; мгновенное раскрытие.",p=>{p.BreachWidth=p.LowerWidth;p.SillElevation=p.DamBed;p.FormationMinutes=0;});
        Add("overflow-moderate","Перелив","Перелив через высокий гребень","Выпуск воды через фиксированный гребень с небольшим превышением уровня.","Гребень 107 м; ширина 45 м; раскрытие 0 мин.",p=>{p.Scenario="Перелив через гребень";p.SillElevation=107;p.BreachWidth=45;p.FormationMinutes=0;});
        Add("overflow-intense","Перелив","Интенсивный перелив","Повышенный напор над широким неподвижным гребнем. Размыв гребня не учитывается.","Гребень 105 м; ширина 60 м; раскрытие 0 мин.",p=>{p.Scenario="Перелив через гребень";p.SillElevation=105;p.BreachWidth=60;p.FormationMinutes=0;});
        Add("overflow-no-head","Перелив","Уровень ниже гребня","Контроль отсутствия перелива, когда вода не достигает гребня.","Гребень 108,5 м выше уровня 108 м.",p=>{p.Scenario="Перелив через гребень";p.SillElevation=108.5;p.BreachWidth=45;p.FormationMinutes=0;});
        Add("reservoir-low","Водохранилище","Пониженный уровень воды","Меньший физический напор при неизменных отметках дна и порога.","Уровень 104 м; объём 2 млн м³; площадь 0,5 км².",p=>{p.ReservoirLevel=104;p.ReservoirVolume=2;});
        Add("reservoir-high","Водохранилище","Повышенный уровень воды","Повышение реального уровня, а не перенос всей системы высот.","Уровень 110 м; объём 5 млн м³; площадь 0,5 км².",p=>{p.ReservoirLevel=110;p.ReservoirVolume=5;});
        Add("reservoir-small","Водохранилище","Малое водохранилище","Более быстрое исчерпание запаса воды при той же начальной глубине.","Объём 0,4 млн м³; площадь 0,05 км².",p=>{p.ReservoirVolume=0.4;p.ReservoirArea=0.05;});
        Add("reservoir-large","Водохранилище","Большое водохранилище","Больший запас воды при сохранении начальной глубины и параметров прорана.","Объём 20 млн м³; площадь 2,5 км²; расчёт 4 ч.",p=>{p.ReservoirVolume=20;p.ReservoirArea=2.5;p.DurationHours=4;});
        Add("channel-narrow","Русло","Узкая долина","Поперечные расстояния русла и береговых горизонталей уменьшены вдвое.","Все поперечные расстояния ×0,5, включая нижний бьеф.",p=>ScaleWidths(p,_=>0.5,0.5));
        Add("channel-wide","Русло","Широкая долина","Увеличенные ширины русла и поймы при прежних высотах.","Все поперечные расстояния ×2,5.",p=>ScaleWidths(p,_=>2.5,2.5));
        Add("channel-constriction","Русло","Сужение в средней части","Условное геометрическое сужение долины; не модель моста или напорного прохода.","Ширины средних створов ×0,45, крайних — без изменений.",p=>ScaleWidths(p,i=>i is 1 or 2?0.45:1,1));
        Add("channel-expansion","Русло","Расширение вниз по течению","Последовательное расширение поперечных сечений по длине участка.","Множитель ширин возрастает от 1 до 2,5.",p=>ScaleWidths(p,i=>1+1.5*i/(p.Sections.Count-1),1));
        Add("slope-gentle","Русло","Малый продольный уклон","Более пологая долина с сохранением относительной геометрии берегов.","Уклон дна 0,2 м/км.",p=>SetSlope(p,0.0002));
        Add("slope-steep","Русло","Повышенный продольный уклон","Проверка течения на более крутом участке; применимость одномерной схемы требует отдельной оценки.","Уклон дна 4 м/км.",p=>SetSlope(p,0.004));
        Add("banks-low","Берега и пойма","Низкая широкая пойма","Пологие берега с большими поперечными расстояниями и низкими горизонталями.","Горизонтали: +0,2 / +0,6 / +1,5 м над бытовым уровнем.",p=>SetBanks(p,new[]{80d,180,320},new[]{0.2,0.6,1.5},new[]{80d,180,320},new[]{0.2,0.6,1.5}));
        Add("banks-high","Берега и пойма","Высокие крутые берега","Высокие отметки берега близко к бытовому руслу.","Горизонтали: +4 / +8 / +15 м на 10 / 20 / 40 м за берегом.",p=>SetBanks(p,new[]{10d,20,40},new[]{4d,8,15},new[]{10d,20,40},new[]{4d,8,15}));
        Add("banks-asymmetric","Берега и пойма","Низкий левый · высокий правый","Асимметричная долина для сравнения границ по берегам.","Слева пологая пойма, справа высокий крутой склон.",p=>SetBanks(p,new[]{80d,180,320},new[]{0.2,0.6,1.5},new[]{10d,20,40},new[]{4d,8,15}));
        Add("roughness-low","Сопротивление","Малое сопротивление русла","Сценарий с меньшим гидравлическим сопротивлением.","Коэффициент Маннинга n = 0,018.",p=>{foreach(var s in p.Sections)s.Manning=0.018;});
        Add("roughness-high","Сопротивление","Заросшее русло","Повышенное сопротивление задаётся коэффициентом Маннинга, без отдельной модели растительности.","n = 0,070; K осушения = 1,3.",p=>{foreach(var s in p.Sections){s.Manning=0.07;s.DrainageFactor=1.3;}});
        Add("roughness-extreme","Сопротивление","Очень высокое сопротивление","Параметрическая оценка при большом коэффициенте сопротивления.","n = 0,120; K осушения = 1,7; расчёт 4 ч.",p=>{foreach(var s in p.Sections){s.Manning=0.12;s.DrainageFactor=1.7;}p.DurationHours=4;});
        Add("initial-shallow","Начальный поток","Маловодный нижний бьеф","Малая, но ненулевая начальная глубина. Это не прорыв на полностью сухое ложе.","Начальная глубина 0,5 м во всех створах и у плотины.",p=>SetInitialDepth(p,0.5));
        Add("initial-deep","Начальный поток","Глубокий нижний бьеф","Большая начальная глубина с соответствующим подъёмом береговых горизонталей.","Начальная глубина 4,5 м.",p=>SetInitialDepth(p,4.5));
        Add("initial-still","Начальный поток","Нулевая начальная скорость","Исследование чувствительности к начальной скорости. Наклонная свободная поверхность может начать двигаться и без аварии.","Начальная скорость 0 м/с во всех створах.",p=>{p.LowerVelocity=0;foreach(var s in p.Sections)s.InitialVelocity=0;});
        Add("duration-short","Временной интервал","Первые 15 минут","Короткий расчёт раннего формирования волны; удалённые створы могут ещё не получить возмущение.","Продолжительность 0,25 ч.",p=>p.DurationHours=0.25);
        Add("duration-long","Временной интервал","Длительный спад · 6 часов","Более длительное наблюдение за спадом. Завершение осушения не гарантируется.","Продолжительность 6 ч.",p=>p.DurationHours=6);
        return list;
    }
    // Преобразования геометрии шаблонов
    private static void ScaleWidths(ProjectData p,Func<int,double> factor,double lowerFactor)
    {
        p.LowerWidth*=lowerFactor;
        for(int i=0;i<p.Sections.Count;i++)
        {var s=p.Sections[i];double k=factor(i);s.LeftWidth*=k;s.RightWidth*=k;foreach(var b in s.LeftBank.Concat(s.RightBank))b.Distance*=k;}
    }
    private static void SetSlope(ProjectData p,double slope)
    {
        foreach(var s in p.Sections){double bed=p.DamBed-s.Distance*slope,delta=bed-s.Bed;s.Bed=bed;foreach(var b in s.LeftBank.Concat(s.RightBank))b.Elevation+=delta;}
    }
    private static void SetBanks(ProjectData p,double[] leftX,double[] leftZ,double[] rightX,double[] rightZ)
    {
        foreach(var s in p.Sections)
        {
            s.LeftBank.Clear();s.RightBank.Clear();
            for(int i=0;i<leftX.Length;i++)s.LeftBank.Add(new(){Distance=s.LeftWidth+leftX[i],Elevation=s.Bed+s.NormalDepth+leftZ[i]});
            for(int i=0;i<rightX.Length;i++)s.RightBank.Add(new(){Distance=s.RightWidth+rightX[i],Elevation=s.Bed+s.NormalDepth+rightZ[i]});
        }
    }
    private static void SetInitialDepth(ProjectData p,double depth)
    {
        p.LowerDepth=depth;
        foreach(var s in p.Sections){double delta=depth-s.NormalDepth;s.NormalDepth=depth;foreach(var b in s.LeftBank.Concat(s.RightBank))b.Elevation+=delta;}
    }
}
