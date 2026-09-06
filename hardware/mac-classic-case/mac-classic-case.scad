/*
 * ----------------------------------------------------------------------------
 * "THE BEER-WARE LICENSE" (Revision 42):
 * Jeroen Domburg <jeroen@spritesmods.com> wrote this file. As long as you retain
 * this notice you can do whatever you want with this stuff. If we meet some day,
 * and you think this stuff is worth it, you can buy me a beer in return.
 * ----------------------------------------------------------------------------
 *
 * mac-classic-case.scad — 1:1 Macintosh Classic style PC case for RetroBox.
 * Shell geometry based on the 1:6 mini Mac by Jeroen Domburg (spritesmods.com).
 *
 * Houses a micro-ATX motherboard mounted sideways against the left wall, a
 * standard ATX (PS/2) power supply on the floor, a 3.5" floppy drive and a
 * slim tray-load optical drive behind the classic front slots, a 9.7" iPad
 * 3/4 retina panel behind the screen window, and a 2.5" drive on the rear
 * wall. Every printable piece fits a 250 x 250 x 250 mm bed: the shell splits
 * into front/back halves, each half splits horizontally, and the back pieces
 * additionally split vertically. See the README for the bill of materials,
 * print notes and assembly order.
 */

//All dimensions in mm

$fa=8; //segments/360 degrees
$fs=0.1; //min size of fragment
$fn=32; //override amount of facets

//1 = full size. The motherboard/PSU/drive mounts are real-world dimensions;
//do not change without reworking every mount.
case_scale=1;

wall=3; //shell wall thickness

overlap=5; //overlap between front/back halves
overlapth=1.5; //material thickness at the overlap

cw = 247.6*case_scale; //outer case width
cd = 241.3*case_scale; //outer case depth

// ========================= Fasteners =========================
//Every M3 anchor takes a heat-set threaded insert: d4.0 hole, >=6.5 deep
//(Ruthex/Voron standard; adjust for your inserts). Self-tapping exceptions:
//the OSD strip (M2.5, thin bosses) and the speakers (M2, if not glued).
insert_d = 4.0;

// ========================= Part selection =========================
//Views (for inspection, not printing):
//  assembly       - complete case with the internal printed parts in place
//  cutaway        - same, sectioned through the middle
//  cage-assembly  - drive cage + pedestals + HDD cradle in position
//Printable parts (each renders in its print orientation):
//  front-lower, front-upper, back-lower-left, back-lower-right,
//  back-upper-left, back-upper-right, drive-cage, cage-pedestals, hdd-cradle,
//  lcd-frame, dvd-tray-cover, fdd-funnel, dvd-funnel, eject-cap, tpu-feet
part = "assembly";

show_hardware = false; //ghost motherboard/PSU/drives in the views (preview only)


// ========================= Motherboard (micro-ATX) =========================
//244 x 200 board standing on edge: the IO edge (244) vertical, the board
//depth (200) horizontal toward the back. Components face inward, IO shield
//faces the case front, so the IO block ends up top-front and the expansion
//slots at the bottom. Verified with a Biostar H55 HD and an MSI PRO H610M-S.
mobo_w        = 244;   //length of the IO edge
mobo_d        = 200;   //board depth
mobo_pcb      = 1.6;   //PCB thickness
standoff_h    = 8;     //printed standoff height
standoff_d    = 9;     //standoff outer diameter
standoff_drill= insert_d; //M3 insert hole
mobo_y_io     = 36;    //Y of the IO edge (connectors overhang toward y-)
mobo_z_top    = 259;   //Z of the board's top edge (IO-side corner up)

//micro-ATX mounting holes (Intel spec) present on 244 x ~200 boards:
//[u, v] with u = distance along the IO edge from the IO-block corner,
//v = depth from the IO edge.
mobo_holes = [
    [  6.35,  33.02],  //0: behind the IO connector block
    [163.83,  10.16],  //1: rear row, between slots
    [209.55,  10.16],  //2: rear row, between slots (Datum B)
    [  6.35, 165.10],  //3: middle row, IO side
    [163.83, 165.10],  //4: middle row
    [209.55, 165.10],  //5: hole S
    [229.87, 165.10],  //6: hole R
];
//Disable the holes your board lacks: a standoff pressed against the solder
//side can short it. Hole S (index 5) is absent on both boards tested.
mobo_hole_on = [true, true, true, true, true, false, true];


// ========================= 3.5" PC floppy drive =========================
//Sits level behind the classic front slot (the front leans -5 degrees; the
//guide funnel bridges the difference). Dimensions: NEC FD1231T.
fdd_w = 101.6; fdd_h = 25.4; fdd_d = 146;
fdd_y_front = -12;         //front face of the drive. Limit ~-12.5: the eject
                           //button (~6 mm proud) plus the cap wall must stay
                           //>=1 mm off the sloped front's inner face. The
                           //funnel and eject cap derive from this value.
fdd_slot_cx = 175.7;       //slot center X (129 + 93.4/2)
fdd_slot_z  = 107.5;       //slot center Z, fixed by the classic front
fdd_slot_below_top = 8.7;  //FD1231T: disk door axis 8.7 below the drive top
fdd_top_z = fdd_slot_z + fdd_slot_below_top;
//Eject button hole (below-right of the slot, own mini-bevel):
//[x from the slot's left end, z from the slot axis (negative=down), w, h]
fdd_eject = [74.8, -13.5, 11, 8];
//Physical button position on the drive, relative to the disk slot (used by
//the ghost and the cage channel only; the cap uses fdd_latch)
fdd_button = [74.8, -10.1];
//This FD1231T has its original button removed: the face keeps a 19.5 wide x
//8 high x ~5 deep pocket with the bare eject lever inside — a 7 x 1 hook,
//vertically centered, starting 5 from the pocket's left edge. Flush with the
//face when empty; protrudes 5 (the eject travel) with a disk in.
fdd_latch = [73.4, 16];   //[hook left edge from the slot, hook top face from
                          //the drive top]

// ========================= Slim optical drive (SATA, tray-load) =========================
//Mounted ABOVE the floppy slot: below it the drive body (126 deep) hits the
//PSU (which reaches z=89 and starts at y=98).
odd_w = 128; odd_h = 12.7; odd_d = 126.1;
odd_y_front = -13;         //front face of the drive. Limit ~-14.9: the top
                           //front corner (z=138.2) meets the sloped front at
                           //y~-16.9; the printed tray face clears the inner
                           //face by ~2 mm.
odd_slot_cx = 157.4;       //center X: chosen so the RIGHT end of mouth and bevel aligns with the floppy's
odd_slot_z  = 132.5;       //slot center Z (25 above the floppy slot; lower hits the classic bezel)
odd_slot_w = 130; odd_slot_h = 12;  //narrow through mouth (128 x ~10 tray + printed face)
odd_bezel_h = 18;                   //bevel height; widths reuse the floppy margins (10.4/8.5)
odd_bottom_z = odd_slot_z - 7;      //the tray axis sits ~7 above the drive base
//Side M2 screws into the drive's threaded side holes (2 per side). Instead of
//fixed holes, ONE sliding slot per bay wall at this z lets the drive move in
//depth. y = odd_y_front + measured hole-to-face distances (24 and 104).
odd_side_screw_d = 2.3;                                       //M2 free pass (= slot height)
odd_side_screws  = [[odd_y_front+24, odd_bottom_z+9.6], [odd_y_front+104, odd_bottom_z+9.6]];


// ========================= DVD tray cover ("dvd-tray-cover") =========================
//Solid funnel-shaped plug glued to the tray face: the same shape as the
//front's funnel cutout (148.9 x 18 mouth tapering to 130 x 12 over 8 deep,
//mouth center shifted -0.95 by the asymmetric 10.4/8.5 margins) minus ~0.3
//clearance per side. The front face sits flush with the skin, leaving only a
//~0.5 perimeter line, and the cone self-centers the cover as the tray closes.
//It glues (CA) to the tray face by two pads that dodge the tray's bezel
//clips: the LEFT pad on the top half, the RIGHT pad on the bottom half.
//Invisible eject: a recess from behind leaves a 0.6 MEMBRANE in front of the
//tray's microswitch (measured: 45 from the tray's right edge, at tray
//mid-height) with a nub in the middle. Pressing there flexes the membrane
//~0.5 and clicks the switch; the rest of the plug is rigid, so pushing the
//tray shut does not trigger it. Local coordinates: origin at the mouth
//center on the front's outer skin; +y inward (toward the tray).
odd_cover_gap = 9.96;   //outer skin -> tray face; if the cover sits proud or
                        //sunken, tune HERE.
odd_switch_c = [64-45, 0]; //microswitch center relative to the tray center
odd_nub_gap = 0.25;     //how short the nub stops before the pad glue plane.
                        //Tune on the real part: if it doesn't click, lower to
                        //0.1; if it self-triggers, raise.
module dvd_cover() {
    union() {
        difference() {
            union() {
                //conical plug: hull between the mouth section (at 0.2 depth,
                //shrunk by taper + 0.3 clearance) and the rear section
                hull() {
                    translate([-0.95-147.8/2, 0.7, -17.2/2])
                        rotate([90,0,0]) flatroundedcube(147.8, 17.2, 0.5, 1);
                    translate([-130.6/2, 8, -11.8/2])
                        rotate([90,0,0]) flatroundedcube(130.6, 11.8, 0.5, 1);
                }
                //glue pads: left on the top half, right on the bottom half
                translate([-50, 7.8, -1]) cube([14, 3.5, 5]);
                translate([40, 7.8, -4])  cube([14, 3.5, 5]);
            }
            //membrane recess: leaves 0.6 of skin at the front and crosses
            //the whole plug body
            translate([odd_switch_c[0]-9, 0.8, odd_switch_c[1]-3.5]) cube([18, 7.4, 5.5]);
            //tray face plane (vertical in world = 5 degrees in the front's
            //local frame): leaves the pad backs flat and flush for gluing
            translate([0, odd_cover_gap, 0]) rotate([5,0,0])
                translate([-100, 0, -50]) cube([200, 50, 100]);
        }
        //eject nub: grows from the membrane and stops odd_nub_gap short of
        //the pad plane (the switch is flush with the tray face)
        difference() {
            translate([odd_switch_c[0]-3, 0.7, odd_switch_c[1]-1.5]) cube([6, 10.5, 3]);
            translate([0, odd_cover_gap-odd_nub_gap, 0]) rotate([5,0,0])
                translate([-100, 0, -50]) cube([200, 50, 100]);
        }
    }
}

// ========================= Drive cage =========================
//Holds the floppy below and the slim DVD above. Prints lying on its left
//face ("drive-cage") and screws with M3 to 8 floor bosses. The front guide
//funnels are SEPARATE pieces ("fdd-funnel"/"dvd-funnel") fitted after the
//drives go in from the front.
cage_wall   = 2.5;  //wall thickness
cage_clr    = 0.4;  //per-side clearance around the floppy (0.3 for the DVD)
cage_boss_h = 4.5;  //floor boss height (the pedestals rest on them)
//The cage sits on TWO PEDESTALS (walls with flanges, printed flat):
//feet down to the floor bosses; flanges up to holes in the cage base.
//Front feet at y=26, no further forward: floor up to y~19.5 belongs to the
//FRONT half and a boss there would float on the back piece.
cage_feet       = [[133,26],[133,88],[217,26],[217,88]];  //foot->boss screws (x,y)
cage_base_holes = [[133,20],[133,78],[217,20],[217,78]];  //base->flange screws (x,y)
fdd_side_slot_z = 6.35; //center height of the floppy's side M3 holes above its base


