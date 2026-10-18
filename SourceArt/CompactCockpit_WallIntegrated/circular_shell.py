"""Compact circular pressure shell. Metres, Z up, pilot faces +Y.

Only geometry is authored here; scene/material setup remains with the caller.
Every swept panel has closed end caps and an explicit outward winding.
"""
import math
from mathutils import Matrix, Vector


def build(cc):
    b = cc.b
    radius = 1.10
    segments = 16
    step = math.tau / segments

    def group(angle):
        # Cardinal wall collections make a clean quarter cutaway possible.
        x, y = math.cos(angle), math.sin(angle)
        if y > abs(x):
            return '04_FrontWall'
        if -y > abs(x):
            return '05_RearWall'
        return '02_LeftWall' if x < 0 else '03_RightWall'

    def sweep(name, g, material, profile, start, end, subdivisions=20):
        """Sweep a CCW closed (radius, height) profile through a sector."""
        count = len(profile)
        verts = []
        for j in range(subdivisions + 1):
            a = start + (end - start) * j / subdivisions
            verts.extend((r * math.cos(a), r * math.sin(a), z)
                         for r, z in profile)
        faces = []
        for j in range(subdivisions):
            for k in range(count):
                following = (k + 1) % count
                faces.append((j * count + k, (j + 1) * count + k,
                              (j + 1) * count + following,
                              j * count + following))
        faces.append(tuple(range(count)))
        faces.append(tuple(reversed(range(subdivisions * count,
                                          (subdivisions + 1) * count))))
        b.begin(name, g)
        b.mesh(g, material, verts, faces)
        b.end()

    def band(name, g, material, inner, outer, bottom, top, start, end,
             subdivisions=20):
        sweep(name, g, material,
              [(inner, bottom), (outer, bottom), (outer, top), (inner, top)],
              start, end, subdivisions)

    def disk(name, g, material, r, bottom, top, n=256):
        verts = [(r * math.cos(math.tau * j / n),
                  r * math.sin(math.tau * j / n), z)
                 for z in (bottom, top) for j in range(n)]
        faces = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
        faces.extend((j, (j + 1) % n, (j + 1) % n + n, j + n)
                     for j in range(n))
        b.begin(name, g)
        b.mesh(g, material, verts, faces)
        b.end()

    # The continuous structural skin closes every decorative panel seam.
    # Decorative panels share the true cylindrical radius, not flat chords.
    for i in range(segments):
        middle = (i + .5) * step
        g = group(middle)
        start, end = i * step, (i + 1) * step
        band('PressureShell_%02d' % i, g, 'Dark', 1.118, 1.175,
             -.025, 1.855, start, end)
        gap = .0032  # Approximately 7 mm of charcoal seam on the inside.
        band('CurvedWallPanel_%02d' % i, g, 'Wall', radius, 1.132,
             .335, 1.837, start + gap, end - gap)
        band('CurvedWallSkirt_%02d' % i, g, 'Ivory', radius, 1.132,
             .042, .277, start + gap, end - gap)
        band('LowOrangeStripe_%02d' % i, g, 'Orange', 1.095, 1.128,
             .296, .313, start, end)
        # Narrow rails keep the joint bands restrained and visually closed.
        band('LowerPanelJoint_%02d' % i, g, 'Wall', 1.103, 1.130,
             .277, .296, start, end)
        band('UpperPanelJoint_%02d' % i, g, 'Wall', 1.103, 1.130,
             .313, .335, start, end)

    disk('CircularFloor_Continuous', '01_Floor', 'Dark', 1.175, -.08, .0)
    # Four broad flush plates, with narrow radial joints and no floor clutter.
    for i in range(4):
        start, end = i * math.pi / 2, (i + 1) * math.pi / 2
        band('CircularFloorPlate_%02d' % i, '01_Floor', 'Floor', .004,
             1.096, .0005, .009, start + .002, end - .002, 64)
    disk('FloorCentreSeamCap', '01_Floor', 'Floor', .009, .0005, .009, 32)

    # Closed conical shoulder, meeting a genuinely round horizontal ceiling.
    # Their dark backing is uninterrupted even where the pale panels divide.
    for i in range(segments):
        start, end = i * step, (i + 1) * step
        sweep('RoofShoulder_Structure_%02d' % i, '06_Ceiling', 'Dark',
              [(1.10, 1.835), (1.175, 1.835), (.975, 2.165), (.90, 2.10)],
              start, end)
        sweep('RoofShoulder_Panel_%02d' % i, '06_Ceiling', 'Wall',
              [(1.090, 1.842), (1.115, 1.842), (.923, 2.111), (.898, 2.096)],
              start + .0035, end - .0035)

    disk('CircularRoof_Continuous', '06_Ceiling', 'Dark', .975,
         2.10, 2.17)
    disk('CircularCeiling_Plain', '06_Ceiling', 'Wall', .901,
         2.092, 2.113)
    # A very slim diffuser ring follows the shoulder. The existing Warm
    # material is retained; the small emitting area keeps it unobtrusive.
    for i in range(segments):
        start, end = i * step, (i + 1) * step
        sweep('PerimeterLightHousing_%02d' % i, '06_Ceiling', 'Dark',
              [(1.009, 1.948), (1.028, 1.948), (1.013, 1.974), (.989, 1.974)],
              start, end)
        sweep('PerimeterLightDiffuser_%02d' % i, '06_Ceiling', 'Warm',
              [(1.004, 1.952), (1.013, 1.952), (1.008, 1.961), (.998, 1.961)],
              start + .001, end - .001)

    # A closed flush hatch is the only architectural feature at the rear.
    # It deliberately overlaps the uninterrupted wall to keep the cabin sealed.
    rear = Matrix.Rotation(math.pi, 3, 'Z') @ cc.FRONT
    cc.panel('RearHatch_Seal', '05_RearWall', 'Dark', (0, -1.062, .94),
             (.682, 1.78, .018), .060, rear)
    cc.panel('RearHatch_Leaf', '05_RearWall', 'Wall', (0, -1.049, .94),
             (.650, 1.742, .020), .051, rear)
    cc.panel('RearHatch_OrangeID', '05_RearWall', 'Orange',
             (0, -1.037, 1.74), (.49, .018, .005), .003, rear)
    cc.label('05_RearWall', '01 / ACCESS', (0, -1.034, 1.66), .027, rear)
    cc.panel('RearHatch_HandleRecess', '05_RearWall', 'Dark',
             (.232, -1.034, .93), (.042, .145, .008), .010, rear)
    cc.pipe('RearHatch_Handle', '05_RearWall',
            [(.232, -1.027, .879), (.232, -.999, .895),
             (.232, -.999, .965), (.232, -1.027, .982)], .0075)

    cc.root['interior_shape'] = 'Circular cylinder with continuous closed shell'
    cc.root['interior_diameter_metres'] = 2.20
    cc.root['interior_height_metres'] = 2.10
    cc.root['interior_metres'] = [2.20, 2.20, 2.10]
    cc.root['scope'] = 'Complete circular enclosure and inward-facing flight console; remaining interior empty.'
    print('Built closed circular cabin: 2.20 m inside diameter, 2.10 m height')
