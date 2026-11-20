using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using RiverGuard.Core;

namespace RiverGuard;

public partial class MainWindow
{
    private async Task VerifySettingsAsync(string directory)
    {
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new InvalidDataException(text);checks.Add("PASS "+text);}
        var initial=preferences.Copy();var originalSize=(Width,Height);
        try
        {
            var p=Examples.Valley();p.Cells=40;p.DurationHours=.08;SetProject(p);ChartTypeBox.SelectedIndex=0;await RunAsync();
            string input=JsonSerializer.Serialize(p,ProjectData.Json),output=JsonSerializer.Serialize(result,ProjectData.Json);byte[] chart=OverviewChart.Png();
            double L(Color c){double F(byte b){double n=b/255d;return n<=.04045?n/12.92:Math.Pow((n+.055)/1.055,2.4);}return .2126*F(c.R)+.7152*F(c.G)+.0722*F(c.B);}
            foreach(string theme in new[]{"Ocean","Light","Graphite","Forest"})
            {
                ApplyPresentation(new(){Theme=theme});Navigation.SelectedIndex=2;UpdateLayout();await Task.Delay(350);
                Check(ThemeService.Current==theme,theme+" applies without restart");
                var header=Descendants<DataGridColumnHeader>(SectionsGrid).First(h=>h.Column==SectionsGrid.Columns[1]);
                var bg=((SolidColorBrush)header.Background).Color;var fg=((SolidColorBrush)header.Foreground).Color;
                Check((Math.Max(L(bg),L(fg))+.05)/(Math.Min(L(bg),L(fg))+.05)>=4.5,theme+" table header text contrast is at least 4.5 to 1");
                Check(header.Template.FindName("HeaderSurface",header) is Border,theme+" uses themed header template without native white artifacts");
                Check(chart.SequenceEqual(OverviewChart.Png()),theme+" UI theme does not alter exported chart pixels");
                Screenshot(Path.Combine(directory,"settings-theme-"+theme.ToLowerInvariant()+".png"));
            }
            Check(input==JsonSerializer.Serialize(p,ProjectData.Json)&&output==JsonSerializer.Serialize(result,ProjectData.Json),"All themes preserve project inputs history and calculation results exactly");
            var simple=new ApplicationPreferences{Theme="Light",InterfaceMode="Simple",ShowHints=true,ReduceMotion=true,TextSize=15};ApplyPresentation(simple);Navigation.SelectedIndex=1;UpdateLayout();await Task.Delay(100);
            Check(GuideBanner.Visibility==Visibility.Visible&&GuideBody.Text.Contains("Проран")&&GuideNext.Content.ToString()!.Contains("река"),"Simple interface explains terminology and next step");
            Check(AdvancedNumerics.Visibility==Visibility.Collapsed&&SimpleAdvancedNotice.Visibility==Visibility.Visible,"Simple interface warns that hidden advanced values remain active");
            Check(((ListBoxItem)Navigation.Items[4]).Visibility==Visibility.Collapsed&&((ListBoxItem)Navigation.Items[8]).Visibility==Visibility.Visible,"Simple navigation hides advanced isolines but preserves sources");
            Check(FontSize==15&&SectionsGrid.ColumnHeaderHeight>=44,"Text size increases readable header height as well as base font");
            GuideNext_Click(this,new());Check(Navigation.SelectedIndex==2&&GuideBody.Text.Contains("Створ"),"Simple guide moves to river geometry and explains section");
            Navigation.SelectedIndex=3;Check(GuideBody.Text.Contains("Не определено")&&PlayButton.IsEnabled,"Reduced motion preserves result navigation and calculation playback controls");
            ApplyPresentation(new(){InterfaceMode="Simple",ShowHints=false});Check(GuideBanner.Visibility==Visibility.Collapsed&&SimpleAdvancedNotice.Visibility==Visibility.Visible,"Turning off guidance does not hide active-setting notice");
            ApplyPresentation(simple);Navigation.SelectedIndex=0;Width=1150;Height=760;UpdateLayout();await Task.Delay(150);Screenshot(Path.Combine(directory,"settings-simple-compact.png"));Width=originalSize.Width;Height=originalSize.Height;
            Check(OverviewDetails.Visibility==Visibility.Collapsed&&RunButton.IsVisible,"Compact simple overview retains calculation action without complex charts");
            ApplyPresentation(new(){InterfaceMode="Expert",Theme="Graphite"});Check(ExpertSummary.Visibility==Visibility.Visible&&ExpertSummary.Text.Contains(result!.ModelVersion)&&SectionsGrid.Columns.All(c=>c.Visibility==Visibility.Visible),"Expert interface exposes all columns and detailed diagnostics");
            Check(input==JsonSerializer.Serialize(p,ProjectData.Json)&&output==JsonSerializer.Serialize(result,ProjectData.Json),"Interface modes and text size do not recalculate or mutate project");
            SetProject(Method1997Examples.Control());ApplyPresentation(new(){InterfaceMode="Simple"});
            Check(SectionsGrid.Columns.OfType<DataGridBoundColumn>().Where(c=>(c.Binding as System.Windows.Data.Binding)?.Path.Path is nameof(RiverSection.MethodShape) or nameof(RiverSection.MethodConnection) or nameof(RiverSection.MethodCoefficientSource)).All(c=>c.Visibility==Visibility.Visible),"Simple mode never hides required methodological coefficients");
            SetProject(p);ApplyPresentation(initial);Navigation.SelectedIndex=0;
            string prefsPath=Path.Combine(ProjectStorage.Folder,"preferences.json");string? prefsBefore=File.Exists(prefsPath)?File.ReadAllText(prefsPath):null;
            _=Dispatcher.InvokeAsync(()=>{var dialog=OwnedWindows.OfType<SettingsWindow>().Single();dialog.ThemeBox.SelectedValue="Light";dialog.ModeBox.SelectedValue="Simple";dialog.Draft.AutomaticUpdates=true;dialog.Preview();Check(!preferences.AutomaticUpdates,"Preview does not enable actual automatic networking preference");dialog.Close();},DispatcherPriority.Background);
            SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
            Check(presentation.Theme==initial.Theme&&presentation.InterfaceMode==initial.InterfaceMode,"Cancelling settings restores previous visual state");
            Check((File.Exists(prefsPath)?File.ReadAllText(prefsPath):null)==prefsBefore,"Cancelling settings never writes preference file");
            _=Dispatcher.InvokeAsync(()=>{var dialog=OwnedWindows.OfType<SettingsWindow>().Single();dialog.ThemeBox.SelectedValue="Light";dialog.ModeBox.SelectedValue="Simple";ThemeService.Walk(dialog).OfType<Slider>().Single().Value=16;dialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},DispatcherPriority.Background);
            StartSettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);
            var loaded=ApplicationPreferences.Load(ProjectStorage.Folder);Check(loaded.Theme=="Light"&&loaded.InterfaceMode=="Simple"&&!loaded.AutomaticUpdates,"Settings button saves theme and interface without enabling updates");
            Check(loaded.TextSize==16&&FontSize==16,"Text-size slider previews and persists its current value without delay");
            Check(input==JsonSerializer.Serialize(p,ProjectData.Json),"Saving application settings leaves project data unchanged");
            string fixture=Path.Combine(directory,"AppearancePreferencesFixture");Directory.CreateDirectory(fixture);File.WriteAllText(Path.Combine(fixture,"preferences.json"),"{\"Version\":1,\"Theme\":\"unknown\",\"InterfaceMode\":\"unknown\",\"TextSize\":999}");
            var normalized=ApplicationPreferences.Load(fixture);Check(normalized.Theme=="Ocean"&&normalized.InterfaceMode=="Standard"&&normalized.TextSize==17&&!normalized.AutomaticUpdates,"Unknown preferences fall back safely and text size is bounded");
            File.WriteAllText(Path.Combine(fixture,"preferences.json"),"{\"Version\":1,\"AutomaticUpdates\":false}");var legacy=ApplicationPreferences.Load(fixture);
            Check(legacy.Theme=="Ocean"&&legacy.InterfaceMode=="Standard"&&legacy.ShowHints,"Legacy update-only preferences retain safe appearance defaults");
            _=Dispatcher.InvokeAsync(()=>{var dialog=OwnedWindows.OfType<SettingsWindow>().Single();dialog.ThemeBox.SelectedValue="Forest";dialog.ModeBox.SelectedValue="Expert";dialog.UpdateLayout();bool themed=ReferenceEquals(dialog.Background,Application.Current.Resources["Bg"])&&ReferenceEquals(dialog.Foreground,Application.Current.Resources["Ink"]);ScreenshotWindow(dialog,Path.Combine(directory,"settings-dialog.png"));dialog.Close();Check(themed,"Settings dialog inherits the selected theme for its surface and text");},DispatcherPriority.Background);
            SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(SettingsButton.Content.ToString()!.Contains("Настройки")&&StartSettingsButton.Content.ToString()!.Contains("Настройки"),"Settings are accessible from workspace and start screen");
        }
        finally{Width=originalSize.Width;Height=originalSize.Height;preferences=initial;preferences.Save(ProjectStorage.Folder);ApplyPresentation(initial);AutomaticUpdatesBox.IsChecked=initial.AutomaticUpdates;}
        File.WriteAllLines(Path.Combine(directory,"settings-checks.txt"),checks);
    }
    private static void ScreenshotWindow(Window window,string path)
    {
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}