// ========================= LCD: 9.7" iPad 3/4 retina panel (2048x1536) =========================
//Active area 196.6 x 147.5. Verified with a Sharp LQ097L1JY01Z; outline and
//borders default to the LG LP097QX1 datasheet (same active area, flex/PCB
//strip at the BOTTOM). The screen window is enlarged from the original
//(scale 1.69 -> 1.81) and raised 5 mm so the active area fits WHOLE without
//hitting the DVD bezel. The panel rests against the front's inner face,
//clamped by a printed frame ("lcd-frame") screwed to 6 M3 bosses.
scr_scale = 1.81;   //window scale (1.69 was the 1:1 of the original Mac)
scr_cz    = 190;    //window center Z (front local coords; original was 185)
//Measure these four against the actual panel before printing the front:
lcd_w = 208.88; lcd_h = 167.12;
lcd_th = 2.6;   //measured LQ097L1JY01Z (the LP097QX1 datasheet says 5.2)
lcd_bottom_border = 8.56; //glass edge to active area, flex side (bottom).
                          //Theoretical 13.56 left the active area 5 mm low
                          //against the window -> pocket raised 5.
lcd_x0 = 123.8 - lcd_w/2;        //outline corner (front local)
lcd_z0 = scr_cz - 147.456/2 - lcd_bottom_border;  //active area centered; thick (flex) edge down
//Centering pocket in the frame (the panel drops into position by itself):
lcd_clr  = 0.3;             //pocket clearance per side
lcd_stop_th = 1.8;          //stop wall thickness
lcd_stop_h  = lcd_th - 0.3; //stop height (reaches 0.8 short of the front's inner face)
lcd_boss_h = lcd_th + 4.8;  //front boss height (protrudes lcd_boss_h-0.5;
                            //long enough for the 6.5 M3 insert)
//The frame plate rests on the boss TIPS (3.4 holes vs d8 boss: it bottoms
//out, cannot slide over). The lip spans boss tip -> panel back:
lcd_lip = (lcd_boss_h - 0.5) - (lcd_th + 0.3);  //=4.0 by construction
//Frame M3 bosses: 3 per side, next to the panel's side edges. The BOTTOM row
//(z local 95) lands on the LOWER front piece (the split runs at z local
//~101.5): the screwed frame ties the two front pieces together as well as
//clamping the panel. (Bottom-right boss at x=238: at 233.7 it hit the DVD
//bezel, which reaches x~231.)
lcd_bosses = [[13.9,95],[13.9,190],[13.9,255],[238,95],[233.7,190],[233.7,255]];

// ========================= Speakers (20x30 oval, stereo) =========================
//Lie against the recessed bottom band of the front (the inward-leaning part,
//like the real Mac's speaker): just a hole grille cut into that vertical
//wall; the speakers GLUE on from the inside (no pocket).
spk_z = 23;                //center Z, world (the recessed band spans ~5 to 44)
spk_x = [55, 192.6];       //center X of both speakers (symmetric about 123.8)


// ========================= LCD OSD button strip (front chin) =========================
//The HDMI driver kit's 5-button strip (POWER/ENTER/UP/DOWN/MENU, 107 x 16.5
//x 1.5, FFC connector on the RIGHT) mounts against the BOTTOM face of the
//front chin (the near-horizontal surface above the recessed band): buttons
//point DOWN and are pressed reaching under the lip, monitor-OSD style —
//invisible from the front. The wall thins to osd_wall and the plungers poke
//~1 mm through d5 holes; the PCB screws to bosses with 2 self-tapping M2.5.
//Coordinates in the sloped front's local frame (the chin is the bulk's
//z_local=0 plane; its usable flat zone spans y_local 0 to ~25).
osd_x0    = 70.3;                      //PCB left edge (centered: 123.8 - 107/2)
osd_y0    = 3.5;                       //PCB FRONT edge (y local; button row at +8.25)
osd_w     = 107; osd_h = 16.5;         //PCB outline
//Button positions verified on a test piece: POWER and ENTER 24 apart (the
//LED sits between them), the rest at 18 pitch.
osd_btn   = [7, 31, 49, 67, 85];       //plunger centers from the left edge
//Mounting holes. The leftmost is measured 7.5 from POWER's center toward the
//edge: it lands at -0.5, i.e. an open notch at the PCB edge; the boss
//overhangs the outline and the screw bites the notch anyway.
osd_holes = [osd_btn[0]-7.5, 57.5, 96];
osd_sw_h  = 7.3;                       //switch height: PCB face -> plunger tip
osd_protrusion = 1.0;                  //how far the plunger pokes past the outer face
osd_wall  = 1.6;                       //wall thickness left in the recess

// ========================= HDD activity LED (front) =========================
//5 mm red LED LEFT of the floppy slot, under the DVD bezel's left end (same
//world z ~107.5 as the floppy slot axis). Inserted from BEHIND: d5.2 lens
//pass + d6.4 recess for the LED flange, which bottoms out 2.5 from the face
//(lens near flush; drop of CA glue).
led_x = 100;   //center X (front local); DVD bezel x~82..231, floppy bezel from x~118.6

// ========================= Power button (rear) =========================
//16 mm momentary panel-mount button in the rear wall, motherboard side
//(lands next to the F_PANEL header, bottom-rear-left).
pwr_x = 80;    //clear of the PSU cutout (starts x~98) and the motherboard (components to x~58)
pwr_z = 60;
pwr_d = 16.4;  //hole for a 16 mm button

// ========================= Front dual USB (right wall, forward) =========================
//Dual USB-A panel-mount connector with ears (screwed from outside). Passes
//through the window in the cage's right pedestal. Connector: 40 total with
//ears, 23.5 x 19 body, 15 x 16.8 metal block, screws 30 apart.
usb_y = 48;            //center (the pedestal window spans y=30..66, z=45..87)
usb_z = 68;
usb_cut = [15.5, 17.5]; //cutout for the 15 x 16.8 metal block (+clearance)
usb_screw_sep = 30;
usb_screw_d = 3.2;


// ========================= LCD HDMI driver board (right wall) =========================
//PCB 107 x 55 x 1.2, M3.5 corner holes 3.5 from the edges (span 100 x 48),
//components up to 16. Vertical on the right wall, above the cage, with the
//port edge (HDMI/VGA/jacks) pointing DOWN.
hdmi_y0 = 30;    //front edge of the PCB
hdmi_z0 = 170;   //bottom edge (the ports hang from here)
hdmi_so_h = 6;   //standoff height
hdmi_drill = insert_d; //M3 insert

// ========================= Auxiliary standoffs (right wall) =========================
//Spare mount for any small board: 65 x 30 outline, M2.5 corner holes 3.5
//from the edges (span 58 x 23). Behind the driver board, same wall.
aux_y0 = 150; aux_z0 = 183;
aux_so_h = 5;
aux_drill = 2.1;  //M2.5 pilot


// ========================= 2.5" drive (rear wall) =========================
//A 2.5" drive (100 x 69.85, 7 to 9.5 thick) rides in a printed CRADLE with a
//clip ("hdd-cradle"): the drive slides in from the right, SATA connector to
//the left (the motherboard's SATA ports land in that corner), and a flexing
//finger locks it. The cradle screws with 4 COUNTERSUNK M3 to rear-wall
//bosses matching the drive's bottom hole pattern (76.6 x 61.72).
hdd_x0 = 125;   //drive's left edge
hdd_z0 = 105;   //bottom edge
hdd_so_h = 5;   //boss height
hdd_drill = insert_d;
//Drive's SATA block (data+power), for the window in the cradle's left wall.
//The block is NOT centered on the 69.85 edge: ~45 wide, starting ~10 from
//one edge. Referenced from the BOTTOM edge by default; if your drive's block
//sits near the top edge, set hdd_sata_from_bottom=false.
hdd_sata_off = 10;            //reference edge to block start
hdd_sata_w   = 45;            //block width
hdd_sata_clr = 2;             //window clearance each side of the block
hdd_sata_from_bottom = true;

// ========================= Ventilation =========================
//Exhaust: two slot banks in the sloped top-back surface (over the CPU cooler
//on the left and over the PSU on the right), behind the handle. Intake:
//slots in the floor, at the front.
vent_slot_w = 2.5;
vent_top_y = 185; vent_top_l = 45;                 //slots run along the slope
vent_top_x = [[30, 10], [145, 10]];                //[start x, count] per bank (7.5 pitch)
vent_floor_y = 30; vent_floor_l = 40;
vent_floor_x  = [[40, 10], [150, 8]];              //[start x, count] per bank (8 pitch)
//Extra exhaust in the REAR wall, upper band: passive left bank (same style
//as the top); the FAN goes on the right (its grille replaces that bank)
vent_rear_z = 185; vent_rear_l = 30;
vent_rear_x = [[30, 10]];                          //[start x, count] per bank (7.5 pitch)

//80x80x25 exhaust fan in the rear wall, top right, blowing OUT. Rests
//against the inner face, screwed from outside with 4 self-tapping M4 into
//the fan's own holes. The grille is slots in the house style (bank of 9
//covering the 76 circle). Position limits: HDD cradle to z~177.5 below,
//top/wall fillet above (z~260), vertical-joint tabs to x~142 on the left,
//fan body whole within the back-upper-right piece (x>125, z>140).
fan_cx = 184; fan_cz = 219;
fan_screw_sep = 71.5;   //between centers of an 80 mm fan's holes
fan_screw_d   = 4.4;    //M4 free pass through the wall
fan_grill_l   = 72;     //grille slot length (centered on fan_cz)


// ========================= ATX power supply (PS/2) =========================
psu_w     = 150;  //width
psu_h     = 86;   //height
psu_depth = 140;  //depth (125 to 180 exist; adjust if needed)
psu_gap   = 2;    //clearance to the right wall

psu_x  = cw - wall - psu_gap - psu_w;  //PSU's left corner
psu_y  = cd - wall - psu_depth - 1.5;  //tail 1.5 off the wall: clears the floor/wall fillet (r=1.5)
psu_cx = psu_x + psu_w/2;              //center of the rear cutout
psu_cz = wall + psu_h/2;

//ATX screw pattern on the rear face ([x,z] offsets from the center of the
//150x86 face, seen FROM OUTSIDE with the IEC inlet right and the fan left).
//Real (asymmetric) pattern: two screws on the fan side (left, +x) and two on
//the inlet side (-x), the lower one shifted 30 in from the edge to clear the
//IEC inlet.
psu_holes = [
    [ 69,  38],   //top-left (6 from the left edge, 5 from the top)
    [ 69, -26],   //bottom-left (6 from left, 64 lower)
    [-69,  37],   //top-right (6 from right, 6 from the top)
    [-45, -37],   //bottom-right (30 from right, 6 from the bottom)
];
psu_hole_d = 4.2; //free pass for #6-32 PSU screws


// ========================= Front->back seam screws (anti-warp) =========================
//6 countersunk M3 pull the front/back seam shut (print warp keeps it from
//closing by itself): 2 vertical in the TOP (behind the seam, clear of the
//handle channel), 1 horizontal per side LOW (behind the trimmed y=-4 edge,
//in the lower front) and 1 horizontal per side HIGH (in the upper front,
//2 cm above the z=146 split). The front carries FLAPS with M3 inserts; the
//back gets matching countersunk through-holes.
flap_top_x = [60, 187.6];  //symmetric (60 off each wall)
flap_top_y = 22;           //hole center (front local coords; seam at y~12.8)
flap_side_y = 7;           //low side hole center (11 behind the y=-4 edge)
flap_side_z = 90;          //low side hole height (from the outer floor)
flap_high_y = 8;           //high side hole center (~9 behind the seam)
flap_high_z = 172;         //high side hole height (32 above the z=140 joint)

