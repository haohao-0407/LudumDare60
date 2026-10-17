"""Run inside Blender after the MarsBase_RetroFuture architecture is built."""
import json
import math
from pathlib import Path

import bpy
from mathutils import Vector


PROJECT = Path(r"D:\unity-projects\LudumDare60")
SOURCE = PROJECT / "SourceArt" / "MarsBase"
COLLECTION_NAME = "MarsBase_Meshy_T25"
BLEND_PATH = SOURCE / "MarsBase_RetroFuture.blend"
METRICS_PATH = SOURCE / "meshy_blender_metrics.json"


def find_glb(name):
    expected = PROJECT / "Assets" / "MarsBase" / "Models" / "Meshy" / name
    if expected.is_file():
        return expected
    matches = sorted(
        p for p in (PROJECT / "Assets" / "MarsBase").rglob("*")
        if p.is_file() and p.name.casefold() == name.casefold()
    )
    if len(matches) != 1:
        raise RuntimeError(f"Expected one {name}; found {len(matches)}: {matches}")
    return matches[0]


def bounds(objects):
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    points = [
        evaluated.matrix_world @ Vector(corner)
        for obj in objects if obj.type == "MESH"
        for evaluated in [obj.evaluated_get(depsgraph)]
        for corner in evaluated.bound_box
    ]
    if not points:
        raise RuntimeError("Imported prop contains no mesh bounds.")
    minimum = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    maximum = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    return minimum, maximum


def normalize(root, objects, xy, longest, yaw_degrees=0.0):
    root.rotation_euler.z = math.radians(yaw_degrees)
    minimum, maximum = bounds(objects)
    extent = max(maximum - minimum)
    if extent <= 1e-6:
        raise RuntimeError(f"Degenerate bounds: {root.name}")
    root.scale = (longest / extent,) * 3
    minimum, maximum = bounds(objects)
    center = (minimum + maximum) * 0.5
    root.location += Vector((xy[0] - center.x, xy[1] - center.y, -0.075 - minimum.z))
    bpy.context.view_layer.update()


def import_prop(path, name, collection, xy, longest, yaw=0.0):
    before = set(bpy.data.objects)
    result = bpy.ops.import_scene.gltf(filepath=str(path))
    if "FINISHED" not in result:
        raise RuntimeError(f"GLB import did not finish: {path}")
    objects = list(set(bpy.data.objects) - before)
    if not any(obj.type == "MESH" for obj in objects):
        raise RuntimeError(f"GLB has no meshes: {path}")
    root = bpy.data.objects.new(name, None)
    collection.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root["source_glb"] = str(path)
    root["ai_model"] = "meshy-t2.5"
    for obj in objects:
        for old_collection in list(obj.users_collection):
            old_collection.objects.unlink(obj)
        collection.objects.link(obj)
        if obj.parent not in objects:
            world = obj.matrix_world.copy()
            obj.parent = root
            obj.matrix_world = world
    normalize(root, objects, xy, longest, yaw)
    return root


def shared_duplicate(root, name, collection, offset):
    originals = [root] + list(root.children_recursive)
    mapping = {obj: obj.copy() for obj in originals}
    for original, duplicate in mapping.items():
        collection.objects.link(duplicate)
        duplicate.parent = mapping.get(original.parent, original.parent)
        # Object.copy retains shared mesh data, materials and texture nodes.
    duplicate_root = mapping[root]
    duplicate_root.name = name
    duplicate_root.location += Vector(offset)
    duplicate_root["shared_duplicate_of"] = root.name
    bpy.context.view_layer.update()
    return duplicate_root


def prop_metrics(root):
    objects = list(root.children_recursive)
    minimum, maximum = bounds(objects)
    triangles = 0
    for obj in objects:
        if obj.type == "MESH":
            obj.data.calc_loop_triangles()
            triangles += len(obj.data.loop_triangles)
    dimensions = maximum - minimum
    return {
        "root": root.name,
        "source_glb": root.get("source_glb"),
        "ai_model": root.get("ai_model"),
        "shared_duplicate_of": root.get("shared_duplicate_of"),
        "mesh_objects": sum(obj.type == "MESH" for obj in objects),
        "triangles": triangles,
        "bounds_min": list(minimum),
        "bounds_max": list(maximum),
        "dimensions": list(dimensions),
        "longest_dimension_m": max(dimensions),
        "yaw_degrees": math.degrees(root.rotation_euler.z),
    }


def main():
    scene = bpy.data.scenes.get("MarsBase_RetroFuture")
    if scene is None:
        raise RuntimeError("Expected existing scene MarsBase_RetroFuture.")
    if bpy.context.window:
        bpy.context.window.scene = scene
    elif bpy.context.scene != scene:
        raise RuntimeError("Run with MarsBase_RetroFuture as the active scene.")
    collection = bpy.data.collections.get(COLLECTION_NAME)
    if collection is not None:
        print(f"Skipped imports: {COLLECTION_NAME} already exists.")
        if not collection.get("import_complete", False):
            raise RuntimeError("Existing prop collection is incomplete; inspect before retrying.")
    else:
        rover = find_glb("Rover_T25.glb")
        life_support = find_glb("LifeSupport_T25.glb")
        collection = bpy.data.collections.new(COLLECTION_NAME)
        scene.collection.children.link(collection)
        import_prop(rover, "Meshy_Rover_T25", collection, (-18, -24), 6.8, -20)
        support = import_prop(life_support, "Meshy_LifeSupport_T25_A", collection, (10, -21), 4.2)
        shared_duplicate(support, "Meshy_LifeSupport_T25_B", collection, (4, 1, 0))
        collection["import_complete"] = True
    roots = sorted((obj for obj in collection.objects if obj.parent is None), key=lambda obj: obj.name)
    metrics = {
        "scene": scene.name,
        "blend_path": str(BLEND_PATH),
        "coordinate_system": "Blender Z-up, metres",
        "props": [prop_metrics(root) for root in roots],
    }
    SOURCE.mkdir(parents=True, exist_ok=True)
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    METRICS_PATH.write_text(json.dumps(metrics, indent=2), encoding="utf-8")
    print(json.dumps(metrics, indent=2))


if __name__ == "__main__":
    main()
