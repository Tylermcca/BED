using System.Numerics;
using System.Text.Json;
using Xunit;
namespace Bed.Tests;
public sealed class SceneMoveJournalPerf20bTests
{
    [Theory]
    [InlineData("ordinary",0f,-0f,1f)]
    [InlineData("quote\"slash\\line\n",1.2345678f,-0.0000001f,1e20f)]
    [InlineData("é_𐐀",float.MaxValue,float.MinValue,float.Epsilon)]
    public void Fast_move_payload_is_byte_identical_to_existing_record_converter_and_replays(string id,float x,float y,float z)
    {
        var move=new MoveObject(id,new(x,y,z));var expected=JsonSerializer.SerializeToElement(move,typeof(MoveObject),CommandCatalog<Scene>.Json).GetRawText();
        var catalog=SceneKit.Catalog();var entry=catalog.ToEntry(move);
        Assert.Equal(expected,entry.Args!.Value.GetRawText());Assert.Equal(move,catalog.FromEntry(entry));Assert.Equal("scene.move",entry.Command);Assert.Equal(1,entry.Version);
    }
    [Theory][InlineData(float.NaN)][InlineData(float.PositiveInfinity)][InlineData(float.NegativeInfinity)]
    public void Nonfinite_move_components_still_refuse_recording(float value)
    {
        var move=new MoveObject("one",new(value,0,0));Assert.Throws<ArgumentException>(()=>SceneKit.Catalog().ToEntry(move));
        Assert.Throws<ArgumentException>(()=>JsonSerializer.SerializeToElement(move,typeof(MoveObject),CommandCatalog<Scene>.Json));
    }
    [Fact]
    public void Null_nonnullable_move_identity_still_refuses_recording()
    {var move=new MoveObject(null!,Vector3.Zero);Assert.Throws<JsonException>(()=>SceneKit.Catalog().ToEntry(move));Assert.Throws<JsonException>(()=>JsonSerializer.SerializeToElement(move,typeof(MoveObject),CommandCatalog<Scene>.Json));}
}
