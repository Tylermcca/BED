using System.Text.Json;
using Bed;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;
namespace Bed.Tests;

/// <summary>INVENTED neutral route map; independent oracles from G03-G11/G16/G20 and public doc comments.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class MapCanvasAcceptanceTests(ITestOutputHelper output)
{
    static JsonElement J(string s)=>JsonDocument.Parse(s).RootElement.Clone();
    static readonly MapKey A=new("left","pin","same"), B=new("right","pin","same");
    const string Json="""{"pins":[{"scope":"left","id":"same","x":0,"y":0},{"scope":"right","id":"same","x":100,"y":100}]}""";
    sealed class Safe:IValidator<DataDocument>{
        public IReadOnlyList<Issue> Validate(DataDocument d)=>d.At("/pins")!.Value.EnumerateArray()
            .All(x=>double.IsFinite(x.GetProperty("x").GetDouble())&&double.IsFinite(x.GetProperty("y").GetDouble()))
            ? []:[Issue.Error("Coordinates must be finite.")];
    }
    sealed class Preview:IPlaySession {public string Status=>"neutral preview";public void Draw(){}public void Dispose(){}}
    static string O(MapKey k)=>k.Scope+":"+k.Id;
    static IEnumerable<(JsonElement Pin,int Index)> Pins(DataDocument d)=>d.At("/pins")!.Value.EnumerateArray().Select((p,i)=>(p,i));
    sealed class Supply:IMapSource<DataDocument>
    {
        internal long Version;internal string FrameId="neutral";internal int Reads,Moves;
        internal Func<MapSnapshot,MapSnapshot>? Alter;
        internal Func<DataDocument,MapKey,MapPoint,MapEdit<DataDocument>?>? Move;
        internal bool ThrowRead;
        public MapSnapshot Read(DataDocument d){
            Reads++;if(ThrowRead)throw new InvalidOperationException("source offline");
            var m=Pins(d).Select(x=>new MapMarker(new(x.Pin.GetProperty("scope").GetString()!,"pin",x.Pin.GetProperty("id").GetString()!),
                x.Pin.GetProperty("scope").GetString()+":"+x.Pin.GetProperty("id").GetString(),"Neutral pin",
                new(x.Pin.GetProperty("x").GetDouble(),x.Pin.GetProperty("y").GetDouble()))).ToArray();
            var s=new MapSnapshot(new(FrameId,Version,"metres","X","Y"),m,[]);
            return Alter?.Invoke(s)??s;
        }
        public MapEdit<DataDocument>? MoveBy(DataDocument d,MapKey key,MapPoint delta){
            Moves++;if(Move!=null)return Move(d,key,delta);
            var n=Pins(d).FirstOrDefault(x=>x.Pin.GetProperty("scope").GetString()==key.Scope&&x.Pin.GetProperty("id").GetString()==key.Id);
            if(n.Pin.ValueKind==JsonValueKind.Undefined)return null;
            return new([new SetValue($"/pins/{n.Index}/x",JsonSerializer.SerializeToElement(n.Pin.GetProperty("x").GetDouble()+delta.X)),
                new SetValue($"/pins/{n.Index}/y",JsonSerializer.SerializeToElement(n.Pin.GetProperty("y").GetDouble()+delta.Y))],[O(key)]);
        }
    }
    sealed class Rig:IDisposable
    {
        internal HeadlessHost Host=new(900,650){Drawing=false};
        internal DocumentSession<DataDocument> Session;
        internal EditorModel<DataDocument> Editor;
        internal Supply Source=new();
        internal MapCanvas<DataDocument> Canvas;
        internal float Height=500;
        internal Rig(string? json=null,MapViewOptions? options=null,IValidator<DataDocument>? validator=null){
            Session=new(new DataFormat(),validator??new Safe(),DataKit.Catalog(),new DataDocument(J(json??Json)));
            Editor=new(Session,new FlatOutline<DataDocument>(d=>Pins(d).Select(x=>(Id:x.Pin.GetProperty("scope").GetString()+":"+x.Pin.GetProperty("id").GetString(),Label:"Pin"))));
            Canvas=new(Editor,Source,options);Frames();
        }
        internal void Draw(){
            ImGui.SetNextWindowPos(new float2(10,10),ImGuiCond.Always);
            ImGui.SetNextWindowSize(new float2(850,600),ImGuiCond.Always);
            ImGui.Begin("Acceptance map"u8);
            Canvas.Draw(Height);ImGui.End();
        }
        internal void Frames()=>Host.Frames(Draw,3);
        internal float2 At(MapKey k)=>Canvas.ScreenOf(k)!.Value;
        internal double X(MapKey k)=>Pins(Session.Current).Single(x=>x.Pin.GetProperty("scope").GetString()==k.Scope).Pin.GetProperty("x").GetDouble();
        internal void Down(MapKey? key=null){var p=At(key??A);Host.MoveMouse(Draw,p.X,p.Y);ImGui.GetIO().AddMouseButtonEvent(0,true);Host.Frame(Draw);Assert.True(Canvas.Dragging);}
        internal void Up(float2 p){ImGui.GetIO().AddMousePosEvent(p.X,p.Y);ImGui.GetIO().AddMouseButtonEvent(0,false);Host.Frame(Draw);Frames();}
        internal void Drag(float dx=35,float dy=15){var p=At(A);Host.Drag(Draw,p,p+new float2(dx,dy));}
        internal void RoundTrip(string before){string after=Session.Format.Write(Session.Current);Assert.NotEqual(before,after);Assert.Equal(1,Session.History.UndoCount);Assert.True(Session.Undo());Assert.Equal(before,Session.Format.Write(Session.Current));Assert.True(Session.Redo());Assert.Equal(after,Session.Format.Write(Session.Current));}
        internal void Unchanged(string before){Assert.Equal(before,Session.Format.Write(Session.Current));Assert.Equal(0,Session.History.UndoCount);}
        internal string Text=>Session.Format.Write(Session.Current);
        public void Dispose()=>Host.Dispose();
    }
    // G09: Pointer updates alter only the ghost; apply a final delta as one history step.
    [Fact] public void Drag_is_a_ghost_until_release_then_one_undo_redo_step(){using var r=new Rig();var was=r.Text;var p=r.At(A);r.Down();r.Host.MoveMouse(r.Draw,p.X+30,p.Y+12);r.Unchanged(was);r.Up(p+new float2(30,12));r.RoundTrip(was);}
    [Fact] public void Diagonal_move_preserves_the_other_scoped_pin(){using var r=new Rig();var was=r.Text;var x=r.X(B);r.Drag();Assert.Equal(x,r.X(B));Assert.True(r.X(A)>0);r.RoundTrip(was);}
    [Fact] public void A_click_has_no_command_history(){using var r=new Rig();var was=r.Text;var p=r.At(A);r.Host.Click(r.Draw,p.X,p.Y);r.Unchanged(was);}
    [Fact] public void A_two_pixel_jitter_is_not_a_move(){using var r=new Rig();var was=r.Text;r.Drag(2,0);r.Unchanged(was);}
    // G03: reject stale gestures and cancel on Escape, capture loss, frame change or play.
    [Fact] public void Escape_drops_a_real_held_drag(){using var r=new Rig();var was=r.Text;var p=r.At(A);r.Down();r.Host.MoveMouse(r.Draw,p.X+40,p.Y);r.Host.Press(r.Draw,ImGuiKey.Escape);r.Up(p+new float2(40,0));r.Unchanged(was);Assert.False(r.Canvas.Dragging);}
    [Fact] public void A_frame_id_change_cancels(){using var r=new Rig();var was=r.Text;var p=r.At(A);r.Down();r.Source.FrameId="replacement";r.Up(p+new float2(30,0));r.Unchanged(was);}
    [Fact] public void A_frame_version_change_cancels(){using var r=new Rig();var was=r.Text;var p=r.At(A);r.Down();r.Source.Version++;r.Up(p+new float2(30,0));r.Unchanged(was);}
    [Fact] public void Starting_play_cancels_without_a_move(){using var r=new Rig();var was=r.Text;var p=r.At(A);r.Down();r.Editor.Add(new PlayMode<DataDocument>("preview","Preview",_=>new Preview()));Assert.True(r.Editor.Play("preview"));r.Up(p+new float2(30,0));r.Unchanged(was);Assert.True(r.Editor.Stop());}
    [Fact] public void A_release_observed_elsewhere_is_cancelled_next_frame(){using var r=new Rig();var was=r.Text;var p=r.At(A);r.Down();ImGui.GetIO().AddMouseButtonEvent(0,false);r.Host.Frame(()=>{});r.Frames();r.Unchanged(was);Assert.False(r.Canvas.Dragging);}
    [Fact] public void Losing_the_read_source_cancels_and_contains_the_exception(){using var r=new Rig();var was=r.Text;r.Down();r.Source.ThrowRead=true;r.Frames();Assert.False(r.Canvas.Dragging);Assert.Contains("source offline",r.Canvas.Said);r.Source.ThrowRead=false;r.Frames();r.Up(r.At(A)+new float2(30,0));r.Unchanged(was);}
    [Fact] public void An_unrelated_document_edit_makes_a_drag_stale(){using var r=new Rig();var p=r.At(A);r.Down();Assert.True(r.Session.Execute(new SetValue("/pins/1/x",JsonSerializer.SerializeToElement(120))));var was=r.Text;r.Up(p+new float2(30,0));Assert.Equal(was,r.Text);Assert.Equal(1,r.Session.History.UndoCount);}
    [Fact] public void Undo_during_a_drag_cancels_it(){using var r=new Rig();Assert.True(r.Session.Execute(new SetValue("/pins/1/x",JsonSerializer.SerializeToElement(120))));r.Frames();var p=r.At(A);r.Down();Assert.True(r.Session.Undo());var was=r.Text;r.Up(p+new float2(30,0));Assert.Equal(was,r.Text);Assert.Equal(0,r.Source.Moves);}
    // G04: scope/kind/id, never array position, determine identity.
    [Fact] public void Two_scopes_with_one_local_id_select_and_move_independently(){using var r=new Rig();var p=r.At(B);r.Host.Click(r.Draw,p.X,p.Y);Assert.Equal(O(B),r.Editor.Primary);var was=r.Text;r.Host.Drag(r.Draw,p,p+new float2(-30,12));Assert.Equal(0,r.X(A));r.RoundTrip(was);}
    [Fact] public void Reordering_the_document_list_cancels_instead_of_moving_a_new_index(){using var r=new Rig();var p=r.At(A);r.Down();Assert.True(r.Session.Execute(new SetValue("/pins",J("""[{"scope":"right","id":"same","x":100,"y":100},{"scope":"left","id":"same","x":0,"y":0}]"""))));var was=r.Text;r.Up(p+new float2(30,0));Assert.Equal(was,r.Text);Assert.Equal(0,r.Source.Moves);}
    [Fact] public void Reordering_only_snapshot_markers_keeps_the_stable_target(){using var r=new Rig();var p=r.At(A);var was=r.Text;r.Down();r.Source.Alter=s=>s with{Markers=s.Markers.Reverse().ToArray()};r.Up(p+new float2(30,0));Assert.True(r.X(A)>0);Assert.Equal(100,r.X(B));r.RoundTrip(was);}
    [Fact] public void A_vanished_marker_cancels(){using var r=new Rig();var p=r.At(A);var was=r.Text;r.Down();r.Source.Alter=s=>s with{Markers=s.Markers.Where(m=>m.Key!=A).ToArray()};r.Up(p+new float2(30,0));r.Unchanged(was);Assert.Equal(0,r.Source.Moves);}
    [Fact] public void An_added_marker_does_not_retarget_a_drag(){using var r=new Rig();var p=r.At(A);var was=r.Text;r.Down();r.Source.Alter=s=>s with{Markers=s.Markers.Append(new MapMarker(new("extra","pin","new"),"not-an-outline-object","New",new(40,60))).ToArray()};r.Up(p+new float2(30,0));Assert.True(r.X(A)>0);r.RoundTrip(was);}
    // G09: release requires a resolvable movable target, not just the captured key.
    [Fact] public void A_marker_that_becomes_immovable_cancels(){using var r=new Rig();var p=r.At(A);var was=r.Text;r.Down();r.Source.Alter=s=>s with{Markers=s.Markers.Select(m=>m.Key==A?m with{Movable=false}:m).ToArray()};r.Up(p+new float2(30,0));r.Unchanged(was);Assert.Equal(0,r.Source.Moves);}
    [Fact] public void MoveBy_throw_is_contained_and_adds_no_history(){using var r=new Rig();r.Source.Move=(_,_,_)=>throw new InvalidOperationException("move failed");var was=r.Text;r.Drag();r.Unchanged(was);Assert.Contains("move failed",r.Canvas.Said);}
    [Fact] public void A_null_edit_is_a_no_op(){using var r=new Rig();r.Source.Move=(_,_,_)=>null;var was=r.Text;r.Drag();r.Unchanged(was);}
    [Fact] public void An_empty_edit_adds_no_history(){using var r=new Rig();r.Source.Move=(_,_,_)=>new([],[]);var was=r.Text;r.Drag();r.Unchanged(was);}
    [Fact] public void Read_is_once_per_idle_frame_even_if_each_read_has_a_new_list(){using var r=new Rig();r.Source.Alter=s=>s with{Markers=s.Markers.ToArray()};var n=r.Source.Reads;r.Host.Frame(r.Draw);Assert.Equal(n+1,r.Source.Reads);}
    // G07: invalid positions and unresolved endpoints give diagnostics, without editable geometry.
    [Theory][InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(double.NegativeInfinity)]
    public void Nonfinite_positions_are_diagnosed_and_not_pickable(double x){using var r=new Rig();r.Source.Alter=s=>s with{Markers=s.Markers.Select(m=>m.Key==A?m with{Position=new(x,0)}:m).ToArray()};r.Frames();Assert.Null(r.Canvas.ScreenOf(A));Assert.NotEmpty(r.Canvas.Problems);Assert.DoesNotContain(r.Canvas.Hits(r.At(B)),m=>m.Key==A);}
    [Fact] public void An_empty_frame_is_finite_and_creates_no_history(){using var r=new Rig("""{"pins":[]}""");Assert.Null(r.Canvas.ScreenOf(A));Assert.Empty(r.Canvas.Hits(new(30,30)));Assert.Equal(0,r.Session.History.UndoCount);}
    [Fact] public void A_single_marker_draws_at_a_finite_point(){using var r=new Rig("""{"pins":[{"scope":"left","id":"same","x":7,"y":7}]}""");var p=r.At(A);Assert.True(float.IsFinite(p.X)&&float.IsFinite(p.Y));}
    [Fact] public void Coincident_markers_can_both_be_reached_by_real_clicks(){using var r=new Rig(Json.Replace("\"x\":100,\"y\":100","\"x\":0,\"y\":0"));var p=r.At(A);r.Host.Click(r.Draw,p.X,p.Y);var first=r.Editor.Primary;r.Host.Click(r.Draw,p.X,p.Y);Assert.NotEqual(first,r.Editor.Primary);Assert.Equal(0,r.Session.History.UndoCount);}
    [Fact] public void Zero_height_is_bounded_and_does_not_dirty_the_document(){using var r=new Rig();var was=r.Text;r.Height=0;r.Frames();Assert.True(float.IsFinite(r.At(A).X));r.Unchanged(was);}
    [Fact] public void An_unresolved_edge_has_a_diagnostic(){using var r=new Rig();r.Source.Alter=s=>s with{Edges=[new(new("left","edge","gap"),A,null,"Missing endpoint")]};r.Frames();Assert.Contains("Missing endpoint",r.Canvas.Problems);}
    [Fact] public void Duplicate_keys_are_diagnosed_without_taking_down_a_frame(){using var r=new Rig();r.Source.Alter=s=>s with{Markers=[s.Markers[0],s.Markers[0]]};var e=Record.Exception(r.Frames);Assert.Null(e);Assert.NotEmpty(r.Canvas.Problems);}
    // G08/G20: reject an unrepresentable projection; never return unusable pixels for finite records.
    [Fact] public void A_huge_finite_span_is_diagnosed_instead_of_returning_NaN_pixels(){using var r=new Rig();r.Source.Alter=s=>s with{Frame=s.Frame with{Version=1},Markers=[s.Markers[0] with{Position=new(-double.MaxValue,0)},s.Markers[1] with{Position=new(double.MaxValue,1)}]};r.Frames();var p=r.Canvas.ScreenOf(A);Assert.True(p is null||float.IsFinite(p.Value.X)&&float.IsFinite(p.Value.Y));Assert.NotEmpty(r.Canvas.Problems);}
    [Fact] public void A_large_origin_keeps_a_small_delta_and_undo_redo(){using var r=new Rig(Json.Replace("\"x\":100,","\"x\":1000000000100,").Replace("\"x\":0,","\"x\":1000000000000,"));var was=r.Text;r.Drag(20,0);Assert.InRange(r.X(A)-1e12,0.01,100);r.RoundTrip(was);}
    // G16: snapping only moves, zero is a free axis, Alt inverts the setting.
    [Fact] public void A_zero_step_is_a_free_axis(){using var r=new Rig(options:new(new(new(0,0),new(0,0))));var was=r.Text;r.Drag(31,12);Assert.NotEqual(Math.Round(r.X(A)),r.X(A));r.RoundTrip(was);}
    [Fact] public void Positive_grid_snaps_once_as_one_undo_redo_step(){using var r=new Rig(options:new(new(new(10,10),new(0,0))));var was=r.Text;r.Drag(50,25);Assert.Equal(0,r.X(A)%10,8);r.RoundTrip(was);}
    [Theory][InlineData(-1)][InlineData(double.NaN)][InlineData(double.PositiveInfinity)]
    public void Invalid_grid_steps_refuse_with_a_diagnostic(double step){using var r=new Rig(options:new(new(new(step,10),new(0,0))));var was=r.Text;r.Drag(35,12);r.Unchanged(was);Assert.True(r.Canvas.Problems.Count>0||r.Canvas.Said.Length>0);}
    [Fact] public void A_huge_grid_can_snap_to_a_no_op_without_history(){using var r=new Rig(options:new(new(new(1e300,1e300),new(0,0))));var was=r.Text;r.Drag();r.Unchanged(was);}
    [Fact] public void A_nonfinite_grid_origin_refuses_without_writing(){using var r=new Rig(options:new(new(new(1,1),new(double.NaN,0))));var was=r.Text;r.Drag();r.Unchanged(was);Assert.Contains("number",r.Canvas.Said);}
    [Fact] public void Alt_temporarily_disables_an_enabled_grid(){using var r=new Rig(options:new(new(new(10,10),new(0,0))));var was=r.Text;ImGui.GetIO().AddKeyEvent(ImGuiKey.Mod_Alt,true);r.Drag(37,10);ImGui.GetIO().AddKeyEvent(ImGuiKey.Mod_Alt,false);r.Frames();Assert.NotEqual(0,r.X(A)%10);r.RoundTrip(was);}
    [Fact] public void Wheel_limits_are_bounded_and_navigation_never_adds_history(){using var r=new Rig();var was=r.Text;var p=r.At(A);r.Host.MoveMouse(r.Draw,p.X,p.Y);ImGui.GetIO().AddMouseWheelEvent(0,10000);r.Frames();Assert.Equal(1000,r.Canvas.Zoom);ImGui.GetIO().AddMouseWheelEvent(0,-10000);r.Frames();Assert.Equal(1,r.Canvas.Zoom);r.Unchanged(was);}
    // ExecuteAt doc: also refused after revision change or when a named object can't be found.
    [Fact] public void ExecuteAt_refuses_old_revisions_after_undo_and_redo(){using var r=new Rig();long rev=r.Session.Revision;Assert.True(r.Editor.ExecuteAt(rev,[new SetValue("/pins/0/x",JsonSerializer.SerializeToElement(7))],O(A)));Assert.True(r.Session.Undo());Assert.False(r.Editor.ExecuteAt(rev,[new SetValue("/pins/0/x",JsonSerializer.SerializeToElement(8))],O(A)));Assert.True(r.Session.Redo());Assert.Equal(7,r.X(A));}
    [Fact] public void ExecuteAt_refuses_a_missing_target_without_history(){using var r=new Rig();var was=r.Text;Assert.False(r.Editor.ExecuteAt(0,[new SetValue("/pins/0/x",JsonSerializer.SerializeToElement(7))],"missing"));r.Unchanged(was);Assert.NotEmpty(r.Editor.Refused);}
    [Fact] public void ExecuteAt_refuses_every_declared_out_of_focus_target(){using var r=new Rig();var was=r.Text;Assert.True(r.Editor.FocusOn(O(A)));Assert.False(r.Editor.ExecuteAt(0,[new SetValue("/pins/1/x",JsonSerializer.SerializeToElement(7))],O(A),O(B)));r.Unchanged(was);}
    [Fact] public void ExecuteAt_refuses_play_and_clears_Refused_after_success(){using var r=new Rig();r.Editor.Add(new PlayMode<DataDocument>("preview","Preview",_=>new Preview()));Assert.True(r.Editor.Play("preview"));Assert.False(r.Editor.ExecuteAt(0,[new SetValue("/pins/0/x",JsonSerializer.SerializeToElement(7))],O(A)));Assert.NotEmpty(r.Editor.Refused);Assert.True(r.Editor.Stop());Assert.True(r.Editor.ExecuteAt(0,[new SetValue("/pins/0/x",JsonSerializer.SerializeToElement(7))],O(A)));Assert.Empty(r.Editor.Refused);}
    [Fact] public void A_complete_two_axis_numeric_edit_has_one_undo_redo_step(){using var r=new Rig();var was=r.Text;Assert.True(r.Editor.ExecuteAt(0,[new SetValue("/pins/0/x",JsonSerializer.SerializeToElement(7)),new SetValue("/pins/0/y",JsonSerializer.SerializeToElement(9))],O(A)));r.RoundTrip(was);}
    sealed class FixedLeft:IValidator<DataDocument>{
        public IReadOnlyList<Issue> Validate(DataDocument d)=>d.At("/pins/0/x")!.Value.GetDouble()==0?[]:[Issue.Error("The left pin is fixed by the native rule.")];
    }
    // G11: native validators still judge the whole candidate; refusals leave history alone.
    [Fact] public void Whole_candidate_validation_refuses_a_drag_atomically(){using var r=new Rig(validator:new FixedLeft());var was=r.Text;r.Drag();r.Unchanged(was);Assert.Contains(r.Session.LastIssues,i=>i.Message.Contains("native rule"));Assert.Contains("native rule",r.Canvas.Said);}
    [Fact] public void A_second_command_refusal_keeps_the_first_axis_unchanged(){using var r=new Rig();var was=r.Text;r.Source.Move=(_,_,_)=>new([new SetValue("/pins/0/x",JsonSerializer.SerializeToElement(7)),new SetValue("/missing/value",JsonSerializer.SerializeToElement(9))],[O(A)]);r.Drag();r.Unchanged(was);Assert.NotEmpty(r.Session.LastIssues);}
    [Fact] public void A_marker_initially_immovable_selects_without_a_drag(){using var r=new Rig();r.Source.Alter=s=>s with{Markers=s.Markers.Select(m=>m with{Movable=false}).ToArray()};r.Frames();var was=r.Text;var p=r.At(A);r.Host.Drag(r.Draw,p,p+new float2(30,0));Assert.Equal(O(A),r.Editor.Primary);r.Unchanged(was);Assert.Equal(0,r.Source.Moves);}
    [Fact] public void Redo_during_a_drag_cancels_the_old_revision(){using var r=new Rig();Assert.True(r.Session.Execute(new SetValue("/pins/1/x",JsonSerializer.SerializeToElement(120))));Assert.True(r.Session.Undo());r.Frames();var p=r.At(A);r.Down();Assert.True(r.Session.Redo());var was=r.Text;r.Up(p+new float2(30,0));Assert.Equal(was,r.Text);Assert.Equal(0,r.Source.Moves);}
    [Fact] public void Alt_enables_a_disabled_grid_only_for_the_move(){using var r=new Rig(options:new(new(new(10,10),new(0,0),false)));var was=r.Text;ImGui.GetIO().AddKeyEvent(ImGuiKey.Mod_Alt,true);r.Drag(50,25);ImGui.GetIO().AddKeyEvent(ImGuiKey.Mod_Alt,false);r.Frames();Assert.Equal(0,r.X(A)%10,8);r.RoundTrip(was);}
    // Explicit trusted-provider observation, not a claim that ExecuteAt verifies opaque command bodies.
    [Fact] public void Incomplete_touches_are_documented_as_trusted_provider_input(){using var r=new Rig();Assert.True(r.Editor.FocusOn(O(A)));Assert.True(r.Editor.ExecuteAt(0,[new SetValue("/pins/1/x",JsonSerializer.SerializeToElement(7))],O(A)));Assert.Equal(7,r.X(B));output.WriteLine("Observation: omitted B touch is trusted; ExecuteAt is not an untrusted-code boundary.");}
}
