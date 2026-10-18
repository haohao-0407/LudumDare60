"""Add two real glazed side ports to the already-materialized round cabin.

Usage: summary = side_windows.apply(cc)
The operation applies exact wall booleans and creates native mesh objects.
It deliberately rejects repeated/partially completed application.
"""
import math
import bpy
from mathutils import Matrix, Vector


CENTER_Y = -.16
CENTER_Z = 1.43
CUT_RADIUS = .24
CLEAR_RADIUS = .23
GLASS_RADIUS = .237
FRAME_X = .995
STATE_KEY = 'side_port_window_operation'


def _enum(owner, prop, wanted):
    options = {item.identifier for item in owner.bl_rna.properties[prop].enum_items}
    if wanted not in options:
        raise RuntimeError('%s does not support %s; available: %s' %
                           (prop, wanted, ', '.join(sorted(options))))
    return wanted


def _basis(axis):
    axis = Vector(axis).normalized()
    u = Vector((0, 0, 1)).cross(axis).normalized()
    v = axis.cross(u).normalized()
    return u, v, axis


def _lathe_geometry(center, axis, profile, segments=160):
    """Closed radial/depth profile in CCW order; no open ring ends."""
    center = Vector(center)
    u, v, axis = _basis(axis)
    count = len(profile)
    verts = []
    for j in range(segments):
        angle = math.tau * j / segments
        radial = u * math.cos(angle) + v * math.sin(angle)
        verts.extend(center + radial * radius + axis * depth
                     for radius, depth in profile)
    faces = []
    for j in range(segments):
        following = (j + 1) % segments
        for k in range(count):
            kk = (k + 1) % count
            faces.append((j * count + k, following * count + k,
                          following * count + kk, j * count + kk))
    return verts, faces, [True] * len(faces)


def _cylinder_geometry(center, axis, radius, bottom, top, segments=160):
    center = Vector(center)
    u, v, axis = _basis(axis)
    verts = [center + (u * math.cos(math.tau * j / segments) +
                       v * math.sin(math.tau * j / segments)) * radius +
             axis * depth
             for depth in (bottom, top) for j in range(segments)]
    faces = [(j, (j + 1) % segments, (j + 1) % segments + segments,
              j + segments) for j in range(segments)]
    faces.extend([tuple(reversed(range(segments))),
                  tuple(range(segments, segments * 2))])
    return verts, faces, [True] * segments + [False, False]


