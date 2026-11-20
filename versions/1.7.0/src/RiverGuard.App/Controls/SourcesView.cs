using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RiverGuard.Core;

namespace RiverGuard.Controls;

public sealed class SourcesView : ScrollViewer
{
    public SourcesView()
    {
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        var panel=new StackPanel{Margin=new Thickness(0,0,16,16)};Content=panel;
        panel.Children.Add(new TextBlock{Text="Источники и проверяемость",FontSize=28,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
        panel.Children.Add(new TextBlock{Text="Основания методов, границы применимости и фактические проверки. Наличие публикации не является сертификатом программы и не доказывает точность прогноза для реального объекта.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,20),Foreground=Brushes.BurlyWood});
        foreach(var source in SourceCatalog.Entries)
        {
            var card=new StackPanel{Margin=new Thickness(0,0,0,24)};
            card.Children.Add(new TextBlock{Text=source.Title,FontSize=18,FontWeight=FontWeights.SemiBold,Foreground=Brushes.Turquoise,Margin=new Thickness(0,0,0,10)});
            foreach(var text in new[]{source.Citation,"Использование в программе: "+source.Application,"Ограничения: "+source.Limitations})card.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,LineHeight=21,Margin=new Thickness(0,0,0,8)});
            if(source.Url.Length>0)
            {
                var button=new Button{Content="Открыть источник ↗",ToolTip=source.Url,HorizontalAlignment=HorizontalAlignment.Left};
                button.Click+=(s,e)=>{try{Process.Start(new ProcessStartInfo(source.Url){UseShellExecute=true});}catch(Exception ex){MessageBox.Show("Не удалось открыть браузер: "+ex.Message,"RiverGuard");}};card.Children.Add(button);
            }
            panel.Children.Add(new Border{Child=card,Padding=new Thickness(18),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromRgb(49,67,94)),CornerRadius=new CornerRadius(12),Margin=new Thickness(0,0,0,14)});
        }
        panel.Children.Add(new TextBox{Text="SHA256 исследованного PDF (37 страниц):\n"+SourceCatalog.ScanSha256+"\nИсходный PDF не включён в дистрибутив. Ссылки открываются только по нажатию; фоновой загрузки нет.",IsReadOnly=true,TextWrapping=TextWrapping.Wrap});
    }
}
