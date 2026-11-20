using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Text.Json;
using RiverGuard.Core;

namespace RiverGuard;

public partial class MainWindow
{
    private ApplicationPreferences presentation=new();
    private string[]? standardNavigation;
    private async void Settings_Click(object sender,RoutedEventArgs e)
    {
        // Модальное окно не отменяет расчёт и не перезаписывает проект.
        var before=preferences.Copy();var window=new SettingsWindow(preferences,ApplyPresentation){Owner=this};
        if(window.ShowDialog()!=true){ApplyPresentation(before);return;}
        var candidate=window.Draft.Copy().Normalize();
        try{candidate.Save(ProjectStorage.Folder);preferences=candidate;AutomaticUpdatesBox.IsChecked=preferences.AutomaticUpdates;ApplyPresentation(preferences);}
        catch(Exception ex){ApplyPresentation(before);Notify("Не удалось сохранить настройки. Прежнее оформление восстановлено.\n"+ex.Message);}
        await Dispatcher.InvokeAsync(()=>ThemeService.Prepare(this));
    }
    private void ApplyPresentation(ApplicationPreferences settings)
    {
        presentation=settings.Copy().Normalize();
        ThemeService.Apply(presentation.Theme,presentation.TextSize);FontSize=presentation.TextSize;
        standardNavigation??=Navigation.Items.Cast<ListBoxItem>().Select(i=>i.Content.ToString()??"").ToArray();
        bool simple=presentation.InterfaceMode=="Simple",expert=presentation.InterfaceMode=="Expert";
        OverviewDetails.Visibility=simple?Visibility.Collapsed:Visibility.Visible;
        Overview.RowDefinitions[2].Height=simple?new GridLength(0):new GridLength(1,GridUnitType.Star);
        string[] plain={"1. Обзор проекта","2. Что произошло","3. Река и берега","4. Результаты","Подробности по берегам","5. Сохранить отчёт","Как работает расчёт","Готовые примеры","Источники"};
        for(int i=0;i<Navigation.Items.Count;i++)((ListBoxItem)Navigation.Items[i]).Content=simple?plain[i]:standardNavigation[i];
        ((ListBoxItem)Navigation.Items[4]).Visibility=simple?Visibility.Collapsed:Visibility.Visible;
        if(simple&&Navigation.SelectedIndex==4)Navigation.SelectedIndex=3;
        if(AdvancedNumerics!=null)AdvancedNumerics.Visibility=simple?Visibility.Collapsed:Visibility.Visible;
        if(SimpleAdvancedNotice!=null)SimpleAdvancedNotice.Visibility=simple?Visibility.Visible:Visibility.Collapsed;
        bool method=vm.Project.CalculationMode==CalculationModes.Method1997;
        foreach(var column in SectionsGrid.Columns.OfType<DataGridBoundColumn>())
        {
            string path=(column.Binding as Binding)?.Path?.Path??"";
            bool technical=path is nameof(RiverSection.Manning) or nameof(RiverSection.DrainageFactor);
            bool methodColumn=path is nameof(RiverSection.MethodShape) or nameof(RiverSection.MethodConnection) or nameof(RiverSection.MethodCoefficientSource);
            column.Visibility=simple&&(technical||methodColumn&&!method)?Visibility.Collapsed:Visibility.Visible;
            if(column.Header is string title)column.Header=path switch
            {
                nameof(RiverSection.Name)=>simple?"Участок реки":"Створ",
                nameof(RiverSection.Distance)=>simple?"От плотины, м":"Удаление, м",
                nameof(RiverSection.NormalDepth)=>simple?"Обычная глубина, м":"H быт., м",
                nameof(RiverSection.InitialVelocity)=>simple?"Обычная скорость, м/с":"V быт., м/с",
                _=>title
            };
        }
        ExpertSummary.Visibility=expert?Visibility.Visible:Visibility.Collapsed;
        ExpertSummary.Text=result==null?"Техническая сводка появится после расчёта.":$"Режим: {result.ModelVersion}\nСтворов: {result.Sections.Count}; шагов: {result.Steps}; расчёт: {result.ElapsedSeconds:F3} с.\n"+(result.HasMassBalance?$"Баланс: {result.BalanceErrorPercent:R} %; подготовка: {result.InitializationHours:R} ч.":"Методический режим: баланс и временные ряды не определяются.");
        if(presentation.ReduceMotion)foreach(var page in new FrameworkElement[]{Overview,ScenarioPage,SectionsPage,ChartsPage,IsoPage,ReportPage,AboutPage,CatalogPage,SourcesPage}){page.BeginAnimation(OpacityProperty,null);page.Opacity=1;page.RenderTransform=null;}
        UpdateGuide();
    }
    private void UpdateGuide()
    {
        if(GuideBanner==null)return;
        GuideBanner.Visibility=presentation.InterfaceMode=="Simple"&&presentation.ShowHints?Visibility.Visible:Visibility.Collapsed;
        int page=Navigation.SelectedIndex;
        var guide=page switch
        {
            1=>("Шаг 2. Опишите аварию","Уровень воды и отметка дна — высоты относительно одного принятого нуля. Объём — сколько воды хранится. Проран — отверстие в плотине. Неизвестные значения нельзя заменять случайными: возьмите их из исходных документов.","Далее: река и берега"),
            2=>("Шаг 3. Опишите реку ниже плотины","Створ — поперечный разрез реки в выбранном месте. Укажите расстояние от плотины, дно, обычную глубину и скорость. Точки берегов показывают, как местность поднимается от реки. Левый и правый берег определяются по направлению течения.","Вернуться к расчёту"),
            3=>("Шаг 4. Разберите результаты","Расход — объём воды, проходящий за секунду. Глубина — расстояние от дна до воды. Фронт — приход волны, гребень — её максимум, хвост — завершение. «Не определено» не означает ноль. Прочитайте предупреждения и проверьте исходные данные.","Далее: сохранить отчёт"),
            5=>("Шаг 5. Сохраните результат","DOCX — отчёт для Word, XLSX — таблицы для Excel, PNG — рисунок. Проект и отчёт — разные файлы: Ctrl+S сохраняет исходные данные и историю. Источники не заменяют инженерной проверки результатов.","К обзору проекта"),
            _=>("С чего начать","1. Можно открыть готовый пример, чтобы познакомиться с программой. 2. Для своего объекта заполните данные плотины и реки. 3. Нажмите «Рассчитать». 4. Изучите предупреждения и только затем сохраняйте отчёт. Пример — не модель вашей плотины.","Начать: данные аварии")
        };
        GuideTitle.Text=guide.Item1;GuideBody.Text=guide.Item2;GuideNext.Content=guide.Item3;
    }
    private void GuideNext_Click(object sender,RoutedEventArgs e)=>Navigation.SelectedIndex=Navigation.SelectedIndex switch{1=>2,2=>0,3=>5,5=>0,_=>1};
    private void Glossary_Click(object sender,RoutedEventArgs e)
    {
        var window=new Window{Owner=this,Title="RiverGuard · простыми словами",Width=740,Height=620,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        window.Content=new ScrollViewer{Margin=new Thickness(28),Content=new TextBlock{Text="Водохранилище — запас воды перед плотиной.\n\nНижний бьеф — река ниже плотины.\n\nПроран — образовавшееся отверстие в плотине. Его размеры не равны размерам плотины.\n\nСтвор — выбранное поперечное сечение реки. Несколько створов позволяют описать разные места вниз по течению.\n\nАбсолютная отметка — высота относительно принятого нуля, а глубина — разность уровня воды и дна. Все отметки должны быть в одной системе.\n\nРасход Q, м³/с — сколько кубометров воды проходит за секунду. Скорость V, м/с — как быстро движется вода. Это разные величины.\n\nФронт, гребень, хвост — приход, максимум и завершение волны; определения зависят от выбранного метода.\n\nСхема зон — приблизительная интерполяция между створами, не карта местности.\n\nRG-1D — численный расчёт движения воды. Методика 1997 — отдельная экспериментальная реализация формул; требует обоснованных коэффициентов.\n\nКрасивый график и малый баланс ошибки не подтверждают достоверность прогноза. Не используйте программу самостоятельно для решений об эвакуации.",TextWrapping=TextWrapping.Wrap,LineHeight=24}};
        window.ShowDialog();
    }
}
