using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
namespace Bed.Editor.ImGui;
using Im=Brutal.ImGuiApi.ImGui;
public static partial class Ui
{
    // Capture before a native popup changes current window/clip. Input contents retain native horizontal
    // scrolling and their complete semantic Value; this evidence measures the fixed caption beside them.
    // Submit navigation and layout even at a scroll edge, but paint a control only when its full
    // height is in view. A partial last line otherwise looks like a broken caption. Native focus
    // scrolling brings the same submitted item into view; it is not removed from the navigation list.
    private readonly struct CompleteControlClip:IDisposable
    {
        private readonly bool pushed;
        private readonly ImGuiWindowPtr window;
        private readonly ImDrawListPtr draw;
        internal CompleteControlClip(float height)
        {
            window=Im.Internal.GetCurrentWindow();draw=Im.GetWindowDrawList();
            var list=draw;var a=list.GetClipRectMin();var b=list.GetClipRectMax();var y=Im.GetCursorScreenPos().Y;
            pushed=window.DC.LayoutType!=ImGuiLayoutType.Horizontal&&height<=b.Y-a.Y&&y<b.Y&&y+height>a.Y&&(y<a.Y||y+height>b.Y);
            if(pushed)Im.PushClipRect(new(a.X,b.Y),b,true);
        }
        public void Dispose(){if(pushed){draw.PopClipRect();var clip=window.ClipRect;clip.Min=draw.GetClipRectMin();clip.Max=draw.GetClipRectMax();window.ClipRect=clip;}}
    }
    internal static void FieldCaption(ImString text,string? semanticId=null)
    {
        var height=Im.CalcTextSize(Label(text,false),false,TextWrapWidth(true)).Y+Im.Internal.GetCurrentWindow().DC.CurrLineTextBaseOffset;
        using var complete=new CompleteControlClip(height);TextWrapped(text,semanticId);
    }
    internal static float WrappedSelectableHeight(ImString label,float width)=>MathF.Max(24*Im.GetStyle().FontScaleDpi,Im.CalcTextSize(Label(label),false,MathF.Max(1,width)).Y);
    internal static bool SelectableWrapped(ImString label,bool selected=false,ImGuiSelectableFlags flags=0,float height=0)
    {
        var origin=Im.GetCursorScreenPos();origin.Y+=Im.Internal.GetCurrentWindow().DC.CurrLineTextBaseOffset;
        var width=MathF.Max(1,MathF.Min(Im.GetContentRegionAvail().X,Im.GetWindowDrawList().GetClipRectMax().X-origin.X));
        var text=Label(label);var size=Im.CalcTextSize(text,false,width);height=MathF.Max(height,WrappedSelectableHeight(label,width));
        using var complete=new CompleteControlClip(height);
        var ink=Im.GetColorU32(ImGuiCol.Text);Im.PushStyleColor(ImGuiCol.Text,0u);
        bool result;try{result=Selectable(label,selected,flags,new float2(0,height));}finally{Im.PopStyleColor();}
        var node=Tree.Count-1;var draw=Im.GetWindowDrawList();draw.AddText(Im.GetFont(),Im.GetFontSize(),origin,ink,text,width);
        var a=draw.GetClipRectMin();var b=draw.GetClipRectMax();RecordCaption(node,CaptionEvidence(new(a.X,a.Y,MathF.Max(0,b.X-a.X),MathF.Max(0,b.Y-a.Y)),new(origin.X,origin.Y,size.X,size.Y)) with {WrapWidth=width});
        return result;
    }
    private static void FitCaption(ImString label)
    {
        var text=Label(label);if(text.Length==0)return;
        var available=Im.GetContentRegionAvail().X-Im.CalcTextSize(text).X-Im.GetStyle().ItemInnerSpacing.X;
        if(Im.CalcItemWidth()>available)Im.SetNextItemWidth(MathF.Max(1,available));
    }
    private static UiTextEvidence NativeCaption(ImString label,float frameWidth=0)
    {
        var text=Label(label);var size=Im.CalcTextSize(text);var origin=Im.GetCursorScreenPos();
        origin.Y+=frameWidth>0?Im.GetStyle().FramePadding.Y:Im.Internal.GetCurrentWindow().DC.CurrLineTextBaseOffset;
        if(frameWidth>0)origin.X+=frameWidth+Im.GetStyle().ItemInnerSpacing.X;
        var list=Im.GetWindowDrawList();var a=list.GetClipRectMin();var b=list.GetClipRectMax();
        var clip=new UiBounds(a.X,a.Y,MathF.Max(0,b.X-a.X),MathF.Max(0,b.Y-a.Y));
        return CaptionEvidence(clip,new(origin.X,origin.Y,size.X,text.Length==0?0:size.Y));
    }
    private static UiTextEvidence TreeCaption(ImString label,ImGuiTreeNodeFlags flags)
    {
        var origin=Im.GetCursorScreenPos();origin.X+=Im.GetTreeNodeToLabelSpacing();
        origin.Y+=(flags&(ImGuiTreeNodeFlags.Framed|ImGuiTreeNodeFlags.FramePadding))!=0?Im.GetStyle().FramePadding.Y:Im.Internal.GetCurrentWindow().DC.CurrLineTextBaseOffset;
        var size=Im.CalcTextSize(Label(label));var list=Im.GetWindowDrawList();var a=list.GetClipRectMin();var b=list.GetClipRectMax();
        return CaptionEvidence(new(a.X,a.Y,MathF.Max(0,b.X-a.X),MathF.Max(0,b.Y-a.Y)),new(origin.X,origin.Y,size.X,size.Y));
    }
    private static UiTextEvidence ComboPreview(ImString preview,float width,ImGuiComboFlags flags)
    {
        var padding=Im.GetStyle().FramePadding;var origin=Im.GetCursorScreenPos()+padding;
        var size=(flags&ImGuiComboFlags.NoPreview)!=0?default:Im.CalcTextSize(Label(preview,false,true));
        var a=Im.GetWindowDrawList().GetClipRectMin();var b=Im.GetWindowDrawList().GetClipRectMax();
        var arrow=(flags&ImGuiComboFlags.NoArrowButton)!=0?0:Im.GetFrameHeight();
        var clip=new UiBounds(MathF.Max(a.X,origin.X),MathF.Max(a.Y,origin.Y),0,0);
        clip=clip with {Width=MathF.Max(0,MathF.Min(b.X,origin.X+width-arrow-2*padding.X)-clip.X),Height=MathF.Max(0,MathF.Min(b.Y,origin.Y+size.Y)-clip.Y)};
        return CaptionEvidence(clip,new(origin.X,origin.Y,size.X,size.Y));
    }
    private static UiTextEvidence CaptionEvidence(UiBounds clip,UiBounds full)=>new(clip,full,-1,!full.IsEmpty&&(clip.IsEmpty||full.X<clip.X||full.Y<clip.Y||full.X+full.Width>clip.X+clip.Width+.01f||full.Y+full.Height>clip.Y+clip.Height+.01f));
    private static UiTextEvidence CombineCaption(UiTextEvidence caption,UiTextEvidence preview)
    {
        if(caption.FullBounds.IsEmpty)return preview;if(preview.FullBounds.IsEmpty)return caption;
        return caption with {FullBounds=caption.FullBounds.Union(preview.FullBounds),Truncated=caption.Truncated||preview.Truncated};
    }
    private static void RecordCaption(int node,UiTextEvidence evidence)
    {
        Tree.Update(node,Tree.At(node) with {TextEvidence=evidence});
        if(evidence.Truncated)
        {
            var item=Tree.At(node);var full=item.Value is {Length:>0}?item.Label+": "+item.Value:item.Label;
            Tree.Update(node,item with {Tooltip=full});

        }
    }
}
