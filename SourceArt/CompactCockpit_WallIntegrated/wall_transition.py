"""Hollow instrument-to-wall fairings for the compact round pressure cabin.

Call embed(cc, name, p, R, width, height) before or after the existing screen.
R's columns are the panel's right, up and outward-facing normal directions.
Call build_seams(cc) once after placing the instrument modules.
"""
import math
from mathutils import Matrix, Vector


GROUP = '07_ConsoleBody'
WALL_RADIUS = 1.115


def _outline(w, h, corner, samples=8):
    """Counter-clockwise rounded rectangle, with identical corner topology."""
    corner = max(.001, min(corner, .45 * w, .45 * h))
    points = []
    for x, y, start in [(w / 2 - corner, h / 2 - corner, 0),
                         (-w / 2 + corner, h / 2 - corner, 90),
                         (-w / 2 + corner, -h / 2 + corner, 180),
                         (w / 2 - corner, -h / 2 + corner, 270)]:
        for j in range(samples + 1):
            angle = math.radians(start + j * 90 / samples)
            points.append((x + corner * math.cos(angle),
                           y + corner * math.sin(angle)))
    return points


def _at_plane(points, p, R, depth):
    return [p + R @ Vector((x, y, depth)) for x, y in points]


def _to_wall(points, radius=WALL_RADIUS):
    result = []
    for point in points:
        radial = math.hypot(point.x, point.y)
        if radial < 1e-7:
            raise ValueError('A wall fairing vertex cannot lie on the cabin axis')
        # Follow the pressure shell's conical shoulder above the wall.
        # An explicit radius is an offset from the same shell profile, so
        # the adjoining seal keeps its existing radial thickness as well.
        shoulder = max(0.0, min(1.0, (point.z - 1.835) / (2.10 - 1.835)))
        target_radius = radius - .20 * shoulder
        result.append(Vector((point.x * target_radius / radial,
                              point.y * target_radius / radial, point.z)))
    return result


def _closed_annular_duct(cc, name, material, front_outer, front_inner,
                         rear_outer, rear_inner):
    """Closed solid rim/duct, with an unobstructed central aperture.

    All loops are CCW as viewed from the pilot. The rear surface may be
    nonplanar; its individual quads follow the cylindrical wall attachment.
    """
    n = len(front_outer)
    verts = front_outer + front_inner + rear_outer + rear_inner
    faces = []
    for i in range(n):
        j = (i + 1) % n
        faces.extend([
            (i, j, n + j, n + i),                 # Front rim, toward pilot.
            (i, 2 * n + i, 2 * n + j, j),         # Outside of the fairing.
            (n + i, n + j, 3 * n + j, 3 * n + i),# Aperture return.
            (2 * n + i, 3 * n + i, 3 * n + j,
             2 * n + j),                         # Rear rim, toward wall.
        ])
    cc.b.begin(name, GROUP)
    cc.b.mesh(GROUP, material, verts, faces)
    cc.b.end()


def embed(cc, name, p, R, w, h):
    """Blend one rectangular instrument module into the round cabin wall.

    w/h describe the complete module, including any edge controls. The
    visible flange is at local n=-.020 m; it never covers the aperture.
    The fairing's rear vertices are projected radially to r=1.115 m below
    z=1.835, tapering to r=.915 m at z=2.10, with their height unchanged.
    The returns physically enter the wall and follow its roof shoulder.
    """
    if min(w, h) <= .045:
        raise ValueError('Instrument enclosure dimensions must exceed 45 mm')
    p = Vector(p)
    corner = min(.045, w * .11, h * .11)
    rim = min(.018, min(w, h) * .065)
    outer = _outline(w, h, corner)
    inner = _outline(w - rim * 2, h - rim * 2,
                     max(.003, corner - rim))

    # A real hollow casing joins the screen mounting plane to the wall.
    # The screen's own body closes its centre; no solid front plate is added.
    front_outer = _at_plane(outer, p, R, -.026)
    front_inner = _at_plane(inner, p, R, -.026)
    rear_outer = _to_wall(front_outer)
    rear_inner = _to_wall(front_inner)
    _closed_annular_duct(cc, name + '_WallFairing', 'Wall',
                         front_outer, front_inner, rear_outer, rear_inner)

    # Slim enamel flange at the front with a black gasket below it. The
    # visible frame stays behind the existing glass and control surfaces.
    gasket_outer = _outline(w + .006, h + .006, corner + .003)
    _closed_annular_duct(cc, name + '_MountGasket', 'Rubber',
                         _at_plane(gasket_outer, p, R, -.023),
                         _at_plane(inner, p, R, -.023),
                         _at_plane(gasket_outer, p, R, -.028),
                         _at_plane(inner, p, R, -.028))
    _closed_annular_duct(cc, name + '_RoundedMountFrame', 'Ivory',
                         _at_plane(outer, p, R, -.020),
                         _at_plane(inner, p, R, -.020),
                         _at_plane(outer, p, R, -.024),
                         _at_plane(inner, p, R, -.024))

    # A fine line marks the actual shell junction. This is also a hollow
    # solid, projected onto the wall; it does not cap the instrument recess.
    wall_outer = _outline(w + .010, h + .010, corner + .005)
    wall_inner = _outline(w - .004, h - .004, max(.003, corner - .002))
    wall_front_outer = _to_wall(_at_plane(wall_outer, p, R, -.026), 1.097)
    wall_front_inner = _to_wall(_at_plane(wall_inner, p, R, -.026), 1.097)
    _closed_annular_duct(cc, name + '_WallJunctionSeal', 'Rubber',
                         wall_front_outer, wall_front_inner,
                         _to_wall(wall_front_outer, 1.118),
                         _to_wall(wall_front_inner, 1.118))

    # Four restrained fasteners sit on the upper/lower flange, outside the
    # opening and behind any screen bezel which overlaps the mount.
    if hasattr(cc, 'bolt'):
        for x in (-1, 1):
            for y in (-1, 1):
                q = p + R @ Vector((x * (w / 2 - corner),
                                     y * (h / 2 - rim * .47), -.019))
                cc.bolt(GROUP, q, R, min(.0034, rim * .18))


