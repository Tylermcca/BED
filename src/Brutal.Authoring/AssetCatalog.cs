using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Brutal.Authoring;

/// <summary>One admission's immutable, byte-backed asset projection. Performs no file reads, import, or policy approval.</summary>
public sealed class AssetCatalog : IDisposable
{
    /// <summary>Resolution state; valid content is not a grant to export or transmit it.</summary>
    public enum State
    {
        /// <summary>Current complete derived content.</summary>
        Valid,
        /// <summary>No source or identity.</summary>
        Missing,
        /// <summary>Duplicate identity.</summary>
        Ambiguous,
        /// <summary>Inputs changed or derivation is pending/incomplete.</summary>
        Stale,
        /// <summary>Derivation failed.</summary>
        Failed
    }

    /// <summary>Declared derived texture encoding. Base-color pixels are decoded RGBA8 with sRGB RGB channels.</summary>
    public enum TextureEncoding
    {
        /// <summary>Top-left-origin decoded RGBA8, sRGB RGB and linear alpha.</summary>
        Rgba8Srgb
    }
    /// <summary>Explicit generation metadata; it is never inferred from an extension or arbitrary derived bytes.</summary>
    public sealed record TextureInfo(int Width, int Height, TextureEncoding Format);

    /// <summary>One producer-pinned external source file, relative to its asset folder. No asset GUID or access permission is inferred.</summary>
    /// <param name="RelativePath">Normalized relative path using forward slashes.</param>
    /// <param name="SourceHash">Lowercase SHA-256 of the exact bytes read.</param>
    public sealed record SourceFile(string RelativePath,string SourceHash);

