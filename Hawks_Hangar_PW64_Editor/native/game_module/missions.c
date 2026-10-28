/*
 * Hawk's Hangar game module, part "missions": up to 128 missions and up to five tests per class (US version).
 *
 *   - The lower data area (DataExtension.cs, 0x801F1000-0x801F5000) is copied from the ROM at the start
 *     (module_ext_load, called by module_init): the mission list taskInit loads, the Birdman missions with points, the
 *     photo missions, the result tables and the class table.
 *   - Photo missions ("Shutter Bug"): the game knows them by class and test (Hang Glider Class A test 1, Class B test 2,
 *     Pilot test 3). testMenuShutterBug, snapPhoto's "Photo" message and the album's points ask the module, which looks the
 *     mission up in the table of the lower data area, so they stay with their mission when tests move.
 *   - The photo subjects of the missions (subjects.c) are copied from the ROM into the high data area at the start too.
 *   - Photos in the saved game: a photo stores its test and class in 2 bits each (snap.c), too few for a fifth test (and
 *     for classes 5-8). The game's 1032 bits of the album are followed by 12 more: per photo the third bit of its test and
 *     of its class (module_photo_pack / module_photo_unpack around func_8033E860 and func_8033F050).
 *   - The total screen (total_results.c) has room for four tests: the module gives it five number buffers and moves the
 *     header of the medal text one line up for five tests.
 *
 * Soft-float like module.c. Function and variable names from the decompilation
 * (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).
 */

typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef signed short s16;
typedef signed int s32;

s32 module_tot_test_count(s32 cls, s32 vehicle);

#define GAME_STATE       (*(u8 *volatile *)0x80362690)                   /* D_80362690 */
#define GAME_MEDIA_COPY  ((void (*)(void *, u32, u32))0x8022A760)       /* _uvMediaCopy(ram, rom, size) */
#define GAME_WRITEBACK   ((void (*)(void))0x8023AE70)                    /* osWritebackDCacheAll */

#define DATA_AREA        0x801F5000u
#define DATA_MAGIC       0x48484D44u /* 'HHMD' */
#define DATA_EXT_SIZE    (*(volatile u32 *)(DATA_AREA + 0x24))
#define EXT_AREA         0x801F1000u
#define EXT_AREA_END     0x801F5000u
#define EXT_MAGIC        0x48484D58u /* 'HHMX' */
#define EXT_ROM_OFFSET   0x9000u
#define EXT_OK           (*(volatile u32 *)EXT_AREA == EXT_MAGIC)
#define EXT_PHOTO(v, c)  (((volatile u8 *)(EXT_AREA + 0x10))[(v) * 8 + (c)])

/* The player's flight data (Unk80362690_Unk0): +2 vehicle, +4 class, +6 test. */
static u8 *player(void) {
    u8 *state = GAME_STATE;
    return state != 0 ? state + 0x0C + *(s32 *)(state + 0x9C) * 0x8C : 0;
}

/* ------------------------------------------------------------------ the lower data area */

/* After the data area: its lower part, from the ROM 0x9000 bytes behind the data area's image. */
#define PHOTO_TABLE      0x807F1000u /* HighDataArea.PhotoSubjectTable */
#define PHOTO_TABLE_ROOM 0x3000u
#define PHOTO_ROM_OFFSET 0xD000u     /* PhotoSubjects.RomOffset */
#define DATA_PHOTO_SIZE  (*(volatile u32 *)(DATA_AREA + 0x34))

