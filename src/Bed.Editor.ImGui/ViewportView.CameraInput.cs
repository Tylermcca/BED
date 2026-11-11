using System.Numerics;
using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;
public sealed partial class ViewportView<TDoc> where TDoc:class
{
    private bool cameraNavigating,flying;
    private bool NavigateCamera(bool hovered,Vector2 size)
    {
        var io=ImGui.GetIO();cameraNavigating=false;
        // A captured transform owns all pointer input until it commits or cancels.
        if(moveGesture is not null||rotationGesture is not null||scaleGesture is not null||gesture is not null||drag is not null)return false;
        if(io.WantTextInput||!ImGui.IsWindowFocused()){flying=false;return false;}
        if(hovered&&io.MouseWheel!=0){CancelInput();Camera=Camera.Zoom(MathF.Pow(.85f,io.MouseWheel));cameraNavigating=true;return true;}
        var rightDown=ImGui.IsMouseDown(ImGuiMouseButton.Right);var middleDown=ImGui.IsMouseDown(ImGuiMouseButton.Middle);
        if(!rightDown)flying=false;
        if(!ImGui.IsItemActive()||!(rightDown||middleDown||io.KeyAlt&&ImGui.IsMouseDown(ImGuiMouseButton.Left)))return false;
        cameraNavigating=true;CancelInput();
        var delta=new Vector2(io.MouseDelta.X,io.MouseDelta.Y);
        var right=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,Camera.Forward));var up=Vector3.Cross(Camera.Forward,right);
        if(rightDown&&!io.KeyAlt&&(ImGui.IsKeyDown(ImGuiKey.W)||ImGui.IsKeyDown(ImGuiKey.S)||ImGui.IsKeyDown(ImGuiKey.A)||ImGui.IsKeyDown(ImGuiKey.D)||ImGui.IsKeyDown(ImGuiKey.E)||ImGui.IsKeyDown(ImGuiKey.Q)))flying=true;
        if(middleDown||rightDown&&io.KeyShift&&!flying)
        {
            var metresPerPixel=2*Camera.Distance*MathF.Tan(Camera.FieldOfView/2)/MathF.Max(1,size.Y);
            Camera=Camera with{Target=Camera.Target+(-right*delta.X+up*delta.Y)*metresPerPixel};return true;
        }
        if(rightDown&&io.KeyAlt){Camera=Camera.Zoom(MathF.Exp(Math.Clamp(delta.Y*.01f,-2,2)));return true;}
        var forward=(ImGui.IsKeyDown(ImGuiKey.W)?1:0)-(ImGui.IsKeyDown(ImGuiKey.S)?1:0);
        var horizontal=(ImGui.IsKeyDown(ImGuiKey.D)?1:0)-(ImGui.IsKeyDown(ImGuiKey.A)?1:0);
        var vertical=(ImGui.IsKeyDown(ImGuiKey.E)?1:0)-(ImGui.IsKeyDown(ImGuiKey.Q)?1:0);
        if(rightDown&&!io.KeyAlt&&(forward!=0||horizontal!=0||vertical!=0))flying=true;
        if(flying)
        {
            var eye=Camera.Eye;var looked=Camera.Orbit(delta.X*.01f,delta.Y*.01f);Camera=looked with{Target=eye+looked.Forward*looked.Distance};
            var movement=Camera.Forward*forward+right*horizontal+Vector3.UnitY*vertical;
            if(movement.LengthSquared()>0)
            {
                var speed=Math.Clamp(Camera.Distance,1,100)*(io.KeyShift?4:1)*Math.Clamp(io.DeltaTime,0,.1f);
                Camera=Camera with{Target=Camera.Target+Vector3.Normalize(movement)*speed};
            }
        }
        else Camera=Camera.Orbit(delta.X*.01f,delta.Y*.01f);
        return true;
    }
}
