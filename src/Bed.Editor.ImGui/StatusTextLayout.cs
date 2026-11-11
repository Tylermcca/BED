using System.Globalization;
using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;

// Single-line notices stay single-line. Preserve explicit line breaks and complete semantic text;
// only the painted prefix changes. StringInfo keeps surrogate pairs/combining sequences intact.
internal static class StatusTextLayout
{
    internal static string Fit(string text,float width,float? measuredWidth=null)
    {
        width=MathF.Max(0,width);
        if((measuredWidth??Brutal.ImGuiApi.ImGui.CalcTextSize(text).X)<=width)return text;
        var lines=text.Split('\n');
        for(var i=0;i<lines.Length;i++)lines[i]=Line(lines[i],width);
        return string.Join('\n',lines);
    }
    private static string Line(string text,float width)
    {
        if(Brutal.ImGuiApi.ImGui.CalcTextSize(text).X<=width)return text;
        var suffix=Brutal.ImGuiApi.ImGui.CalcTextSize("..."u8).X<=width?"...":Brutal.ImGuiApi.ImGui.CalcTextSize("."u8).X<=width?".":"";
        if(suffix.Length==0)return "";
        var starts=StringInfo.ParseCombiningCharacters(text);var low=0;var high=starts.Length;
        while(low<high)
        {
            var middle=(low+high+1)/2;var end=middle==starts.Length?text.Length:starts[middle];
            if(Brutal.ImGuiApi.ImGui.CalcTextSize(text[..end]+suffix).X<=width)low=middle;else high=middle-1;
        }
        var cut=low==starts.Length?text.Length:starts[low];
        return text[..cut]+suffix;
    }
}