def _box_geometry(center, u, v, normal, width, height, depth):
    center = Vector(center)
    verts = [center + u * x * width / 2 + v * y * height / 2 +
             normal * z * depth / 2
             for x, y, z in [(-1, -1, -1), (1, -1, -1), (1, 1, -1),
                             (-1, 1, -1), (-1, -1, 1), (1, -1, 1),
                             (1, 1, 1), (-1, 1, 1)]]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
             (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return verts, faces, [False] * len(faces)


def _combine(parts):
    verts, faces, smooth = [], [], []
    for pv, pf, ps in parts:
        offset = len(verts)
        verts.extend(pv)
        faces.extend(tuple(index + offset for index in face) for face in pf)
        smooth.extend(ps)
    return verts, faces, smooth


def _object(cc, name, group, material, geometry, parent=True):
    verts, faces, smooth = geometry
    mesh = bpy.data.meshes.new(name + '_Mesh')
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    if material is not None:
        mesh.materials.append(material)
    for polygon, flag in zip(mesh.polygons, smooth):
        polygon.use_smooth = flag
    obj = bpy.data.objects.new(name, mesh)
    cc.collections[group].objects.link(obj)
    if parent:
        obj.parent = cc.b.groups[group]
        obj.matrix_parent_inverse = obj.parent.matrix_world.inverted()
    obj.matrix_world = Matrix.Identity(4)
    return obj


def _glass_material(cc):
    material = bpy.data.materials.new('WindowGlass')
    material.use_nodes = True
    material.diffuse_color = (.18, .43, .46, .18)
    bsdf = next(node for node in material.node_tree.nodes
                if node.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = (.18, .43, .46, .18)
    bsdf.inputs['Alpha'].default_value = .18
    bsdf.inputs['Roughness'].default_value = .13
    bsdf.inputs['Metallic'].default_value = 0
    bsdf.inputs['IOR'].default_value = 1.45
    transmission = bsdf.inputs.get('Transmission Weight') or bsdf.inputs.get('Transmission')
    if transmission is not None:
        transmission.default_value = .25
    if hasattr(material, 'surface_render_method'):
        options = {item.identifier for item in material.bl_rna.properties[
            'surface_render_method'].enum_items}
        chosen = next((name for name in ('DITHERED', 'BLENDED') if name in options), None)
        if chosen is None:
            raise RuntimeError('WindowGlass needs a supported transparent surface render method')
        material.surface_render_method = _enum(material, 'surface_render_method', chosen)
    elif hasattr(material, 'blend_method'):
        material.blend_method = _enum(material, 'blend_method', 'BLEND')
    material.use_backface_culling = False
    if hasattr(material, 'use_screen_refraction'):
        material.use_screen_refraction = True
    material['purpose'] = 'Thin transparent port glass; direct alpha retained for glTF/Unity'
    material['base_alpha'] = .18
    cc.b.materials['WindowGlass'] = material
    return material


def _has_parent(obj, target):
    parent = obj.parent
    while parent is not None:
        if parent == target:
            return True
        parent = parent.parent
    return False


def _candidates(cc, side, group):
    # Conservative world-space bounds filter before the exact boolean.
    low = Vector((side * 1.10 - .36, CENTER_Y - CUT_RADIUS, CENTER_Z - CUT_RADIUS))
    high = Vector((side * 1.10 + .36, CENTER_Y + CUT_RADIUS, CENTER_Z + CUT_RADIUS))
    result = []
    for obj in cc.scene.objects:
        if obj.type != 'MESH' or not _has_parent(obj, cc.b.groups[group]):
            continue
        if not obj.name.startswith(('PressureShell_', 'CurvedWallPanel_')):
            continue
        corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
        minimum = Vector(tuple(min(p[k] for p in corners) for k in range(3)))
        maximum = Vector(tuple(max(p[k] for p in corners) for k in range(3)))
        if all(minimum[k] <= high[k] + 1e-6 and maximum[k] >= low[k] - 1e-6
               for k in range(3)):
            result.append(obj)
    if not any(o.name.startswith('PressureShell_') for o in result):
        raise RuntimeError('No structural side wall found for %s' % group)
    if not any(o.name.startswith('CurvedWallPanel_') for o in result):
        raise RuntimeError('No decorative side wall found for %s' % group)
    return result


def _cut(cc, side, group, name, targets):
    cutter = _object(cc, '_CockpitPortCutter_' + name, group, None,
                     _cylinder_geometry((side * 1.10, CENTER_Y, CENTER_Z),
                                        (-side, 0, 0), CUT_RADIUS, -.36, .36),
                     parent=False)
    cutter.hide_render = True
    layer = bpy.context.view_layer
    changed = []
    try:
        for obj in targets:
            before = len(obj.data.polygons)
            modifier = obj.modifiers.new('PortWindow_ThroughHole', 'BOOLEAN')
            modifier.operation = _enum(modifier, 'operation', 'DIFFERENCE')
            modifier.solver = _enum(modifier, 'solver', 'EXACT')
            modifier.object = cutter
            for selected in list(bpy.context.selected_objects):
                selected.select_set(False)
            obj.hide_set(False)
            obj.select_set(True)
            layer.objects.active = obj
            with bpy.context.temp_override(scene=cc.scene, view_layer=layer,
                                           object=obj, active_object=obj,
                                           selected_objects=[obj],
                                           selected_editable_objects=[obj]):
                result = bpy.ops.object.modifier_apply(modifier=modifier.name)
            if 'FINISHED' not in result or len(obj.data.vertices) == 0:
                raise RuntimeError('Window boolean failed on ' + obj.name)
            obj['port_window_cut'] = name
            changed.append({'object': obj.name, 'faces_before': before,
                            'faces_after': len(obj.data.polygons)})
        return changed
    finally:
        cutter_mesh = cutter.data
        bpy.data.objects.remove(cutter, do_unlink=True)
        if cutter_mesh.users == 0:
            bpy.data.meshes.remove(cutter_mesh)


def _frame(cc, side, group, name, glass_material):
    prefix = 'PortWindow_' + name
    center = Vector((side * FRAME_X, CENTER_Y, CENTER_Z))
    normal = Vector((-side, 0, 0))
    u, v, _ = _basis(normal)
    objects = []

    def add(suffix, mat, geometry):
        obj = _object(cc, prefix + '_' + suffix, group, mat, geometry)
        obj['port_window_side'] = name
        objects.append(obj.name)
        return obj

    # Continuous solid sleeve: 205 mm of depth spans the curved pressure
    # wall at every point of the 600 mm outer frame, avoiding side gaps.
    # Keep the sleeve inside the 240 mm cut and the seals proud of its
    # inner wall; coplanar seal/sleeve faces flicker in realtime renderers.
    profile = [(.236, -.205), (.282, -.205), (.293, -.193),
               (.300, -.017), (.292, .005), (.246, .009),
               (.232, -.001), (.232, -.028), (.236, -.055)]
    add('Frame', cc.b.materials['Ivory'], _lathe_geometry(center, normal, profile))
    add('OrangeRing', cc.b.materials['Orange'],
        _lathe_geometry(center, normal,
                        [(.269, .006), (.276, .006), (.276, .010), (.269, .010)]))
    add('InnerSeal', cc.b.materials['Rubber'],
        _lathe_geometry(center, normal,
                        [(.230, -.006), (.240, -.006), (.240, -.001), (.230, -.001)]))
    # A second small seal overlaps the edge of the glass, closing the sleeve.
    add('GlassSeal', cc.b.materials['Rubber'],
        _lathe_geometry(center, normal,
                        [(.230, -.041), (.241, -.041), (.241, -.035), (.230, -.035)]))
    glass_center = center - normal * .039
    glass = add('Glass', glass_material,
                _cylinder_geometry(glass_center, normal, GLASS_RADIUS, -.0015, .0015))
    glass['transparent_glass'] = True
    glass['thickness_metres'] = .003
    glass['radius_metres'] = GLASS_RADIUS

    washers, bolts, slots = [], [], []
    for i in range(12):
        angle = math.tau * i / 12 + math.pi / 12
        radial = u * math.cos(angle) + v * math.sin(angle)
        q = center + radial * .2848 + normal * .005
        washers.append(_lathe_geometry(q, normal,
                        [(.003, 0), (.0066, 0), (.0066, .0015), (.003, .0015)], 24))
        bolts.append(_cylinder_geometry(q, normal, .0048, .0012, .0062, 6))
        slots.append(_box_geometry(q + normal * .0065, radial,
                                   normal.cross(radial), normal, .0057, .0011, .0007))
    add('FastenerWashers', cc.b.materials['Rubber'], _combine(washers))
    add('Fasteners', cc.b.materials['Metal'], _combine(bolts))
    add('FastenerSlots', cc.b.materials['Rubber'], _combine(slots))
    return {'side': name, 'group': group, 'center': list(center),
            'normal_toward_cabin': list(normal), 'glass_center': list(glass_center),
            'clear_diameter_metres': CLEAR_RADIUS * 2,
            'cut_diameter_metres': CUT_RADIUS * 2,
            'outer_frame_diameter_metres': .60,
            'ray_origin_inside': [side * .80, CENTER_Y, CENTER_Z],
            'ray_direction_outward': [side, 0, 0], 'objects': objects}


def apply(cc):
    """Cut and frame both ports on cc.scene after cc.materialize()."""
    if cc.scene.get(STATE_KEY) or any(o.name.startswith('PortWindow_')
                                      for o in cc.scene.objects):
        raise RuntimeError('Side windows already exist or a prior application is incomplete; refusing to recut')
    if bpy.context.scene != cc.scene:
        raise RuntimeError('Make cc.scene the current scene before applying side windows')
    if bpy.context.mode != 'OBJECT':
        raise RuntimeError('Apply side windows in Object Mode')
    for name in ('Ivory', 'Orange', 'Rubber', 'Metal'):
        if name not in cc.b.materials:
            raise RuntimeError('Missing cockpit material: ' + name)
    requests = [(-1, '02_LeftWall', 'Left'), (1, '03_RightWall', 'Right')]
    targets = [(side, group, name, _candidates(cc, side, group))
               for side, group, name in requests]
    original_active = bpy.context.view_layer.objects.active
    original_selection = list(bpy.context.selected_objects)
    cc.scene[STATE_KEY] = 'in_progress'
    windows = []
    try:
        glass = _glass_material(cc)
        for side, group, name, wall_objects in targets:
            modified = _cut(cc, side, group, name, wall_objects)
            summary = _frame(cc, side, group, name, glass)
            summary['wall_booleans'] = modified
            windows.append(summary)
        cc.scene[STATE_KEY] = 'complete'
        cc.scene['port_window_clear_diameter_metres'] = .46
        return {'status': 'complete', 'windows': windows,
                'glass_material': glass.name, 'glass_alpha': .18,
                'object_count': sum(len(window['objects']) for window in windows)}
    finally:
        for obj in list(bpy.context.selected_objects):
            obj.select_set(False)
        for obj in original_selection:
            if obj.name in cc.scene.objects:
                obj.select_set(True)
        if original_active is not None and original_active.name in cc.scene.objects:
            bpy.context.view_layer.objects.active = original_active

