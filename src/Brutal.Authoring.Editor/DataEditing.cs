using System.Globalization;
using System.Text.Json;
using Brutal.Authoring.Data;

namespace Brutal.Authoring.Editor;

/// <summary>
/// Any JSON data file in the editor, described by its schema (goal 15): an outline of the file's sections and their list
/// items, and an inspector whose rows come from the schema: text, numbers, whole numbers, on/off and choices
/// (<c>enum</c>), labelled by the schema's <c>title</c> (else the field's key), with <c>description</c> as help and
/// <c>default</c> as the reset value. A game needs no
/// code for this; its schemas are the description. Lists and nested objects show as a summary row: open them as items
/// in the outline, or edit their parts by pointer.
/// </summary>
public static class DataEditing
{
    private const char RowMark = '\u001f';

    /// <summary>Where in the selected object a row of this type's stands, as a JSON Pointer.</summary>
    public static string PlaceOfRow(string rowId) => rowId.StartsWith(RowMark) ? rowId[1..] : Pointer.Join([rowId]);

    /// <summary>
    /// The outline: each top-level field holding an object or a list is a place (<c>/factories</c>), and each object in
    /// such a list is an item inside it (<c>/factories/3</c>), labelled by its <c>name</c>, else <c>id</c>, else
    /// <c>title</c>, else its position.
    /// </summary>
    public static IDocumentOutline<DataDocument> Outline { get; } = new DataOutline(null, "");

    /// <summary>The outline, where an item's schema may name the field that labels it: <c>"x-label": "display_name"</c>
    /// (the user, 2026-10-31). Items whose schema doesn't say fall back to <c>name</c>, <c>id</c>, <c>title</c>, then
    /// their position.</summary>
    public static IDocumentOutline<DataDocument> OutlineFor(JsonSchemas schemas, string schema) => new DataOutline(schemas, schema);

    /// <summary>The outline, where a game labels items from outside the schema, such as a reference map's labels
    /// (<c>ReferenceMap.Label</c>): <paramref name="label"/> gives an item's pointer and value its label, or null to fall
    /// back to the schema's <c>x-label</c>, then <c>name</c>, <c>id</c>, <c>title</c>, then position. It must give the same
    /// answer for the same pointer and value: the outline is labelled once per document and schema version, not every frame.</summary>
    public static IDocumentOutline<DataDocument> OutlineFor(JsonSchemas schemas, string schema, Func<string, JsonElement, string?> label) =>
        new DataOutline(schemas, schema, label);

    /// <summary>The inspector type for any object in a data document, its rows read from <paramref name="schemas"/>.</summary>
    public static IObjectType<DataDocument> ObjectType(JsonSchemas schemas, string schema) => new DataObjectType(schemas, schema);

    /// <summary>
    /// Where a field's value points: given the document, a field's pointer and its value, the pointer of
    /// the item it names, or null when it names nothing (or isn't a reference). A game supplies one; the editor never
    /// guesses from field names.
    /// </summary>
    public delegate string? References(DataDocument document, string fieldPointer, JsonElement value);

