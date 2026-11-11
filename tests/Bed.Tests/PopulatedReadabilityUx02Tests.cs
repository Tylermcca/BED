using Bed.Editor.ImGui;
using Xunit;
namespace Bed.Tests;
public sealed partial class ReadabilityUx02Tests
{
    public static IEnumerable<object[]> PopulatedMatrix(){foreach(var width in new[]{1280,1920})foreach(var scale in new[]{1f,1.5f,2f})foreach(var theme in new[]{EditorThemePreference.Light,EditorThemePreference.Dark})foreach(var mode in new[]{"data/selected","v1/selected","3d/selected","xy/selected","data/preferences-theme","data/preferences-keymap","data/table-list"})yield return [width,width==1280?720:1080,scale,theme,mode];}
    [Theory][MemberData(nameof(PopulatedMatrix))]
    public void Populated_selections_and_open_choices_are_readable(int width,int height,float scale,EditorThemePreference theme,string mode)=>Audit(width,height,scale,theme,mode);
}