def _arc_band(cc, name, material, r0, r1, z0, z1,
              start_degrees=-75, end_degrees=75, subdivisions=96):
    """Closed curved rectangular rail; angle zero points toward +Y."""
    verts = []
    profile = [(r0, z0), (r1, z0), (r1, z1), (r0, z1)]
    # Reverse the conventional polar angle so vertex order stays CCW.
    start = math.radians(90 - end_degrees)
    end = math.radians(90 - start_degrees)
    for i in range(subdivisions + 1):
        theta = start + (end - start) * i / subdivisions
        verts.extend((r * math.cos(theta), r * math.sin(theta), z)
                     for r, z in profile)
    faces = []
    for i in range(subdivisions):
        for k in range(4):
            j = (k + 1) % 4
            faces.append((i * 4 + k, (i + 1) * 4 + k,
                          (i + 1) * 4 + j, i * 4 + j))
    faces.extend([tuple(range(4)),
                  tuple(reversed(range(subdivisions * 4,
                                       (subdivisions + 1) * 4)))])
    cc.b.begin(name, GROUP)
    cc.b.mesh(GROUP, material, verts, faces)
    cc.b.end()


def build_seams(cc):
    """Restrained front-wall integration details, leaving the rear half plain."""
    # Two long rails tie the separately angled instrument casings together.
    # They sit directly on the pressure wall, not across the standing space.
    _arc_band(cc, 'InstrumentWall_LowerStructure', 'Metal',
              1.087, 1.114, .448, .463)
    _arc_band(cc, 'InstrumentWall_LowerOrangeLine', 'Orange',
              1.086, 1.113, .466, .474)
    _arc_band(cc, 'InstrumentWall_UpperStructure', 'Metal',
              1.089, 1.113, 1.285, 1.296)

    for side in (-1, 1):
        angle = math.radians(side * 68)
        radial = Vector((math.sin(angle), math.cos(angle), 0))
        normal = -radial
        u = Vector((math.cos(angle), -math.sin(angle), 0))
        v = Vector((0, 0, 1))
        R = Matrix((u, v, normal)).transposed()
        p = radial * 1.086 + Vector((0, 0, 1.00))
        prefix = 'WallService_Left' if side < 0 else 'WallService_Right'
        cc.panel(prefix + '_Seal', GROUP, 'Rubber', p,
                 (.159, .300, .023), .018, R)
        cc.panel(prefix + '_Cover', GROUP, 'Ivory', p + normal * .016,
                 (.145, .286, .014), .014, R)
        cc.panel(prefix + '_IDStripe', GROUP, 'Orange',
                 p + R @ Vector((-.046, 0, .025)),
                 (.006, .215, .004), .002, R)
        if hasattr(cc, 'bolt'):
            for x in (-.054, .054):
                for z in (-.124, .124):
                    cc.bolt(GROUP, p + R @ Vector((x, z, .025)), R, .003)
        # Short wall-hugging conduit terminates underneath the service cover.
        points = []
        for degrees, z, radius in [(side * 73, .625, 1.119),
                                    (side * 73, .625, 1.094),
                                    (side * 73, .634, 1.079),
                                    (side * 73, .652, 1.076),
                                    (side * 73, .720, 1.076),
                                    (side * 68, .785, 1.076),
                                    (side * 68, .855, 1.076)]:
            theta = math.radians(degrees)
            points.append((radius * math.sin(theta),
                           radius * math.cos(theta), z))
        cc.pipe(prefix + '_ShortConduit', GROUP, points, .0075, 'Metal')

        # The lower elbow enters a small sealed wall socket. The pipe starts
        # inside the pressure skin, so neither end presents an open cut tube.
        theta = math.radians(side * 73)
        socket_radial = Vector((math.sin(theta), math.cos(theta), 0))
        socket_normal = -socket_radial
        socket_u = Vector((math.cos(theta), -math.sin(theta), 0))
        socket_R = Matrix((socket_u, v, socket_normal)).transposed()
        socket = socket_radial * 1.097 + Vector((0, 0, .625))
        cc.panel(prefix + '_ConduitSocketSeal', GROUP, 'Rubber', socket,
                 (.047, .049, .014), .007, socket_R)
        cc.panel(prefix + '_ConduitSocketBase', GROUP, 'Metal',
                 socket + socket_normal * .009,
                 (.043, .045, .007), .006, socket_R)
        cc.b.begin(prefix + '_ConduitSocketCollar', GROUP)
        cc.b.lathe(GROUP, 'Metal', socket + socket_normal * .013,
                   [(-.002, .013), (.002, .013), (.005, .011), (.007, .011)],
                   24, socket_normal)
        cc.b.torus(GROUP, 'Rubber', socket + socket_normal * .020,
                   .0089, .0015, socket_normal, 24, 8)
        cc.b.end()
        if hasattr(cc, 'bolt'):
            for x in (-.014, .014):
                for z in (-.015, .015):
                    cc.bolt(GROUP, socket + socket_R @ Vector((x, z, .0135)),
                            socket_R, .0022)

