"""Inspect delivered Meshy GLBs without importing or changing a Unity scene."""
import hashlib
import json
import struct
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[3]
MODELS = PROJECT / "Assets" / "MarsCabin" / "Models" / "Meshy"


def dimensions(data, mime):
    if data[:8] == b"\x89PNG\r\n\x1a\n":
        return list(struct.unpack_from(">II", data, 16))
    if mime == "image/jpeg" or data[:2] == b"\xff\xd8":
        offset = 2
        while offset + 4 < len(data):
            if data[offset] != 255:
                offset += 1
                continue
            marker = data[offset + 1]
            offset += 2
            if marker in (0xD8, 0xD9):
                continue
            length = struct.unpack_from(">H", data, offset)[0]
            if marker in (0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF):
                height, width = struct.unpack_from(">HH", data, offset + 3)
                return [width, height]
            if length < 2:
                break
            offset += length
    return None


def inspect(path):
    data = path.read_bytes()
    magic, version, length = struct.unpack_from("<III", data, 0)
    if magic != 0x46546C67 or version != 2 or length != len(data):
        raise ValueError(f"Invalid GLB header: {path}")
    offset, document, binary = 12, None, b""
    while offset + 8 <= len(data):
        size, kind = struct.unpack_from("<II", data, offset)
        chunk = data[offset + 8:offset + 8 + size]
        if kind == 0x4E4F534A:
            document = json.loads(chunk)
        elif kind == 0x004E4942:
            binary = chunk
        offset += 8 + size
    accessors = document.get("accessors", [])
    triangle_counts = []
    for mesh in document.get("meshes", []):
        count = 0
        for primitive in mesh.get("primitives", []):
            accessor = primitive.get("indices", primitive.get("attributes", {}).get("POSITION"))
            vertices = accessors[accessor]["count"]
            mode = primitive.get("mode", 4)
            count += vertices // 3 if mode == 4 else max(vertices - 2, 0) if mode in (5, 6) else 0
        triangle_counts.append(count)
    images = []
    for index, image in enumerate(document.get("images", [])):
        image_data = b""
        if "bufferView" in image:
            view = document["bufferViews"][image["bufferView"]]
            start = view.get("byteOffset", 0)
            image_data = binary[start:start + view["byteLength"]]
        images.append({"index": index, "name": image.get("name"), "mime_type": image.get("mimeType"),
                       "embedded": "bufferView" in image, "dimensions": dimensions(image_data, image.get("mimeType")),
                       "bytes": len(image_data)})
    materials = []
    for material in document.get("materials", []):
        pbr = material.get("pbrMetallicRoughness", {})
        channels = {}
        for name, value in (("base_color", pbr.get("baseColorTexture")),
                            ("metallic_roughness", pbr.get("metallicRoughnessTexture")),
                            ("normal", material.get("normalTexture")),
                            ("occlusion", material.get("occlusionTexture")),
                            ("emission", material.get("emissiveTexture"))):
            if value is not None:
                texture = document["textures"][value["index"]]
                channels[name] = texture.get("source")
        materials.append({"name": material.get("name"), "texture_channels": channels})
    return {"asset_path": path.relative_to(PROJECT).as_posix(), "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest(), "unique_meshes": len(triangle_counts),
            "unique_mesh_triangles": sum(triangle_counts),
            "instanced_node_triangles": sum(triangle_counts[node["mesh"]] for node in document.get("nodes", []) if "mesh" in node),
            "images": images, "materials": materials}


if __name__ == "__main__":
    report = {"models": [inspect(path) for path in sorted(MODELS.glob("*.glb"))]}
    destination = Path(__file__).with_name("mesh_asset_metrics.json")
    destination.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
