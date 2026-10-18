"""Export the complete cabin and five reusable screen modules for Unity.

Run ``run()`` in the live Blender session after building the wall-integrated
cockpit. This does not save or replace the Blender source file.
"""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import struct
import sys

import bpy
from mathutils import Vector


HERE = Path(__file__).resolve().parent
OUTPUT = HERE / 'UnityParts'
SCENE_NAME = 'CompactCockpit_WallIntegrated'
SCREEN_NAMES = ('RADAR', 'ATTITUDE', 'NAVIGATION', 'COMMUNICATIONS', 'SYSTEMS')
GEOMETRY_TYPES = {'MESH', 'CURVE', 'FONT'}


def _glb_summary(path):
    """Check the exported container and report useful import diagnostics."""
    with path.open('rb') as stream:
        magic, version, total = struct.unpack('<4sII', stream.read(12))
        if magic != b'glTF' or version != 2 or total != path.stat().st_size:
            raise RuntimeError('Invalid GLB container: ' + str(path))
        chunk_size, chunk_type = struct.unpack('<II', stream.read(8))
        if chunk_type != 0x4E4F534A:
            raise RuntimeError('GLB is missing its JSON chunk: ' + str(path))
        document = json.loads(stream.read(chunk_size).decode('utf-8'))
    if len(document.get('scenes', [])) != 1:
        raise RuntimeError('Export unexpectedly contains multiple scenes: ' + str(path))
    return {
        'file': path.name,
        'bytes': total,
        'scenes': len(document.get('scenes', [])),
        'meshes': len(document.get('meshes', [])),
        'images': len(document.get('images', [])),
        'embedded_images': sum('bufferView' in item for item in document.get('images', [])),
    }


def _export_snapshot(source_scene, source_objects, path):
    """Bake only the requested evaluated objects into an isolated export scene."""
    previous_scene = bpy.context.window.scene
    export_scene = bpy.data.scenes.new('_UnityCockpitExportTemporary')
    copies = []
    meshes = []
    try:
        bpy.context.window.scene = source_scene
        bpy.context.view_layer.update()
        depsgraph = bpy.context.evaluated_depsgraph_get()
        for source in source_objects:
            mesh = bpy.data.meshes.new_from_object(
                source.evaluated_get(depsgraph),
                preserve_all_data_layers=True,
                depsgraph=depsgraph,
            )
            meshes.append(mesh)
            copy = bpy.data.objects.new(source.name, mesh)
            copies.append(copy)
            export_scene.collection.objects.link(copy)
            copy.matrix_world = source.matrix_world.copy()
        bpy.context.window.scene = export_scene
        bpy.context.view_layer.update()
        result = bpy.ops.export_scene.gltf(
            filepath=str(path),
            export_format='GLB',
            use_selection=False,
            use_active_scene=True,
            export_apply=True,
            export_yup=True,
            export_animations=False,
            export_cameras=False,
            export_lights=False,
        )
        if 'FINISHED' not in result:
            raise RuntimeError('glTF export did not finish: ' + str(path))
        return _glb_summary(path)
    finally:
        bpy.context.window.scene = previous_scene
        for obj in copies:
            bpy.data.objects.remove(obj, do_unlink=True)
        for mesh in meshes:
            if mesh.users == 0:
                bpy.data.meshes.remove(mesh)
        bpy.data.scenes.remove(export_scene)


def _dispose_authored_scene(scene, restore_scene):
    """Remove just the temporary screen scene, its objects and geometry."""
    bpy.context.window.scene = restore_scene
    objects = list(scene.objects)
    collections = list(scene.collection.children)
    geometry = {obj.data for obj in objects if obj.type in GEOMETRY_TYPES}
    for obj in objects:
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.scenes.remove(scene)
    for collection in collections:
        if collection.users == 0:
            bpy.data.collections.remove(collection)
    for data in geometry:
        if data.users != 0:
            continue
        if isinstance(data, bpy.types.Mesh):
            bpy.data.meshes.remove(data)
        elif isinstance(data, bpy.types.Curve):
            bpy.data.curves.remove(data)