// ========================= Horizontal splits (250 mm bed) =========================
//The back (334 tall) splits at z=140: a band clear on all 4 walls (above the
//motherboard's middle standoffs z~95, the front USB z~87 and the HDD's lower
//boss z~113; below the HDMI ports z~158 and the HDD's upper boss z~167).
//The front (~335) splits at z=146: a smooth band between the DVD bezel and
//the screen window. Joint: an inner overlap ring on the lower piece
//following the hull shape + internal M3 bosses/tabs on the back. The front
//seam is bridged by the screwed LCD frame instead.
back_split_z  = 140;
front_split_z = 146;
split_lip_h  = 8;
split_lip_th = 1.8;
split_clr    = 0.15;
//Back-split joints: HORIZONTAL M3 screws driven from inside (the driver
//comes in through the open front; vertical was impossible with the top 2 cm
//away). Format [px, py, nx, ny]: point on the wall's inner face + inward
//normal. The left wall gets ONE (y=14): the motherboard sits behind the rest
//(y 36..236) and itself bridges the seam (standoffs at z~95 and z~253). The
//rear wall gets ONE (x=70): behind the HDD (x 125..225) a boss would stab
//the drive, and the HDD cradle already bridges that seam (bosses at z~109
//and z~171, screwed on after joining the halves).
back_split_joints = [[3, 14, 1, 0], [244.6, 20, -1, 0], [244.6, 200, -1, 0], [70, 238.3, 0, -1]];
split_boss_z = back_split_z - 11;  //joint screw axis height

// ========================= Vertical split of the back (250 mm bed) =========================
//The back also splits into left/right halves: no orientation of the whole
//pieces gets under 247.6 wide, which does not fit a 250 bed with slicer
//margin. The x=125 plane runs through the gap between vent banks (floor
//114.5..150, top/rear 100..145), clears the front tabs (x 20/230), the cage
//bosses (x 128.5..137.5), the power button (x<89) and leaves the x=122.6 PSU
//screw whole on the left. Joint: a two-step vertical tongue (same section as
//the horizontal ring, always on the LEFT half) + M3 bosses/tabs with the
//same geometry as the horizontal split. NO screws in the floor: the cage's
//left pedestal covers x127..144 and the PSU sits on y>96.8; that seam is
//held by the tongue, the rear screw, the front tabs (one per half, bridged
//by the screwed front) and the horizontal ring clamped by the upper halves.
vsplit_x = 125;
//Joint z positions on the rear wall. They clear: the PSU (z<89), the HDD and
//its SATA window (x110..225, z105..175), the exhaust slots (z185..215
//outside the x100..145 gap) and the horizontal ring (z134..148).
vsplit_rear_lower = [97];
vsplit_rear_upper = [183, 230];
//Joints hung from the top (vertical screw from below, driven with the shell
//open), in the x100..145 gap between slots. The top at x=125 is NOT flat:
//high front cover, steep slope between y~95 and y~150, low rear cover.
//Format [y, z]: z = measured inner face - 0.5 (the boss embeds 2 and
//tolerates the mild slope).
vsplit_top          = [[60, 326.6], [160, 271.3], [210, 267]];

// ========================= TPU floor feet ("tpu-feet") =========================
//Rubber feet GLUED under the floor so the case doesn't drag on the desk.
//A blind pocket swallows the front screws' pan heads (the floor's d6.6
//countersink doesn't hide them; they sit ~2 mm proud). The FRONT pair
//centers on the floor screw holes (x=25/235, y=12); the REAR pair glues near
//the back corners (no screw there, the pocket doesn't matter — all 4 pieces
//are identical). Print in TPU, desk face on the bed: the pocket ends up on
//top and needs no support.
foot_d        = 15;  //outer diameter (MAX 15: the front screw sits at y=12
                     //and the flat floor starts at y=4.5 — the shell's lower
                     //edge is rounded r4.5; any bigger overhangs)
foot_h        = 5;   //total height (how much it lifts the case)
foot_pocket_d = 9;   //pocket for the head (M3 pan: ~d6 x 2.5, with play)
foot_pocket_h = 3;   //pocket depth (2 mm of rubber remain below)

module tpu_foot() {
    difference() {
        union() {
            cylinder(d1=foot_d-2, d2=foot_d, h=1); //chamfer against the desk
            translate([0,0,1]) cylinder(d=foot_d, h=foot_h-1);
        }
        translate([0,0,foot_h-foot_pocket_h])
            cylinder(d=foot_pocket_d, h=foot_pocket_h+0.1);
    }
}

// ========================= Part dispatch =========================

if (part == "cutaway") {
    difference() {
        union() {
            front_half();
            back_half();
            color("tomato") drive_cage();
            color("red") { fdd_funnel(); odd_funnel(); }
            color("orange") { cage_pedestal(-1); cage_pedestal(1); }
            color("orange") hdd_cradle();
        }
        translate([125*case_scale, -500, -500]) cube([1000, 1000, 1000], false);
    }
    if (show_hardware) ghost_hardware();
} else if (part == "assembly") {
    union() {
        front_half();
        back_half();
    }
    color("tomato") drive_cage();
    color("red") { fdd_funnel(); odd_funnel(); }
    color("orange") { cage_pedestal(-1); cage_pedestal(1); }
    color("orange") hdd_cradle();
    //the frame plate rests on the boss tips (they protrude lcd_boss_h-0.5)
    color("orange") translate([0,-25.4,44.5]) rotate([-5,0,0]) translate([0, lcd_boss_h-0.5+4, 0]) rotate([90,0,0]) lcd_frame();
    if (show_hardware) ghost_hardware();
} else if (part == "cage-assembly") {
    drive_cage();
    color("red") { fdd_funnel(); odd_funnel(); }
    color("orange") { cage_pedestal(-1); cage_pedestal(1); }
    color("orange") hdd_cradle();
    if (show_hardware) ghost_hardware();
} else if (part == "drive-cage") {
    //lying on its left face (its minimum x, without the funnels, is the DVD
    //bay's left wall)
    translate([145, 0, -(odd_slot_cx - odd_w/2 - 0.3 - cage_wall - 0.05)]) rotate([0,-90,0]) drive_cage();
} else if (part == "cage-pedestals") {
    //both pedestals lying on the flat (flangeless) face of their wall
    translate([0, 0, 144.05]) rotate([0,90,0]) cage_pedestal(-1);
    translate([0, 110, 228.05]) rotate([0,90,0]) cage_pedestal(1);
} else if (part == "hdd-cradle") {
    //flat on the bed (base down, walls and finger up)
    translate([-100, 0, cd-wall-hdd_so_h]) rotate([-90,0,0]) hdd_cradle();
} else if (part == "lcd-frame") {
    //already flat in XY
    translate([-lcd_x0+6, -lcd_z0, 0]) lcd_frame();
} else if (part == "front-lower") {
    render() front_lower();
} else if (part == "front-upper") {
    render() translate([0, 0, -front_split_z]) front_upper(); //resting on the split plane
} else if (part == "eject-cap") {
    //standing on the plug's back (chamfer down, plunger up; the hook slot
    //prints as a 1.1 internal bridge, no support)
    translate([-195, -90, -12.5]) rotate([-90,0,0]) eject_cap();
} else if (part == "dvd-tray-cover") {
    //face down: smooth face on the bed, pads and nub up (the membrane is the
    //recess's first 3 layers, no support)
    translate([0, 0, -0.2]) rotate([90,0,0]) dvd_cover();
} else if (part == "back-lower-left") {
    render() back_lower_left();
} else if (part == "back-lower-right") {
    render() translate([-vsplit_x+2, 0, 0]) back_lower_right();
} else if (part == "back-upper-left") {
    //lying on its back (flat rear wall on the bed): resting on the split
    //plane the tabs would hang 19 below the bed
    render() translate([0, -(split_boss_z-8), cd]) rotate([-90,0,0]) back_upper_left();
} else if (part == "back-upper-right") {
    render() translate([-vsplit_x+2, -(split_boss_z-8), cd]) rotate([-90,0,0]) back_upper_right();
} else if (part == "fdd-funnel") {
    //mouth face down: the guiding interior prints clean; supports only touch
    //the outside (the bell and the hug-plate bridges)
    translate([-119, 118.6, 16]) rotate([90,0,0]) fdd_funnel();
} else if (part == "dvd-funnel") {
    //mouth face down, like the floppy funnel
    translate([-88, 140.5, 15]) rotate([90,0,0]) odd_funnel();
} else if (part == "tpu-feet") {
    //4 TPU feet, desk face on the bed (pocket up)
    for (i=[0,1], j=[0,1]) translate([i*(foot_d+8), j*(foot_d+8), 0]) tpu_foot();
}


// ========================= Halves =========================

module front_half() {
    render() difference() {
        union() {
        difference() {
        scale(case_scale) union() {
            difference() {
                mac_hull(wall/case_scale);
                mac_crack_divider();
            }
            intersection() {
                mac_overlap_inner_edge(overlapth/case_scale);
                difference() {
                    mac_crack_divider_expanded(-0.8/case_scale);
                    mac_crack_divider_expanded(overlap/case_scale);
                }
            }
            //drive cage retainers (straighten cage warp as the front closes)
            cage_retainers();
            //LCD frame M3 bosses (the panel rests against the front's inner face)
            translate([0, -25.4, 44.5]) rotate([-5,0,0])
                for (p=lcd_bosses) translate([p[0], -0.5, p[1]]) rotate([-90,0,0]) difference() {
                    cylinder(d=8, h=lcd_boss_h);
                    translate([0,0,lcd_boss_h-7]) cylinder(d=insert_d, h=7.5);
                }
        }
        //OSD strip recess + holes (cuts the shell only, not the bosses)
        osd_cutout();
        }
        osd_bosses();
        }
        //front floor screws (into the back half's floor tabs)
        front_floor_screw_holes();
    }
}

//OSD strip negative: thins the chin to osd_wall over the PCB zone and opens
//the 5 d5 plunger passes (vertical, pointing down)
module osd_cutout() {
    translate([0,-25.4,44.5]) rotate([-5,0,0]) {
        translate([osd_x0-1.5, osd_y0-1.5, osd_wall]) cube([osd_w+3, osd_h+3, 8]);
        for (b=osd_btn) translate([osd_x0+b, osd_y0+osd_h/2, -1]) cylinder(d=5, h=osd_wall+2);
    }
}

//OSD strip M2.5 bosses: the tip lands where the PCB face rests (the switches
//almost touch the thinned wall and the plungers poke out osd_protrusion)
module osd_bosses() {
    translate([0,-25.4,44.5]) rotate([-5,0,0]) for (hx=osd_holes)
        translate([osd_x0+hx, osd_y0+osd_h/2, osd_wall-0.2]) difference() {
            cylinder(d=6, h=osd_sw_h-osd_protrusion-osd_wall+0.2);
            translate([0,0,0.8]) cylinder(d=2.2, h=20);  //M2.5 pilot (blind toward the wall)
        }
}

//CAGE RETAINERS: brackets on the front's inner face that hug the mouth of
//the DVD funnel (the cage's tallest, most warp-prone part) and straighten it
//as the front closes. Two 20 mm ramp wedges under the funnel body (0.3
//clearance; the tip ramp lifts a sagging cage) + two vertical fingers with a
//2 mm flare that center the mouth in x (0.2/0.3 clearance). Rooted in the
//front's band between the floppy and DVD bezels, clear of the visible bezel
//recesses. World coords: DVD mouth x 88.9..225.9, funnel body from z 122.8.
module cage_retainers() {
    for (s=[0,1]) {
        xs = s==0 ? 88.6 : 206.2;          //wedge x0 (20.2 long)
        xf = s==0 ? 86.1 : 226.2;          //finger x0 (2.6 thick)
        fl = s==0 ? -2 : 2;                //flare of the finger tip
        //wedge: flat stop at z=122.5 (0.3 under the funnel body) with a top
        //ramp toward the tip; the root blends into the front's inner face
        hull() {
            translate([xs, -19.5, 116.5]) cube([20.2, 0.3, 6]);
            translate([xs, -15.2, 118.5]) cube([20.2, 0.3, 4]);
            translate([xs, -13.5, 119.5]) cube([20.2, 0.3, 1]);
        }
        //side finger: grows from the wedge (clear of the DVD bezel recess)
        hull() {
            translate([xf, -18.5, 121]) cube([2.6, 3.3, 21.5]);
            translate([xf+fl, -13.5, 121]) cube([2.6, 0.3, 21.5]);
        }
    }
}

