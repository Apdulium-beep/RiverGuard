using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RiverGuard.Core;

namespace RiverGuard;

public partial class MainWindow
{
    private async Task VerifyTableThemeAsync(string directory)
    {
        var checks=new List<string>();void Check(bool value,string text){if(!value)throw new InvalidDataException(text);checks.Add("PASS "+text);}
        SetProject(Method1997Examples.Control());Navigation.SelectedIndex=2;UpdateLayout();await Task.Delay(350);
        foreach(var (grid,label) in new[]{(SectionsGrid,"Sections"),(LeftGrid,"Left bank"),(RightGrid,"Right bank")})
        {
            Check(grid.Columns.All(c=>c.ReadLocalValue(DataGridColumn.HeaderStyleProperty)==DependencyProperty.UnsetValue),label+" columns do not suppress implicit header theme with local null");
            grid.ScrollIntoView(grid.Items[0],grid.Columns[0]);grid.UpdateLayout();
            var headers=Descendants<DataGridColumnHeader>(grid).Where(h=>h.Column!=null).ToArray();
            Check(headers.Length>0&&headers.All(h=>h.Background is SolidColorBrush b&&b.Color==Color.FromRgb(27,43,66)&&h.Foreground is SolidColorBrush ink&&ink.Color==Color.FromRgb(164,188,216)&&h.Template.FindName("HeaderSurface",h) is Border),label+" numeric and text headers use dark surface and legible text");
            Check(headers.All(h=>h.Template.FindName("PART_RightHeaderGripper",h) is Thumb),label+" headers retain resizing grips");
        }
        var number=Descendants<DataGridColumnHeader>(SectionsGrid).First(h=>h.Column==SectionsGrid.Columns[1]);
        SectionsGrid.Columns[1].SortDirection=ListSortDirection.Descending;SectionsGrid.UpdateLayout();
        Check(number.Template.FindName("SortArrow",number) is System.Windows.Shapes.Path arrow&&arrow.Visibility==Visibility.Visible&&number.Background is SolidColorBrush brush&&brush.Color==Color.FromRgb(27,43,66),"Sorted numeric header retains dark theme and visible sort indicator");
        SectionsGrid.Columns[1].SortDirection=null;
        Screenshot(Path.Combine(directory,"table-theme-sections.png"));
        var first=Descendants<DataGridColumnHeader>(SectionsGrid).First(h=>h.Column==SectionsGrid.Columns[0]);
        first.IsEnabled=false;SectionsGrid.UpdateLayout();
        Check(first.Foreground is SolidColorBrush disabled&&disabled.Color==Color.FromRgb(125,145,170),"Disabled header preserves readable themed foreground");first.IsEnabled=true;
        File.WriteAllLines(Path.Combine(directory,"table-theme-checks.txt"),checks);
    }
}
