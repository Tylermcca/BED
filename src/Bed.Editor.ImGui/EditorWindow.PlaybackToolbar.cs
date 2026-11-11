using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;
public sealed partial class EditorWindow<TDoc> where TDoc:class
{
    private void DrawPlaybackToolbar()
    {
        var style=ImGui.GetStyle();
        if(Ui.BeginPlaybackBar(ImGui.GetFrameHeight()+style.WindowPadding.Y*2))
        {
            var actions=UiActions();var playId=ProjectPlayAction is not null?PlayAction+"project":null;
            var modes=Editor.PlayModes;
            if(playId is null&&modes.Count>0){var selected=modes.FirstOrDefault(m=>m.Id==toolbarPlay)??modes[0];toolbarPlay=selected.Id;playId=PlayAction+toolbarPlay;}
            var resume=actions.FirstOrDefault(a=>a.Id=="editor.resume_simulation")?.Present(UiActionContext.Palette).Enabled==true;
            var labels=new List<string>{"Play"};if(ProjectBuildAndPlayAction is not null)labels.Add("Build and Play");labels.AddRange([resume?"Resume":"Pause","Step","Stop"]);
            var width=labels.Sum(label=>ImGui.CalcTextSize(label).X+style.FramePadding.X*2)+style.ItemSpacing.X*(labels.Count-1);
            if(ProjectPlayAction is null&&modes.Count>1)width+=140+style.ItemSpacing.X;
            ImGui.SetCursorPosX(MathF.Max(style.WindowPadding.X,(ImGui.GetWindowSize().X-width)/2));
            if(ProjectPlayAction is null&&modes.Count>1)
            {
                ImGui.SetNextItemWidth(140);
                Ui.NameNextItem("##playback.mode"u8,"Play mode");
                if(Ui.BeginCombo("##playback.mode"u8,modes.First(m=>m.Id==toolbarPlay).Title))
                {foreach(var mode in modes)if(Ui.Selectable(mode.Title,mode.Id==toolbarPlay))toolbarPlay=mode.Id;Ui.EndCombo();}
                playId=PlayAction+toolbarPlay;ImGui.SameLine();
            }
            void Button(string? id,string label)
            {
                var action=actions.FirstOrDefault(a=>a.Id==id);var view=action?.Present(UiActionContext.Palette);
                var enabled=view?.Enabled==true;var reason=view?.Reason??"This project has not supplied this control.";
                Ui.BeginDisabled(!enabled,reason);if(Ui.Button(label))action?.Invoke();Ui.EndDisabled();
                Tooltips.Show(enabled?view!.Value.Label+(action?.Shortcut is {} shortcut?" ("+shortcut+")":""):reason);
            }
            Button(playId,"Play");
            if(ProjectBuildAndPlayAction is not null){ImGui.SameLine();Button("editor.build_play","Build and Play");}
            ImGui.SameLine();Button(resume?"editor.resume_simulation":"editor.pause_simulation",resume?"Resume":"Pause");
            ImGui.SameLine();Button("editor.step_simulation","Step");ImGui.SameLine();Button("editor.stop","Stop");
        }
        Ui.End();
    }
}