//Frame that clamps the LCD against the front (flat printable piece,
//"lcd-frame"). Built in the sloped front's XY plane: X = local x, Y = local
//z, 4 thick in Z. Flush with the panel at the bottom (to clear the DVD
//funnel), window for the eDP connector in the flex strip below.
module lcd_frame() {
    iw = 201; ih = 149.5;                  //inner window (active area + ~1 per side, asymmetric below)
    ix0 = 123.8 - iw/2;
    iz0 = scr_cz - 147.456/2 - 1;
    px0 = lcd_x0 - lcd_clr; px1 = lcd_x0 + lcd_w + lcd_clr;
    py0 = lcd_z0 - lcd_clr; py1 = lcd_z0 + lcd_h + lcd_clr;
    difference() {
        union() {
            translate([lcd_x0-6, lcd_z0, 0]) cube([lcd_w+12, lcd_h+6, 4]);
            for (p=lcd_bosses) translate([p[0], p[1], 0]) cylinder(d=13, h=4);
            //straps joining the bottom-row ears (below the plate's edge) to
            //the frame body
            for (p=lcd_bosses) if (p[1] < lcd_z0)
                translate([p[0]-6.5, p[1], 0]) cube([13, lcd_z0-p[1]+2, 4]);
            //CAGE PRESS BAR: straight band from ear to ear (bottom row) that
            //drops to minimal clearance over the cage's DVD funnel roof
            //(world z 140.5). If the cage warps upward, screwing the frame
            //flattens it into place. Bottom edge at y=97.6: with the front
            //sloped 5 degrees the rear-bottom edge lands at world 140.78
            //(0.28 clearance) and the front one at 141.13. Do not move the
            //97.6 without redoing that math. The ear holes (y=95+-1.7) stay
            //clear.
            translate([13.9, 97.6, 0]) cube([238-13.9, lcd_z0-97.6+1, 4]);
            //CENTERING STOPS: a low-walled pocket on the panel face. Rest
            //the panel on the frame, it drops in centered, and frame+panel
            //screw to the bosses together. Short segments: they dodge the
            //panel's 4 corner metal tabs, the frame's own d13 ears (z=190
            //and 255) and the driver/flex bar below.
            //CLAMP LIP: drops lcd_lip (4.0) from the plate face to the panel
            //back and presses it on the glass edge. 4 wide strips on the
            //sides and top; below only two pads in the windows left free by
            //the driver bar (which rides raised on the panel's back and must
            //not be pressed).
            translate([px0, py0+5, 4-0.1])   cube([4, (py1-5)-(py0+5), lcd_lip+0.1]);
            translate([px1-4, py0+5, 4-0.1]) cube([4, (py1-5)-(py0+5), lcd_lip+0.1]);
            for (x=[lcd_x0+15, lcd_x0+lcd_w-27])
                translate([x, py0, 4-0.1]) cube([12, 4, lcd_lip+0.1]);
            //STOPS: grow from the plate face and exceed the lip by
            //lcd_stop_h, hugging the glass edge. Sides: 2 segments per side
            for (x=[px0-lcd_stop_th, px1], seg=[[py0+5, 70], [py0+100, 40]])
                translate([x, seg[0], 4-0.1]) cube([lcd_stop_th, seg[1], lcd_lip+lcd_stop_h+0.1]);
            //top edge: central segment, away from the corner tabs
            translate([123.8-60, py1, 4-0.1]) cube([120, lcd_stop_th, lcd_lip+lcd_stop_h+0.1]);
            //bottom edge (flex side): two short stops in the free windows
            //between the corner tab and the driver bar. Adjust these offsets
            //if they touch either when offering up the panel.
            for (x=[lcd_x0+15, lcd_x0+lcd_w-27]) {
                translate([x, py0-lcd_stop_th, 4-0.1]) cube([12, lcd_stop_th, lcd_lip+lcd_stop_h+0.1]);
                translate([x, py0-lcd_stop_th-1, 0]) cube([12, 6, 4]); //pad extending the plate
            }
        }
        translate([ix0, iz0, -1]) cube([iw, ih, 6]);
        //eDP connector + flex window (panel's back face, bottom center)
        translate([123.8-40, lcd_z0-1, -1]) cube([80, 13.5, 6]);
        for (p=lcd_bosses) translate([p[0], p[1], -1]) cylinder(d=3.4, h=6);
    }
}


module back_half() {
    difference() {
        union() {
            render() scale(case_scale) difference() {
                intersection() {
                    mac_hull(wall/case_scale);
                    mac_crack_divider();
                }
                intersection() {
                    mac_overlap_inner_edge(overlapth/case_scale);
                    difference() {
                        mac_crack_divider();
                        mac_crack_divider_expanded(overlap/case_scale);
                    }
                }
            }
            mobo_standoffs();
            psu_stops();
            cage_floor_bosses();
            right_wall_standoffs();
            hdd_standoffs();
            front_screw_tabs();
        }
        mobo_standoff_holes();
        hdd_standoff_holes();
        psu_cutout();
        cage_floor_boss_holes();
        power_button_cutout();
        usb_cutout();
        right_wall_standoff_holes();
        top_vents();
        floor_vents();
        rear_vents();
    }
}


// ========================= Motherboard mount =========================

//Places children() in the board's local coordinate system: local X = along
//the IO edge (grows DOWNWARD in the case), local Y = board depth (backward),
//local Z = components (inward). The local Z=0 plane (solder side) rests on
//the standoffs.
module at_mobo() {
    translate([wall+standoff_h, mobo_y_io, mobo_z_top]) rotate([0,90,0]) children();
}

module mobo_standoffs() {
    for (i=[0:len(mobo_holes)-1]) if (mobo_hole_on[i])
        translate([wall-0.1, mobo_y_io+mobo_holes[i][1], mobo_z_top-mobo_holes[i][0]])
            rotate([0,90,0]) cylinder(d=standoff_d, h=standoff_h+0.1);
}

module mobo_standoff_holes() {
    for (i=[0:len(mobo_holes)-1]) if (mobo_hole_on[i])
        translate([wall+standoff_h+0.01, mobo_y_io+mobo_holes[i][1], mobo_z_top-mobo_holes[i][0]])
            rotate([0,-90,0]) cylinder(d=standoff_drill, h=standoff_h+1.4); //leaves ~1.6 of wall
}

//Ghost motherboard, in board-local coordinates
module mobo_ghost() {
    color("darkgreen") cube([mobo_w, mobo_d, mobo_pcb]);
    //IO connector block: overhangs the front edge
    color("silver") translate([3, -11, mobo_pcb]) cube([158, 11, 40]);
    //generic component zone (CPU cooler, RAM, etc.)
    color("darkgreen", 0.3) translate([15, 30, mobo_pcb]) cube([214, 155, 45]);
    //mounting holes (visual reference, component side)
    for (h=mobo_holes) translate([h[0], h[1], 0.05]) cylinder(d=4, h=mobo_pcb+1);
}


// ========================= PSU mount =========================

module psu_cutout() {
    //rear opening (leaves a 12 mm frame for the screws)
    translate([psu_cx-63, cd-wall-1, psu_cz-31]) cube([126, wall+2, 62]);
    //screws
    for (h=psu_holes)
        translate([psu_cx+h[0], cd-wall-1, psu_cz+h[1]])
            rotate([-90,0,0]) cylinder(d=psu_hole_d, h=wall+2);
}

//Right-wall standoffs for the HDMI driver board and the auxiliary mount.
//Both boards' holes sit 3.5 from their edges.
function hdmi_standoff_points() = [for (dy=[3.5, 103.5], dz=[3.5, 51.5]) [hdmi_y0+dy, hdmi_z0+dz]];
function aux_standoff_points()  = [for (dy=[3.5, 61.5],  dz=[3.5, 26.5]) [aux_y0+dy, aux_z0+dz]];

module right_wall_standoffs() {
    for (p=hdmi_standoff_points()) translate([cw-wall+0.1, p[0], p[1]]) rotate([0,-90,0]) cylinder(d=8, h=hdmi_so_h+0.1);
    for (p=aux_standoff_points())  translate([cw-wall+0.1, p[0], p[1]]) rotate([0,-90,0]) cylinder(d=6, h=aux_so_h+0.1);
}

module right_wall_standoff_holes() {
    for (p=hdmi_standoff_points()) translate([cw-wall-hdmi_so_h-0.01, p[0], p[1]]) rotate([0,90,0]) cylinder(d=hdmi_drill, h=hdmi_so_h+1.4);
    for (p=aux_standoff_points())  translate([cw-wall-aux_so_h-0.01, p[0], p[1]]) rotate([0,90,0]) cylinder(d=aux_drill, h=aux_so_h+1.4);
}

//2.5" drive standoffs on the rear wall (drive's bottom hole pattern)
function hdd_standoff_points() = [for (dx=[14, 90.6], dz=[4.07, 65.78]) [hdd_x0+dx, hdd_z0+dz]];

module hdd_standoffs() {
    for (p=hdd_standoff_points()) translate([p[0], cd-wall+0.1, p[1]]) rotate([90,0,0]) cylinder(d=8, h=hdd_so_h+0.1);
}

module hdd_standoff_holes() {
    for (p=hdd_standoff_points()) translate([p[0], cd-wall-hdd_so_h-0.01, p[1]]) rotate([-90,0,0]) cylinder(d=hdd_drill, h=hdd_so_h+1.4);
}

//2.5" drive cradle (printable): base against the bosses, walls top/bottom,
//two tabs on the left (leaving a window for the SATA block) and a clip
//finger on the right. The drive slides in from the right.
module hdd_cradle() {
    x0 = hdd_x0 - 0.2;  x1 = hdd_x0 + 100.2;   //interior with clearance
    z0 = hdd_z0 - 0.2;  z1 = hdd_z0 + 70.05;
    yb = cd - wall - hdd_so_h;                  //back face of the base (rests on the bosses)
    yw = yb - 3 - 11;                           //front face of the walls
    difference() {
        union() {
            //base
            translate([x0-2.5, yb-3, z0-2.5]) cube([x1-x0+5, 3, z1-z0+5]);
            //bottom wall (full: anchors the finger) and top wall (short, so the finger can flex)
            translate([x0-2.5, yw, z0-2.5]) cube([x1-x0+5, 11, 2.5]);
            translate([x0-2.5, yw, z1]) cube([x1-x0-3.5, 11, 2.5]);
            //lips: C-channel retaining the drive's face (45-degree chamfer to print)
            hull() {
                translate([x0-2.5, yw, z0]) cube([x1-x0+5, 0.1, 2.5]);
                translate([x0-2.5, yw, z0+2.4]) cube([x1-x0+5, 1.2, 0.1]);
            }
            hull() {
                translate([x0-2.5, yw, z1-2.5]) cube([x1-x0-3.5, 0.1, 2.5]);
                translate([x0-2.5, yw, z1-0.1]) cube([x1-x0-3.5, 1.2, 0.1]);
            }
            //left tabs: leave a WINDOW aligned with the SATA block, with
            //hdd_sata_clr clearance each side
            win_lo = hdd_sata_from_bottom ? z0 + hdd_sata_off - hdd_sata_clr
                                          : z1 - hdd_sata_off - hdd_sata_w - hdd_sata_clr;
            win_hi = win_lo + hdd_sata_w + 2*hdd_sata_clr;
            translate([x0-2.5, yw, z0])     cube([2.5, 11, win_lo - z0]); //bottom tab
            translate([x0-2.5, yw, win_hi]) cube([2.5, 11, z1 - win_hi]); //top tab
            //flexible right finger with ramped bumper and pull tab
            translate([x1+0.2, yw, z0-2.5]) cube([2.2, 11, 50]);
            hull() {
                translate([x1+0.2, yw, z0+27]) cube([0.1, 11, 12]);
                translate([x1-1.3, yw, z0+31.5]) cube([0.1, 11, 3]);
            }
            translate([x1+0.2, yw, z0+47.5]) cube([7, 11, 2.5]); //pull tab to open
        }
        //countersunk holes (flat-head M3) toward the bosses
        for (p=hdd_standoff_points()) translate([p[0], yb-3-0.01, p[1]]) rotate([-90,0,0]) {
            cylinder(d=3.4, h=5);
            cylinder(d1=6.6, d2=3.4, h=1.7);
        }
    }
}

