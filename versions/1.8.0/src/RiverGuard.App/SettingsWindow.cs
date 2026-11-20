using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace RiverGuard;

internal sealed class SettingsWindow : Window
{
    internal ApplicationPreferences Draft {get;}
    internal ComboBox ThemeBox {get;}=new();
    internal ComboBox ModeBox {get;}=new();
    internal Button SaveButton {get;}
    private readonly Action<ApplicationPreferences> preview;
    internal SettingsWindow(ApplicationPreferences current,Action<ApplicationPreferences> preview)
    {
        this.preview=preview;Draft=current.Copy();DataContext=Draft;
        SetResourceReference(BackgroundProperty,"Bg");SetResourceReference(ForegroundProperty,"Ink");SetResourceReference(FontSizeProperty,"UiTextSize");
        Title="RiverGuard · Настройки";Width=790;Height=760;MinWidth=650;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var dock=new DockPanel{Margin=new Thickness(28)};Content=dock;
        var heading=new TextBlock{Text="Сделайте программу удобной для себя",FontSize=25,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)};DockPanel.SetDock(heading,Dock.Top);dock.Children.Add(heading);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};
        var save=new Button{Content="Сохранить настройки",Style=(Style)FindResource("Primary"),IsDefault=true};SaveButton=save;save.Click+=(s,e)=>{Draft.Normalize();DialogResult=true;};
        var cancel=new Button{Content="Отмена",IsCancel=true};buttons.Children.Add(save);buttons.Children.Add(cancel);DockPanel.SetDock(buttons,Dock.Bottom);dock.Children.Add(buttons);
        var panel=new StackPanel{Margin=new Thickness(0,0,14,0)};dock.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        void Text(string text,bool title=false){panel.Children.Add(new TextBlock{Text=text,FontSize=title?17:13,FontWeight=title?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,title?18:6,0,10)});}
        Text("Тема и режим меняются сразу для предварительного просмотра. «Отмена» вернёт прежний вид. Исходные данные, формулы и результаты расчёта не изменяются.");
        Text("Цветовая тема",true);Fill(ThemeBox,new[]{("Ocean","Океан · исходная тёмная"),("Light","Светлая · для дневной работы"),("Graphite","Графит · нейтральная тёмная"),("Forest","Лес · мягкая зелёная")},Draft.Theme);panel.Children.Add(ThemeBox);
        Text("Сложность интерфейса",true);Fill(ModeBox,new[]{("Simple","Простой · с чего начать и что делать дальше"),("Standard","Стандартный · привычные разделы"),("Expert","Экспертный · все поля и техническая сводка")},Draft.InterfaceMode);panel.Children.Add(ModeBox);
        Text("Простой режим поясняет термины, показывает шаги работы и убирает часть технических колонок. Сохранённые значения не сбрасываются. Для методики 1997 обязательные коэффициенты остаются видимыми в любом режиме.");
        Text("Читаемость",true);
        var size=new Slider{Minimum=12,Maximum=17,TickFrequency=1,IsSnapToTickEnabled=true,Margin=new Thickness(0,8,0,8)};size.SetBinding(Slider.ValueProperty,new Binding(nameof(Draft.TextSize)){Mode=BindingMode.TwoWay});panel.Children.Add(size);
        var sizeLabel=new TextBlock();sizeLabel.SetBinding(TextBlock.TextProperty,new Binding(nameof(Draft.TextSize)){StringFormat="Размер основного текста: {0:F0}"});panel.Children.Add(sizeLabel);
        Check("Показывать пошаговые пояснения в простом режиме",nameof(Draft.ShowHints));Check("Уменьшить движение · без анимации переходов",nameof(Draft.ReduceMotion));
        Text("Обновления",true);Check("Проверять обновления автоматически при запуске",nameof(Draft.AutomaticUpdates));Text("По умолчанию выключено. Включение опции не запускает загрузку или установку. Ручная проверка остаётся доступна отдельно.");
        var reset=new Button{Content="Вернуть оформление по умолчанию",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,18,0,0)};panel.Children.Add(reset);
        reset.Click+=(s,e)=>{ThemeBox.SelectedValue="Ocean";ModeBox.SelectedValue="Standard";size.Value=13;Draft.ShowHints=true;Draft.ReduceMotion=false;DataContext=null;DataContext=Draft;Preview();};
        ThemeBox.SelectionChanged+=(s,e)=>{Draft.Theme=ThemeBox.SelectedValue as string??"Ocean";Preview();};
        ModeBox.SelectionChanged+=(s,e)=>{Draft.InterfaceMode=ModeBox.SelectedValue as string??"Standard";Preview();};
        size.ValueChanged+=(s,e)=>{Draft.TextSize=size.Value;sizeLabel.Text=$"Размер основного текста: {size.Value:F0}";Preview();};
        void Check(string label,string property)
        {
            var check=new CheckBox{Content=label,Margin=new Thickness(0,12,0,0)};check.SetResourceReference(Control.ForegroundProperty,"Ink");check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding(property){Mode=BindingMode.TwoWay});
            check.Click+=(s,e)=>Preview();panel.Children.Add(check);
        }
    }
    private static void Fill(ComboBox box,(string Key,string Label)[] choices,string selected)
    {
        box.SelectedValuePath="Tag";foreach(var item in choices)box.Items.Add(new ComboBoxItem{Tag=item.Key,Content=item.Label});box.SelectedValue=selected;
    }
    internal void Preview()=>preview(Draft.Copy().Normalize());
}