def _export_screen(entry, restore_scene):
    # A fresh builder supplies fresh geometry batches and ensures screws and
    # the nameplate accompany this screen, rather than other displays' batches.
    module_name = '_unity_screen_' + entry['name'].lower()
    spec = importlib.util.spec_from_file_location(module_name, str(HERE / 'build_cockpit.py'))
    builder = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = builder
    temporary_scene = None
    try:
        spec.loader.exec_module(builder)
        builder.setup()
        temporary_scene = builder.scene
        builder.screen(
            entry['name'], (0, 0, 0), entry['width'], entry['height'],
            builder.FRONT, entry['texture'],
        )
        builder.materialize()
        objects = [obj for obj in temporary_scene.objects if obj.type in GEOMETRY_TYPES]
        summary = _export_snapshot(temporary_scene, objects, OUTPUT / entry['file'])
        if summary['images'] != 1 or summary['embedded_images'] != 1:
            raise RuntimeError('Screen texture was not embedded: ' + entry['name'])
        return summary
    finally:
        # setup() normally sets scene immediately; handle a partial setup error.
        temporary_scene = temporary_scene or getattr(builder, 'scene', None)
        if temporary_scene is not None:
            _dispose_authored_scene(temporary_scene, restore_scene)
        else:
            bpy.context.window.scene = restore_scene
        sys.modules.pop(module_name, None)


def run():
    original_scene = bpy.context.window.scene
    source_scene = bpy.data.scenes.get(SCENE_NAME)
    builder = bpy.app.driver_namespace.get('cockpit_wall')
    if source_scene is None or builder is None:
        raise RuntimeError('The wall-integrated scene and cockpit_wall builder must be loaded.')
    specs = {item['name']: item for item in builder.screen_specs}
    if set(specs) != set(SCREEN_NAMES):
        raise RuntimeError('Expected the five authored wall screens; found ' + repr(sorted(specs)))
    entries = []
    for name in SCREEN_NAMES:
        item = specs[name]
        normal = Vector(item['normal']).normalized()
        right = Vector((0, 0, 1)).cross(normal).normalized()
        up = normal.cross(right).normalized()
        entries.append({
            'name': name,
            'file': 'Screen_' + name + '.glb',
            'position': list(Vector(item['center']) - normal * .063),
            'normal': list(normal),
            'up': list(up),
            'width': item['size'][0],
            'height': item['size'][1],
            'texture': item['texture'],
        })
    OUTPUT.mkdir(parents=True, exist_ok=True)
    summaries = []
    captured_output = io.StringIO()
    try:
        # Keep verbose exporter and builder logs out of the MCP result.
        with contextlib.redirect_stdout(captured_output):
            architecture = [
                obj for obj in source_scene.objects
                if obj.type in GEOMETRY_TYPES
                and not (obj.parent and obj.parent.name.startswith('08_Displays'))
            ]
            summaries.append(_export_snapshot(
                source_scene, architecture, OUTPUT / 'CompactCockpit_Architecture.glb',
            ))
            if summaries[0]['images']:
                raise RuntimeError('Architecture unexpectedly retains a display texture.')
            for entry in entries:
                summaries.append(_export_screen(entry, source_scene))
        manifest = {
            'scene': source_scene.name,
            'units': 'metres',
            'assembly_coordinates': 'Blender world coordinates: X right, Y forward, Z up',
            'screen_local_coordinates': 'Blender: normal -Y, up +Z; glTF Y-up: normal +Z, up +Y',
            'architecture': 'CompactCockpit_Architecture.glb',
            'entries': entries,
            'exports': summaries,
        }
        manifest_path = OUTPUT / 'display_assembly.json'
        manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    finally:
        bpy.context.window.scene = original_scene
    result = {'manifest': str(manifest_path), 'directory': str(OUTPUT), 'exports': summaries}
    print(json.dumps(result, ensure_ascii=False))
    return result


if __name__ == '__main__':
    run()