//Sloped-top exhaust slots (vertical cuts through the slope; the y=185..230
//zone is behind the handle and clear of everything)
module top_vents() {
    for (b=vent_top_x, i=[0:b[1]-1])
        translate([b[0]+i*7.5, vent_top_y, 255]) flatroundedcube(vent_slot_w, vent_top_l, 70, 1.2);
}

//Floor intake slots, at the front (clear of cage bosses, pedestals and PSU stops)
module floor_vents() {
    for (b=vent_floor_x, i=[0:b[1]-1])
        translate([b[0]+i*8, vent_floor_y, -1]) flatroundedcube(vent_slot_w, vent_floor_l, wall+2, 1.2);
}

//Rear-wall exhaust: passive left bank (z 185..215) + the 80 mm fan grille on
//the right (longer slots) + M4 fan screws
module rear_vents() {
    for (b=vent_rear_x, i=[0:b[1]-1])
        translate([b[0]+i*7.5, cd-wall-1, vent_rear_z+vent_rear_l]) rotate([-90,0,0])
            flatroundedcube(vent_slot_w, vent_rear_l, wall+2, 1.2);
    //fan grille: 9 slots (x 152.5..215, centered on fan_cx=~184; the two end
    //slots were dropped to leave solid wall around the M4s)
    for (i=[0:8])
        translate([152.5+i*7.5, cd-wall-1, fan_cz+fan_grill_l/2]) rotate([-90,0,0])
            flatroundedcube(vent_slot_w, fan_grill_l, wall+2, 1.2);
    //fan M4 holes
    for (sx=[-1,1], sz=[-1,1])
        translate([fan_cx+sx*fan_screw_sep/2, cd-wall-1, fan_cz+sz*fan_screw_sep/2])
            rotate([-90,0,0]) cylinder(d=fan_screw_d, h=wall+2);
}

//Power button hole in the rear wall
module power_button_cutout() {
    translate([pwr_x, cd-wall-1, pwr_z]) rotate([-90,0,0]) cylinder(d=pwr_d, h=wall+2);
}

//Dual USB cutout in the right wall: central block + 2 screws
module usb_cutout() {
    translate([cw-wall-1, usb_y-usb_cut[0]/2, usb_z+usb_cut[1]/2]) rotate([0,90,0])
        flatroundedcube(usb_cut[1], usb_cut[0], wall+2, 1.5);
    for (s=[1,-1]) translate([cw-wall-1, usb_y+s*usb_screw_sep/2, usb_z]) rotate([0,90,0])
        cylinder(d=usb_screw_d, h=wall+2);
}

//Tabs to screw the FRONT down: beams in the back half's floor crossing the
//divider through the air (0.2 above the front's floor); the front fixes with
//2 countersunk M3 driven FROM BELOW the case.
module front_screw_tabs() {
    for (x=[20, 230]) difference() {
        union() {
            translate([x, 8, wall+0.2]) cube([10, 32, 8]);
            translate([x, 22, wall-0.2]) cube([10, 18, 1]); //fusion with the back floor
        }
        translate([x+5, 12, wall-0.9]) cylinder(d=insert_d, h=8);  //M3 insert from under the beam
    }
}

//Countersunk holes in the FRONT's floor for the tab screws
module front_floor_screw_holes() {
    for (x=[25, 235]) translate([x, 12, -0.1]) {
        cylinder(d=3.4, h=wall+0.4);
        cylinder(d1=6.6, d2=3.4, h=1.7);
    }
}

//Floor stops so the PSU can't shift (the floor carries the weight, the 4
//rear screws fix it)
module psu_stops() {
    translate([psu_x+15, psu_y-3, wall-0.1]) cube([120, 3, 8]);       //front stop
    translate([psu_x-3, psu_y+15, wall-0.1]) cube([3, 110, 8]);       //left side stop
}

module psu_ghost() {
    color("gray") difference() {
        cube([psu_w, psu_depth, psu_h]);
        translate([psu_w/2, psu_depth-5, psu_h/2]) rotate([-90,0,0]) cylinder(d=60, h=6); //tail reference
    }
}


// ========================= Drive cage =========================

//Printable piece, in case coordinates. Everything derives from the drive
//positions, which in turn derive from the front slots.
module drive_cage() {
    jw  = cage_wall;
    fx0 = fdd_slot_cx - fdd_w/2 - cage_clr;    //floppy bay: interior
    fx1 = fdd_slot_cx + fdd_w/2 + cage_clr;
    fzb = fdd_top_z - fdd_h - 0.2;             //top face of the base (drive rests here)
    fz1 = fdd_top_z + 0.4;                     //floppy bay's inner roof
    ox0 = odd_slot_cx - odd_w/2 - 0.3;         //DVD bay: interior
    ox1 = odd_slot_cx + odd_w/2 + 0.3;
    ozb = odd_bottom_z - 0.2;                  //top face of the DVD shelf
    ztop= odd_bottom_z + odd_h + 2.3;          //top edge of the DVD walls

    bz  = fzb - cage_wall - 2;                 //bottom face of the base (pedestals reach here)

    difference() {
        union() {
            //floppy base (stops short of the PSU, which starts at y=98):
            //2 mm plate + support ribs, so the pedestal screw heads hide
            //between the ribs
            translate([fx0-jw, 0, bz]) cube([fx1-fx0+2*jw, 94, 2]);
            for (x=[124.5, 150, 190, 220]) translate([x, 0, bz]) cube([6.5, 94, fzb-bz]);
            //floppy side walls (rise up to the DVD shelf)
            for (x=[fx0-jw, fx1]) translate([x, 0, bz]) cube([jw, 138, ozb-bz]);
            //DVD shelf (reaches behind the drive's tail, 126.1 deep)
            translate([ox0-jw, 0, ozb-jw]) cube([fx1+jw-(ox0-jw), 125, jw]);
            //DVD side walls
            for (x=[ox0-jw, ox1]) translate([x, 0, ozb-jw]) cube([jw, 125, ztop-ozb+jw]);
            //DVD rear stop: centered in the bay (leaves the slimline SATA
            //connector free, which exits rear-left)
            translate([(ox0+ox1)/2-7.5, odd_y_front+odd_d+0.3, ozb]) cube([15, 2.5, 11]);
        }
        //relief: the walls passing over the PSU don't drop below z=91.5
        translate([fx0-jw-1, 94, bz-1]) cube([fx1-fx0+2*jw+2, 100, 91.5-bz+1]);
        //sliding M3 slots for the floppy's side holes (starts at y=12: the
        //drive moved 8 forward with fdd_y_front=-12)
        for (x=[fx0-jw-1, fx1-1]) translate([x, 12, fzb+fdd_side_slot_z-1.7]) cube([jw+2, 108, 3.4]);
        //DVD sliding side M2 slot (like the floppy's): ONE slot in Y at the
        //odd_side_screws height to move the DVD in depth. CLOSED at the
        //front (starts y=6, doesn't break the front face) and reaching only
        //HALFWAY along the wall (y=62.5): covers the front screw (y=11) with
        //play. Height = odd_side_screw_d.
        for (x=[ox0-jw-1, ox1-1])
            translate([x, odd_side_screws[0][0]-5,
                          odd_side_screws[0][1]-odd_side_screw_d/2])
                cube([jw+2,
                      125/2 - (odd_side_screws[0][0]-5),
                      odd_side_screw_d]);
        //base M3 holes -> pedestal flanges
        for (p=cage_base_holes) translate([p[0], p[1], bz-1]) cylinder(d=3.4, h=4);
        //access holes in the DVD shelf: the screwdriver comes in from above
        //(the cage has no roof) down to the base screws; the DVD covers them
        //afterwards. The left ones shift 1.5 in x to avoid leaving a 0.4
        //wall against the M2 slot (the d8 still clears)
        for (p=cage_base_holes) translate([p[0] + (p[0]<200 ? 1.5 : 0), p[1], ozb-jw-1]) cylinder(d=8, h=jw+2);
    }
}

// ================== Guide funnels ("fdd-funnel" / "dvd-funnel") ==================
//The cage prints WITHOUT funnels: the drives go in from the FRONT (front
//shell off) with the cage already screwed down, fix with their side screws,
//and only then each funnel slips on from the front. Each piece is the
//original funnel cut at y=0 (the cage's front face; deburr flush if needed)
//plus side plates that hug the bay walls from OUTSIDE (0.25 clearance per
//side): they self-align the piece and cover the cut scar. GLUE (a drop of
//CA) at the side plates against the bay walls — screws weakened the piece.
//Install the DVD one FIRST, then the floppy's (the eject cap goes in through
//its channel last).

//Floppy funnel
module fdd_funnel() {
    jw  = cage_wall;
    fx0 = fdd_slot_cx - fdd_w/2 - cage_clr;
    fx1 = fdd_slot_cx + fdd_w/2 + cage_clr;
    fzb = fdd_top_z - fdd_h - 0.2;
    fz1 = fdd_top_z + 0.4;
    bz  = fzb - cage_wall - 2;
    union() {
            //the original cage funnel, cut at y=0. Keeps the eject channel
            //(the cap enters from the front once mounted)
            intersection() {
                difference() {
                    hull() {
                        translate([fx0-jw, fdd_y_front-0.5, bz]) cube([fx1-fx0+2*jw, 1-fdd_y_front, fz1-bz+2]);
                        translate([fdd_slot_cx-51, -16, fdd_slot_z-6.5]) cube([102, 1, 13]);
                    }
                    translate([fx0, fdd_y_front-0.5, fzb]) cube([fx1-fx0, 2-fdd_y_front, fz1-fzb]);
                    hull() {
                        translate([fx0, fdd_y_front-0.5, fzb]) cube([fx1-fx0, 0.5, fz1-fzb]);
                        translate([fdd_slot_cx-48.5, -17, fdd_slot_z-4]) cube([97, 1, 8]);
                    }
                    //eject channel
                    translate([fdd_slot_cx-93.4/2+fdd_button[0]-9, -17, fzb-4]) cube([18, 17, 101.3-fzb+4]);
                }
                translate([fx0-jw-10, -25, bz-5]) cube([fx1-fx0+2*jw+20, 25, fz1-bz+12]);
            }
            //hug plates + solid bridge at the front (y<0) welding them to
            //the body. They end at z=94 to clear the drive's front side
            //screw (the sliding slot runs z~95.2..98.7)
            translate([fx0-jw-2.45, 0, bz]) cube([2.2, 14, 94-bz]);
            translate([fx0-jw-2.45, -6, bz]) cube([2.95, 6, 94-bz]);
            translate([fx1+jw+0.25, 0, bz]) cube([2.2, 14, 94-bz]);
            translate([fx1+jw-0.5, -6, bz]) cube([2.95, 6, 94-bz]);
    }
}

//DVD funnel
module odd_funnel() {
    jw  = cage_wall;
    ox0 = odd_slot_cx - odd_w/2 - 0.3;
    ox1 = odd_slot_cx + odd_w/2 + 0.3;
    ozb = odd_bottom_z - 0.2;
    ztop= odd_bottom_z + odd_h + 2.3;
    union() {
            //the original cage funnel, cut at y=0
            intersection() {
                difference() {
                    hull() {
                        translate([ox0-jw, odd_y_front-0.5, ozb-jw]) cube([ox1-ox0+2*jw, 1-odd_y_front, ztop-ozb+jw]);
                        translate([odd_slot_cx-68.5, -15, odd_slot_z-9]) cube([137, 1, 18]);
                    }
                    translate([ox0, odd_y_front-0.5, ozb]) cube([ox1-ox0, 2-odd_y_front, odd_slot_z+6.5-ozb]);
                    hull() {
                        translate([ox0, odd_y_front-0.5, ozb]) cube([ox1-ox0, 0.5, odd_slot_z+6.5-ozb]);
                        translate([odd_slot_cx-65.75, -16, odd_slot_z-6.5]) cube([131.5, 1, 13]);
                    }
                }
                translate([ox0-jw-10, -25, ozb-jw-5]) cube([ox1-ox0+2*jw+20, 25, ztop-ozb+jw+10]);
            }
            //hug plates, capped at z=132 to clear the drive's front side M2
            //screw (slot at z~134..136.3)
            translate([ox0-jw-2.45, 0, ozb-jw]) cube([2.2, 14, 132-(ozb-jw)]);
            translate([ox0-jw-2.45, -6, ozb-jw]) cube([2.95, 6, 132-(ozb-jw)]);
            //the right one starts ON the shelf (which runs through to the
            //floppy wall) and is thinner: it fits the 2.7 gap to that wall
            translate([ox1+jw+0.25, 0, ozb+0.25]) cube([2.0, 14, 132-(ozb+0.25)]);
            translate([ox1+jw-0.5, -6, ozb+0.25]) cube([2.75, 6, 132-(ozb+0.25)]);
    }
}

