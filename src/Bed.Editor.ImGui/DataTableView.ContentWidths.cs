using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;
public sealed partial class DataTableView
{
    private object? widthsFor;private string? widthsIdentity;private float widthsFont;private float[] widths=[];
    // Bounded initial-content measurement; native settings keep the person's later resize/order choices.
    private float[] ContentWidths(string identity,IReadOnlyList<string> visible,IReadOnlyList<InspectorRow> columns)
    {
        var document=Editor.Session.Current;var font=ImGui.GetFontSize();
        if(ReferenceEquals(widthsFor,document)&&widthsIdentity==identity&&widthsFont==font&&widths.Length==columns.Count+1)return widths;
        widthsFor=document;widthsIdentity=identity;widthsFont=font;var style=ImGui.GetStyle();var pad=style.FramePadding.X*2+style.CellPadding.X*2+2;
        float Measure(string value)=>ImGui.CalcTextSize(value.Length>200?value[..200]:value).X;
        widths=new float[columns.Count+1];widths[0]=Measure("Item")+pad;
        for(var c=0;c<columns.Count;c++)
        {
            widths[c+1]=Measure(columns[c].Label)+pad;
            if(columns[c].Kind==PropertyKind.Toggle)widths[c+1]=MathF.Max(widths[c+1],ImGui.GetFrameHeight());
            if(columns[c].Kind==PropertyKind.Choice)
                foreach(var choice in columns[c].Choices.Take(128))widths[c+1]=MathF.Max(widths[c+1],Measure(choice)+ImGui.GetFrameHeight()+pad);
        }
        foreach(var item in visible.Take(32))
        {
            widths[0]=MathF.Max(widths[0],MathF.Min(240*font/16,Measure(RowLabel(document,item)))+pad);
            var cells=RowsOf(item);
            for(var c=0;c<columns.Count;c++)if(cells.TryGetValue(columns[c].Id,out var cell)&&cell.Value is {} value&&cell.Kind is PropertyKind.Text or PropertyKind.Number or PropertyKind.Integer or PropertyKind.Choice)
                widths[c+1]=MathF.Max(widths[c+1],MathF.Min(240*font/16,Measure(AsText(value)))+pad+(cell.Kind==PropertyKind.Choice?ImGui.GetFrameHeight():0)+(LinkOf(item,cell.Id) is not null?Measure("->")+style.FramePadding.X*2+style.ItemSpacing.X:0));
        }
        return widths;
    }
}