void module_ext_load(u32 dataRom) {
    u32 size;
    *(volatile u32 *)EXT_AREA = 0;
    *(volatile u32 *)PHOTO_TABLE = 0;
    if (*(volatile u32 *)DATA_AREA != DATA_MAGIC || *(volatile u32 *)(DATA_AREA + 4) < 2) {
        return;
    }
    size = DATA_EXT_SIZE;
    if (size == 0 || size > EXT_AREA_END - EXT_AREA || (size & 7) != 0) {
        return;
    }
    GAME_WRITEBACK();
    GAME_MEDIA_COPY((void *)EXT_AREA, dataRom + EXT_ROM_OFFSET, size);

    /* The photo subjects of the missions (subjects.c), from the ROM 0xD000 bytes behind the data area's image into the
       high data area. */
    size = DATA_PHOTO_SIZE;
    if (size != 0 && size <= PHOTO_TABLE_ROOM && (size & 7) == 0) {
        GAME_WRITEBACK();
        GAME_MEDIA_COPY((void *)PHOTO_TABLE, dataRom + PHOTO_ROM_OFFSET, size);
    }
}

/* ------------------------------------------------------------------ photo missions */

static int photo_mission(u32 veh, u32 cls, u32 test) {
    return EXT_OK && veh < 7 && cls < 8 && test < 5 && ((EXT_PHOTO(veh, cls) >> test) & 1);
}

s32 module_has_photo_subjects(u32 veh, u32 cls, u32 test); /* subjects.c */

/* For subjects.c. */
s32 module_is_photo_mission(u32 veh, u32 cls, u32 test) {
    return photo_mission(veh, cls, test);
}



/* testMenuShutterBug (its entry jumps here): the Hang Glider's test shown on the test screen (sCurTestIdx) is a photo
   mission. */
#define TEST_MENU_TEST (*(volatile s32 *)0x803509B4) /* sCurTestIdx */
s32 module_own_samples(void); /* subjects.c */

/* A test with sample photos: a Hang Glider photo mission (the island's or its own sample photos), or a Birdman photo
   mission with photo subjects of its own. */
static s32 has_samples(u32 veh, u32 cls, u32 test) {
    return photo_mission(veh, cls, test) && (veh == 0 || (veh == 6 && module_has_photo_subjects(veh, cls, test)));
}

s32 module_shutter_bug(void) {
    u8 *p = player();
    return p != 0 && has_samples(*(u16 *)(p + 2), *(u16 *)(p + 4), (u32)TEST_MENU_TEST);
}

/* The pause menu of a flight (func_8032CF28): the menu with the sample photos for the flight's test (the game: only the
   Hang Glider's Class A test 1, Class B test 2 and Pilot test 3). */
s32 module_pause_photo(u8 *flight) {
    return has_samples(*(u16 *)(flight + 2), *(u16 *)(flight + 4), *(u16 *)(flight + 6));
}

/* A photo (Unk80373060: class +0x30, test +0x32, vehicle +0x34, u16 each): snapPhoto shows "Photo" for a subject in a
   photo mission (the game does not check the vehicle). */
s32 module_photo_record(u8 *photo) {
    return photo_mission(*(u16 *)(photo + 0x34), *(u16 *)(photo + 0x30), *(u16 *)(photo + 0x32));
}

/* The album (func_8033ADD4) shows the points of a photo taken in a photo mission; the game never for the Birdman, the
   module for a Birdman photo mission with photo subjects of its own (the game's Birdman photo missions give no points). */
s32 module_photo_album(u8 *photo) {
    u32 veh = *(u16 *)(photo + 0x34);
    return (veh != 6 || module_has_photo_subjects(veh, *(u16 *)(photo + 0x30), *(u16 *)(photo + 0x32)))
        && module_photo_record(photo);
}

/* ------------------------------------------------------------------ photos in the saved game */

#define GAME_PHOTO_PACK   ((void (*)(u8 **))0x8033E860) /* func_8033E860: the album into a bit stream (*out = D_80373300) */
#define GAME_PHOTO_UNPACK ((void (*)(u8 *))0x8033F050)  /* func_8033F050: the bit stream into the album */
#define PHOTOS            ((u8 *)0x80373060)            /* D_80373060[6], 0x70 bytes each */
#define PHOTO_COUNT       (*(volatile s32 *)0x80350528) /* D_80350528 */
#define PHOTO_MAX         6
#define PHOTO_SIZE        0x70
#define PHOTO_BITS        172
#define PHOTO_EXTRA       (PHOTO_MAX * PHOTO_BITS) /* 1032: the 12 bits behind the album */