//Pedestals holding the cage: 5 mm walls with a flange on top (the cage base
//screws to it) and a foot below (to the floor bosses). Printed lying on
//their face, no supports. side: -1=left, 1=right.
module cage_pedestal(side) {
    //On BOTH sides the wall sits on the right side of the foot (flange
    //toward x-): on the left, a wall at x122..127 blocked the PSU's front
    //cable exit (its ~50 left mm, x~93..143, y~96.8); at x139..144 the
    //harness passes. The holes (x=133) don't change.
    wall_x   = (side<0) ? 139 : 223;            //left face of the 5 mm wall
    flange_x = (side<0) ? 127 : 211;            //flange origin (points toward x-)
    zb = cage_boss_h + wall;                    //7.5: rests on the floor bosses
    zt = fdd_top_z - fdd_h - 0.2 - cage_wall - 2; //87.4: bottom face of the cage base
    cx = (side<0) ? 133 : 217;                  //screw axis
    difference() {
        union() {
            //wall and foot end at y=93: the PSU front stop starts at y=93.8
            //(0.8 clearance instead of overlapping 0.2)
            translate([wall_x, 4, zb]) cube([5, 89, zt-zb]);
            translate([flange_x, 8, zt-8]) cube([12, 82, 8]);   //top flange (8 thick: houses the insert)
            translate([flange_x, 4, zb]) cube([12, 89, 4]);     //foot
        }
        //foot holes (M3 through -> boss)
        for (y=[26, 88]) translate([cx, y, zb-1]) cylinder(d=3.4, h=6);
        //M3 insert in the top flange (screwed from the cage base)
        for (y=[20, 78]) translate([cx, y, zt-7]) cylinder(d=insert_d, h=8);
        //window in the right pedestal for the right wall's front USB
        if (side>0) translate([wall_x-1, 30, 45]) cube([7, 36, zt-45+1]);
        //cable window in the left pedestal: the PSU harness (exits the PSU
        //front on its ~50 left mm) and the front USB cable to the
        //motherboard headers cross here
        if (side<0) translate([wall_x-1, 20, 14]) cube([7, 60, 56]);
    }
}

//Floor bosses the cage screws to (part of the back half)
module cage_floor_bosses() {
    for (p=cage_feet) translate([p[0], p[1], wall-0.1]) cylinder(d=9, h=cage_boss_h+0.1);
}

module cage_floor_boss_holes() {
    for (p=cage_feet) translate([p[0], p[1], wall+cage_boss_h]) rotate([180,0,0]) cylinder(d=insert_d, h=6);
}


// ========================= Ghost drives =========================

//3.5" PC floppy, origin at the front-left-bottom corner
module fdd_ghost() {
    color("wheat") difference() {
        cube([fdd_w, fdd_d, fdd_h]);
        //disk slot
        translate([(fdd_w-91)/2, -1, fdd_h-fdd_slot_below_top-1.6]) cube([91, 3, 3.2]);
    }
    //eject button (typical ~6 mm protrusion, below-right of the slot)
    color("wheat") translate([(fdd_w-93.4)/2 + fdd_button[0] - 4, -5, fdd_h-fdd_slot_below_top+fdd_button[1]-3]) cube([8, 5, 6]);
}

//Slim tray-load DVD, origin at the front-left-bottom corner
module odd_ghost() {
    color("lightsteelblue") cube([odd_w, odd_d, odd_h]);
    //tray face (the printed one: 128.5 max to pass through the 130 mouth)
    color("lightsteelblue") translate([-0.25, -2, 0.5]) cube([odd_w+0.5, 2, odd_h-1]);
}

// ========================= Ghost hardware =========================

module ghost_hardware() {
    %at_mobo() mobo_ghost();
    %translate([psu_x, psu_y, wall]) psu_ghost();
    %translate([fdd_slot_cx-fdd_w/2, fdd_y_front, fdd_top_z-fdd_h]) fdd_ghost();
    %translate([odd_slot_cx-odd_w/2, odd_y_front, odd_bottom_z]) odd_ghost();
    //power button (body behind the rear wall)
    %color("black") translate([pwr_x, cd-wall, pwr_z]) rotate([90,0,0]) union() {
        cylinder(d=18, h=24);
        translate([0,0,-wall-2.5]) cylinder(d=19, h=2.5); //outer ring
    }
    //dual USB (23.5x19 body behind the right wall + pigtail)
    %color("black") translate([cw-wall-30, usb_y-11.75, usb_z-9.5]) cube([30, 23.5, 19]);
    //HDMI driver board (vertical PCB, ports down)
    %color("green") translate([cw-wall-hdmi_so_h-1.2, hdmi_y0, hdmi_z0]) union() {
        cube([1.2, 107, 55]);
        translate([-14, 10, -12]) cube([14, 88, 12]); //hanging port zone
    }
    //auxiliary board
    %color("green") translate([cw-wall-aux_so_h-1.2, aux_y0, aux_z0]) cube([1.2, 65, 30]);
    //2.5" drive against the rear wall
    %color("dimgray") translate([hdd_x0, cd-wall-hdd_so_h-9.5, hdd_z0]) cube([100, 9.5, 69.85]);
    //80x80x25 exhaust fan against the rear wall
    %color("dimgray") difference() {
        translate([fan_cx-40, cd-wall-25, fan_cz-40]) cube([80, 25, 80]);
        translate([fan_cx, cd-wall-26, fan_cz]) rotate([-90,0,0]) cylinder(d=76, h=27);
    }
    //LCD panel against the front
    %color("black") translate([0, -25.4, 44.5]) rotate([-5,0,0]) translate([lcd_x0, 0.3, lcd_z0]) cube([lcd_w, lcd_th, lcd_h]);
}


// ========================= Floppy eject cap =========================
//This FD1231T is missing its original button (see fdd_latch). The cap FILLS
//the 19.5x8 pocket: a 19.1x7.6 plug with a chamfered back (to re-enter the
//pocket when pressed), a slot at the bottom that bites the 7x1 hook (0.1
//clearance: gentle press fit + a drop of CA, the hook has no tooth) and a
//plunger exiting through the front hole. Mounted WITH a diskette in (hook
//out 5): slip on and glue. Drawn in that position, in case coordinates.
//Pressing travels 5 and the plug enters the pocket (0.5 left at the bottom);
//the body front stays 1.5 off the sloped front's inner face at its most
//protruded point.
module eject_cap() {
    gx0 = fdd_slot_cx - 93.4/2 + fdd_latch[0];   //hook's left edge
    gzt = fdd_top_z - fdd_latch[1];              //hook's top face
    hx0 = gx0 - 5;                               //bottom-left corner of the 19.5x8 pocket
    hz0 = gzt - 0.5 - 4;                         //(hook centered in the height)
    difference() {
        union() {
            //plug: fills the pocket with 0.2 clearance per side; the back's
            //last mm chamfered by hull so it re-enters without snagging
            hull() {
                translate([hx0+0.2, -19, hz0+0.2]) cube([19.1, 5.5, 7.6]);
                translate([hx0+1.0, -19, hz0+1.0]) cube([17.5, 6.5, 6.0]);
            }
            //plunger through the front hole (11x8 -> 9x5.5 centered in the
            //hole, with margin because the hole leans 5 degrees; long enough
            //to protrude as a proper button). The hook sits 5.7 ABOVE the
            //hole center, so the plunger hangs below the body...
            translate([fdd_slot_cx-93.4/2+fdd_eject[0]-4.5, -31.4, fdd_slot_z+fdd_eject[1]-2.75])
                cube([9, 12.5, 5.5]);
            //...and this chin joins them: fills the plunger->body step in
            //the front stretch. Ends at y=-17.5: pressed (+5) it stays 0.5
            //off the drive face, which is solid there (outside the pocket).
            translate([fdd_slot_cx-93.4/2+fdd_eject[0]-4.5, -19, fdd_slot_z+fdd_eject[1]-2.75])
                cube([9, 1.5, (hz0+0.7)-(fdd_slot_z+fdd_eject[1]-2.75)]);
        }
        //hook slot (7x1 -> 7.3x1.2, open to the back)
        translate([gx0-0.15, -17.15, gzt-1.1]) cube([7.3, 5.1, 1.2]);
    }
}

// ========================= Split geometry =========================

//Canonical joint geometry for the vertical split (same as the horizontal
//split's): wall at x=0 with the interior toward +x, joint plane at z=0, the
//boss-carrying piece at z<0 and the tab piece at z>0. The boss embeds 2 into
//the wall (tolerates the top's curvature).
module vjoint_boss()     { translate([-2, 0, -11]) rotate([0,90,0]) cylinder(d=9, h=14); }
module vjoint_pilot()    { translate([12.09, 0, -11]) rotate([0,-90,0]) cylinder(d=insert_d, h=9.6); }
module vjoint_notch()    { translate([-1, -10, -0.1]) cube([19, 20, 8.8]); }
module vjoint_tab() {
    translate([-1, -7, 0]) cube([16.8, 14, 4]);
    translate([12.3, -7, -19]) cube([3.5, 14, 23]);
}
module vjoint_tab_hole() { translate([16.5, 0, -11]) rotate([0,-90,0]) cylinder(d=3.4, h=5.5); }

//Place the canonical geometry at each vertical joint. The left half
//(x<vsplit_x) gets the z<0 side (boss); the right half the z>0 side (tab).
module at_lower_vjoints() {
    for (z=vsplit_rear_lower) translate([vsplit_x, cd-wall, z]) rotate([-90,0,-90]) children();
}
module at_upper_vjoints() {
    for (z=vsplit_rear_upper) translate([vsplit_x, cd-wall, z]) rotate([-90,0,-90]) children();
    for (p=vsplit_top) translate([vsplit_x, p[0], p[1]]) rotate([0,90,0]) children();
}

//Vertical-split tongue, two steps like split_ring: a base fused into the
//left side + a tongue crossing split_lip_h to the right, hugging the hull's
//inner face (runs along floor, rear wall and top).
module vsplit_tongue() {
    intersection() {
        union() {
            //fused base
            intersection() {
                difference() {
                    scale(case_scale) mac_hull_solid((wall-0.5)/case_scale);
                    scale(case_scale) mac_hull_solid((wall+split_clr+split_lip_th)/case_scale);
                }
                translate([vsplit_x-6, -300, -1]) cube([6, 600, 400]);
            }
            //tongue
            intersection() {
                difference() {
                    scale(case_scale) mac_hull_solid((wall+split_clr)/case_scale);
                    scale(case_scale) mac_hull_solid((wall+split_clr+split_lip_th)/case_scale);
                }
                translate([vsplit_x-0.5, -300, -1]) cube([split_lip_h+0.5, 600, 400]);
            }
        }
        //only on the back side of the divider, 3 in (like the horizontal ring)
        scale(case_scale) mac_crack_divider_expanded(3/case_scale);
    }
}

//orients children() (a cylinder along +z) along the wall normal (nx, ny)
module along_normal(nx, ny) { rotate(ny!=0 ? [90,0,0] : (nx>0 ? [0,90,0] : [0,-90,0])) children(); }

