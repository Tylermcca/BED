using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class AssetSettingsTests
{
    private static JsonElement Json(string text) { using var doc=JsonDocument.Parse(text);return doc.RootElement.Clone(); }
    private static AssetRecord Record()=>new("11111111111111111111111111111111","shape.mesh",new string('a',64),"synthetic",3,Json("{\"scale\":1}"),new("fixture","self-authored","fixture",Declared:AssetOrigin.Ours),AiUse.Forbidden);
    private static JsonSchemas Schemas()=>new JsonSchemas().Add("settings","""{"type":"object","required":["scale"],"additionalProperties":false,"properties":{"scale":{"type":"number","minimum":0.1,"maximum":10}}}""");

    [Fact]
    public void Transaction_changes_settings_only_and_undo_redo_restore_exact_candidates()
    {
        var original=Record();var s=new AssetSettingsSession(original,Schemas(),"settings");
        Assert.True(s.TryReplace(Json("{\"scale\":2}")));Assert.True(s.Dirty);Assert.Equal(1,s.UndoCount);
        Assert.Equal(original.Write(),(s.Candidate with { Settings=original.Settings }).Write());
        Assert.Equal(2,s.Settings.GetProperty("scale").GetInt32());
        Assert.True(s.Undo());Assert.False(s.Dirty);Assert.Equal(original.Write(),s.Candidate.Write());
        Assert.True(s.Redo());Assert.Equal(2,s.Settings.GetProperty("scale").GetInt32());
        Assert.Equal(1,original.Settings.GetProperty("scale").GetInt32());
    }

    [Theory]
    [InlineData("{\"scale\":0}")][InlineData("{\"scale\":2,\"unknown\":true}")][InlineData("[]")][InlineData("null")]
    public void Unsupported_or_invalid_settings_do_not_change_draft_history_or_revision(string candidate)
    {
        var s=new AssetSettingsSession(Record(),Schemas(),"settings");var before=s.Candidate.Write();var revision=s.Revision;
        Assert.False(s.TryReplace(Json(candidate)));Assert.NotEmpty(s.LastIssues);
        Assert.Equal(before,s.Candidate.Write());Assert.Equal(revision,s.Revision);Assert.Equal(0,s.UndoCount);Assert.False(s.Dirty);
    }

    [Fact]
    public void Json_document_lifetime_does_not_invalidate_draft_or_candidate()
    {
        AssetSettingsSession s;
        using(var doc=JsonDocument.Parse("{\"scale\":1}"))s=new(Record() with { Settings=doc.RootElement },Schemas(),"settings");
        using(var changed=JsonDocument.Parse("{\"scale\":3}"))Assert.True(s.TryReplace(changed.RootElement));
        Assert.Equal(3,s.Candidate.Settings.GetProperty("scale").GetInt32());Assert.True(s.Undo());Assert.Equal(1,s.Settings.GetProperty("scale").GetInt32());
    }

    [Fact]
    public void Invalid_initial_settings_are_not_admitted_as_a_valid_session()
    {
        Assert.Throws<InvalidDocumentException>(()=>new AssetSettingsSession(Record() with { Settings=Json("{\"scale\":-1}") },Schemas(),"settings"));
        Assert.Throws<ArgumentException>(()=>new AssetSettingsSession(Record() with { Settings=Json("[]") },Schemas(),"settings"));
    }
}
