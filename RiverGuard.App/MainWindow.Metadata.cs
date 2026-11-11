using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RiverGuard;

public partial class MainWindow
{
    // Свойства проекта
    private void RenameProject_Click(object s,RoutedEventArgs e)
    {
        if(!hasActiveProject||cancellation!=null)return;
        RenameText.Text=vm.Project.Name;RenameError.Visibility=Visibility.Collapsed;
        WorkspaceSidebar.IsEnabled=WorkspaceHeader.IsEnabled=Pages.IsEnabled=false;
        RenameOverlay.Visibility=Visibility.Visible;RenameText.Focus();RenameText.SelectAll();
    }
    private void CancelRename_Click(object s,RoutedEventArgs e)
    {RenameOverlay.Visibility=Visibility.Collapsed;ShowWorkspace();}
    private void ApplyRename_Click(object s,RoutedEventArgs e)
    {
        string name=RenameText.Text.Trim();
        if(name.Length==0){RenameError.Text="Название не может быть пустым.";RenameError.Visibility=Visibility.Visible;RenameText.Focus();return;}
        vm.Project.Name=name;RenameOverlay.Visibility=Visibility.Collapsed;ShowWorkspace();
        Status.Text="Название изменено · Ctrl+S — сохранить проект";Status.Foreground=new SolidColorBrush(Amber);
    }
    private void Rename_KeyDown(object s,KeyEventArgs e)
    {
        if(e.Key==Key.Escape){CancelRename_Click(this,new());e.Handled=true;}
        if(e.Key==Key.Enter||(e.Key==Key.S&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control))){ApplyRename_Click(this,new());e.Handled=true;}
    }
}