//Two-step overlap ring: a base FUSED to the lower piece's wall (invades the
//hull by 0.5) + a tongue rising split_lip_h with 0.15 clearance against the
//upper piece's inner face
module split_ring(zc) {
    //fused base
    intersection() {
        difference() {
            scale(case_scale) mac_hull_solid((wall-0.5)/case_scale);
            scale(case_scale) mac_hull_solid((wall+split_clr+split_lip_th)/case_scale);
        }
        translate([-1, -300, zc-6]) cube([250, 600, 6]);
    }
    //tongue
    intersection() {
        difference() {
            scale(case_scale) mac_hull_solid((wall+split_clr)/case_scale);
            scale(case_scale) mac_hull_solid((wall+split_clr+split_lip_th)/case_scale);
        }
        translate([-1, -300, zc-0.5]) cube([250, 600, split_lip_h+0.5]);
    }
}

//THROUGH holes in the back for the M3 screws of the front's flaps
//(countersunk from outside, aimed at the flap's insert)
module side_flap_holes(y, z) {
    for (s=[0,1]) translate([s==0 ? -1 : cw+1, y, z]) rotate([0, s==0 ? 90 : -90, 0]) {
        cylinder(d=3.4, h=5.2);                          //through (wall 0..3 / 244.6..247.6)
        translate([0,0,0.95]) cylinder(d1=6.6, d2=3.4, h=1.7);  //countersink flush with the outer face
    }
}
module top_flap_holes() {
    //same local frame as the upper front's top flaps (top inner face at
    //z local 289.1, outer at 292.1)
    translate([0,-25.4,44.5]) rotate([-5,0,0]) for (x=flap_top_x)
        translate([x, flap_top_y, 288]) {
            cylinder(d=3.4, h=5.5);                      //through
            translate([0,0,2.4])  cylinder(d1=3.4, d2=6.6, h=1.7); //countersink (mouth at 292.1)
            translate([0,0,3.95]) cylinder(d=6.6, h=3);  //head clearance over the face
        }
}

module back_lower() {
    difference() {
        union() {
            intersection() {
                back_half();
                translate([-1, -300, -1]) cube([250, 600, back_split_z+1]);
            }
            //overlap ring (only on the back side of the divider, 3 in)
            intersection() {
                split_ring(back_split_z);
                scale(case_scale) mac_crack_divider_expanded(3/case_scale);
            }
            //horizontal joint bosses (embedded 0.5 in the wall)
            for (u=back_split_joints) translate([u[0]-u[2]*0.5, u[1]-u[3]*0.5, split_boss_z])
                along_normal(u[2], u[3]) cylinder(d=9, h=12.5);
        }
        //notches in the ring for the upper piece's tab arms
        for (u=back_split_joints) {
            if (u[2]!=0) translate([u[2]>0 ? u[0]-1 : u[0]-18, u[1]-10, back_split_z-0.1]) cube([19, 20, 8.8]);
            else         translate([u[0]-10, u[1]-18, back_split_z-0.1]) cube([20, 19, 8.8]);
        }
        //horizontal M3 pilots (from the boss tip toward the wall)
        for (u=back_split_joints) translate([u[0]+u[2]*12.1, u[1]+u[3]*12.1, split_boss_z])
            along_normal(-u[2], -u[3]) translate([0,0,-0.01]) cylinder(d=insert_d, h=9.6);
        //trim of the skirts' front edge (to y=-4): leaves the piece 245.3
        //deep to fit a 250 bed WITH slicer margin. The trimmed edge is
        //covered by the front's overlap ring; the upper back's skirts reach
        //-4.3, so the edge stays practically continuous.
        translate([-1, -350, -1]) cube([250, 346, 300]);
        //screws of the lower front's LOW side flaps
        side_flap_holes(flap_side_y, flap_side_z);
    }
}

module back_upper() {
    difference() {
    union() {
        intersection() {
            back_half();
            translate([-1, -300, back_split_z]) cube([250, 600, 400]);
        }
        //L-tabs: horizontal arm over the ring notch + vertical plate hanging
        //in front of the boss, with an M3 through hole
        difference() {
            union() for (u=back_split_joints) {
                if (u[2]!=0) {
                    translate([u[2]>0 ? u[0]-1 : u[0]-15.8, u[1]-7, back_split_z]) cube([16.8, 14, 4]);
                    translate([u[2]>0 ? u[0]+12.3 : u[0]-15.8, u[1]-7, split_boss_z-8]) cube([3.5, 14, back_split_z+4-(split_boss_z-8)]);
                } else {
                    translate([u[0]-7, u[1]-15.8, back_split_z]) cube([14, 16.8, 4]);
                    translate([u[0]-7, u[1]-15.8, split_boss_z-8]) cube([14, 3.5, back_split_z+4-(split_boss_z-8)]);
                }
            }
            for (u=back_split_joints) translate([u[0]+u[2]*16.5, u[1]+u[3]*16.5, split_boss_z])
                along_normal(-u[2], -u[3]) cylinder(d=3.4, h=5.5);
        }
    }
    //screws of the upper front's flaps: top + high sides
    top_flap_holes();
    side_flap_holes(flap_high_y, flap_high_z);
    }
}

//Left/right halves of the back pieces (vertical split at x=vsplit_x).
//The left carries tongue + bosses; the right the L-tabs.
module back_lower_left() {
    difference() {
        union() {
            intersection() { back_lower(); translate([-10, -300, -10]) cube([vsplit_x+10, 600, 400]); }
            difference() {
                //tongue only up to the horizontal ring's base (z split-6)
                intersection() { vsplit_tongue(); translate([-10, -300, -10]) cube([400, 600, back_split_z-6.5+10]); }
                //no tongue in the PSU zone: it rests on the floor (y>94) and
                //its tail sits 1.5 off the rear wall (z<91)
                translate([vsplit_x-7, 94, -2]) cube([17, 250, 93]);
            }
            at_lower_vjoints() vjoint_boss();
        }
        at_lower_vjoints() { vjoint_notch(); vjoint_pilot(); }
    }
}

module back_lower_right() {
    difference() {
        union() {
            intersection() { back_lower(); translate([vsplit_x, -300, -10]) cube([300, 600, 400]); }
            at_lower_vjoints() vjoint_tab();
        }
        at_lower_vjoints() vjoint_tab_hole();
    }
}

module back_upper_left() {
    difference() {
        union() {
            intersection() { back_upper(); translate([-10, -300, -10]) cube([vsplit_x+10, 600, 400]); }
            //tongue from above the horizontal ring's tongue (z split+8)
            intersection() { vsplit_tongue(); translate([-10, -300, back_split_z+split_lip_h+0.5]) cube([400, 600, 400]); }
            at_upper_vjoints() vjoint_boss();
        }
        at_upper_vjoints() { vjoint_notch(); vjoint_pilot(); }
    }
}

module back_upper_right() {
    difference() {
        union() {
            intersection() { back_upper(); translate([vsplit_x, -300, -10]) cube([300, 600, 400]); }
            at_upper_vjoints() vjoint_tab();
        }
        at_upper_vjoints() vjoint_tab_hole();
    }
}

module front_lower() {
    union() {
        intersection() {
            front_half();
            translate([-1, -300, -1]) cube([250, 600, front_split_z+1]);
        }
        //overlap ring (only on the front side of the divider, 3 in)
        difference() {
            split_ring(front_split_z);
            scale(case_scale) mac_crack_divider_expanded(-3/case_scale);
        }
        //side skirt fill: the lower back is trimmed at y=-4 (to fit a 250
        //bed) but the sloped seam drops to y~-10.8 on the sides, leaving an
        //open wedge up to ~7 wide between z~63 and z~140. The front takes
        //that band up to y=-4.3 (0.3 clearance against the back's trimmed
        //edge). Only in this piece: the whole front half still ends at the
        //seam.
        intersection() {
            scale(case_scale) mac_hull(wall/case_scale);
            scale(case_scale) mac_crack_divider();
            translate([-1, -30, -1]) cube([250, 30-4.3, 141]);
        }
        //SIDE flaps: 3 mm vertical plate against the back wall's inner face
        //(0.2 clearance), a rib fused to the skirt wedge's skin and a d9
        //horizontal boss with M3 insert. Countersunk screw from outside the
        //side. (Slicer: local support under the plate.)
        for (s=[0,1]) {
            xp = s==0 ? wall+0.2 : cw-wall-0.2-3;      //plate face: 3.2 / 241.4
            xn = s==0 ? wall-0.5 : cw-wall-0.3;        //rib embedded 0.5 in the skin
            difference() {
                union() {
                    translate([xp, -5.5, flap_side_z-6]) cube([3, 19.5, 12]);      //plate
                    translate([xn, -7.5, flap_side_z-6]) cube([0.8, 2.7, 12]);     //rib
                    translate([s==0 ? xp : xp-7.1, flap_side_y, flap_side_z])
                        rotate([0, 90, 0]) cylinder(d=9, h=7.1+3);                 //boss (includes the plate)
                }
                translate([s==0 ? xp-0.01 : xp+3.01, flap_side_y, flap_side_z])
                    rotate([0, s==0 ? 90 : -90, 0]) cylinder(d=insert_d, h=6.6);
            }
        }
    }
}

module front_upper() {
    union() {
        intersection() {
            front_half();
            translate([-1, -300, front_split_z]) cube([250, 600, 400]);
        }
        //TOP flaps: 3 mm plate slipping under the back's skin (0.2
        //clearance), a rib fusing it to the front's skin and a d9 boss with
        //M3 insert pointing up. The screw enters countersunk from outside
        //the top through the back's hole. Slicer: in the print orientation
        //the plate overhangs — enable support only there (12x28, two spots).
        translate([0,-25.4,44.5]) rotate([-5,0,0]) for (x=flap_top_x) difference() {
            union() {
                translate([x-6, 2, 285.9]) cube([12, 28, 3]);   //plate (top inner face at 289.1)
                translate([x-6, 2, 288.4]) cube([12, 8, 1.2]);  //fusion rib (0.5 in the plate, 0.5 in the skin)
                translate([x, flap_top_y, 281.9]) cylinder(d=9, h=7); //insert boss
            }
            translate([x, flap_top_y, 288.9-6.6]) cylinder(d=insert_d, h=6.7);
        }
        //HIGH side flaps (warp bites hardest on this piece): same scheme as
        //the lower front's, plate at z=166..178 (starts 20 above the z=146
        //split). The seam here sits at y~-1.3; the rib fuses to the front's
        //skin at y=-4..-2 and the back's hole goes at (y=8, z=172).
        //(Slicer: local support under the plate.)
        for (s=[0,1]) {
            xp = s==0 ? wall+0.2 : cw-wall-0.2-3;      //plate face: 3.2 / 241.4
            xn = s==0 ? wall-0.5 : cw-wall-0.4;        //rib embedded 0.5 in the skin
            difference() {
                union() {
                    translate([xp, -3, flap_high_z-6]) cube([3, 17, 12]);        //plate
                    translate([xn, -4, flap_high_z-6]) cube([0.9, 2.2, 12]);     //rib
                    translate([s==0 ? xp : xp-7.1, flap_high_y, flap_high_z])
                        rotate([0, 90, 0]) cylinder(d=9, h=7.1+3);               //boss (includes the plate)
                }
                translate([s==0 ? xp-0.01 : xp+3.01, flap_high_y, flap_high_z])
                    rotate([0, s==0 ? 90 : -90, 0]) cylinder(d=insert_d, h=6.6);
            }
        }
    }
}


// ========================= Original shell (unchanged) =========================

//generates square hull you can cut the inner overlap out of
module mac_overlap_inner_edge(overlapth) {
    difference() {
        translate([overlapth, -200, overlapth]) cube([247.6-overlapth*2,500,334-overlapth*2]);
        translate([overlapth*2, -200, overlapth*2]) cube([247.6-overlapth*4,500,334-overlapth*4]);
    }
}


