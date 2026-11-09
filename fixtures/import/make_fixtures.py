"""INVENTED: original tiny import fixtures for A005; no game or external content.
Blender 5.2: blender -b --factory-startup -P make_fixtures.py -- <out_dir>
Runtime-only JSON/OBJ/bad-name cases return {filename: bytes}; their caller owns
a disposable test directory. Main writes only the card's allowed GLB/PNG/JSON.
"""
from pathlib import Path
import copy
import hashlib
import json
import math
import struct
import sys
import zlib

def canonical(value):
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2, allow_nan=False) + "\n").encode("utf-8")

def read_glb(path):
    raw = Path(path).read_bytes()
    magic, version, size = struct.unpack_from("<4sII", raw)
    assert magic == b"glTF" and version == 2 and size == len(raw)
    n, kind = struct.unpack_from("<I4s", raw, 12)
    assert kind == b"JSON"
    doc = json.loads(raw[20:20+n])
    pos = 20+n
    blob = b""
    if pos < len(raw):
        n, kind = struct.unpack_from("<I4s", raw, pos)
        assert kind == b"BIN\0"
        blob = raw[pos+8:pos+8+n]
    return doc, blob

def glb_bytes(doc, blob):
    text = json.dumps(doc, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")
    text += b" " * (-len(text) % 4)
    chunks = struct.pack("<I4s", len(text), b"JSON") + text
    if blob:
        blob += b"\0" * (-len(blob) % 4)
        chunks += struct.pack("<I4s", len(blob), b"BIN\0") + blob
    return struct.pack("<4sII", b"glTF", 2, 12+len(chunks)) + chunks

def patch(path, change):
    doc, blob = read_glb(path)
    change(doc)
    Path(path).write_bytes(glb_bytes(doc, blob))

def generated_truncated(root):
    data = (Path(root)/"im01_cube.glb").read_bytes()
    return {"truncated.glb": data[:-11]}

def generated_wrong_extension(root):
    # A genuine self-authored GLB under an unsupported extension; no FBX exporter dependency.
    return {"unsupported.fbx": (Path(root)/"im01_cube.glb").read_bytes()}

def missing_references():
    # Fixture-only neutral scene schema, deliberately not the Toy board or a production API.
    return {"missing.scene.json": canonical({"kind":"fixture-scene","version":1,"objects":[
        {"id":"healthy","asset":"im01_cube.glb","position":[0,0,0],"properties":{"label":"Healthy"}},
        {"id":"placeholder","asset":"missing_asset.glb","position":[2,0,0],"properties":{"label":"Keep reference"}},
        {"id":"textured","asset":"im07_missing_texture.glb","position":[4,0,0],"properties":{"label":"Optional texture"}}]})}

def roundtrip_documents():
    base = {"kind":"fixture-scene","version":1,"objects":[
        {"id":"first","asset":"im01_cube.glb","position":[0.125,0.375,0.625],
         "rotation":[0,0,0,1],"scale":[1,2,1],"properties":{"label":"café","enabled":True},
         "references":{"peer":"second"}},
        {"id":"second","asset":"im01_cube.glb","position":[2,0,0],
         "rotation":[0,0,0,1],"scale":[1,1,1],"properties":{"label":"Keep me"},"references":{}}]}
    good = canonical(base)
    duplicate = copy.deepcopy(base)
    duplicate["objects"][1]["id"] = "first"
    unknown = copy.deepcopy(base)
    unknown["unexpected"] = True
    future = copy.deepcopy(base)
    future["version"] = 999
    return {"roundtrip.scene.json":good, "duplicate_ids.scene.json":canonical(duplicate),
            "unknown_property.scene.json":canonical(unknown),
            "duplicate_property.scene.json":good.replace(b'"version": 1', b'"version": 1, "version": 1'),
            "future_version.scene.json":canonical(future)}

def module_with_markers():
    # Native, right-handed Z-up analogue: no glTF frame conversion is applied.
    vertices = [(-.5,-.5,0),(.5,-.5,0),(.5,.5,0),(-.5,.5,0),
                (-.5,-.5,1),(.5,-.5,1),(.5,.5,1),(-.5,.5,1)]
    faces = [(1,4,3,2),(5,6,7,8),(1,2,6,5),(2,3,7,6),(3,4,8,7),(4,1,5,8)]
    obj = "# Self-authored fixture. Native Z-up; no UVs.\no Hull\n"
    obj += "".join("v %g %g %g\n" % v for v in vertices)
    for a,b,c,d in faces:
        obj += "f %d %d %d\nf %d %d %d\n" % (a,b,c,a,c,d)
    markers = {"kind":"fixture-markers","version":1,"module_id":"sample.module",
               "frame":"native_z_up_right_handed","placement":"identity","collision":"module.obj#Hull",
               "markers":[{"id":"dock","position":[.5,0,.25],"up":[0,0,1],"outward":[1,0,0]},
                          {"id":"top","position":[0,0,1],"up":[0,1,0],"outward":[0,0,1]}]}
    return {"module.obj":obj.encode("utf-8"),"markers.json":canonical(markers)}

def clone_identity():
    source = {"kind":"fixture-group","version":1,"objects":[
        {"id":"root","parent":None,"references":{"child":"child","external":"outside"}},
        {"id":"child","parent":"root","references":{"parent":"root"}}]}
    expected = {"kind":"fixture-group","version":1,"objects":[
        {"id":"copy_root","parent":None,"references":{"child":"copy_child","external":"outside"}},
        {"id":"copy_child","parent":"copy_root","references":{"parent":"copy_root"}}]}
    return {"source.group.json":canonical(source),"expected_clone.group.json":canonical(expected)}

def png(path, rgba):
    def chunk(kind, data):
        return struct.pack(">I",len(data))+kind+data+struct.pack(">I",zlib.crc32(kind+data)&0xffffffff)
    # Original 2x2 constant colour pixels: deterministic, no timestamp/metadata chunks.
    row = bytes(rgba)*2
    raw = b"\x89PNG\r\n\x1a\n"
    raw += chunk(b"IHDR",struct.pack(">IIBBBBB",2,2,8,6,0,0,0))
    raw += chunk(b"IDAT",zlib.compress(b"\0"+row+b"\0"+row,9))+chunk(b"IEND",b"")
    Path(path).write_bytes(raw)

def reset():
    import bpy
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 24
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 25

def bvec(g):
    return (g[0], -g[2], g[1])

def mesh(name, points, faces):
    import bpy, bmesh
    data = bpy.data.meshes.new(name+"Mesh")
    data.from_pydata([bvec(v) for v in points],[],faces)
    data.update()
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    obj = bpy.data.objects.new(name,data)
    bpy.context.collection.objects.link(obj)
    return obj

def cube(name="Cube", size=1):
    import bpy
    bpy.ops.mesh.primitive_cube_add(size=size,location=(0,0,0))
    obj=bpy.context.object
    obj.name=name
    obj.data.name=name+"Mesh"
    return obj

def empty(name, location=(0,0,0), scale=(1,1,1), parent=None):
    import bpy
    obj=bpy.data.objects.new(name,None)
    bpy.context.collection.objects.link(obj)
    obj.location=bvec(location)
    obj.scale=(scale[0],scale[2],scale[1])
    obj.parent=parent
    return obj

def export(path, animated=False, draco=False):
    import bpy
    options=dict(filepath=str(path),export_format="GLB",export_yup=True,
        export_texcoords=True,export_normals=True,export_tangents=False,
        export_materials="EXPORT",export_image_format="AUTO",export_extras=True,
        export_animations=animated,export_frame_range=True,export_frame_step=1,
        export_force_sampling=True,export_animation_mode="ACTIONS",export_nla_strips=False,
        export_anim_slide_to_zero=True,export_skins=True,export_leaf_bone=False,
        export_def_bones=False,export_armature_object_remove=False,
        export_rest_position_armature=True,export_apply=False,export_cameras=False,
        export_lights=False,export_morph=False,export_draco_mesh_compression_enable=draco,
        export_meshopt_compression_enable=False,export_use_gltfpack=False,
        export_gpu_instances=False,export_shared_accessors=False)
    bpy.ops.export_scene.gltf(**options)
    assert Path(path).stat().st_size < 200_000

def make_geometry(root):
    import bpy
    reset()
    cube()
    export(root/"im01_cube.glb")
    export(root/"im06_draco.glb", draco=True)

    reset()
    obj=cube("Textured")
    png(root/"base.png",(128,64,32,255))
    png(root/"normal.png",(128,128,255,255))
    base=bpy.data.images.load(str(root/"base.png"),check_existing=False)
    base.colorspace_settings.name="sRGB"
    normal=bpy.data.images.load(str(root/"normal.png"),check_existing=False)
    normal.colorspace_settings.name="Non-Color"
    first=bpy.data.materials.new("Textured")
    first.use_nodes=True
    nt=first.node_tree
    bsdf=nt.nodes.get("Principled BSDF")
    tex=nt.nodes.new("ShaderNodeTexImage"); tex.image=base
    nt.links.new(tex.outputs["Color"],bsdf.inputs["Base Color"])
    texn=nt.nodes.new("ShaderNodeTexImage"); texn.image=normal
    normal_node=nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(texn.outputs["Color"],normal_node.inputs["Color"])
    nt.links.new(normal_node.outputs["Normal"],bsdf.inputs["Normal"])
    second=bpy.data.materials.new("Factor")
    second.diffuse_color=(.25,.5,.75,1)
    obj.data.materials.append(first); obj.data.materials.append(second)
    for face in obj.data.polygons: face.material_index=face.index%2
    export(root/"im02_embedded.glb")
    def factors(doc):
        for mat in doc["materials"]:
            if mat["name"]=="Textured":
                mat["alphaMode"]="MASK"; mat["alphaCutoff"]=.4
                mat["pbrMetallicRoughness"]["baseColorFactor"]=[1,1,1,1]
            else: mat["pbrMetallicRoughness"]["baseColorFactor"]=[.25,.5,.75,1]
    patch(root/"im02_embedded.glb",factors)
    doc,blob=read_glb(root/"im02_embedded.glb")
    for image in doc["images"]:
        name=image.get("name","").lower()
        image.pop("bufferView",None)
        image["uri"]="normal.png" if "normal" in name else "base.png"
    (root/"im02_relative.glb").write_bytes(glb_bytes(doc,blob))
    doc,blob=read_glb(root/"im02_relative.glb")
    for image in doc["images"]:
        if image["uri"]=="normal.png": image["uri"]="missing_normal.png"
    (root/"im07_missing_texture.glb").write_bytes(glb_bytes(doc,blob))

    reset()
    parent=empty("Parent",scale=(2,3,4))
    tri=mesh("Slanted",[(0,0,0),(1,0,0),(0,1,1)],[(0,1,2)])
    tri.parent=parent; tri.scale=(.5,.25,2) # glTF scale (.5,2,.25)
    mirror=cube("Mirror"); mirror.location=bvec((4,0,0)); mirror.scale=(-1,1,1)
    reflected=empty("ReflectionParent",location=(8,0,0),scale=(-1,1,1))
    double=cube("DoubleReflection"); double.parent=reflected; double.scale=(-1,1,1)
    export(root/"im04_transforms.glb")
    reset()
    zero=cube("Zero"); zero.scale=(0,1,1)
    export(root/"im04_zero.glb")

    reset()
    profile=[(-.1,0),(.1,0),(.1,1.3),(.3,1.3),(0,2),(-.3,1.3),(-.1,1.3)]
    points=[(x,y,z) for y in [-.05,.05] for x,z in profile]
    n=len(profile)
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    mesh("Arrow",points,faces)
    marker=empty("RightMarker",(-.35,.2,.7))
    marker["up"]=[0,1,0]; marker["outward"]=[-1,0,0]
    export(root/"im05_arrow.glb")

    reset()
    triangle=mesh("RepairTriangle",[(0,0,0),(1,0,0),(0,1,0)],[(0,1,2)])
    export(root/"im06_degenerate.glb")
    # Deliberately duplicate one valid triangle as a zero-area index triplet.
    doc,blob=read_glb(root/"im06_degenerate.glb")
    primitive=doc["meshes"][0]["primitives"][0]
    accessor=doc["accessors"][primitive["indices"]]
    view=doc["bufferViews"][accessor["bufferView"]]
    offset=len(blob); blob+=b"\0"*(-len(blob)%4); offset=len(blob)
    blob+=struct.pack("<6H",0,1,2,0,0,0)
    doc["bufferViews"].append({"buffer":0,"byteOffset":offset,"byteLength":12,"target":34963})
    accessor.update({"bufferView":len(doc["bufferViews"])-1,"byteOffset":0,"count":6,"componentType":5123})
    accessor.pop("max",None); accessor.pop("min",None)
    doc["buffers"][0]["byteLength"]=len(blob)
    (root/"im06_degenerate.glb").write_bytes(glb_bytes(doc,blob))
    doc,blob=read_glb(root/"im01_cube.glb")
    doc["extensionsUsed"]=["VENDOR_fixture_required"]
    doc["extensionsRequired"]=["VENDOR_fixture_required"]
    doc.setdefault("extensions",{})["VENDOR_fixture_required"]={"original_fixture":True}
    (root/"im06_required_extension.glb").write_bytes(glb_bytes(doc,blob))
    doc,blob=read_glb(root/"im06_degenerate.glb")
    a=doc["accessors"][doc["meshes"][0]["primitives"][0]["indices"]]
    bv=doc["bufferViews"][a["bufferView"]]
    damaged=bytearray(blob); struct.pack_into("<H",damaged,bv.get("byteOffset",0)+a.get("byteOffset",0),65535)
    (root/"im06_bad_index.glb").write_bytes(glb_bytes(doc,bytes(damaged)))

    reset()
    left=cube("Left"); left.data.name="SharedDisplay"; left.location=bvec((-2,0,0))
    left["instance_id"]="left"; left["asset_id"]="asset_a"
    right=bpy.data.objects.new("Right",left.data); bpy.context.collection.objects.link(right)
    right.location=bvec((2,0,0)); right["instance_id"]="right"; right["asset_id"]="asset_a"
    export(root/"im10_instances.glb")
    reset()
    other=cube("Other",size=2); other.data.name="SharedDisplay"
    other["instance_id"]="other"; other["asset_id"]="asset_b"
    export(root/"im10_other_asset.glb")

    reset()
    obj=mesh("CornerOrigin",[(x,y,z) for z in (0,3) for y in (0,2) for x in (0,1)],
             [(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)])
    obj.location=bvec((-.075,.375,1.225))
    obj.rotation_euler[2]=math.pi/2
    obj["placement_anchor"]=[.125,.375,.625]; obj["pivot"]=[.2,0,.4]; obj["snap_enabled"]=False
    export(root/"im11_origin.glb")

def make_skin(root):
    import bpy
    reset()
    data=bpy.data.armatures.new("ThreeJointRig")
    rig=bpy.data.objects.new("Rig",data); bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active=rig; rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    previous=None
    for i in range(3):
        bone=data.edit_bones.new("Joint"+str(i)); bone.head=(0,0,i); bone.tail=(0,0,i+1)
        bone.parent=previous; previous=bone
    bpy.ops.object.mode_set(mode="OBJECT")
    obj=mesh("Skinned",[(0,0,0),(1,0,0),(0,1,0)],[(0,1,2)])
    for i in range(3): obj.vertex_groups.new(name="Joint"+str(i)).add([i],1.0,"REPLACE")
    obj.parent=rig
    modifier=obj.modifiers.new("Skin","ARMATURE"); modifier.object=rig
    bone=rig.pose.bones["Joint0"]
    bone.location=(0,0,0); bone.keyframe_insert(data_path="location",frame=1)
    bone.location=(.25,0,0); bone.keyframe_insert(data_path="location",frame=25)
    rig.animation_data.action.name="OneSecond"
    bpy.context.scene.frame_set(1)
    export(root/"im03_skin.glb",animated=True)


def expected():
    tol=1e-5
    def case(id, file, outcome, diagnostics, checks, why):
        return dict(id=id,file=file,outcome=outcome,diagnostics=diagnostics,checks=checks,why=why)
    def made(function):
        return {"generated_by":"make_fixtures.py:"+function}
    cases=[
      case("IM-01","im01_cube.glb","imports",[],
           {"triangles":12,"bounds_min":[-.5,-.5,-.5],"bounds_max":[.5,.5,.5],"normals":True,"uv0":True,"tolerance":tol},
           "A unit cube establishes metres, UVs and outward triangle normals."),
      case("IM-02","im02_embedded.glb","imports",[],
           {"materials":2,"images":2,"embedded_images":2,"texture_roles":{"baseColorTexture":"sRGB","normalTexture":"linear"},
            "base_rgba8":[128,64,32,255],"normal_rgba8":[128,128,255,255],"image_size":[2,2],
            "Textured":{"baseColorFactor":[1,1,1,1],"alphaMode":"MASK","alphaCutoff":.4},
            "Factor":{"baseColorFactor":[.25,.5,.75,1]},"tolerance":tol},
           "Two material roles distinguish encoded colour from linear normal data without needing rendering."),
      case("IM-02","im02_relative.glb","imports",[],
           {"materials":2,"relative_images":["base.png","normal.png"],"allow_resource_root":"fixture directory",
            "texture_roles":{"baseColorTexture":"sRGB","normalTexture":"linear"},"image_size":[2,2]},
           "Permitted relative PNG resources resolve beside their asset."),
      case("IM-03","im03_skin.glb","imports-with-warnings",["warning: skinning not displayed (deferred) on mesh 'Skinned'"],
           {"skins":1,"joints":3,"clips":1,"duration_seconds":1.0,"static_pose":"bind/rest at t=0; animation is reported, not played",
            "bind_vertices":[[0,0,0],[1,0,0],[0,1,0]],"weights_per_vertex":[1,1,1],"inverse_bind_matrices":3,"tolerance":tol},
           "A three-joint skin reports one second of animation while keeping its declared bind pose."),
      case("IM-04","im04_transforms.glb","imports-with-warnings",["warning: negative world determinant on node 'Mirror'; winding corrected once"],
           {"policy":"bake world transforms; inverse-transpose normals; combined determinant once",
            "Slanted":{"world_scale":[1,6,1],"world_vertices":[[0,0,0],[1,0,0],[0,6,1]],
                       "normal":[0,-1/math.sqrt(37),6/math.sqrt(37)],"determinant":6},
            "Mirror":{"determinant":-1,"winding_flips":1,"bounds_min":[3.5,-.5,-.5],"bounds_max":[4.5,.5,.5]},
            "DoubleReflection":{"determinant":1,"winding_flips":0,"bounds_min":[7.5,-.5,-.5],"bounds_max":[8.5,.5,.5]},"tolerance":tol},
           "Nested scaling and one versus two reflections expose incorrect normal or double-winding fixes."),
      case("IM-04","im04_zero.glb","refused",["error: singular zero scale on node 'Zero'; choose a non-zero scale"],
           {"node":"Zero","world_determinant":0,"committed_output_changes":0},
           "A singular transform cannot produce an inverse-transpose normal and must not replace good output."),
      case("IM-05","im05_arrow.glb","imports",[],
           {"triangles":24,"length":2,"bounds_min":[-.3,-.05,0],"bounds_max":[.3,.05,2],"signed_volume":.047,
            "input":{"front":[0,0,1],"up":[0,1,0],"right":[-1,0,0],"marker":[-.35,.2,.7],"marker_up":[0,1,0],"marker_outward":[-1,0,0]},
            "y_up_left_handed":{"map":"(-x,y,z)","determinant":-1,"front":[0,0,1],"up":[0,1,0],"marker":[.35,.2,.7],"marker_outward":[1,0,0],"winding_flips":1},
            "z_up_right_handed":{"map":"(-x,z,y)","determinant":1,"front":[0,1,0],"up":[0,0,1],"marker":[.35,.7,.2],"marker_outward":[1,0,0],"winding_flips":0},"tolerance":tol},
           "An asymmetric marker fixes the sign of right while the two-metre arrow fixes forward and winding."),
      case("IM-06",made("generated_truncated"),"refused",["error: truncated.glb: declared GLB length exceeds available bytes"],
           {"source":"im01_cube.glb","bytes_removed":11,"committed_output_changes":0},"A truncated container is refused before allocation or output replacement."),
      case("IM-06",made("generated_wrong_extension"),"refused",["error: unsupported.fbx: FBX is not supported; export glTF 2.0"],
           {"signature":"glTF","extension":".fbx","committed_output_changes":0},"An unsupported extension is not reported as a successful import."),
      case("IM-06","im06_required_extension.glb","refused",["error: im06_required_extension.glb: required extension VENDOR_fixture_required is unsupported"],
           {"required_extension":"VENDOR_fixture_required","committed_output_changes":0},"Required unknown features are refused rather than silently ignored."),
      case("IM-06","im06_draco.glb","refused",["error: im06_draco.glb: required compression KHR_draco_mesh_compression is unsupported"],
           {"required_extension":"KHR_draco_mesh_compression","compressed":True,"committed_output_changes":0},"A real compressed fixture pins the deferred-compression diagnostic."),
      case("IM-06","im06_bad_index.glb","refused",["error: im06_bad_index.glb: index 65535 exceeds POSITION count 3"],
           {"bad_index":65535,"position_count":3,"committed_output_changes":0},"An out-of-range index cannot be accepted or used for an unbounded allocation."),
      case("IM-06","im06_degenerate.glb","imports-with-warnings",["warning: im06_degenerate.glb: removed 1 degenerate triangle"],
           {"input_triangles":2,"removed_degenerate_triangles":1,"triangles":1,"policy":"remove zero-area triangles; preserve valid geometry","tolerance":tol},
           "A permitted geometric repair reports its exact count rather than pretending the input was clean."),
      case("IM-07",made("missing_references"),"imports-with-warnings",
           ["error: object 'placeholder': asset 'missing_asset.glb' not found; retain placeholder",
            "warning: object 'textured': optional texture 'missing_normal.png' not found; keep material fallback"],
           {"object_count":3,"ids":["healthy","placeholder","textured"],"placeholder_count":1,
            "preserved_asset_path":"missing_asset.glb","preserved_texture_path":"missing_normal.png",
            "unaffected_object":"healthy","save_preserves_all_references":True},
           "Missing resources retain placeholders, unaffected objects and unresolved paths across save."),
      case("IM-07","im07_missing_texture.glb","imports-with-warnings",["warning: im07_missing_texture.glb: optional normal texture 'missing_normal.png' not found"],
           {"materials":2,"missing_texture":"missing_normal.png","other_texture":"base.png","triangles":12,"preserved_reference":True},
           "A missing optional normal map is distinct from a missing required asset."),
      case("IM-08",{"generated_by":"make_fixtures.py:roundtrip_documents"},"imports",[],
           {"document":"fixture-scene version 1, roundtrip.scene.json member","ids":["first","second"],"save_load_save_byte_identical":True,
            "first_position":[.125,.375,.625],"first_scale":[1,2,1],"first_properties":{"label":"café","enabled":True},
            "peer_reference":"second","tolerance":tol,
            "lifecycle":{"dirty_after_edit":True,"revert_restores_position":[.125,.375,.625],"own_save_disk":"Unchanged",
                         "failed_save_old_bytes_unchanged":True,"failed_save_temporary_files":0,"external_edit_writes":0,
                         "undo_after_save_dirty":True,"cancel_close_preserves_open_document":True}},
           "A neutral scene pins canonical round trips and the later session lifecycle acceptance cases."),
      case("IM-08",{"generated_by":"make_fixtures.py:roundtrip_documents"},"refused",["error: duplicate_ids.scene.json: object id 'first' is duplicated"],
           {"member":"duplicate_ids.scene.json","duplicate_id":"first","committed_output_changes":0},"Duplicate object ids cannot create a partly loaded scene."),
      case("IM-08",{"generated_by":"make_fixtures.py:roundtrip_documents"},"refused",["error: unknown_property.scene.json: $.unexpected is unknown"],
           {"member":"unknown_property.scene.json","path":"$.unexpected","committed_output_changes":0},"Unknown document fields are diagnosed rather than silently discarded."),
      case("IM-08",{"generated_by":"make_fixtures.py:roundtrip_documents"},"refused",["error: duplicate_property.scene.json: $.version occurs twice"],
           {"member":"duplicate_property.scene.json","path":"$.version","committed_output_changes":0},"Duplicate JSON properties are refused even when their values match."),
      case("IM-08",{"generated_by":"make_fixtures.py:roundtrip_documents"},"refused",["error: future_version.scene.json: $.version 999 is unsupported"],
           {"member":"future_version.scene.json","version":999,"committed_output_changes":0},"A future version cannot be guessed or rewritten as the current format."),
      case("IM-09",made("module_with_markers"),"imports-with-warnings",["warning: module.obj: UV0 is absent (intentional native kit analogue)"],
           {"triangles":12,"bounds_min":[-.5,-.5,0],"bounds_max":[.5,.5,1],"uv0":False,"frame":"native_z_up_right_handed",
            "conversion_count":0,"collision":"module.obj#Hull","markers":[
            {"id":"dock","position":[.5,0,.25],"up":[0,0,1],"outward":[1,0,0]},
            {"id":"top","position":[0,0,1],"up":[0,1,0],"outward":[0,0,1]}],"tolerance":tol},
           "An original native-frame module preserves marker vectors and collision identity without a second conversion."),
      case("IM-10","im10_instances.glb","imports",[],
           {"meshes":1,"mesh_nodes":2,"node_names":["Left","Right"],"instance_ids":["left","right"],
            "positions":[[-2,0,0],[2,0,0]],"shared_mesh":True,"asset_id":"asset_a","tolerance":tol},
           "Two distinct node identities share geometry without collapsing placement or instances."),
      case("IM-10","im10_other_asset.glb","imports",[],
           {"asset_id":"asset_b","other_asset":"im10_instances.glb","other_asset_id":"asset_a","mesh_display_name":"SharedDisplay",
            "same_display_name_different_identity":True,"triangles":12,"bounds_min":[-1,-1,-1],"bounds_max":[1,1,1],"tolerance":tol},
           "Matching display names in two assets do not merge their persistent identities."),
      case("IM-10",made("clone_identity"),"imports",[],
           {"source_ids":["root","child"],"clone_ids":["copy_root","copy_child"],"clone_child_parent":"copy_root",
            "clone_root_child_reference":"copy_child","external_reference":"outside","save_undo_reload_preserve_identity":True},
           "A clone target remaps internal ids while retaining its external reference."),
      case("IM-11","im11_origin.glb","imports",[],
           {"local_bounds_min":[0,0,0],"local_bounds_max":[1,2,3],"placement_anchor":[.125,.375,.625],"pivot":[.2,0,.4],
            "rotation_y_degrees":90,"node_translation":[-.075,.375,1.225],
            "world_bounds_min":[-.075,.375,.225],"world_bounds_max":[2.925,2.375,1.225],
            "snap_enabled":False,"grid_step":1,"optional_snapped_anchor":[0,0,1],
            "optional_snapped_world_bounds_min":[-.2,0,.6],"optional_snapped_world_bounds_max":[2.8,2,1.6],
            "roundtrip_preserves_anchor_and_pivot":True,"tolerance":tol},
           "Free placement of a rotated minimum-corner object retains the authored pivot and off-grid anchor.")]
    return {"kind":"import-fixtures","version":1,"cases":cases}

def main():
    import bpy
    assert bpy.app.background, "Run Blender with -b"
    assert bpy.app.version[:2] == (5,2), "Fixtures pin Blender 5.2"
    root=Path(sys.argv[sys.argv.index("--")+1]).resolve()
    root.mkdir(parents=True,exist_ok=True)
    make_geometry(root); make_skin(root)
    draco,_=read_glb(root/"im06_draco.glb")
    assert "KHR_draco_mesh_compression" in draco.get("extensionsRequired",[])
    (root/"expected.json").write_bytes(canonical(expected()))
    for p in sorted(root.iterdir()):
        if p.is_file() and p.suffix in {".glb",".bin",".png",".json"}:
            assert p.stat().st_size<200_000, (p.name,p.stat().st_size)
            print("SHA256",p.name,hashlib.sha256(p.read_bytes()).hexdigest())

if __name__=="__main__":
    main()
