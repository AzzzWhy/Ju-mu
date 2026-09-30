"""Confirm a Windows PE executable contains icon and icon-group resources."""
import struct
import sys

blob = open(sys.argv[1], "rb").read()
pe = struct.unpack_from("<I", blob, 0x3C)[0]
assert blob[pe:pe + 4] == b"PE\0\0", "Not a PE executable"
coff = pe + 4
sections_count, optional_size = struct.unpack_from("<H", blob, coff + 2)[0], struct.unpack_from("<H", blob, coff + 16)[0]
optional = coff + 20
magic = struct.unpack_from("<H", blob, optional)[0]
directory = optional + (112 if magic == 0x20B else 96)
resource_rva, resource_size = struct.unpack_from("<II", blob, directory + 2 * 8)
assert resource_rva and resource_size, "Missing resource directory"
sections = optional + optional_size

def from_rva(rva):
    for i in range(sections_count):
        section = sections + 40 * i
        virtual_size, virtual_address, raw_size, raw_offset = struct.unpack_from("<IIII", blob, section + 8)
        if virtual_address <= rva < virtual_address + max(virtual_size, raw_size):
            return raw_offset + rva - virtual_address
    raise AssertionError(f"Resource RVA not mapped: {rva:x}")

root = from_rva(resource_rva)
named, ids = struct.unpack_from("<HH", blob, root + 12)
types = set()
for i in range(named + ids):
    name, _ = struct.unpack_from("<II", blob, root + 16 + 8 * i)
    if not name & 0x80000000:
        types.add(name)
assert 3 in types and 14 in types, f"Expected RT_ICON (3) and RT_GROUP_ICON (14), found {sorted(types)}"
print(f"Embedded icon resources found in {sys.argv[1]}")
