using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace RiverGuard.Controls;

// Три знака при просмотре без округления редактируемого значения
public sealed class PrecisionTextColumn : DataGridTextColumn
{
    protected override FrameworkElement GenerateEditingElement(DataGridCell cell,object dataItem)
    {
        var element=base.GenerateEditingElement(cell,dataItem);
        if(element is TextBox box&&Binding is Binding source)
            box.SetBinding(TextBox.TextProperty,new Binding(source.Path.Path){Mode=BindingMode.TwoWay,UpdateSourceTrigger=source.UpdateSourceTrigger,StringFormat="R",ValidatesOnExceptions=true,ConverterCulture=source.ConverterCulture});
        return element;
    }
}
