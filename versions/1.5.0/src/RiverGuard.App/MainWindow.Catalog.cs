using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RiverGuard.Core;

namespace RiverGuard;

public partial class MainWindow
{
    // Инициализация и фильтрация каталога
    private void InitializeCatalog()
    {
        CatalogCategory.ItemsSource=new[]{"Все категории"}.Concat(ScenarioCatalog.All.Select(p=>p.Category).Distinct()).ToArray();
        CatalogCategory.SelectedIndex=0;
        FilterCatalog();
    }
    private void CatalogFilter_Changed(object s,SelectionChangedEventArgs e)=>FilterCatalog();
    private void CatalogSearch_Changed(object s,TextChangedEventArgs e)=>FilterCatalog();
    private void FilterCatalog()
    {
        if(CatalogList==null||CatalogCategory==null||CatalogSearch==null||CatalogCount==null)return;
        string search=CatalogSearch.Text.Trim(),category=CatalogCategory.SelectedItem as string??"Все категории";
        string? selected=(CatalogList.SelectedItem as ScenarioPreset)?.Id;
        var filtered=ScenarioCatalog.All.Where(p=>(category=="Все категории"||p.Category==category)&&(search.Length==0||$"{p.Name} {p.Description} {p.Changes} {p.Category}".Contains(search,StringComparison.OrdinalIgnoreCase))).ToList();
        CatalogList.ItemsSource=filtered;
        CatalogList.SelectedItem=filtered.FirstOrDefault(p=>p.Id==selected)??filtered.FirstOrDefault();
        CatalogCount.Text=$"{filtered.Count} / {ScenarioCatalog.All.Count} сценариев";
        UpdateCatalogPreview();
    }
    // Предварительный просмотр исходных условий
    private void CatalogSelection_Changed(object s,SelectionChangedEventArgs e)=>UpdateCatalogPreview();
    private void UpdateCatalogPreview()
    {
        if(CatalogPreviewName==null||CatalogPreviewParameters==null||ApplyPresetButton==null)return;
        bool selected=CatalogList.SelectedItem is ScenarioPreset;
        ApplyPresetButton.IsEnabled=SavePresetButton.IsEnabled=selected;
        if(CatalogList.SelectedItem is not ScenarioPreset preset)
        {CatalogPreviewName.Text="Ничего не найдено";CatalogPreviewCategory.Text=CatalogPreviewDescription.Text=CatalogPreviewChanges.Text=CatalogPreviewParameters.Text="";return;}
        var p=preset.Create();
        CatalogPreviewCategory.Text=preset.Category.ToUpperInvariant();CatalogPreviewName.Text=preset.Name;
        CatalogPreviewDescription.Text=preset.Description;CatalogPreviewChanges.Text=preset.Changes;
        bool overflow=ScenarioMode.IsOverflow(p.Scenario);
        string opening=overflow?$"Длина гребня: {p.BreachWidth:F3} м\nОтметка гребня: {p.SillElevation:F3} м\nФиксированный гребень, без раскрытия":$"Ширина прорана: {p.BreachWidth:F3} м\nПорог: {p.SillElevation:F3} м\nРаскрытие: {p.FormationMinutes:F1} мин";
        CatalogPreviewParameters.Text=$"Механизм: {p.Scenario}\nУровень / дно: {p.ReservoirLevel:F3} / {p.DamBed:F3} м\nОбъём: {p.ReservoirVolume:F3} млн м³\nПлощадь зеркала: {p.ReservoirArea:F3} км²\n{opening}\nНачальная глубина НБ: {p.LowerDepth:F3} м\nДлительность: {p.DurationHours:F2} ч\nСетка: {p.Cells} ячеек\nСтворов: {p.Sections.Count}";
    }
    // Применение и сохранение шаблонов
    private void ApplyPreset_Click(object s,RoutedEventArgs e)
    {
        if(CatalogList.SelectedItem is not ScenarioPreset preset||!ConfirmReplace())return;
        SetProject(preset.Create());Navigation.SelectedIndex=1;
        Status.Text="Загружен шаблон: "+preset.Name+" · измените параметры или нажмите F5";
    }
    private void SavePreset_Click(object s,RoutedEventArgs e)
    {
        if(CatalogList.SelectedItem is not ScenarioPreset preset)return;
        var dialog=new SaveFileDialog{Filter="Проект RiverGuard|*.riverguard",FileName=SafeName(preset.Name),Title="Сохранить отдельную копию сценария"};
        if(dialog.ShowDialog()!=true)return;
        try{ProjectStorage.Save(preset.Create(),dialog.FileName);Status.Text="Шаблон сохранён: "+dialog.FileName;}catch(Exception ex){Notify("Не удалось сохранить шаблон: "+ex.Message);}
    }
    // Проверка элементов каталога
    private void VerifyCatalogControls()
    {
        CatalogSearch.Text="несуществующий_сценарий_000";
        if(CatalogList.Items.Count!=0||ApplyPresetButton.IsEnabled)throw new InvalidDataException("Фильтр пустого результата каталога не работает.");
        CatalogSearch.Text="";CatalogCategory.SelectedItem="Перелив";
        if(CatalogList.Items.Count!=3)throw new InvalidDataException("Фильтр категории каталога не работает.");
        CatalogCategory.SelectedIndex=0;
        if(CatalogList.Items.Count!=ScenarioCatalog.All.Count)throw new InvalidDataException("Каталог сценариев заполнен не полностью.");
        foreach(var preset in ScenarioCatalog.All)
        {
            CatalogList.SelectedItem=preset;
            if(CatalogPreviewName.Text!=preset.Name||!ApplyPresetButton.IsEnabled)throw new InvalidDataException("Предпросмотр сценария не работает: "+preset.Id);
        }
        CatalogList.SelectedIndex=0;
    }
}
