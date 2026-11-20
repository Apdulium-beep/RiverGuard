using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RiverGuard.Controls;

namespace RiverGuard;

// Только оформление: никаких обращений к модели проекта или сетевых запросов.
internal static class ThemeService
{
    private static readonly Dictionary<string,(Color Color,string Role)> originals=new();
    internal static string Current {get;private set;}="Ocean";
    private static Color C(string text)=>(Color)ColorConverter.ConvertFromString(text);
    internal static void Initialize()
    {
        foreach(string hex in new[]{"0C1220","141E30","20314A","0F192A","276C73","111C2D","152237","1B2B42","263E58","214A5B","19283E","2B495E","20504F","344B65","4F9E9B","1C3946"})Register(C("#"+hex),"Bg");
        foreach(string hex in new[]{"EDF4FF","91A4BE","A4BCD8","7D91AA","83A3BF","93A8C1","65F0D2"})Register(C("#"+hex),"Fg");
        foreach(string hex in new[]{"26344B","334864","31435E","213148","2A3C55","3A516B"})Register(C("#"+hex),"Line");
        Apply("Ocean",13);
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,e)=>{if(ReferenceEquals(sender,e.OriginalSource)&&sender is Window window)Prepare(window);}));
        EventManager.RegisterClassHandler(typeof(FrameworkElement),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,e)=>{if(ReferenceEquals(sender,e.OriginalSource)&&sender is FrameworkElement element)PrepareElement(element);}));
    }
    internal static Color Transform(Color color,string role="Fg")
    {
        if(Current=="Ocean"||color.A==0)return color;
        double brightness=(color.R*.2126+color.G*.7152+color.B*.0722)/255;
        double spread=(Math.Max(color.R,Math.Max(color.G,color.B))-Math.Min(color.R,Math.Min(color.G,color.B)))/255d;
        Color value=color;
        if(Current=="Light")
        {
            if(role=="Line")value=C("#C4D1E1");
            else if(role=="Bg")
            {
                string hex=$"#{color.R:X2}{color.G:X2}{color.B:X2}";
                string? surface=hex switch{"#0C1220"=>"#EFF3F8","#141E30" or "#111C2D" or "#0F192A"=>"#FFFFFF","#152237"=>"#F2F6FB","#1B2B42"=>"#DFE8F3","#20314A"=>"#E4EBF5","#101A2C"=>"#E6EDF6","#0F1727"=>"#EDF2F8","#214A5B"=>"#D1E9EF","#1C3946"=>"#D7EDE7","#263E58"=>"#E0EAF6","#2B251E"=>"#FFF4DE",_=>null};
                if(surface!=null)value=C(surface);
                else if(brightness<.42){int shade=(int)Math.Clamp(255-brightness*60,227,252);value=Color.FromRgb((byte)(shade-3),(byte)(shade-1),(byte)shade);}
                else value=color;
            }
            else if(brightness>.42)
            {
                if(spread>.18){double factor=.42;value=Color.FromRgb((byte)(color.R*factor),(byte)(color.G*factor),(byte)(color.B*factor));}
                else value=C("#263B54");
            }
            else if(brightness>.2)value=C("#4A607A");
        }
        else if(role=="Bg"||role=="Line")
        {
            int shade=(int)Math.Clamp(brightness*255,12,110);
            value=Current=="Graphite"?Color.FromRgb((byte)shade,(byte)shade,(byte)(shade+3)):Color.FromRgb((byte)Math.Max(8,shade-9),(byte)(shade+7),(byte)Math.Max(10,shade-1));
        }
        else if(spread>.24&&color.G>color.R*1.1)value=Current=="Graphite"?C("#A7C7FF"):C("#A4DCA7");
        value.A=color.A;return value;
    }
    private static string Register(Color color,string role)
    {
        string key="T"+role+color.ToString().Replace("#FF","").Replace("#","");
        if(!originals.ContainsKey(key)){originals[key]=(color,role);Application.Current.Resources[key]=new SolidColorBrush(Transform(color,role));}
        return key;
    }
    internal static void SetForeground(FrameworkElement element,Color color)=>element.SetResourceReference(TextBlock.ForegroundProperty,Register(color,"Fg"));
    internal static void Apply(string theme,double textSize)
    {
        Current=theme;
        foreach(var entry in originals)Application.Current.Resources[entry.Key]=new SolidColorBrush(Transform(entry.Value.Color,entry.Value.Role));
        foreach(var (key,hex,role) in new[]{("Bg","#0C1220","Bg"),("Card","#141E30","Bg"),("Line","#26344B","Line"),("Muted","#91A4BE","Fg"),("Ink","#EDF4FF","Fg")})Application.Current.Resources[key]=new SolidColorBrush(Transform(C(hex),role));
        Application.Current.Resources["Accent"]=new SolidColorBrush(C(theme=="Light"?"#087E75":theme=="Graphite"?"#A7C7FF":theme=="Forest"?"#91D59A":"#4CE0C1"));
        Application.Current.Resources["PrimaryInk"]=new SolidColorBrush(C(theme=="Light"?"#FFFFFF":"#092A28"));
        Application.Current.Resources["UiTextSize"]=textSize;
        Application.Current.Resources["UiHeaderHeight"]=Math.Max(42,(textSize+3)*2+8);
        foreach(Window window in Application.Current.Windows){Prepare(window);foreach(var chart in Walk(window).OfType<ChartView>())chart.Refresh();foreach(var plan in Walk(window).OfType<ZonePlanView>())plan.Refresh();}
    }
    internal static void Prepare(Window window)
    {
        foreach(var element in Walk(window).OfType<FrameworkElement>())PrepareElement(element);
    }
    private static void PrepareElement(FrameworkElement element)
    {
            if(element is DataGrid grid)grid.SetResourceReference(DataGrid.ColumnHeaderHeightProperty,"UiHeaderHeight");
            var locals=element.GetLocalValueEnumerator();var replacements=new List<(DependencyProperty Property,Color Color,string Role)>();
            var aliases=new List<(DependencyProperty Property,string Key)>();
            while(locals.MoveNext())
            {
                var entry=locals.Current;
                if(entry.Value is not SolidColorBrush brush||brush.Color.A==0)continue;
                string? role=entry.Property.Name switch{"Background" or "RowBackground" or "AlternatingRowBackground"=>"Bg","Foreground" or "Fill" or "Stroke"=>"Fg","BorderBrush"=>"Line",_=>null};
                if(role==null)continue;
                // Новые окна могут получить уже преобразованную общую кисть.
                string? alias=new[]{"Bg","Card","Ink","Muted","Line","Accent","PrimaryInk"}.FirstOrDefault(key=>ReferenceEquals(brush,Application.Current.Resources[key]));
                if(alias!=null)
                {
                    aliases.Add((entry.Property,alias));continue;
                }
                Color original=brush.Color;
                if(Current!="Ocean"&&!originals.Values.Any(v=>v.Color==original&&v.Role==role))
                {
                    var match=originals.Values.FirstOrDefault(v=>v.Role==role&&Transform(v.Color,role)==original);
                    if(match.Role!=null)original=match.Color;
                }
                replacements.Add((entry.Property,original,role));
            }
            foreach(var entry in aliases)element.SetResourceReference(entry.Property,entry.Key);
            foreach(var entry in replacements)element.SetResourceReference(entry.Property,Register(entry.Color,entry.Role));
    }
    internal static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        var seen=new HashSet<DependencyObject>();var queue=new Queue<DependencyObject>();queue.Enqueue(root);
        while(queue.Count>0)
        {
            var item=queue.Dequeue();if(!seen.Add(item))continue;yield return item;
            foreach(var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())queue.Enqueue(child);
            if(item is Visual||item is System.Windows.Media.Media3D.Visual3D)for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)queue.Enqueue(VisualTreeHelper.GetChild(item,i));
        }
    }
}
