using System.Windows;
using System.Windows.Markup;
using System.Globalization;
using System.IO;

namespace RiverGuard;

// Запуск и обработка ошибок
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("ru-RU")));
        CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("ru-RU");
        DispatcherUnhandledException+=(s,args)=>
        {
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RiverGuard");Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder,"errors.log"),DateTime.Now+"\n"+args.Exception+"\n");
            MessageBox.Show("Не удалось выполнить действие.\n"+args.Exception.Message,"RiverGuard",MessageBoxButton.OK,MessageBoxImage.Warning);args.Handled=true;
        };
        base.OnStartup(e);
    }
}
