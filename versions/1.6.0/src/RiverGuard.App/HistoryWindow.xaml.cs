using System.Windows;
using System.Windows.Controls;
using RiverGuard.Core;

namespace RiverGuard;

// Просмотр и сопоставление сохранённых запусков
public partial class HistoryWindow : Window
{
    public ProjectData? ChosenInput {get;private set;}
    public HistoryWindow(ProjectData project)
    {
        InitializeComponent();var records=project.CalculationHistory.OrderByDescending(r=>r.Created).ToList();FirstRun.ItemsSource=records;SecondRun.ItemsSource=records;
        FirstRun.SelectedIndex=records.Count>0?0:-1;SecondRun.SelectedIndex=records.Count>1?1:records.Count>0?0:-1;
        HistoryHint.Text=records.Count==0?"История пуста. Выполните расчёт.":$"Записей: {records.Count}. Сопоставление створов — по имени и удалению.";
        RefreshComparison();
    }
    private void Selection_Changed(object sender,SelectionChangedEventArgs e)=>RefreshComparison();
    internal void RefreshComparison()
    {
        if(ComparisonGrid==null||FirstRun==null||SecondRun==null)return;
        TakeInputButton.IsEnabled=FirstRun.SelectedItem is CalculationRecord;
        ComparisonGrid.ItemsSource=FirstRun.SelectedItem is CalculationRecord a&&SecondRun.SelectedItem is CalculationRecord b?HistoryComparison.Compare(a,b):null;
    }
    private void TakeInput_Click(object sender,RoutedEventArgs e)
    {
        if(FirstRun.SelectedItem is not CalculationRecord record)return;
        ChosenInput=record.Input.CopyInputs();ChosenInput.Name+=" · из истории "+record.Created.ToString("dd.MM HH:mm");Close();
    }
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
}
