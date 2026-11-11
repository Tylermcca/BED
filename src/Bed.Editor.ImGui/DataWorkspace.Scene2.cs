using System.Numerics;
using System.Runtime.CompilerServices;
using Bed.Editor;
using Brutal.ImGuiApi;
namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;

public sealed partial class DataWorkspace
{
    private bool scene2WorkspaceClosed;
    internal Func<DataWorkspace,bool>? Scene2OwnerCurrent {get;init;}
    private static readonly ConditionalWeakTable<Scene2DocumentEntry,NativeScene2> nativeScene2=new();
    private readonly Dictionary<string,NativeScene2> scene2Windows=new(StringComparer.Ordinal);
    internal EditorWindow<Scene2>? Scene2Window=>Active is {} f&&Documents.TryGetOpenScene2(f.Name,out var e)&&scene2Windows.TryGetValue(f.Name,out var v)&&ReferenceEquals(e,v.Entry)?v.Window:null;
    private bool ShowScene2(Data.DataFile file,Scene2DocumentEntry entry)
    {
        StopPlayFor(file);var view=nativeScene2.GetValue(entry,e=>new NativeScene2(this,file.Name,e));view.Owner=this;view.BindFlatCamera();scene2Windows[file.Name]=view;BindScene2Clipboard(view);
        Active=file;AdapterProblem=null;Said=$"Showing {file.Name}.";return true;
    }
    private bool GoToScene2(Data.ReferenceTarget target)
    {
        if(!Documents.OpenScene2(target.File,out var entry,out var why)){Said=why;return false;}
        var resolved=Scene2Resolver.Resolve(entry!.Session.Current);
        bool Holds(string pointer)=>target.Pointer==pointer||target.Pointer.StartsWith(pointer+"/",StringComparison.Ordinal);
        var candidates=resolved.Origins.Select(p=>(Id:p.Key,Length:p.Value.FieldOwners.Values.Append(p.Value.SourcePointer).Where(Holds).Select(s=>s.Length).DefaultIfEmpty(-1).Max())).Where(p=>p.Length>=0).ToArray();
        if(candidates.Length==0){Said="This problem doesn't point at an object in the level.";return false;}
        var best=candidates.Max(p=>p.Length);var ids=candidates.Where(p=>p.Length==best).Select(p=>p.Id).Distinct(StringComparer.Ordinal).ToArray();
        if(ids.Length!=1){Said="That prefab setting is shared by several prefab copies; choose one in the Outliner.";return false;}
        if(!entry.Editor.InFocus(ids[0])){Said="Step out of focus before opening that object.";return false;}
        var from=NavigationHere();if(!Show(target.File))return false;if(from is not null)history.Push(from);
        entry.Editor.Select(ids[0]);Said=$"Went to {entry.Editor.Outline.Label(entry.Session.Current,ids[0])} in {target.File}.";return true;
    }
    private sealed partial class NativeScene2
    {
        internal DataWorkspace Owner;
        internal readonly string Name;
        internal readonly Scene2DocumentEntry Entry;
        internal readonly EditorWindow<Scene2> Window;
        private readonly Scene2ViewportSource source;
        private readonly ViewportView<Scene2> viewport;
        internal ViewportView<Scene2> Viewport=>viewport;
        private readonly Scene2FlatView? flatView;
        private IReadOnlyList<PrefabSourceInfo> prefabSources=[];
        private string[] prefabNames=[];private int prefabIndex;private readonly Dictionary<string,string> slotBindings=new(StringComparer.Ordinal);
        private AssetCatalog.Selection? imageSelection;
        private AssetCatalog.Input[] images=[];private string[] imageNames=[];private int imageIndex;
        private readonly ImInputString rename=new(256,""u8);
        private bool focusUpdate,focusReview;
        private Scene2EditPlan? update;
        private string[] updateChanges=[];
        private int updatePage;
        private readonly Dictionary<int,int> updateTextPages=new();
        private string said="";
        internal NativeScene2(DataWorkspace owner,string name,Scene2DocumentEntry entry)
        {
            Owner=owner;Name=name;Entry=entry;ViewportView<Scene2>? view=null;
            source=new(()=>{if(view is not null){view.CancelInput();view.SourceGeneration++;}});viewport=view=new(entry.Editor,source){TransformRefusal=Guard,BoundsOverlay=DrawBodyBounds};
            owner.shortcuts.Default("editor.close","Ctrl+W");
            Window=new(entry.Editor,new Inspector<Scene2>(entry.Editor).Add(new Scene2InspectorType()),owner.shortcuts)
            {
                CloseDocument=()=>Owner.RequestScene2Close(Name,Entry),
                PanelCloseRequested=id=>{if(id is "editor.viewport" or "scene2.flat")Owner.RequestScene2Close(Name,Entry);else Entry.Editor.SetOpen(id,false);},
                SaveAction=()=>
                {
                    if(Owner.scene2WorkspaceClosed||Owner.Scene2OwnerCurrent?.Invoke(Owner)==false)return(false,"This workspace is closed or no longer current; use the current project.");
                    if(!Owner.Documents.TryGetOpenScene2(Name,out var current)||!ReferenceEquals(current,Entry))return(false,"This scene window is no longer current; use the reopened scene.");
                    var ok=Owner.Documents.SaveScene2(Name,out var why);return(ok,why);
                },
                SaveRefusal=()=>Owner.Project.ReadOnlyReason(Name)??"Scene files keep their project binding. Use Save to update this level; create another level through your game's project tools.",
                SaveAsUnavailable=()=>Owner.Project.ReadOnlyReason(Name)??"Scene files keep their project binding. Use Save to update this level; create another level through your game's project tools.",
                OutlineMarks=id=>OutlineMark.ForScene2(Entry.Session.Current,id),
            };
            owner.BindProblemReport(Window);
            var previous=entry.Editor.EditRefusal;entry.Editor.EditRefusal=(doc,commands,touches)=>
            {
                if(previous?.Invoke(doc,commands,touches) is {} old)return old;
                if(Guard() is {} why)return why;
                if(doc.Space!=SceneSpace.TwoDXY||Owner.SceneAssets is null)return null;
                var candidate=doc;foreach(var command in commands){var result=command.Apply(candidate);if(!result.Ok)return string.Join(" ",result.Issues.Select(i=>i.Message));candidate=result.Document!;}
                return AssetRefusal(doc,candidate);
            };
            entry.Editor.Add(owner.FilesPanel with{Draw=()=>Owner.DrawFiles()});
            entry.Editor.Add(new Panel("scene2.assets","Assets",()=>{if(Owner.sceneAssetPicker is {} picker)picker.Draw();else Ui.TextWrapped("No assets are available for this project."u8);},OpenAtStart:false,Width:420,Height:450));
            entry.Editor.Add(new Panel("scene2.prefabs","Prefab and images",DrawActions,Width:420,Height:450,Side:PanelSide.Right));
            if(entry.Session.Current.Space==SceneSpace.ThreeD)entry.Editor.Add(viewport.AsPanel());
            else {flatView=new Scene2FlatView(entry.Editor,Guard,()=>Owner.SceneAssets){DropTarget=at=>Owner.scenePlacement?.DrawTargetAt(at),BoundsOverlay=DrawBodyBounds,IsCurrent=()=>!Owner.scene2WorkspaceClosed&&Owner.Scene2OwnerCurrent?.Invoke(Owner)!=false&&ReferenceEquals(Owner.Scene2Window,Window)};entry.Editor.Add(new Panel("scene2.flat","Flat XY",flatView.Draw,Width:640,Height:450));}
            entry.Editor.Add(new Panel("scene2.physics","Physics",DrawPhysics,OpenAtStart:false,Width:420,Height:450,Side:PanelSide.Right));
            RefreshPrefabs();RefreshImages();
        }
        internal void CancelInput(){viewport.CancelInput();flatView?.CancelInput();}
        internal void BindFlatCamera()
        {if(Owner.sceneViewPreferences is {} folder)flatView?.BindPreferences(Owner.Project.DataFolder,Name,folder);}
        internal string? Guard()
        {
            if(Owner.scene2WorkspaceClosed||Owner.Scene2OwnerCurrent?.Invoke(Owner)==false)return "This workspace is closed or no longer current; use the current project.";
            if(!Owner.Documents.TryGetOpenScene2(Name,out var current)||!ReferenceEquals(current,Entry))return "The scene owner changed; reopen this scene.";
            if(Owner.Project.Judging)return "A save check is running.";
            if(Owner.scene2Close is not null)return "Finish the scene close decision before editing.";
            if(Entry.Editor.Playing is not null||Owner.ProjectPlay is {HasCompleted:false})return "Stop Play before editing.";
            if(Owner.Project.FileAccessRefusal(Name) is {} admission)return admission;
            if(Owner.Project.ReadOnlyReason(Name) is {} readOnly)return readOnly;
            if(Entry.Session.CheckDisk()!=DiskState.Unchanged)return "The saved level was changed outside BED; choose which version to keep.";
            return null;
        }
        internal void Draw()
        {source.Catalog=Owner.SceneAssets;Viewport.MaterialCatalog=Owner.SceneAssets;Viewport.DropTarget=Owner.scenePlacement is {} placement?placement.DrawTarget:null;Window.RecentProjectsMenu=Owner.RecentProjectsMenu;Window.DrawDocked();DrawUpdate();}
        private string? AssetRefusal(Scene2 before,Scene2 candidate)
        {
            if(candidate.Space!=SceneSpace.TwoDXY||Owner.SceneAssets is not {} catalog)return null;
            var old=Scene2ViewProjection.For(before).ById;
            foreach(var item in Scene2ViewProjection.For(candidate).Resolved.Objects.Where(o=>o.Sprite is not null))
            {
                if(old.TryGetValue(item.Object.Id,out var previous)&&previous.Object.Asset==item.Object.Asset)continue;
                var resolution=catalog.Resolve(item.Object.Asset!);
                if(resolution.State==AssetCatalog.State.Valid&&resolution.Current?.Texture is null)
                    return $"{item.Object.Id}: model/opaque assets are unsupported in a flat level; choose an available PNG image.";
                if(resolution.State is not(AssetCatalog.State.Valid or AssetCatalog.State.Missing))return item.Object.Id+": "+resolution.Reason;
            }
            return null;
        }
        private bool Edit(string action,object args)
        {
            if(Guard() is {} refusal){said=refusal;return false;}
            if(Entry.Editor.FocusedOn is not null){said="Step out of focus before changing prefab structure.";return false;}
            var before=Entry.Session.Current;
            if(!Owner.Documents.CaptureScene2Edit(Name,action,Scene2InspectorType.J(args),out var plan,out said))return false;
            if(AssetRefusal(before,plan!.Candidate) is {} assetRefusal){said=assetRefusal;return false;}
            var oldIds=Scene2Resolver.Resolve(before).Objects.Select(o=>o.Object.Id).ToHashSet(StringComparer.Ordinal);
            var added=Scene2Resolver.Resolve(plan!.Candidate).Objects.Select(o=>o.Object.Id).Where(id=>!oldIds.Contains(id)).ToArray();
            var newPiece=plan.Candidate.Instances.FirstOrDefault(i=>before.Instances.All(old=>old.Key!=i.Key))?.Placement.Id;
            var ok=Owner.Documents.CommitScene2Edit(Name,plan,out said);
            if(ok){said="Applied "+action+".";if(newPiece is not null)Entry.Editor.Select(newPiece);else if(added.Length>0)Entry.Editor.Select(added);}
            Owner.Said=said;return ok;
        }
        private static string Signature(Scene2Object item)
        {
            var o=item.Object;var q=o.Rotation;var s=item.Sprite;
            return Scene2InspectorType.J(new{o.Id,o.Asset,o.Parent,position=new[]{o.Position.X,o.Position.Y,o.Position.Z},rotation=new[]{q.X,q.Y,q.Z,q.W},scale=new[]{o.Scale.X,o.Scale.Y,o.Scale.Z},
                body=item.Body is {} body?Scene2InspectorType.BodyJson(body):null, sprite=s is null?null:new{size=new[]{s.Size.X,s.Size.Y},pivot=new[]{s.Pivot.X,s.Pivot.Y},s.Order,s.Filter},properties=o.Properties,references=o.References}).GetRawText();
        }
        private void Review(string instance)
        {
            update=null;
            if(Guard() is {} refusal){said=refusal;return;}
            if(Entry.Editor.FocusedOn is not null){said="Step out of focus before reviewing a whole prefab copy.";return;}
            if(!Owner.Documents.CaptureScene2Edit(Name,"update",Scene2InspectorType.J(new{instance}),out update,out said))return;
            var before=Scene2Resolver.Resolve(Entry.Session.Current).Objects.ToDictionary(o=>o.Object.Id,StringComparer.Ordinal);
            var after=Scene2Resolver.Resolve(update!.Candidate).Objects.ToDictionary(o=>o.Object.Id,StringComparer.Ordinal);
            var changes=new List<string>();
            foreach(var id in before.Keys.Union(after.Keys,StringComparer.Ordinal))
            {
                if(!before.ContainsKey(id)){changes.Add("Added "+id);continue;}
                if(!after.ContainsKey(id)){changes.Add("Removed "+id);continue;}
                var oldLabel=Scene2Editing.Outline.Label(Entry.Session.Current,id);var newLabel=Scene2Editing.Outline.Label(update.Candidate,id);
                if(oldLabel!=newLabel)changes.Add($"{id}: label {oldLabel} to {newLabel}");
                using var oldJson=System.Text.Json.JsonDocument.Parse(Signature(before[id]));using var newJson=System.Text.Json.JsonDocument.Parse(Signature(after[id]));
                foreach(var field in oldJson.RootElement.EnumerateObject())
                {
                    var now=newJson.RootElement.GetProperty(field.Name);if(field.Value.GetRawText()==now.GetRawText())continue;
                    var text=$"{id}: {field.Name}: {field.Value.GetRawText()} to {now.GetRawText()}";
                    changes.Add(text);
                }
            }
            updateChanges=changes.ToArray();updatePage=0;updateTextPages.Clear();focusUpdate=true;
            said=updateChanges.Length==0?"Nothing in the level changes; accepting marks this prefab version as seen.":$"Review {updateChanges.Length} change{(updateChanges.Length==1?"":"s")}. Your local changes stay.";
        }
        private static bool Choose(string label,ref int selected,string[] names)
        {
            var changed=false;
            if(Ui.BeginCombo(label,selected>=0&&selected<names.Length?names[selected]:"Choose"))
            {
                try
                {
                    var clipper=new ImGuiListClipper();clipper.Begin(names.Length);if(selected>=0&&selected<names.Length)clipper.IncludeItemByIndex(selected);
                    while(clipper.Step())for(var i=clipper.DisplayStart;i<clipper.DisplayEnd;i++)
                    {ImGui.PushID(i);try{if(Ui.Selectable(names[i],i==selected)){selected=i;changed=true;}if(i==selected)ImGui.SetItemDefaultFocus();}finally{ImGui.PopID();}}
                    clipper.End();
                }
                finally{Ui.EndCombo();}
            }
            return changed;
        }
        private void RefreshPrefabs()
        {if(!Owner.Documents.ListPrefabSources(out prefabSources,out said)){prefabNames=[];return;}prefabSources=prefabSources.Where(s=>s.Space==Entry.Session.Current.Space).ToArray();prefabNames=prefabSources.Select(s=>s.Name).ToArray();prefabIndex=0;slotBindings.Clear();}
        private void RefreshImages()
        {
            images=Owner.SceneAssets?.CurrentInputs().Where(i=>string.Equals(Path.GetExtension(i.ProducingRecord.Source),".png",StringComparison.OrdinalIgnoreCase)).OrderBy(i=>i.ProducingRecord.Source,StringComparer.Ordinal).ToArray()??[];
            imageNames=images.Select(i=>i.ProducingRecord.Source).ToArray();imageIndex=0;imageSelection=images.Length==0?null:Owner.SceneAssets?.Select(images[0].Guid);
        }
        private void DrawUpdate()
        {
            if(update is not {} candidate)return;
            var viewport=ImGui.GetMainViewport();
            ImGui.SetNextWindowSize(new Brutal.Numerics.float2(MathF.Min(700,viewport.WorkSize.X-24),MathF.Min(520,viewport.WorkSize.Y-24)),ImGuiCond.Always);
            ImGui.SetNextWindowPos(viewport.WorkPos+viewport.WorkSize/2,ImGuiCond.Always,new Brutal.Numerics.float2(.5f,.5f));
            if(focusUpdate)ImGui.SetNextWindowFocus();
            if(Ui.Begin("Review prefab update###scene2.update"u8,ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoDocking|ImGuiWindowFlags.NoCollapse))
            {
                Ui.TextWrapped("Review the changes saved in the prefab. Changes you made to this copy stay."u8);
                var height=MathF.Max(80,ImGui.GetContentRegionAvail().Y-3*ImGui.GetFrameHeightWithSpacing());
                ImGui.BeginChild("##update.changes"u8,new Brutal.Numerics.float2(0,height));
                const int pageSize=32,textSize=1500;
                var pages=Math.Max(1,(updateChanges.Length+pageSize-1)/pageSize);
                Ui.TextWrapped($"Changes page {updatePage+1} of {pages} ({updateChanges.Length} changes).");
                if(pages>1)
                {
                Ui.BeginDisabled(updatePage==0,"Already at the first page.");
                if(Ui.Button("Previous changes"u8))updatePage--;
                Ui.EndDisabled();
                Ui.BeginDisabled(updatePage+1>=pages,"Already at the last page.");
                if(Ui.Button("Next changes"u8))updatePage++;
                Ui.EndDisabled();
                }
                for(var i=updatePage*pageSize;i<Math.Min(updateChanges.Length,(updatePage+1)*pageSize);i++)
                {
                    ImGui.PushID(i);
                    try
                    {
                        var change=updateChanges[i];var textPage=updateTextPages.GetValueOrDefault(i);var textPages=Math.Max(1,(change.Length+textSize-1)/textSize);
                        var offset=textPage*textSize;var end=Math.Min(change.Length,offset+textSize);
                        // Keep a UTF-16 pair together while retaining exact contiguous text on every page.
                        if(end<change.Length&&char.IsHighSurrogate(change[end-1]))end--;
                        if(offset>0&&char.IsLowSurrogate(change[offset]))offset--;
                        if(textPages>1)
                        {
                            Ui.TextWrapped($"Change {i+1}: text page {textPage+1} of {textPages}.");
                            Ui.BeginDisabled(textPage==0,"Already at the first text page.");
                            if(Ui.Button("Previous text"u8))updateTextPages[i]=textPage-1;
                            Ui.EndDisabled();
                            Ui.BeginDisabled(textPage+1>=textPages,"Already at the last text page.");
                            if(Ui.Button("Next text"u8))updateTextPages[i]=textPage+1;
                            Ui.EndDisabled();
                        }
                        Ui.FieldCaption(change[offset..end]);
                    }
                    finally{ImGui.PopID();}
                }
                Ui.FieldCaption(said);ImGui.EndChild();
                var why=Guard()??AssetRefusal(Entry.Session.Current,candidate.Candidate);
                if(focusUpdate){ImGui.SetKeyboardFocusHere();focusUpdate=false;}
                Ui.BeginDisabled(why is not null,why);
                if(Ui.Button("Accept update"u8)){if(Owner.Documents.CommitScene2Edit(Name,candidate,out said)){update=null;focusReview=true;said="Accepted the prefab update.";}}
                Ui.EndDisabled();
                var width=ImGui.CalcTextSize("Accept update"u8).X+ImGui.CalcTextSize("Cancel update"u8).X+4*ImGui.GetStyle().FramePadding.X+ImGui.GetStyle().ItemSpacing.X;
                if(ImGui.GetContentRegionAvail().X>width)ImGui.SameLine();
                if(Ui.Button("Cancel update"u8)){update=null;focusReview=true;said="Prefab update cancelled; level unchanged.";}
                if(why is not null)Ui.TextWrapped(why);
                if(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)&&ImGui.IsKeyPressed(ImGuiKey.Escape,false)){update=null;focusReview=true;said="Prefab update cancelled; level unchanged.";}
            }
            Ui.End();
        }
        private void DrawActions()
        {
            var scene=Entry.Session.Current;var resolved=Scene2ViewProjection.For(scene).Resolved;var selected=Entry.Editor.Primary;
            var origin=selected is not null&&resolved.Origins.TryGetValue(selected,out var o)?o:null;
            var initialRefusal=Guard();Ui.BeginDisabled(initialRefusal is not null,initialRefusal);
            if(origin?.InstanceKey is {} key)
            {
                var available=ImGui.GetContentRegionAvail().X;var rowWidth=0f;
                void NextAction(string label)
                {
                    var width=ImGui.CalcTextSize(label).X+ImGui.GetStyle().FramePadding.X*2;
                    if(rowWidth>0&&rowWidth+ImGui.GetStyle().ItemSpacing.X+width<=available){ImGui.SameLine();rowWidth+=ImGui.GetStyle().ItemSpacing.X;}else rowWidth=0;
                    rowWidth+=width;
                }
                NextAction("Review update");
                Ui.NameNextItem("Review update"u8,"Review prefab update");
                if(Ui.Button("Review update"u8))Review(key);
                if(focusReview){ImGui.SetKeyboardFocusHere(-1);ImGui.SetScrollHereY(.2f);focusReview=false;}
                NextAction("Break Link");
                if(Ui.Button("Break Link"u8))Edit("break_link",new{instance=key});
                NextAction("Delete prefab copy");
                if(Ui.Button("Delete prefab copy"u8))Edit("remove_instance",new{instance=key});
                NextAction("Duplicate");
                Ui.NameNextItem("Duplicate##instance"u8,"Duplicate prefab copy");
                if(Ui.Button("Duplicate##instance"u8))
                {
                    var ids=resolved.Origins.Where(p=>p.Value.InstanceKey==key).Select(p=>p.Key).ToDictionary(id=>id,_=>"copy_"+Guid.NewGuid().ToString("N"));
                    Edit("duplicate",new{instance=key,newKey=Guid.NewGuid().ToString("N"),idMap=ids});
                }
            }
            if(scene.Space==SceneSpace.TwoDXY)
            {
                if(Ui.Button("Refresh images"u8))RefreshImages();
                if(imageNames.Length>0&&Choose("Image",ref imageIndex,imageNames))imageSelection=Owner.SceneAssets?.Select(images[imageIndex].Guid);
                if(imageNames.Length>0)
                {
                Ui.BeginDisabled(imageSelection is null,"Choose an available PNG image.");
                if(Ui.Button("Add sprite"u8))
                {
                    if(imageSelection is null)said="Choose an available PNG image from this project.";
                    else if(Owner.Documents.CaptureSpritePlacement(Name,imageSelection,out var capture,out said))
                        Owner.Documents.PlaceSprite(capture!,"sprite_"+Guid.NewGuid().ToString("N"),Vector2.Zero,out said);
                }
                Ui.EndDisabled();
                }
                else {ImGui.SameLine();Ui.TextWrapped("Add a PNG image to assets."u8);}
            }
            Ui.EndDisabled();
            Ui.TextWrapped(origin is null?"Select an object to see its prefab.":origin.Kind switch{Scene2OriginKind.PrefabNode=>"Part of a prefab copy. Changes you make in the Inspector apply only to this copy; Revert puts back the prefab's value.",Scene2OriginKind.Placement=>"Prefab copy. Moving, turning or resizing it moves the whole piece.",_=>"Ordinary object; changes belong to this level."});
            if(Owner.history.Count>0&&Ui.Button("Back"u8))Owner.Back();
            if(selected is not null)
            {
                var item=resolved.Objects.Single(x=>x.Object.Id==selected);
                foreach(var reference in item.Object.References)
                {
                    var target=reference.Value;var label=Entry.Editor.Outline.Label(scene,target)??target;
                    Ui.BeginDisabled(!Entry.Editor.InFocus(target),"Step out of focus before following this reference.");
                    if(Ui.Button("Go to "+reference.Key+": "+label))
                    {if(Owner.NavigationHere() is {} from)Owner.history.Push(from);Entry.Editor.Select(target);said="Selected "+label+".";}
                    Ui.EndDisabled();
                }
            }
            var refusal=Guard();Ui.BeginDisabled(refusal is not null,refusal);
            Ui.NameNextItem("Add empty object"u8,"Add empty object");
            if(Ui.Button("Add empty object"u8))Edit("object.add",new{@object=new{id="object_"+Guid.NewGuid().ToString("N")}});
            if(Entry.Editor.Selection.Count>0)
            {
                if(Ui.Button("Group selection"u8))Edit("group",new{ids=Entry.Editor.Selection.ToArray()});
                if(origin?.Kind==Scene2OriginKind.Ordinary)
                {
                    if(Ui.Button("Duplicate objects"u8))Edit("object.duplicate",new{ids=Entry.Editor.Selection.ToArray()});
                    if(Entry.Editor.Selection.Count==1&&Ui.Button("Delete object"u8))Edit("object.remove",new{id=selected});
                }
                if(origin?.Kind is Scene2OriginKind.Ordinary or Scene2OriginKind.Placement)
                {
                    Ui.InputText("New object ID"u8,rename);rename.EvaluateLength();
                    if(Ui.Button("Rename object"u8)&&selected is not null)
                    {var next=rename.ToString();if(Edit("object.rename",new{id=selected,newId=next}))Entry.Editor.Select(next);}
                }
            }
            Ui.NameNextItem("Refresh prefabs"u8,"Refresh prefab sources");
            if(Ui.Button("Refresh prefabs"u8))RefreshPrefabs();
            if(prefabNames.Length>0){if(Choose("Prefab",ref prefabIndex,prefabNames))slotBindings.Clear();}
            else Ui.TextWrapped("No prefabs yet. Save an object as a prefab to reuse it."u8);
            var chosen=prefabIndex>=0&&prefabIndex<prefabSources.Count?prefabSources[prefabIndex]:null;
            if(chosen is not null)
            {
                var targets=resolved.Objects.Select(o=>o.Object.Id).ToArray();
                var names=new[]{"Choose target"}.Concat(targets.Select(id=>$"{Entry.Editor.Outline.Label(scene,id)} ({id})")).ToArray();
                foreach(var slot in chosen.Slots)
                {
                    var target=slotBindings.TryGetValue(slot.Key,out var bound)?Array.IndexOf(targets,bound)+1:0;
                    ImGui.PushID(slot.Key);
                    try{if(Choose("Bind "+slot.Label+(slot.Required?" (required)":" (optional)"),ref target,names)){if(target==0)slotBindings.Remove(slot.Key);else slotBindings[slot.Key]=targets[target-1];}}
                    finally{ImGui.PopID();}
                }
            }
            var missingSlot=chosen?.Slots.FirstOrDefault(s=>s.Required&&!slotBindings.ContainsKey(s.Key));
            var placeRefusal=chosen is null?"Choose a prefab from this project.":missingSlot is not null?$"Bind required slot {missingSlot.Label} to a scene object first.":null;
            if(chosen is not null)
            {
            Ui.BeginDisabled(placeRefusal is not null,placeRefusal);
            if(Ui.Button("Place prefab"u8)&&chosen is not null)
                Edit("instantiate",new{source=chosen.Guid.ToString("N"),placement=new{id="piece_"+Guid.NewGuid().ToString("N")},bindings=chosen.Slots.Where(s=>slotBindings.ContainsKey(s.Key)).ToDictionary(s=>s.Key,s=>slotBindings[s.Key])});
            Ui.EndDisabled();if(placeRefusal is not null)Ui.TextWrapped(placeRefusal);
            }
            Ui.EndDisabled();if(refusal is not null)Ui.TextWrapped(refusal);
            Ui.StatusWrapped(said,"scene2.action.status");
        }
    }
}
