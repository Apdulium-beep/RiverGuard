using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RiverGuard;

// Встроенная инструкция переносимого запуска
public sealed class LaunchGuideWindow : Window
{
    internal const string Guide="""
1. Скачайте ZIP Windows x64 из GitHub Releases проекта Apdulium-beep/RiverGuard. Распакуйте архив целиком в доступную вам папку. Не запускайте EXE прямо внутри ZIP и не удаляйте THIRD_PARTY_NOTICES.txt.

2. Запустите RiverGuard.exe. Сборка self-contained: отдельно устанавливать .NET и Microsoft Office для расчёта и создания DOCX/XLSX не требуется. Права администратора не нужны; не выбирайте защищённую папку Program Files для хранения проектов.

3. Если Windows показывает предупреждение о неизвестном издателе, сначала проверьте источник загрузки и SHA256 архива по приложенному файлу. Контрольная сумма проверяет целостность, но не заменяет цифровую подпись издателя. Не отключайте антивирус целиком.

4. На стартовом экране создайте проект или импортируйте старый файл .riverguard. Обычная папка новых проектов — Документы\RiverGuard\Проекты. Импортированный файл остаётся по исходному пути. Прежние проекты SchemaVersion 1 открываются новой версией; редактирование нового проекта в старом EXE может потерять новые поля и историю.

5. Задайте исходные данные и исполнителя, нажмите F5. Перед аварией устанавливается бытовое течение. Примеры синтетические: проверьте отдельную отметку дна нижнего бьефа, бытовой приток, объём и геометрию. При незавершённом спаде увеличьте расчётное время. Модель не воспроизводит алгоритм «Волны» и не является основанием для эвакуации.

6. Enter — следующее поле, Shift+Enter — предыдущее. В таблицах Enter завершает ячейку, переходит дальше, в конце последней строки создаёт новую. Ctrl+Enter вставляет строку в заметках. Ctrl+S сохраняет проект. При просмотре числа имеют три знака; в редакторе числовой ячейки показывается полное значение, внутренние данные не округляются.

7. Окно «История расчётов» хранит входные снимки и сводки запусков, позволяет сравнить два запуска и взять данные в новый расчёт. В существующем файле запись сохраняется автоматически после попытки расчёта; для ещё не сохранённого проекта история остаётся в памяти до Ctrl+S. Не перезаписывайте личные проекты тестовыми примерами.

8. В «Анализе и графиках» выберите «Схема зон в плане». Её можно сохранить в PNG; схема и графики всех створов входят в DOCX/HTML/ZIP. Это схема с разными масштабами по осям, не ГИС-карта. Площади считаются за бытовыми берегами между заданными створами.

9. DOCX создаётся без Word, XLSX — без Excel. Для PDF экспортируйте HTML и используйте печать браузера. Поле «Исполнитель» задаётся до расчёта и входит в снимок исходных данных.

10. Автоматическая проверка обновлений по умолчанию выключена. Ручная кнопка есть на стартовом экране и в разделе «Модель и ограничения». Включение фоновой проверки — только по вашему выбору. Обновления устанавливаются отдельно; существующие проекты и прежний EXE не удаляются.

Сборка рассчитана на Windows 10/11 x64. Локальный запуск проверяется без повышения прав; отдельные испытания на чистых Windows 10/11 без установленного SDK/Office пока не выполнены.

Автор приложения: Apdulium-beep.
""";
    public LaunchGuideWindow()
    {
        Title="RiverGuard · Инструкция запуска";Width=820;Height=730;MinWidth=600;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=(Brush)Application.Current.FindResource("Bg");Foreground=(Brush)Application.Current.FindResource("Ink");FontFamily=new FontFamily("Segoe UI");
        var panel=new DockPanel{Margin=new Thickness(26)};var title=new TextBlock{Text="Запуск и работа",FontSize=26,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,18)};DockPanel.SetDock(title,Dock.Top);panel.Children.Add(title);
        var close=new Button{Content="Закрыть",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};close.Click+=(s,e)=>Close();DockPanel.SetDock(close,Dock.Bottom);panel.Children.Add(close);
        panel.Children.Add(new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new TextBlock{Text=Guide,FontSize=13,LineHeight=22,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,16,0)}});Content=panel;
    }
}