    /// <summary>A copied, already-acquired input. Producer must pin source/settings/dependencies and associate derived bytes
    /// with those exact inputs. This class does not run an importer or infer that association from a path.</summary>
    public sealed class Input
    {
        internal readonly AssetRecord Record;
        internal readonly byte[]? Source, Derived;
        internal readonly string[] Dependencies;
        internal readonly bool Complete;
        internal readonly string Profile, PolicyInputs, Reason;
        internal readonly State RequestedState;
        internal readonly TextureInfo? Texture;
        /// <summary>Copied and sorted external source identities; immutable metadata, never a path resolver or payload.</summary>
        public IReadOnlyList<SourceFile> SourceFiles {get;}
        /// <summary>Deep-owned producing metadata for pure export review, not a new acquisition or permission grant.</summary>
        public AssetRecord ProducingRecord=>Record with {Settings=Record.Settings.Clone(),RightsFound=Record.RightsFound is null?null:Array.AsReadOnly(Record.RightsFound.ToArray())};
        /// <summary>Stable sidecar identity.</summary>
        public string Guid => Record.Guid;
        /// <summary>Replaces this same GUID's content after a validated reimport, retaining policy inputs and, unless explicitly replaced, its dependency set.</summary>
        /// <param name="dependencies">Optional producer-verified complete replacement dependency set; supplying it clears prior incomplete derivation state.</param>
        public Input WithReimportedContent(AssetRecord record,byte[] pinnedSource,byte[] derived,string profile,IEnumerable<SourceFile>? sourceFiles=null,IEnumerable<string>? dependencies=null)
        {
            ArgumentNullException.ThrowIfNull(record);
            if(record.Guid!=Guid)throw new ArgumentException("Reimport must preserve the asset GUID.",nameof(record));
            return new(record,pinnedSource,derived,dependencies??Dependencies,dependencies is not null||Complete,profile,PolicyInputs,sourceFiles:sourceFiles??SourceFiles);
        }
        /// <summary>Copies all input ownership, including JSON and rights metadata. Dependencies must be explicitly complete.</summary>
        public Input(AssetRecord record, byte[]? pinnedSource, byte[]? derived, IEnumerable<string> dependencies,
            bool dependenciesComplete, string profile, string policyInputs, State state = State.Valid, string reason = "", TextureInfo? texture = null,IEnumerable<SourceFile>? sourceFiles=null)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!IsGuid(record.Guid)) throw new ArgumentException("Expected a 32-lowercase-hex asset GUID.", nameof(record));
            if (state is State.Ambiguous) throw new ArgumentException("Ambiguity is calculated by the catalog.", nameof(state));
            if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
            ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(policyInputs);
            Record = record with { Settings = record.Settings.Clone(), RightsFound = record.RightsFound is null ? null : Array.AsReadOnly(record.RightsFound.ToArray()) };
            Source = pinnedSource?.ToArray(); Derived = derived?.ToArray(); Texture = texture;
            var sources=(sourceFiles??[]).Select(s=>s is null?throw new ArgumentException("Null source file.",nameof(sourceFiles)):new SourceFile(s.RelativePath,s.SourceHash))
                .OrderBy(s=>s.RelativePath,StringComparer.Ordinal).ToArray();
            if(sources.Any(s=>!ValidSourceFile(s))||sources.Select(s=>s.RelativePath).Distinct(StringComparer.Ordinal).Count()!=sources.Length)
                throw new ArgumentException("External source files need unique normalized relative paths and lowercase SHA-256 identities.",nameof(sourceFiles));
            SourceFiles=Array.AsReadOnly(sources);
            Dependencies = dependencies.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (Dependencies.Any(d => !IsGuid(d))) throw new ArgumentException("Dependencies must be GUIDs.", nameof(dependencies));
            Complete = dependenciesComplete; Profile = profile; PolicyInputs = policyInputs; RequestedState = state; Reason = reason;
        }
    }

    /// <summary>Pinned managed content. Retaining this object retains its bytes independently of catalog refresh/close.</summary>
    public sealed class Lease
    {
        private readonly byte[] bytes;
        internal Lease(byte[] bytes, string content, string policy, string sourceHash, TextureInfo? texture)
        { this.bytes = bytes; ContentRevision = content; PolicyRevision = policy; SourceHash = sourceHash; Texture = texture; }
        /// <summary>SHA256 verified against this generation's pinned source bytes before publication.</summary>
        public string SourceHash { get; }
        /// <summary>Generation-declared decoded texture metadata, null for an ordinary opaque derived artifact.</summary>
        public TextureInfo? Texture { get; }
        /// <summary>Declared decoded encoding, absent for ordinary assets.</summary>
        public TextureEncoding? TextureFormat => Texture?.Format;
        /// <summary>Declared texture width, absent for ordinary assets.</summary>
        public int? Width => Texture?.Width;
        /// <summary>Declared texture height, absent for ordinary assets.</summary>
        public int? Height => Texture?.Height;
        /// <summary>Fingerprint of source/settings/importer/profile/dependency content.</summary>
        public string ContentRevision { get; }
        /// <summary>Fingerprint of recorded policy and dependency policy; not approval.</summary>
        public string PolicyRevision { get; }
        /// <summary>Owned byte count.</summary>
        public int Length => bytes.Length;
        /// <summary>Copies pinned bytes without exposing mutation.</summary>
        public void CopyTo(Span<byte> destination) => bytes.AsSpan().CopyTo(destination);
    }

    /// <summary>Selection identity captured for later re-resolution, never a path fallback.</summary>
    public sealed record Selection(Guid Admission, string AssetGuid, long Generation, string ContentRevision, string PolicyRevision);
    /// <summary>Immutable result. LastGood may be displayed only as retained content when State is not Valid.</summary>
    public sealed record Resolution(Guid Admission, long Generation, string AssetGuid, State State, string Reason,
        string? ContentRevision, string? PolicyRevision, Lease? Current, Lease? LastGood);
    /// <summary>Opaque latest-refresh token, scoped to this catalog instance.</summary>
    public sealed class Refresh
    {
        internal Refresh() { }
    }

    private readonly object gate = new();
    private readonly Guid admission = Guid.NewGuid();
    private Dictionary<string, Resolution> entries = new(StringComparer.Ordinal);
    private Refresh? pending;
    private Input[] currentInputs=[];
    private long generation;
    private bool closed;
    /// <summary>Identity changes on every project admission, even if generation numbers restart.</summary>
    public Guid Admission => admission;
    /// <summary>The complete owned input set of the last committed generation, including failed/missing entries.
    /// Inputs already copy their byte/JSON ownership; the returned list cannot mutate catalog membership.</summary>
    public IReadOnlyList<Input> CurrentInputs()
    {lock(gate){ObjectDisposedException.ThrowIf(closed,this);return Array.AsReadOnly(currentInputs.ToArray());}}
    /// <summary>Starts a refresh and marks old resolutions stale until completion. Supersedes earlier refresh tokens.</summary>
    public Refresh BeginRefresh()
    {
        lock (gate) { ObjectDisposedException.ThrowIf(closed, this); return pending = new(); }
    }

    /// <summary>Publishes one complete copied input set only for the latest refresh. Inputs are an acquisition boundary:
    /// the producer must revalidate its read set before calling, not claim live filesystem atomicity.</summary>
    public bool Publish(Refresh refresh, IEnumerable<Input> inputs, out string why)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var copied = inputs.ToArray();
        lock (gate)
        {
            if (closed || pending is null || !ReferenceEquals(refresh, pending))
            { why = "This refresh is superseded, foreign, or closed."; return false; }
            var next = checked(generation + 1);
            var groups = copied.GroupBy(i => i.Guid, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            var built = new Dictionary<string, Resolution>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            Resolution Build(string id)
            {
                if (built.TryGetValue(id, out var ready)) return ready;
                entries.TryGetValue(id, out var previous);
                var last = previous?.Current ?? previous?.LastGood;
                Resolution Refuse(State state, string reason) => new(admission, next, id, state, reason, null, null, null, last);
                if (!groups.TryGetValue(id, out var group)) return Refuse(State.Missing, "Asset identity is missing.");
                if (group.Length != 1) return built[id] = Refuse(State.Ambiguous, "Multiple records have this GUID.");
                if (visiting.Count >= 256) return Refuse(State.Failed, "Asset dependency depth exceeds 256.");
                if (!visiting.Add(id)) return Refuse(State.Failed, "Asset dependency cycle.");
                var input = group[0];
                Resolution result;
                if (input.Source is null || input.RequestedState == State.Missing) result = Refuse(State.Missing, "Asset source is missing.");
                else if (input.RequestedState != State.Valid) result = Refuse(input.RequestedState, input.Reason.Length == 0 ? "Asset is not ready." : input.Reason);
                else if (!input.Complete) result = Refuse(State.Stale, "Dependency set is incomplete.");
                else if (Hash(input.Source) != input.Record.SourceHash) result = Refuse(State.Stale, "Source bytes differ from the imported record.");
                else if (input.Derived is null) result = Refuse(State.Stale, "Derived content is not ready.");
                else if (input.Texture is { } texture && !ValidTexture(texture,input.Derived.Length)) result = Refuse(State.Failed,"Decoded texture metadata or RGBA8 byte length is invalid.");
                else
                {
                    var dependencies = input.Dependencies.Select(Build).ToArray();
                    var bad = dependencies.FirstOrDefault(d => d.State != State.Valid);
                    if (bad is not null) result = Refuse(State.Stale, $"Dependency {bad.AssetGuid} is not ready: {bad.Reason}");
                    else
                    {
                        var content = Fingerprint("asset-content-v1", Hash(input.Source), Canonical(input.Record.Settings), input.Record.Importer,
                            input.Record.ImporterVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), input.Profile,
                            string.Join(";", dependencies.Select(d => d.AssetGuid + ":" + d.ContentRevision)));
                        if(input.SourceFiles.Count>0)content=Fingerprint("asset-source-files-v1",content,JsonSerializer.Serialize(input.SourceFiles));
                        if(input.Texture is not null)content=Fingerprint("asset-texture-content-v1",content,Hash(input.Derived),JsonSerializer.Serialize(input.Texture));
                        var policy = Fingerprint("asset-policy-v1", JsonSerializer.Serialize(input.Record.Provenance), input.Record.AiUse.ToString(),
                            JsonSerializer.Serialize(input.Record.RightsFound), input.Record.RightsUnread ?? "", input.PolicyInputs,
                            string.Join(";", dependencies.Select(d => d.AssetGuid + ":" + d.PolicyRevision)));
                        var lease = new Lease(input.Derived, content, policy, Hash(input.Source), input.Texture);
                        result = new(admission, next, id, State.Valid, "", content, policy, lease, lease);
                    }
                }
                visiting.Remove(id);
                return built[id] = result;
            }
            foreach (var id in groups.Keys.Order(StringComparer.Ordinal)) Build(id);
            // Retain removed identities as explicit missing entries while a last-good preview can still be labelled.
            foreach (var id in entries.Keys.Except(groups.Keys)) built[id] = Build(id);
            entries = built; currentInputs=copied; generation = next; pending = null; why = ""; return true;
        }
    }

    /// <summary>Resolves only an exact GUID in this admission. No basename or live-path fallback.</summary>
    public Resolution Resolve(string id)
    {
        lock (gate)
        {
            entries.TryGetValue(id, out var value);
            value ??= new(admission, generation, id, State.Missing, "Asset identity is missing.", null, null, null, null);
            if (!closed && pending is null) return value;
            return value with { State = State.Stale, Reason = closed ? "Project admission is closed." : "Catalog refresh is pending.", Current = null, LastGood = value.Current ?? value.LastGood };
        }
    }

    /// <summary>Captures a currently valid selection for a later typed action.</summary>
    public Selection? Select(string id)
    {
        lock (gate)
        {
            var r = Resolve(id);
            return r.State == State.Valid ? new(admission, id, r.Generation, r.ContentRevision!, r.PolicyRevision!) : null;
        }
    }
    /// <summary>Re-resolves selection; unrelated generation changes are allowed but content/policy changes are refused.</summary>
    public bool TryResolve(Selection selection, out Lease? lease, out string why)
    {
        lock (gate)
        {
            lease = null;
            var r = Resolve(selection.AssetGuid);
            if (selection.Admission != admission) { why = "Selection belongs to another project admission."; return false; }
            if (r.State != State.Valid) { why = r.Reason; return false; }
            if (selection.ContentRevision != r.ContentRevision || selection.PolicyRevision != r.PolicyRevision)
            { why = "Asset content or policy changed; refresh the selection."; return false; }
            lease = r.Current; why = ""; return true;
        }
    }
    /// <summary>Resolves a current base-color texture by exact admitted selection and material source hash.
    /// Missing generation metadata, unsupported encoding or stale source identity is refused; ordinary opaque leases stay unchanged.</summary>
    public bool TryResolveTexture(Selection selection, string sourceHash, out Lease? lease, out string why)
    {
        if(!TryResolve(selection,out lease,out why))return false;
        if(!string.Equals(lease!.SourceHash,sourceHash,StringComparison.Ordinal))
        {lease=null;why="The texture source hash does not match the material.";return false;}
        if(lease.Texture is not { } texture || !ValidTexture(texture,lease.Length))
        {lease=null;why="The texture needs declared decoded RGBA8-sRGB dimensions and format.";return false;}
        why="";return true;
    }
    private static bool ValidSourceFile(SourceFile source)=>!string.IsNullOrWhiteSpace(source.RelativePath)
        &&!source.RelativePath.Any(c=>c is '\\' or ':'||char.IsControl(c))
        &&source.RelativePath.Split('/').All(p=>p.Length>0&&p is not ("." or ".."))
        &&source.SourceHash is {Length:64}&&source.SourceHash.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool ValidTexture(TextureInfo texture,int length) => texture.Format==TextureEncoding.Rgba8Srgb
        && texture.Width>0&&texture.Height>0&&(long)texture.Width*texture.Height<=int.MaxValue/4
        &&(long)texture.Width*texture.Height*4==length;

    /// <summary>Stops publication and resolution admission; already acquired managed leases remain readable.</summary>
    public void Dispose() { lock (gate) { closed = true; pending = null; } }
    private static bool IsGuid(string s) => s.Length == 32 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Fingerprint(params string[] fields) => Hash(JsonSerializer.SerializeToUtf8Bytes(fields));
    // Version 1 sorts object properties ordinally; number lexical forms are deliberately preserved.
    private static string Canonical(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(value, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
        static void Write(JsonElement element, Utf8JsonWriter writer)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                var properties = element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
                if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                    throw new ArgumentException("Duplicate settings properties are not canonical.");
                foreach (var p in properties) { writer.WritePropertyName(p.Name); Write(p.Value, writer); }
                writer.WriteEndObject();
            }
            else if (element.ValueKind == JsonValueKind.Array) { writer.WriteStartArray(); foreach (var child in element.EnumerateArray()) Write(child, writer); writer.WriteEndArray(); }
            else element.WriteTo(writer);
        }
    }
}

