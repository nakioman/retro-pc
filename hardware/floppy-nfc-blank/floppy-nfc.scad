// RetroFloppy NFC disk — top + base shells joined into one printable part.
//
// top.stl and base.stl are two halves of a 3.5" floppy shell (made in Shapr3D,
// third-party files, not committed) that arrive already positioned as an
// assembly: the base's walls rise to z = 2.4 and the top sits exactly on that
// plane, so a plain union produces the closed disk. Both halves already model
// the open head window (cut through) and their own leading-edge notch profile,
// which is kept exactly as designed — nothing is cut into the outline here.
//
// The one addition is the NFC tag seat. The top shell is only 0.6 mm thick in
// the head-window zone, far too thin to hold a blind seat, so a pedestal is
// raised from the base plate, up through the open window, to carry the seat
// floor; the seat is then sunk into it. The tag ends up 0.4 mm below the label
// field with solid material underneath — nothing through, nothing proud.
//
// Render: mise run floppy-stl
// Print:  flat, tag seat up, no supports. PLA or PETG, never carbon-filled or
//         metallic filament: the filler shields the antenna, the tag stops
//         reading.

$fa = 1;
$fs = 0.3;

/* [Source shells] */
top_file = "top.stl";
base_file = "base.stl";
// Measured off the shells, in mm: X 0..90, Y 0..93.75 with the leading edge at
// the high-Y end, assembly z 0.8..4.0, top label field at z = 3.6, bottom face
// at z = 0.8.
source_width = 90;
source_leading_edge_y = 93.75;
field_z = 3.6;
bottom_z = 0.8;

/* [NFC tag] */
// Round NTAG21x sticker, 25 mm is the common size.
tag_diameter = 25;
tag_thickness = 0.8;
// Added to the diameter so the sticker drops in without being forced.
tag_fit = 0.4;
// Extra depth, so the sticker sits below the label field and nothing in the
// drive can scrape it.
tag_recess = 0.4;
// Distance from the leading edge to the tag centre: 16 mm is the placement the
// bench unit validated, centred on the head-window zone.
tag_offset_from_leading_edge = 16;
tag_offset_x = 0;
// Which face the seat opens on — the face the PN532 antenna looks at.
tag_face = "top"; // [top, bottom]
// Set false for the raw union of the two shells, exactly as designed.
tag_seat = true;

/* [Pedestal] */
// How far the solid backing extends beyond the seat, and how thick the floor
// under the sticker is. The pedestal hangs from the top plate and reaches no
// further down than it must, so nothing shows on the opposite face: through
// the open window on the back you see its underside a full millimetre inside,
// like the media of a real disk, never a plug at the surface.
pedestal_margin = 2;
pedestal_floor = 1.2;

seat_diameter = tag_diameter + tag_fit;
seat_depth = tag_thickness + tag_recess;
// Both variants hang the pedestal from the top plate (fused 3.0..3.4) and keep
// it clear of the opposite face. Top seat: floor just under the seat, cut from
// above. Bottom seat: floor above the cut, cut from below.
seat_floor_z = tag_face == "top" ? field_z - seat_depth : bottom_z + seat_depth;
pedestal_from_z = tag_face == "top" ? seat_floor_z - pedestal_floor : seat_floor_z;
pedestal_to_z = field_z - 0.2;

tag_x = source_width / 2 + tag_offset_x;
tag_y = source_leading_edge_y - tag_offset_from_leading_edge;

module shells() {
    import(top_file, convexity = 10);
    import(base_file, convexity = 10);
}

module pedestal() {
    translate([tag_x, tag_y, pedestal_from_z]) {
        cylinder(h = pedestal_to_z - pedestal_from_z, d = seat_diameter + 2 * pedestal_margin);
    }
}

// Cut well past the face it opens on, so raised details inside the circle go too.
module seat_cut() {
    if (tag_face == "top") {
        translate([tag_x, tag_y, seat_floor_z]) cylinder(h = 5, d = seat_diameter);
    } else {
        translate([tag_x, tag_y, seat_floor_z - 5]) cylinder(h = 5, d = seat_diameter);
    }
}

difference() {
    union() {
        shells();
        if (tag_seat) pedestal();
    }
    if (tag_seat) seat_cut();
}
