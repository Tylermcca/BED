using System.Text.Json.Nodes;
using System.Numerics;
using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;

public sealed partial class DataWorkspace
{
    private sealed partial class NativeScene2
    {
        private bool showBodyBounds;
        private readonly Scene2CollisionPreview collisionPreview=new();
        private void DrawBodyBounds(Scene2 scene,Func<Vector3,Vector2?> project)
        {
            if(!showBodyBounds)return;var draw=ImGui.GetWindowDrawList();var colour=ImGui.GetColorU32(ImGuiCol.PlotHistogram);
            foreach(var item in Scene2ViewProjection.For(scene).Resolved.Objects.Where(o=>o.Body is not null&&Entry.Editor.InFocus(o.Object.Id)))
            {
                if(!collisionPreview.TryGet(scene,item.Object.Id,Owner.SceneAssets,out var bounds,out _))continue;
                for(var i=0;i<8;i++)for(var bit=1;bit<=4;bit*=2)
                {
                    var j=i^bit;if(j<i)continue;
                    if(project(bounds!.Corners[i]) is {} a&&project(bounds.Corners[j]) is {} b&&float.IsFinite(a.X)&&float.IsFinite(a.Y)&&float.IsFinite(b.X)&&float.IsFinite(b.Y))
                        draw.AddLine(new(a.X,a.Y),new(b.X,b.Y),colour,2);
                }
            }
        }
        private bool PhysicsEdit(string action,object args,Func<bool>? currentAsset=null)
        {
            var before=Entry.Session.Current;var revision=Entry.Session.Revision;var policy=Entry.Editor.EditRefusal;
            if(Guard() is {} guard){said=guard;return false;}
            if(currentAsset?.Invoke()==false){said="The available model preview changed; review the current bounds.";return false;}
            try
            {
                var command=Scene2Kit.Command(action,Scene2InspectorType.J(args));
                var ids=Entry.Editor.Selection.ToArray();
                if(policy?.Invoke(before,[command],ids) is {} refusal){said=refusal;return false;}
                if(!ReferenceEquals(before,Entry.Session.Current)||revision!=Entry.Session.Revision||!ReferenceEquals(policy,Entry.Editor.EditRefusal)||currentAsset?.Invoke()==false)
                {said="The scene or edit policy changed; try the physics edit again.";return false;}
                return Edit(action,args);
            }
            catch(Exception e)when(e is not OutOfMemoryException){said="Physics edit refused: "+e.Message;return false;}
        }
        private void DrawPhysics()
        {
            var scene=Entry.Session.Current;var refusal=Guard()??(Entry.Editor.FocusedOn is not null?"Step out of focus before editing world physics.":null);
            Ui.TextWrapped("Edit physics here. Your game runs the simulation."u8);
            Ui.NameNextItem("Bounds###physics.bounds"u8,"Collision bounds");
            Ui.Checkbox("Bounds###physics.bounds"u8,ref showBodyBounds);
            Ui.TooltipLast("Show collision bounds in this level view.");
            Ui.BeginDisabled(refusal is not null,refusal);
            if(scene.Physics is null)
            {
                if(Ui.Button("Add physics"u8))PhysicsEdit("set_physics",new{physics=new{gravity=new[]{0f,0f,0f},fixedStepSeconds=1f/60}});
            }
            else if(Ui.Button("Remove physics"u8))PhysicsEdit("set_physics",new{});
            var resolved=Scene2ViewProjection.For(scene).Resolved;var ids=Entry.Editor.Selection.ToArray();var selectedIds=ids.ToHashSet(StringComparer.Ordinal);
            var selected=resolved.Objects.Where(o=>selectedIds.Contains(o.Object.Id)).ToArray();
            var bodyRefusal=scene.Physics is null?"Add world physics before adding a body.":selected.Length==0?"Select an ordinary object or prefab node.":selected.Any(o=>resolved.Origins[o.Object.Id].Kind==Scene2OriginKind.Placement)?"A placement owns only its transform; select a prefab node.":null;
            Ui.BeginDisabled(bodyRefusal is not null,bodyRefusal);
            if(selected.Length>0&&selected.All(o=>o.Body is null))
            {
                if(Ui.Button("Add body"u8))PhysicsEdit("edit_bodies",new{edits=ids.Select(id=>new{id,field="/body",value=new{motion="static",shape=new{kind="box",halfExtents=new[]{.5f,.5f,.5f}},friction=.5f}}).ToArray()});
            }
            if(selected.Length>0&&selected.All(o=>o.Body is not null))
            {
                if(Ui.Button("Remove body"u8))PhysicsEdit("edit_bodies",new{edits=ids.Select(id=>new{id,field="/body"}).ToArray()});
            }
            if(selected.Length==1&&selected[0].Body?.Shape is SceneMeshBoundsShape)
            {
                var item=selected[0];var assets=Owner.SceneAssets;var capture=assets?.Select(item.Object.Asset!);
                var fitted=collisionPreview.TryGet(scene,item.Object.Id,assets,out var bounds,out var reason);
                Ui.BeginDisabled(!fitted||capture is null,reason.Length>0?reason:"No current available model preview.");
                if(Ui.Button("Use preview as explicit box"u8)&&bounds is not null)
                {
                    var id=item.Object.Id;var origin=resolved.Origins[id];
                    var whole=origin.Kind==Scene2OriginKind.PrefabNode&&scene.Instances.Single(i=>i.Key==origin.InstanceKey).Overrides.Any(e=>e.Node==origin.NodeKey&&e.Field=="/body");
                    var shape=new JsonObject{{"kind","box"},{"center",new JsonArray(bounds.Center.X,bounds.Center.Y,bounds.Center.Z)},{"halfExtents",new JsonArray(bounds.HalfExtents.X,bounds.HalfExtents.Y,bounds.HalfExtents.Z)}};
                    var value=whole?Scene2InspectorType.BodyJson(item.Body!):shape;if(whole)value["shape"]=shape;
                    PhysicsEdit("edit_bodies",new{edits=new[]{new{id,field=whole?"/body":"/body/shape",value}}},()=>ReferenceEquals(Owner.SceneAssets,assets)&&assets!.Select(item.Object.Asset!)==capture);
                }
                Ui.EndDisabled();
            }
            var local=new JsonArray();
            foreach(var id in ids)
            {
                if(!resolved.Origins.TryGetValue(id,out var origin)||origin.Kind!=Scene2OriginKind.PrefabNode)continue;
                foreach(var edit in scene.Instances.Single(i=>i.Key==origin.InstanceKey).Overrides.Where(e=>e.Node==origin.NodeKey&&e.Field.StartsWith("/body",StringComparison.Ordinal)))
                    local.Add(new JsonObject{{"id",id},{"field",edit.Field},{"revert",true}});
            }
            if(local.Count>0&&Ui.Button("Revert body changes"u8))PhysicsEdit("edit_bodies",new JsonObject{{"edits",local}});
            Ui.EndDisabled();
            if(scene.Physics is {} world)
            {
                Ui.TextWrapped("World acceleration and fixed step:"u8);
                for(var axis=0;axis<(scene.Space==SceneSpace.TwoDXY?2:3);axis++)
                {
                    var gravity=world.Gravity;double value=gravity[axis];
                    Ui.TextWrapped("Gravity "+"XYZ"[axis]+" (m/s2)");ImGui.SetNextItemWidth(MathF.Min(160,ImGui.GetContentRegionAvail().X));Ui.NameNextItem("##gravity"+axis,"Gravity "+"XYZ"[axis]+" (m/s2)");
                    if(Ui.InputDouble("##gravity"+axis,ref value,0,0,"%g"u8,ImGuiInputTextFlags.EnterReturnsTrue))
                    {gravity[axis]=(float)value;PhysicsEdit("set_physics",new{physics=new{gravity=new[]{gravity.X,gravity.Y,gravity.Z},fixedStepSeconds=world.FixedStepSeconds}});}
                }
                double step=world.FixedStepSeconds;Ui.TextWrapped("Fixed step (s)"u8);ImGui.SetNextItemWidth(MathF.Min(160,ImGui.GetContentRegionAvail().X));Ui.NameNextItem("##step"u8,"Fixed step (s)");
                if(Ui.InputDouble("##step"u8,ref step,0,0,"%g"u8,ImGuiInputTextFlags.EnterReturnsTrue))
                    PhysicsEdit("set_physics",new{physics=new{gravity=new[]{world.Gravity.X,world.Gravity.Y,world.Gravity.Z},fixedStepSeconds=step}});
            }
            foreach(var item in selected.Where(o=>o.Body is not null))
            {if(collisionPreview.TryGet(scene,item.Object.Id,Owner.SceneAssets,out var bounds,out var why))Ui.TextWrapped(bounds!.Status);else Ui.TextWrapped(why);}
            Ui.TextWrapped("Edit body fields in the Inspector. Edits to a whole-body local value remain whole-body changes; Revert restores its accepted body."u8);
            Ui.TextWrapped("Add physics starts with zero gravity and a 1/60 second step. Collision bounds are personal to this open view; spheres and capsules show enclosing boxes."u8);
            Ui.TextWrapped("Add body explicitly creates a static box: 0.5 m half extents, center zero, friction 0.5. Shape changes start with 0.5 m dimensions; dynamic motion starts at 1 kg. Inspector fields remain editable."u8);
            Ui.EndDisabled();
            if(refusal is not null)Ui.TextWrapped(refusal);if(bodyRefusal is not null)Ui.TextWrapped(bodyRefusal);Ui.Status(said,"scene2.physics.status");
        }
    }
}