module mac_hull(wall) {
    difference() {
        mac_hull_solid(0);
        union() {
            mac_hull_solid(wall);
             translate([0, -25.4, 44.5]) rotate([-5,0, 0]) union() {            //Cutout the screen in the back of the frontplate as well
                translate([247.6/2, 0, scr_cz]) rotate([90, 0, 0]) scale(scr_scale) mac_screen_cutout(wall/scr_scale);
                //Cutout fdd slot
                translate([129, -5.2, 62.8]) rotate([90,0,0]) fdd_cutout();
                //Cutout slim DVD slot (local z derived from the desired world z)
                translate([odd_slot_cx, -5.2, (odd_slot_z - 44.5 - 5.2*sin(5))/cos(5)]) rotate([90,0,0]) dvd_cutout();
                //HDD activity LED hole (same height as the floppy slot)
                translate([led_x, -5.2, 62.8]) rotate([90,0,0]) {
                    translate([0,0,-8]) cylinder(d=5.2, h=16);      //lens pass
                    translate([0,0,-12]) cylinder(d=6.4, h=9.5);    //rear recess for the flange
                }
             }
             //Speaker grilles in the recessed bottom band (vertical wall, no slope)
             for (x=spk_x) translate([x, 0, spk_z]) rotate([-90,0,0]) speaker_grille();
        }
    }
}


module mac_hull_solid(shrink) {
    rnd=4.5; //rounding of outer case
    r=(rnd<shrink)?0.01:rnd-shrink;
    difference() {
        union() {
            difference() {
                //Cube that makes up bottom and back of mac
                translate([shrink, shrink, shrink]) roundedcube(247.6-shrink*2, 241.3-shrink*2, 310-shrink*2, r);
                //cut off bit that sticks through front panel
                translate([-500, -1000+150, 150]) cube([1000, 1000, 1000]);
            }
            //add in rotated front bulk
            translate([0, -25.4, 44.5]) rotate([-5,0, 0]) mac_front_bulk(4.5, 2, shrink);
        }
        union() {
            //Cut off diagonal bit in top-back of case
            translate([-1, 241-shrink, 266]) rotate([30, 0, 0]) cube([300, 300, 300], false);
            //Handle handle
            translate([247.6/2,105-shrink,280]) rotate([90+5,0,180]) mac_handle_hole(shrink);
        }
    }
}

module mac_front_bulk(or, ir, shrink) {
    fp_th=5.2; //thickness of front plate)
    fp_extra=(shrink<fp_th)?0:shrink-fp_th;
    or=(or<shrink)?0.01:or-shrink;
    ir=(ir<shrink)?0.01:ir-shrink;
    difference() {
        union() {
            difference() {
                translate([0+shrink, -20+shrink, 0+shrink]) roundedcube(247.6-shrink*2, 233.9+20-shrink*2, 292.1-shrink*2, or);
                union() {
                    //Cut off front where faceplate goes
                    translate([-500, -1000+fp_extra, -500]) cube([1000, 1000, 1000]);
                }
            }
            if (shrink==0) {
                mac_front_faceplate(or, ir);
            } else {
                translate([247.6/2, 50, scr_cz])  rotate([90,0,0])scale([scr_scale, scr_scale, 100]) mac_screen_cutout_back();
            }
        }
    }
}


//negative: handle hole (as in: hole for the handle to carry the mac)
//needs to be bigger when shrink increases
module mac_handle_hole(shrink) {
    r=6.4;
    difference() {
        union() {
            hull() {
                translate([-97/2+r-shrink, r-shrink ,0]) cylinder(r=r, 200);
                translate([97/2-r+shrink, r-shrink ,0]) cylinder(r=r, 200);
                translate([-103/2+r-shrink, 46-r ,0]) cylinder(r=r, 200);
                translate([103/2-r+shrink, 46-r ,0]) cylinder(r=r, 200);
            }
            if (shrink==0) {
                difference() {
                    translate([-103/2-r, 46-r, 0]) cube([103+2*r, 200, 200]);
                    union() {
                        translate([-103/2-r, 46-r ,0]) cylinder(r=r, 200);
                        translate([103/2+r, 46-r ,0]) cylinder(r=r, 2200);
                    }
                }
            }
        }
        if (shrink==0) {
            //thing where you hold the mac
            translate([-100, 35, 0]) rotate([90,0,90]) hull() {
                cylinder(d=14.6, 200);
                translate([0, 35, 0]) cylinder(d=14.6, 200);
            }
        }
    }
}

//crack divider, but moved up/right to get the overlap edge
module mac_crack_divider_expanded(ex) {
    translate([0,ex,ex]) mac_crack_divider();
}

module mac_crack_divider() {
    difference() {
        rotate([-5, 0, 0]) translate([0, -15, 65]) union() {
            hull () {
                translate([0,0,0]) rotate([0,90,0]) cylinder(r=1.5, 1000);
                translate([0,0,1000]) rotate([0,90,0]) cylinder(r=1.5, 1000);
                translate([0,1000,0]) rotate([0,90,0]) cylinder(r=1.5, 1000);
            }
            translate([-500,20,-500]) cube([1000,1000,1000]);
        }
        translate([0, 15, 0]) hull() {
            translate([-500, 0, -200]) rotate([0,90,0]) cylinder(r=4.5, 1000);
            translate([-500, 0, 58.4]) rotate([0,90,0]) cylinder(r=4.5, 1000);
            translate([-500, -200, 58.4]) rotate([0,90,0]) cylinder(r=4.5, 1000);
        }
    }
}


module mac_front_faceplate(or=4.5, ir=2) {
    mac_front_faceplate_outer(or, ir);
}

module mac_front_faceplate_outer(or, ir) {
    hull() {
        //connection to bulk of case
        translate([0+or, 0, 0+or]) rotate([90,0,0]) cylinder(1, or);
        translate([0+or, 0, 292.1-or]) rotate([90,0,0]) cylinder(1, or);
        translate([247.6-or, 0, 0+or]) rotate([90,0,0]) cylinder(1, or);
        translate([247.6-or, 0, 292.1-or]) rotate([90,0,0]) cylinder(1, or);
        //face of mac
        translate([0+3.4+ir, 1-5.2, 0+8+ir]) rotate([90,0,0]) cylinder(1, ir);
        translate([0+3.4+ir, 1-5.2, 292.1-10-ir]) rotate([90,0,0]) cylinder(1, ir);
        translate([247.6-3.4-ir, 1-5.2, 0+8+ir]) rotate([90,0,0]) cylinder(1, ir);
        translate([247.6-3.4-ir, 1-5.2, 292.1-10-ir]) rotate([90,0,0]) cylinder(1, ir);
    }
}


//WARNING: Everything inhere is adjusted using the wrong scale because I didn't pay attention. I'm too lazy to fix it all up, so if you use it scale(1.69) it first.
module mac_screen_cutout(depth) {
    or=2.5;
    ow=123-or*2;
    oh=94-or*2;
    hull() {
        //back cutout; fakes CRT curves
        translate([0,0,2.5-depth]) mac_screen_cutout_back();
        //very front of bezel
        translate([ow/2, oh/2, 5.2-1]) cylinder(1, r=or);
        translate([ow/2, -oh/2, 5.2-1]) cylinder(1, r=or);
        translate([-ow/2, oh/2, 5.2-1]) cylinder(1, r=or);
        translate([-ow/2, -oh/2, 5.2-1]) cylinder(1, r=or);
    }
}

//Same scale(1.69) is required here.
module mac_screen_cutout_back() {
    intersection() {
        intersection() {
            union() {
                translate([0, 23-(46-42), 0]) scale([246, 46, 1]) cylinder(10, d=1);
                translate([0, -23+(46-42), 0]) scale([246, 46, 1]) cylinder(10, d=1);
            }
            union() {
                translate([-56/2, 0, 0]) scale([56, 300, 1]) cylinder(1, d=1);
                translate([0, 0, 0]) scale([56, 300, 1]) cylinder(1, d=1);
                translate([56/2, 0, 0]) scale([56, 300, 1]) cylinder(1, d=1);
            }
        }

        union() {
            dr=6.2;
            w=111-dr*2;
            h=82-dr*2;
            translate([w/2, h/2, 0]) cylinder(10, r=6.2);
            translate([w/2, -h/2, 0]) cylinder(10, r=6.2);
            translate([-w/2, h/2, 0]) cylinder(10, r=6.2);
            translate([-w/2, -h/2, 0]) cylinder(10, r=6.2);
            translate([-w/2, -500, 0]) cube([w, 1000, 10]);
            translate([-500, -h/2, 0]) cube([1000, h, 10]);
        }
    }
}


//Negative space of FDD.
//Macintosh Classic style: ONE long even mouth (without the wide right notch
//of the original model) + separate eject hole below-right.
module fdd_cutout() {
        union() {
            fdd_cutout_inner_slot();
            //long, even bevel
            hull() {
                translate([0,0,4]) fdd_cutout_inner_slot();
                translate([-10.4, -11.5/2, 0]) flatroundedcube(112.3, 11.5, 10, 1);
            }
            //PC floppy eject button hole, with its own mini-bevel
            translate([fdd_eject[0], fdd_eject[1], 0]) {
                translate([-fdd_eject[2]/2, -fdd_eject[3]/2, -50])
                    flatroundedcube(fdd_eject[2], fdd_eject[3], 100, 2.5);
                hull() {
                    translate([-fdd_eject[2]/2, -fdd_eject[3]/2, 4]) flatroundedcube(fdd_eject[2], fdd_eject[3], 6, 2.5);
                    translate([-fdd_eject[2]/2-2, -fdd_eject[3]/2-2, 0]) flatroundedcube(fdd_eject[2]+4, fdd_eject[3]+4, 10, 3);
                }
            }
        }
}

//Negative space of the slim DVD slot, centered on the origin. SAME funnel
//geometry as the floppy: deep mouth (-12..5), funnel by hull of the mouth
//shifted +4 against the bevel rectangle, identical margins (10.4 left, 8.5
//right, 3.15 top/bottom, r=1 rounding).
module dvd_slot_inner() {
    translate([-odd_slot_w/2, -odd_slot_h/2, -12]) cube([odd_slot_w, odd_slot_h, 17]);
}

module dvd_cutout() {
    dvd_slot_inner();
    hull() {
        translate([0,0,4]) dvd_slot_inner();
        translate([-odd_slot_w/2-10.4, -odd_bezel_h/2, 0]) flatroundedcube(odd_slot_w+18.9, odd_bezel_h, 10, 1);
    }
}


//Speaker grille (lying oval): d2.2 holes inside a ~25x16 horizontal oval,
//centered on the origin, piercing along its z
module speaker_grille() {
    for (dx=[-12:4:12], dy=[-7:3.5:7])
        if (pow(dx/12.5,2)+pow(dy/8,2) <= 1)
            translate([dx, dy, -50]) cylinder(d=2.2, h=100);
}

module fdd_cutout_inner_slot() {
    //deepened from the original (which left a 0.2 skin: the mini mac's slot
    //was decorative; a real diskette passes through this one)
    translate([0, -2.6, -12]) cube([93.4, 5.2, 17]);
}

//Cube with 4 edges rounded; front/back are flat
module flatroundedcube(x,y,z,r) {
    hull(){
        translate([0+r, 0+r, 0]) cylinder(z, r=r);
        translate([0+r, y-r, 0]) cylinder(z, r=r);
        translate([x-r, 0+r, 0]) cylinder(z, r=r);
        translate([x-r, y-r, 0]) cylinder(z, r=r);
    }
}


//Cube with all edges rounded
module roundedcube(x, y, z, r) {
    hull() {
        translate([0+r,0+r,0+r]) sphere(r);
        translate([x-r,0+r,0+r]) sphere(r);
        translate([0+r,y-r,0+r]) sphere(r);
        translate([x-r,y-r,0+r]) sphere(r);
        translate([0+r,0+r,z-r]) sphere(r);
        translate([x-r,0+r,z-r]) sphere(r);
        translate([0+r,y-r,z-r]) sphere(r);
        translate([x-r,y-r,z-r]) sphere(r);
    }
}
