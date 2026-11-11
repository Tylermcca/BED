using System.Collections.Immutable;
using System.Numerics;
using Bed.Editor;
using Xunit;
namespace Bed.Tests;
public sealed class SceneInspectorKeyPerf20bTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void Repeated_large_selection_queries_do_not_rebuild_large_keys_and_selection_changes_refresh_rows()
    {
        var scene=new Scene(Enumerable.Range(0,5000).Select(i=>SceneObject.At($"cube_{i:D4}",null,new(i,0,0))).ToImmutableArray());
        var editor=new EditorModel<Scene>(new(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene),SceneEditing.Outline);
        var inspector=new Inspector<Scene>(editor).Add(SceneEditing.ObjectType());Assert.True(editor.Select(scene.Objects.Select(o=>o.Id).ToArray()));
        var rows=inspector.Rows();foreach(var row in rows)Assert.Empty(inspector.Nested(row));Assert.True(rows.Single(r=>r.Id=="position.x").Mixed);
        var start=GC.GetAllocatedBytesForCurrentThread();
        for(var pass=0;pass<10;pass++)foreach(var row in inspector.Rows())Assert.Empty(inspector.Nested(row));
        var allocated=GC.GetAllocatedBytesForCurrentThread()-start;output.WriteLine($"Repeated queries allocated {allocated:N0} bytes.");Assert.True(allocated<500_000,$"Repeated queries allocated {allocated:N0} bytes.");
        Assert.True(editor.Select("cube_0000"));Assert.Equal(0d,Assert.IsType<double>(inspector.Rows().Single(r=>r.Id=="position.x").Value));
        // Content comparison also catches a mutable alias of the pre-existing read-only-list view.
        ((IList<string>)editor.Selection)[0]="cube_0001";Assert.Equal(1d,Assert.IsType<double>(inspector.Rows().Single(r=>r.Id=="position.x").Value));
        Assert.Empty(editor.Session.Journal);Assert.False(editor.Session.Dirty);
    }
}