    /// <summary>
    /// References declared in the schema: a field whose schema says <c>"x-ref": "/wares"</c> holds the id of an item in
    /// that list, matched on its <c>id</c> (or on the field named by <c>"x-ref-key"</c>). INVENTED, opt-in in the same
    /// spirit as <c>x-label</c>; a game with other rules supplies its own <see cref="References"/>. A value naming no
    /// item, or naming more than one, resolves to nothing.
    /// </summary>
    public static References SchemaReferences(JsonSchemas schemas, string schema)
    {
        // A table asks this of every cell it draws, every frame (found by A179). What a field's schema says, and which items
        // have which id, are kept for one document and schema version: documents don't change, an edit makes a new one.
        var gate = new object();
        DataDocument? forDoc = null;
        var forGeneration = -1L;
        var notes = new Dictionary<string, (string List, string Key)?>(StringComparer.Ordinal);
        var ids = new Dictionary<(string List, string Key), Dictionary<string, string?>>();
        return (doc, field, value) =>
        {
            if (value.ValueKind != JsonValueKind.String) return null;
            lock (gate)
            {
                if (!ReferenceEquals(forDoc, doc) || forGeneration != schemas.Generation)
                {
                    notes.Clear();
                    ids.Clear();
                    forDoc = doc;
                    forGeneration = schemas.Generation;
                }
                if (!notes.TryGetValue(field, out var note))
                {
                    var here = schemas.At(doc.Root, schema, field);
                    string? Note(string k) => here.Select(s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null)
                        .FirstOrDefault(x => x is { Length: > 0 });
                    notes[field] = note = Note("x-ref") is { } named ? (named, Note("x-ref-key") ?? "id") : null;
                }
                if (note is not { } n || doc.At(n.List) is not { ValueKind: JsonValueKind.Array } items) return null;
                if (!ids.TryGetValue(n, out var byId))
                {
                    // Each id's item, or null for an id more than one item has.
                    ids[n] = byId = new Dictionary<string, string?>(StringComparer.Ordinal);
                    var i = 0;
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(n.Key, out var k) && k.ValueKind == JsonValueKind.String)
                            byId[k.GetString()!] = byId.ContainsKey(k.GetString()!) ? null : $"{n.List}/{i}";
                        i++;
                    }
                }
                // A value naming no item, or naming more than one, resolves to nothing.
                return byId.GetValueOrDefault(value.GetString()!);
            }
        };
    }

    // The whole file, as the outline's id for its own plain fields: the empty pointer.
    private const string TopLevel = "";

    private sealed class DataOutline(JsonSchemas? schemas, string schema, Func<string, JsonElement, string?>? label = null) : IDocumentOutline<DataDocument>
    {
        public IEnumerable<string> ObjectIds(DataDocument d)
        {
            if (d.Root.ValueKind != JsonValueKind.Object) yield break;
            // The file itself, when it has plain fields of its own: otherwise nothing could select them (found by A126 on a game's story notes).
            if (d.Root.EnumerateObject().Any(p => p.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))) yield return TopLevel;
            foreach (var p in d.Root.EnumerateObject().Where(p => p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array))
            {
                var place = Pointer.Join([p.Name]);
                yield return place;
                if (p.Value.ValueKind != JsonValueKind.Array) continue;
                var i = 0;
                foreach (var item in p.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object) yield return $"{place}/{i}";
                    i++;
                }
            }
        }

        public string? Label(DataDocument d, string id)
        {
            if (d.At(id) is not { } v) return null;
            if (id == TopLevel) return "(top level)";
            var parts = Pointer.Split(id);
            if (parts.Count == 1) return parts[0];
            if (label?.Invoke(id, v) is { Length: > 0 } given) return given;
            var named = schemas?.At(d.Root, schema, id).Select(s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("x-label"u8, out var x) ? x.GetString() : null)
                .FirstOrDefault(x => x is { Length: > 0 });
            foreach (var key in named is null ? new[] { "name", "id", "title" } : [named, "name", "id", "title"])
                if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty(key, out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } s) return s;
            return $"#{parts[^1]}";
        }

        public string? Parent(DataDocument d, string id) => Pointer.Split(id) is { Count: 2 } parts ? Pointer.Join([parts[0]]) : null;

        // Labels read the document and the schemas, and a game's label, which answers from the pointer and value it's given
        // (as a reference map's does: the map doesn't change once read).
        public long? Version => schemas?.Generation ?? 0;
    }

    private sealed class DataObjectType(JsonSchemas schemas, string schema) : IObjectType<DataDocument>
    {
        public string Id => "data.object";

        public string Label => "Data";

        // The fields to show: the schema's declared properties in order, then any others the objects carry.
        private static IEnumerable<string> Fields(IReadOnlyList<JsonElement> schemasHere, IEnumerable<JsonElement> objects) =>
            schemasHere.Where(s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("properties"u8, out _))
                .SelectMany(s => s.GetProperty("properties"u8).EnumerateObject().Select(p => p.Name))
                .Concat(objects.SelectMany(o => o.EnumerateObject().Select(p => p.Name)))
                .Distinct(StringComparer.Ordinal);

        public IReadOnlyList<InspectorRow> Rows(DataDocument d, IReadOnlyList<string> ids)
        {
            var objects = ids.Select(d.At).ToList();
            // Records in a list can be selected for themselves, and anything else can't, so a list holding anything else shows its items: otherwise an added one couldn't be seen or typed into (found by A119).
            if (ids.Count == 1 && objects[0] is { ValueKind: JsonValueKind.Array } list && list.EnumerateArray().Any(i => i.ValueKind != JsonValueKind.Object))
                return ItemRows(d, ids[0], "", list);
            if (objects.Count == 0 || objects.Any(o => o is not { ValueKind: JsonValueKind.Object })) return [];
            var here = schemas.At(d.Root, schema, ids[0]);
            var rows = new List<InspectorRow>();
            var fields = Fields(here, objects.Select(o => o!.Value)).ToList();
            var fixedFields = here.Where(s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("properties"u8, out _))
                .SelectMany(s => s.GetProperty("properties"u8).EnumerateObject().Select(p => p.Name)).ToHashSet(StringComparer.Ordinal);
            var entries = IsMap(here);
            var bySchema = schemas.Children(d.Root, schema, ids[0], fields);   // one walk to the object, then a step per field (A033)
            foreach (var field in fields)
            {
                var fieldSchemas = bySchema[field];
                var values = objects.Select(o => o!.Value.TryGetProperty(field, out var v) ? v : (JsonElement?)null).ToList();
                var (kind, choices) = KindOf(fieldSchemas, values);
                var shown = values.Select(v => Show(v, kind)).ToList();
                var same = shown.All(v => Equals(v, shown[0]));
                // An explicit null is shown as "null" and not edited in place, so it isn't confused with a field that
                // isn't there ("(not set)"), which can be (A032).
                var scalar = values.All(v => v is null || v.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null));
                // A list the object doesn't have yet is added to, not typed into: no text could ever be right there.
                var unsetList = IsList(fieldSchemas) && values.All(v => v is null);
                if (unsetList) scalar = false;
                var dflt = Pick(fieldSchemas, "default");
                var label = Pick(fieldSchemas, "title")?.GetString() is { Length: > 0 } title ? title : field;   // the schema's title, else the key
                // A key starting with the row mark is written as a marked pointer, so it can't be read as one (found by A141).
                var rowId = field.StartsWith(NestedMark) ? NestedMark + Pointer.Join([field]) : field;
                rows.Add(new InspectorRow(rowId, label, kind, same ? shown[0] : null, !same, scalar,
                    dflt is { } df && values.Any(v => v is null || !JsonSchemas.Same(v.Value, df)) && values.Any(v => v is not null),
                    dflt is not null, choices, Pick(fieldSchemas, "description")?.GetString() ?? "", RangeOf(fieldSchemas, kind),
                    CanAdd: ids.Count == 1 && (unsetList || values[0] is { ValueKind: JsonValueKind.Array }),
                    CanRemove: ids.Count == 1 && entries && !fixedFields.Contains(field) && values[0] is not null,
                    NamedEntries: ids.Count == 1 && IsMap(fieldSchemas) && values[0] is null or { ValueKind: JsonValueKind.Object }));
            }
            return rows;
        }

        // A nested row's id marks itself as a pointer below the selected object, so no field name, whatever its spelling,
        // can be taken for one (a key may hold a slash).
        private const char NestedMark = RowMark;

        private static string PlaceOf(string propertyId) => propertyId.StartsWith(NestedMark) ? propertyId[1..] : Pointer.Join([propertyId]);

        /// <inheritdoc />
        public long? RowsVersion => schemas.Generation;

        // A nested object's fields, worked out as if it were the selected object, then named from the object above it.
        // Its lists stay summaries: they're edited as items, not typed. INVENTED; the same rows, one level in.
        public IReadOnlyList<InspectorRow> NestedRows(DataDocument d, IReadOnlyList<string> ids, string propertyId)
        {
            var place = PlaceOf(propertyId);
            var inner = ids.Select(id => id + place).ToList();
            if (ids.Count == 1 && d.At(inner[0]) is { ValueKind: JsonValueKind.Array } list) return ItemRows(d, inner[0], place, list);
            if (inner.Any(id => d.At(id) is not { ValueKind: JsonValueKind.Object })) return [];
            return Rows(d, inner).Select(r => r with { Id = NestedMark + place + PlaceOf(r.Id) }).ToList();
        }

        // A list's items, each with its own row: a plain value is edited there, and an object or list opens further under
        // it. Only for one selected object: lists of different lengths have no shared meaning for several.
        private IReadOnlyList<InspectorRow> ItemRows(DataDocument d, string list, string place, JsonElement items)
        {
            var rows = new List<InspectorRow>();
            var i = 0;
            foreach (var item in items.EnumerateArray())
            {
                var itemSchemas = schemas.At(d.Root, schema, $"{list}/{i}");
                var (kind, choices) = KindOf(itemSchemas, [item]);
                var plain = item.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null);
                rows.Add(new InspectorRow($"{NestedMark}{place}/{i}", ItemLabel(item, i), kind, Show(item, kind), false, plain, false, false,
                    choices, Pick(itemSchemas, "description")?.GetString() ?? "", RangeOf(itemSchemas, kind), CanAdd: item.ValueKind == JsonValueKind.Array, CanRemove: true));   // with its help (found by A129)
                i++;
            }
            return rows;
        }

        private static string ItemLabel(JsonElement item, int index)
        {
            if (item.ValueKind == JsonValueKind.Object)
                foreach (var key in new[] { "name", "id", "title" })
                    if (item.TryGetProperty(key, out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } s) return $"{index + 1}. {s}";
            return $"{index + 1}.";
        }

        public IReadOnlyList<ICommand<DataDocument>>? AddCommands(DataDocument d, IReadOnlyList<string> ids, string propertyId)
        {
            if (ids.Count != 1) return null;
            var list = ids[0] + PlaceOf(propertyId);
            var field = Pointer.Split(list) is { Count: > 0 } named ? named[^1] : "";   // a whole file can be a list, and then it has no name (found by A119)
            if (d.At(list) is null)   // a list it doesn't have yet: made with its first item
            {
                if (!IsList(schemas.At(d.Root, schema, list))) return null;
                if (NewItem(d, list, field) is not { } first) return null;
                return [new SetValue(list, JsonSerializer.SerializeToElement(new[] { first }))];
            }
            if (d.At(list) is not { ValueKind: JsonValueKind.Array } items) return null;
            var last = items.GetArrayLength() - 1;
            // A list of choices gets the first one it doesn't have: a copy would just repeat what's there.
            if (Pick(ItemSchemas(d, list), "enum") is { } choices)
                foreach (var choice in choices.EnumerateArray())
                    if (!items.EnumerateArray().Any(i => JsonSchemas.Same(i, choice))) return [new InsertItem(list, items.GetArrayLength(), choice)];
            var fresh = last >= 0 ? items[last] : NewItem(d, list, field);
            if (fresh is not { } value) return null;
            return [new InsertItem(list, items.GetArrayLength(), Renumbered(value, items))];
        }

        // What an item of the list must be: as the first item resolves it, which follows references, else the list's own word.
        private IReadOnlyList<JsonElement> ItemSchemas(DataDocument d, string list)
        {
            if (d.At(list) is { ValueKind: JsonValueKind.Array } items && items.GetArrayLength() > 0 && schemas.At(d.Root, schema, list + "/0") is { Count: > 0 } first) return first;
            // Nothing there to walk to: ask of a copy with an empty item in place, so references are followed.
            if (new SetValue(list, JsonSerializer.SerializeToElement(new[] { new { } })).Apply(d) is { Ok: true, Document: { } probe }
                && schemas.At(probe.Root, schema, list + "/0") is { Count: > 0 } followed) return followed;
            return schemas.At(d.Root, schema, list).Where(s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("items"u8, out _)).Select(s => s.GetProperty("items"u8)).ToList();
        }

        public IReadOnlyList<ICommand<DataDocument>>? AddHereCommands(DataDocument d, IReadOnlyList<string> ids) =>
            ids.Count == 1 && d.At(ids[0]) is { ValueKind: JsonValueKind.Array } ? AddCommands(d, ids, NestedMark.ToString()) : null;

        public IReadOnlyList<ICommand<DataDocument>>? RemoveHereCommands(DataDocument d, IReadOnlyList<string> ids)
        {
            if (ids.Count != 1 || d.At(ids[0]) is null) return null;
            var parts = Pointer.Split(ids[0]);
            return parts.Count >= 1 && d.At(Pointer.Join(parts.Take(parts.Count - 1))) is { ValueKind: JsonValueKind.Array } ? [new RemoveItem(ids[0])] : null;
        }

        // An object whose entries are named by whoever adds them: the schema says what each is, and not what it's called.
        private static bool IsMap(IReadOnlyList<JsonElement> s) =>
            s.Any(x => x.ValueKind == JsonValueKind.Object
                && (x.TryGetProperty("additionalProperties"u8, out var extra) && extra.ValueKind == JsonValueKind.Object || x.TryGetProperty("patternProperties"u8, out _)));

        public IReadOnlyList<ICommand<DataDocument>>? AddNamedCommands(DataDocument d, IReadOnlyList<string> ids, string propertyId, string name)
        {
            if (ids.Count != 1 || name.Length == 0) return null;
            var place = ids[0] + PlaceOf(propertyId);
            var entry = place + Pointer.Join([name]);
            if (d.At(entry) is not null || !IsMap(schemas.At(d.Root, schema, place))) return null;
            ICommand<DataDocument> Adding(JsonElement value) => d.At(place) is null
                ? new SetValue(place, JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [name] = value }))
                : new SetValue(entry, value);
            // What this name must be can differ from its neighbours (found by A110): each pattern has its own schema. So ask of a copy with the entry in place.
            if (Adding(JsonSerializer.SerializeToElement(new { })).Apply(d) is not { Ok: true, Document: { } probe }) return null;
            var item = schemas.At(probe.Root, schema, entry);
            // Like another of its kind if there is one, for correcting, else what the schema settles, else an empty one of the right type:
            // the first that's valid under this name. If none is, the first goes forward, and the validator says why.
            var named = schemas.At(d.Root, schema, place).Where(s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("properties"u8, out _))
                .SelectMany(s => s.GetProperty("properties"u8).EnumerateObject().Select(f => f.Name)).ToHashSet(StringComparer.Ordinal);
            var last = d.At(place) is { ValueKind: JsonValueKind.Object } here ? here.EnumerateObject().Where(f => !named.Contains(f.Name)).Select(f => (JsonElement?)f.Value).LastOrDefault() : null;
            var candidates = new[] { last, Settled(item), FromSchema(item) }.Where(c => c is not null).Select(c => c!.Value).ToList();
            if (candidates.Count == 0) return null;
            var under = Pointer.ToPath(entry);
            foreach (var candidate in candidates)
                if (Adding(candidate).Apply(d) is { Ok: true, Document: { } tried }
                    && !schemas.Validate(tried.Root, schema).Any(i => i.Severity == Severity.Error && (i.Path ?? "").StartsWith(under, StringComparison.Ordinal)))
                    return [Adding(candidate)];
            return [Adding(candidates[0])];
        }

        private static bool IsList(IReadOnlyList<JsonElement> s) =>
            s.Any(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("type"u8, out var t)
                && (t.ValueKind == JsonValueKind.String ? t.GetString() == "array" : t.ValueKind == JsonValueKind.Array && t.EnumerateArray().Any(v => v.GetString() == "array")));

        public IReadOnlyList<ICommand<DataDocument>>? RemoveCommands(DataDocument d, IReadOnlyList<string> ids, string propertyId)
        {
            if (ids.Count != 1) return null;
            var at = ids[0] + PlaceOf(propertyId);
            var parts = Pointer.Split(at);
            if (parts.Count == 0 || d.At(at) is null) return null;
            var above = Pointer.Join(parts.Take(parts.Count - 1));
            // An entry in an object of named entries goes as a list item does. A field its schema names doesn't: that's Reset's job.
            if (d.At(above) is { ValueKind: JsonValueKind.Object })
                return IsMap(schemas.At(d.Root, schema, above)) && !schemas.At(d.Root, schema, above).Any(s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("properties"u8, out var named) && named.TryGetProperty(parts[^1], out _))
                    ? [new SetValue(at, null)] : null;
            if (!propertyId.StartsWith(NestedMark) || d.At(above) is not { ValueKind: JsonValueKind.Array }) return null;
            return [new RemoveItem(at)];
        }

        // What a list with nothing to copy starts with: what the schema settles by itself; else another item of the same
        // name, if it's the right kind of thing (found by A079: a same-named list elsewhere may hold something else
        // entirely); else an empty value, which the schema then judges.
        private JsonElement? NewItem(DataDocument d, string list, string field)
        {
            var item = ItemSchemas(d, list);
            if (Settled(item) is { } settled) return settled;
            if (SampleOf(d.Root, field) is { } sample && FitsType(sample, item)) return sample;
            if (SameShape(d, list, item) is { } alike) return alike;
            return FromSchema(item);
        }

        // An item of another list whose items the schema describes the same way: two lists can share one definition under
        // different names (found on a game's production methods: their byproducts and inputs).
        private JsonElement? SameShape(DataDocument d, string list, IReadOnlyList<JsonElement> item)
        {
            if (item.Count == 0) return null;
            var wanted = item[0].GetRawText();
            JsonElement? Search(JsonElement value, string at)
            {
                switch (value.ValueKind)
                {
                    case JsonValueKind.Object:
                        foreach (var p in value.EnumerateObject())
                            if (Search(p.Value, at + Pointer.Join([p.Name])) is { } found) return found;
                        return null;
                    case JsonValueKind.Array:
                        if (at != list && value.GetArrayLength() > 0 && schemas.At(d.Root, schema, at + "/0") is { Count: > 0 } here && here[0].GetRawText() == wanted) return value[0];
                        var i = 0;
                        foreach (var entry in value.EnumerateArray())
                        {
                            if (Search(entry, $"{at}/{i}") is { } inner) return inner;
                            i++;
                        }
                        return null;
                    default:
                        return null;
                }
            }
            return Search(d.Root, "");
        }

        private static bool FitsType(JsonElement value, IReadOnlyList<JsonElement> s)
        {
            var type = Pick(s, "type");
            if (type is not { ValueKind: JsonValueKind.String } t) return true;
            return t.GetString() switch
            {
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                "string" => value.ValueKind == JsonValueKind.String,
                "number" or "integer" => value.ValueKind == JsonValueKind.Number,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                _ => true,
            };
        }

        // A value the schema alone decides: its default, its first choice, an empty list, or an object whose required
        // fields all are decided too. Null when it would be a guess.
        private static JsonElement? Settled(IReadOnlyList<JsonElement> s)
        {
            if (Pick(s, "default") is { } d) return d;
            if (Pick(s, "enum") is { ValueKind: JsonValueKind.Array } e && e.GetArrayLength() > 0) return e[0];
            if (Pick(s, "type") is not { ValueKind: JsonValueKind.String } t) return null;
            if (t.GetString() == "array")
            {
                // A list that must hold some starts with that many, each what its item's schema settles or an empty one of its type,
                // so there's a row to type into (found by A161: a map of non-empty lists couldn't get its first entry).
                var least = Pick(s, "minItems") is { ValueKind: JsonValueKind.Number } m && m.TryGetDouble(out var n) ? (int)Math.Min(n, 64) : 0;
                if (least <= 0 || Pick(s, "items") is not { ValueKind: JsonValueKind.Object } items) return JsonSerializer.SerializeToElement(Array.Empty<int>());
                var one = Settled([items]) ?? FromSchema([items]);
                return JsonSerializer.SerializeToElement(Enumerable.Repeat(one, least).ToArray());
            }
            if (t.GetString() != "object" || Pick(s, "properties") is not { ValueKind: JsonValueKind.Object } properties) return null;
            var fields = new System.Text.Json.Nodes.JsonObject();
            if (Pick(s, "required") is { ValueKind: JsonValueKind.Array } required)
                foreach (var name in required.EnumerateArray().Select(n => n.GetString()!))
                {
                    if (!properties.TryGetProperty(name, out var field) || Settled([field]) is not { } value) return null;
                    fields[name] = System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText());
                }
            return JsonSerializer.SerializeToElement(fields);
        }

        // An item to start from when its list is empty: another item in a list of the same name, so it has the right shape.
        // INVENTED: the schema alone can't say which of a oneOf's shapes is the useful one.
        private static JsonElement? SampleOf(JsonElement value, string field)
        {
            if (value.ValueKind == JsonValueKind.Array)
                return value.EnumerateArray().Select(v => SampleOf(v, field)).FirstOrDefault(s => s is not null);
            if (value.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in value.EnumerateObject())
            {
                if (p.Name == field && p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() > 0) return p.Value[0];
                if (SampleOf(p.Value, field) is { } found) return found;
            }
            return null;
        }

        // A first value from the schema alone, when nothing can be copied: a choice's first, a default, or an empty value.
        private static JsonElement? FromSchema(IReadOnlyList<JsonElement> s)
        {
            if (Pick(s, "default") is { } d) return d;
            if (Pick(s, "enum") is { } e && e.GetArrayLength() > 0) return e[0];
            var (kind, _) = KindOf(s);
            return kind switch
            {
                PropertyKind.Integer or PropertyKind.Number => JsonSerializer.SerializeToElement(0),
                PropertyKind.Toggle => JsonSerializer.SerializeToElement(false),
                _ when Pick(s, "type") is { ValueKind: JsonValueKind.String } t && t.GetString() == "object" => JsonSerializer.SerializeToElement(new { }),
                _ => JsonSerializer.SerializeToElement(""),
            };
        }

        // A copied item keeps every value but a string id, which must differ from its neighbours' or it's no id at all.
        private static JsonElement Renumbered(JsonElement value, JsonElement items)
        {
            if (value.ValueKind == JsonValueKind.String)   // a list of names: a repeat is rarely wanted, and may be refused
            {
                var names = items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()).ToHashSet();
                var name = value.GetString()!;
                for (var n = 2; names.Contains(name); n++) name = $"{value.GetString()}_{n}";
                return JsonSerializer.SerializeToElement(name);
            }
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("id"u8, out var id) || id.ValueKind != JsonValueKind.String) return value;
            var taken = items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object && i.TryGetProperty("id"u8, out _)).Select(i => i.GetProperty("id"u8).ToString()).ToHashSet();
            var fresh = id.GetString()!;
            for (var n = 2; taken.Contains(fresh); n++) fresh = $"{id.GetString()}_{n}";
            var copy = System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText())!.AsObject();
            copy["id"] = fresh;
            return JsonSerializer.SerializeToElement(copy);
        }

        public IReadOnlyList<ICommand<DataDocument>>? EditCommands(DataDocument d, IReadOnlyList<string> ids, string propertyId, object? value, bool reset)
        {
            if (ids.Any(id => d.At(id) is not { ValueKind: JsonValueKind.Object or JsonValueKind.Array })) return null;
            var place = PlaceOf(propertyId);
            // A list or object field is never overwritten by one typed value: it's edited as items (found by the table's
            // tests: a cell could replace a list with text, which only the schema then stopped).
            if (ids.Any(id => d.At(id + place) is { ValueKind: JsonValueKind.Object or JsonValueKind.Array })) return null;
            var fieldSchemas = schemas.At(d.Root, schema, ids[0] + place);
            if (IsList(fieldSchemas)) return null;
            if (reset) return Pick(fieldSchemas, "default") is { } df ? ids.Select(id => (ICommand<DataDocument>)new SetValue(id + place, df)).ToList() : null;
            var (kind, _) = KindOf(fieldSchemas, ids.Select(id => d.At(id + place)));
            // A choice is saved as the schema lists it: 90 stays a number, not the text "90" (found by A126).
            var json = kind == PropertyKind.Choice && Pick(fieldSchemas, "enum") is { } listed
                ? listed.EnumerateArray().Where(e => (e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText()) == value?.ToString()).Select(e => (JsonElement?)e.Clone()).FirstOrDefault() ?? ToJson(value, kind)
                : ToJson(value, kind);
            return json is null ? null : ids.Select(id => (ICommand<DataDocument>)new SetValue(id + place, json)).ToList();
        }

        // How a field is shown, from its schemas: an enum is a choice; integer, number, boolean, else text.
        private static (PropertyKind Kind, IReadOnlyList<string> Choices) KindOf(IReadOnlyList<JsonElement> s)
        {
            // Every list of choices that applies at once: only the values all of them allow (A132).
            var lists = s.Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("enum"u8, out var l) && l.ValueKind == JsonValueKind.Array).Select(x => x.GetProperty("enum"u8).EnumerateArray().ToList()).ToList();
            if (lists.Count > 0)
                return (PropertyKind.Choice, lists[0].Where(v => lists.Skip(1).All(l => l.Any(w => JsonSchemas.Same(v, w)))).Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText()).ToList());
            var types = s.Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("type"u8, out _)).Select(x => x.GetProperty("type"u8))
                .SelectMany(t => t.ValueKind == JsonValueKind.Array ? t.EnumerateArray().Select(v => v.GetString()!) : [t.GetString()!]).ToHashSet();
            return types.Contains("integer") ? (PropertyKind.Integer, []) : types.Contains("number") ? (PropertyKind.Number, [])
                : types.Contains("boolean") ? (PropertyKind.Toggle, []) : (PropertyKind.Text, []);
        }

        // Where the schemas say nothing of a field's type, it keeps the kind its values already have: true/false stays a toggle and a
        // number stays a number, so typing "true" into an open-shaped record can't turn it into text (found by A123). INVENTED.
        private static (PropertyKind Kind, IReadOnlyList<string> Choices) KindOf(IReadOnlyList<JsonElement> s, IEnumerable<JsonElement?> values)
        {
            var fromSchemas = KindOf(s);
            if (Pick(s, "enum") is not null || Pick(s, "type") is not null) return fromSchemas;
            var present = values.Where(v => v is not null).Select(v => v!.Value.ValueKind).ToList();
            if (present.Count == 0) return fromSchemas;
            if (present.All(k => k is JsonValueKind.True or JsonValueKind.False)) return (PropertyKind.Toggle, []);
            if (present.All(k => k == JsonValueKind.Number)) return (PropertyKind.Number, []);
            return fromSchemas;
        }

        // A number field's limits from its schemas: the tightest of minimum/exclusiveMinimum and maximum/exclusiveMaximum.
        private static NumberRange? RangeOf(IReadOnlyList<JsonElement> s, PropertyKind kind)
        {
            if (kind is not (PropertyKind.Number or PropertyKind.Integer)) return null;
            IEnumerable<double> All(string k) => s.Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number)
                .Select(x => x.GetProperty(k).GetDouble());
            (double? V, bool Excl) Tight(string inclusive, string exclusive, bool low)
            {
                var a = All(inclusive).Select(v => (V: v, Excl: false)).Concat(All(exclusive).Select(v => (V: v, Excl: true))).ToList();
                if (a.Count == 0) return (null, false);
                var best = low ? a.OrderByDescending(x => x.V).ThenByDescending(x => x.Excl).First() : a.OrderBy(x => x.V).ThenByDescending(x => x.Excl).First();
                return (best.V, best.Excl);
            }
            var (min, minEx) = Tight("minimum", "exclusiveMinimum", true);
            var (max, maxEx) = Tight("maximum", "exclusiveMaximum", false);
            return min is null && max is null ? null : new NumberRange(min, minEx, max, maxEx);
        }

        private static JsonElement? Pick(IReadOnlyList<JsonElement> s, string keyword) =>
            s.Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty(keyword, out _)).Select(x => (JsonElement?)x.GetProperty(keyword)).FirstOrDefault();

        // The value as the inspector shows it: numbers as numbers, true/false as a bool, text as text, a list or
        // object as a short summary.
        private static object? Show(JsonElement? v, PropertyKind kind) => v switch
        {
            null => null,
            { ValueKind: JsonValueKind.Number } n when kind == PropertyKind.Integer && n.TryGetInt64(out var l) => l,
            { ValueKind: JsonValueKind.Number } n => n.GetDouble(),
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            { ValueKind: JsonValueKind.String } s => s.GetString(),
            { ValueKind: JsonValueKind.Array } a => $"(a list of {a.GetArrayLength()})",
            { ValueKind: JsonValueKind.Object } o => $"({o.EnumerateObject().Count()} fields)",
            { ValueKind: JsonValueKind.Null } => "null",
            _ => null,
        };

        // A whole number, or null: 1.5 isn't rounded to 2 for a whole-number field (found by A129).
        private static long? Whole(object value)
        {
            try { var d = Convert.ToDecimal(value, CultureInfo.InvariantCulture); return d == decimal.Truncate(d) && d >= long.MinValue && d <= long.MaxValue ? (long)d : null; }
            catch (OverflowException) { return null; }
        }

        // A value typed in the inspector, as JSON of the field's kind; null when it can't be.
        private static JsonElement? ToJson(object? value, PropertyKind kind)
        {
            object? typed = kind switch
            {
                PropertyKind.Integer => value is null ? null : Whole(value),
                // Infinity and NaN aren't JSON numbers, and a typed 2e400 reads as infinity: refused, never thrown (found by A209).
                PropertyKind.Number => value is null ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture) is var n && double.IsFinite(n) ? n : null,
                PropertyKind.Toggle => value is bool b ? b : null,
                _ => value?.ToString(),
            };
            return typed is null ? null : JsonSerializer.SerializeToElement(typed);
        }
    }
}

