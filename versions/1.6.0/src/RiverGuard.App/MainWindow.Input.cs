using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RiverGuard.Core;
using RiverGuard.Controls;

namespace RiverGuard;

public partial class MainWindow
{
    // Единый числовой формат представления
    private void ConfigureInput()
    {
        foreach(var box in LogicalDescendants<TextBox>(this).Concat(Descendants<TextBox>(this)).Distinct())
        {
            var binding=BindingOperations.GetBinding(box,TextBox.TextProperty);if(binding?.Path==null||binding.Converter!=null)continue;
            Type? type=typeof(Workspace);
            foreach(string part in binding.Path.Path.Split('.'))type=type?.GetProperty(part)?.PropertyType;
            if(type==typeof(double)||type==typeof(double?))box.SetBinding(TextBox.TextProperty,NumericBinding(binding));
        }
        foreach(var grid in new[]{SectionsGrid,LeftGrid,RightGrid}.Concat(LogicalDescendants<DataGrid>(this)).Distinct())
            foreach(var column in grid.Columns.OfType<DataGridBoundColumn>().ToArray())
                if(column.Binding is Binding binding&&binding.Path!=null&&new[]{typeof(RiverSection),typeof(BankPoint),typeof(SectionResult),typeof(IsoRow)}.Any(t=>t.GetProperty(binding.Path.Path)?.PropertyType is Type p&&(p==typeof(double)||p==typeof(double?))))
                {
                    if(!grid.IsReadOnly&&!column.IsReadOnly&&column is DataGridTextColumn text)
                    {
                        int index=grid.Columns.IndexOf(column);grid.Columns[index]=new PrecisionTextColumn{Header=text.Header,Width=text.Width,MinWidth=text.MinWidth,MaxWidth=text.MaxWidth,Binding=NumericBinding(binding),SortMemberPath=text.SortMemberPath,Visibility=text.Visibility,HeaderStyle=text.HeaderStyle,ElementStyle=text.ElementStyle,EditingElementStyle=text.EditingElementStyle};
                    }
                    else column.Binding=NumericBinding(binding);
                }
        AddHandler(Keyboard.PreviewKeyDownEvent,new KeyEventHandler(Input_KeyDown));
        AddHandler(Keyboard.GotKeyboardFocusEvent,new KeyboardFocusChangedEventHandler((s,e)=>{if(e.NewFocus is TextBox box)Dispatcher.InvokeAsync(()=>{if(box.IsKeyboardFocused)box.SelectAll();},DispatcherPriority.Input);}));
        AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler((s,e)=>{if(e.ChangedButton==MouseButton.Left&&InputAncestor<TextBox>(e.OriginalSource as DependencyObject) is TextBox box&&!box.IsKeyboardFocusWithin){box.Focus();box.SelectAll();e.Handled=true;}}));
    }
    private static Binding NumericBinding(Binding original)=>new(original.Path.Path)
    {
        Mode=original.Mode,UpdateSourceTrigger=original.UpdateSourceTrigger,StringFormat="F3",TargetNullValue=original.TargetNullValue,FallbackValue=original.FallbackValue,
        ValidatesOnExceptions=original.ValidatesOnExceptions,ValidatesOnDataErrors=original.ValidatesOnDataErrors,NotifyOnValidationError=original.NotifyOnValidationError,ConverterCulture=original.ConverterCulture
    };
    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject parent) where T:DependencyObject
    {
        foreach(object item in LogicalTreeHelper.GetChildren(parent))if(item is DependencyObject child){if(child is T target)yield return target;foreach(var nested in LogicalDescendants<T>(child))yield return nested;}
    }
    private static T? InputAncestor<T>(DependencyObject? item) where T:DependencyObject
    {
        while(item!=null){if(item is T target)return target;item=item is Visual||item is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(item):LogicalTreeHelper.GetParent(item);}
        return null;
    }
    // Enter и Shift+Enter в полях и таблицах
    private void Input_KeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key!=Key.Enter||RenameOverlay.Visibility==Visibility.Visible)return;
        if(e.OriginalSource is TextBox multiline&&multiline.AcceptsReturn&&Keyboard.Modifiers==ModifierKeys.Control){int start=multiline.SelectionStart;multiline.SelectedText=Environment.NewLine;multiline.CaretIndex=start+Environment.NewLine.Length;e.Handled=true;return;}
        if((Keyboard.Modifiers&~ModifierKeys.Shift)!=ModifierKeys.None)return;
        bool backwards=Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var grid=InputAncestor<DataGrid>(e.OriginalSource as DependencyObject);
        if(grid!=null&&!grid.IsReadOnly&&(grid==SectionsGrid||grid==LeftGrid||grid==RightGrid))
        {e.Handled=true;if(!e.IsRepeat)AdvanceGrid(grid,backwards);return;}
        if(e.OriginalSource is TextBox box&&!box.IsReadOnly)
        {
            var expression=box.GetBindingExpression(TextBox.TextProperty);if(expression?.IsDirty==true)expression.UpdateSource();e.Handled=true;
            if(System.Windows.Controls.Validation.GetHasError(box))return;
            MoveInputFocus(box,backwards);
        }
    }
    private static void MoveInputFocus(UIElement current,bool backwards)
    {
        var direction=backwards?FocusNavigationDirection.Previous:FocusNavigationDirection.Next;
        for(int i=0;i<100;i++)
        {
            if(!current.MoveFocus(new TraversalRequest(direction)))return;
            if(Keyboard.FocusedElement is TextBox or ComboBox or CheckBox)return;
            if(Keyboard.FocusedElement is not UIElement next)return;current=next;
        }
    }
    internal bool AdvanceGrid(DataGrid grid,bool backwards)
    {
        if(!grid.CommitEdit(DataGridEditingUnit.Cell,true)||!grid.CommitEdit(DataGridEditingUnit.Row,true))return false;
        var columns=grid.Columns.Where(c=>!c.IsReadOnly&&c.Visibility==Visibility.Visible).OrderBy(c=>c.DisplayIndex).ToArray();if(columns.Length==0)return false;
        int row=grid.Items.IndexOf(grid.CurrentCell.Item),column=Array.IndexOf(columns,grid.CurrentCell.Column);row=Math.Max(0,row);column=Math.Max(0,column)+(backwards?-1:1);
        if(column<0){column=columns.Length-1;row--;}
        if(column>=columns.Length){column=0;row++;}
        if(row<0){MoveInputFocus(grid,true);return true;}
        if(row>=grid.Items.Count)
        {
            if(grid==SectionsGrid)
            {
                int number=vm.Project.Sections.Count+1;while(vm.Project.Sections.Any(s=>s.Name=="Створ "+number))number++;
                vm.Project.Sections.Add(new RiverSection{Name="Створ "+number,Distance=vm.Project.Sections.Count>0?vm.Project.Sections.Max(s=>s.Distance)+500:500});
            }
            else if(vm.SelectedSection is RiverSection section)
            {
                var bank=grid==LeftGrid?section.LeftBank:section.RightBank;double width=grid==LeftGrid?section.LeftWidth:section.RightWidth;
                bank.Add(new BankPoint{Distance=bank.Count>0?bank[^1].Distance+25:width+25,Elevation=bank.Count>0?bank[^1].Elevation+2:section.Bed+section.NormalDepth+2});
            }
            else return false;
        }
        object item=grid.Items[row];var target=columns[column];grid.ScrollIntoView(item,target);grid.UpdateLayout();grid.SelectedItem=item;grid.CurrentCell=new DataGridCellInfo(item,target);grid.Focus();
        if(target.GetCellContent(item)?.Parent is DataGridCell cell)cell.Focus();grid.BeginEdit();
        Dispatcher.InvokeAsync(()=>{if(target.GetCellContent(item) is TextBox editor){editor.Focus();editor.SelectAll();}},DispatcherPriority.Input);return true;
    }
}