static u32 get_bit(const u8 *data, u32 n) {
    return (data[n / 8] >> (n % 8)) & 1;
}

static void set_bit(u8 *data, u32 n, u32 value) {
    if (value) {
        data[n / 8] |= (u8)(1 << (n % 8));
    } else {
        data[n / 8] &= (u8)~(1 << (n % 8));
    }
}

/* saveFileWrite's call of func_8033E860: then per photo the third bit of its test and of its class behind the album
   (the stream D_80373300 has room: 144 bytes). A slot without a photo gets 0, as the game packs it. */
void module_photo_pack(u8 **out) {
    u8 *bits;
    u32 i;
    GAME_PHOTO_PACK(out);
    bits = *out;
    for (i = 0; i < PHOTO_MAX; i++) {
        u8 *photo = PHOTOS + i * PHOTO_SIZE;
        int used = (s32)i < PHOTO_COUNT && photo[0x43] != 0;
        set_bit(bits, PHOTO_EXTRA + i * 2, used ? (*(u16 *)(photo + 0x32) >> 2) & 1 : 0);
        set_bit(bits, PHOTO_EXTRA + i * 2 + 1, used ? (*(u16 *)(photo + 0x30) >> 2) & 1 : 0);
    }
}

/* saveFileLoad's call of func_8033F050 (its buffer holds 1044 bits): the game takes the photos whose first object is not
   0 (bits 72-75 of the record) one after the other; their third bits are added to test and class. */
void module_photo_unpack(u8 *data) {
    u32 i, k = 0;
    GAME_PHOTO_UNPACK(data);
    for (i = 0; i < PHOTO_MAX; i++) {
        u32 first = i * PHOTO_BITS + 72;
        if (get_bit(data, first) | get_bit(data, first + 1) | get_bit(data, first + 2) | get_bit(data, first + 3)) {
            u8 *photo = PHOTOS + k * PHOTO_SIZE;
            if ((s32)k < PHOTO_COUNT) {
                *(u16 *)(photo + 0x32) |= (u16)(get_bit(data, PHOTO_EXTRA + i * 2) << 2);
                *(u16 *)(photo + 0x30) |= (u16)(get_bit(data, PHOTO_EXTRA + i * 2 + 1) << 2);
            }
            k++;
        }
    }
}

/* ------------------------------------------------------------------ the total screen */

/* The number buffers of the tests (sTestPtsStr0 ... 3 of the game and a fifth one) and their "pt."/"pts." texts. */
static s16 sTotalPoints4[8];
s16 *module_tot_points[5] = { (s16 *)0x8037ACE0, (s16 *)0x8037ACF0, (s16 *)0x8037AD00, (s16 *)0x8037AD10, sTotalPoints4 };
s16 *module_tot_units[5];

/* totResultDrawTally: the first line of the medal text at y 180, for five tests one line (16) higher, above the first
   test (the tests are drawn upwards from y 100 + 16 per test). */
u32 module_tot_header_y(void) {
    u8 *p = player();
    return p != 0 && module_tot_test_count(*(u16 *)(p + 4), *(u16 *)(p + 2)) >= 5 ? 196 : 180;
}

/* Called instead of "lui s0, 0x8038; lw s0, sMedalName": s0 = sMedalName, s1 = the header's y (the next instruction,
   "li s1, 180", is a nop). */
__asm__(".section .text\n"
        ".set push\n"
        ".set noreorder\n"
        ".globl module_tot_header\n"
        "module_tot_header:\n"
        "    addiu $sp, $sp, -24\n"
        "    sw $ra, 20($sp)\n"
        "    jal module_tot_header_y\n"
        "    nop\n"
        "    move $s1, $v0\n"
        "    lui $s0, 0x8038\n"
        "    lw $s0, -0x52E4($s0)\n"
        "    lw $ra, 20($sp)\n"
        "    jr $ra\n"
        "    addiu $sp, $sp, 24\n"
        ".set pop\n");
