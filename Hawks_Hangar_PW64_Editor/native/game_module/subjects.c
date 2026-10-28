/*
 * Hawk's Hangar game module, part "photo subjects" (stage 8).
 *
 * A mission can have up to ten photo subjects of its own (the editor's Missions tab): each one an object type of the
 * game - the six main subjects (shuttle, ferry, Missi the lake monster, whale, fountain, flame) or the moving objects the game only
 * records in a photo (whale pod, planes, glider toys, boats) - optionally one single object of that type, its points,
 * an optional direction the camera must look into, and a curve of the distance between camera and subject (a factor
 * in percent). The editor writes these missions into the photo table (PhotoSubjects.cs), which module_ext_load copies
 * from the ROM to PHOTO_TABLE at the start.
 *
 *   - snapPhoto (func_80338A14, its two calls in the shutters of the Hang Glider and the Birdman) goes through
 *     module_snap_photo. A mission without own subjects uses the game's function, unchanged. With own subjects:
 *       - the photo's subject is the nearest of the mission's subjects that is in front of the camera and within
 *         -50 ... 370 x -50 ... 290 pixels (the game's test); if none is, the nearest of the six main subjects as the
 *         game takes it (no points), so the album can draw the photo as it was;
 *       - points = the subject's points x centring (as the game: full in the middle, 0 outside the middle half of the
 *         picture) x curve(distance); with a direction only within direction +- tolerance / 2 (the game's PHTS mode 1);
 *       - the best photo of each subject counts (the game: of each object type); the photo keeps its subject in the
 *         byte 0x37 (subject number + 1);
 *       - the other moving objects in the picture are recorded as by the game (func_803386A4), so the album draws them
 *         at their place.
 *   - The radar marks of the main subjects (whaleLoad, fountainLoad, missiLoadLStates, ferryLoad, shuttleLoad,
 *     oilPlantLoad): the game shows them only in the Hang Glider's photo missions Class A test 1, Class B test 2 and
 *     Pilot test 3. Their call of taskGetClsVehTest goes through module_radar_<subject>, which answers with the first of
 *     those tests when the current mission is a photo mission that photographs this subject (with own subjects: one of
 *     them; without: a Hang Glider photo mission, as the game), otherwise with no vehicle.
 *
 * Built with the FPU (-mhard-float); only integers and pointers cross into module.c and missions.c. Function and
 * variable names from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).
 */

typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef signed short s16;
typedef signed int s32;
typedef float f32;
typedef f32 Mtx4F[4][4];
typedef struct {
    f32 x, y, z;
} Vec3F;

/* ------------------------------------------------------------------ missions.c */

s32 module_photo_record(u8 *photo);
s32 module_is_photo_mission(u32 veh, u32 cls, u32 test);

/* ------------------------------------------------------------------ module.c */

const u16 *module_subject_name(s32 kind);

/* ------------------------------------------------------------------ game addresses (US) */

#define GAME_STATE          (*(u8 *volatile *)0x80362690)                     /* D_80362690 */
#define GAME_SNAP_PHOTO     ((void (*)(void))0x80338A14)                       /* func_80338A14 (snapPhoto) */
#define GAME_CAMERA_POSE    ((void (*)(Mtx4F *, Vec3F *, Vec3F *))0x8034AF94)  /* func_8034AF94 */
#define GAME_CLS_VEH_TEST   ((void (*)(u16 *, u16 *, u16 *))0x8034633C)        /* taskGetClsVehTest */
#define GAME_IN_FRONT       ((s32(*)(Vec3F, Vec3F, Mtx4F *))0x80338964)        /* func_80338964 */
#define GAME_PROJECT        ((void (*)(s32, Vec3F, Vec3F *))0x8034B354)        /* func_8034B354 */
#define GAME_DISTANCE       ((f32(*)(Vec3F, Vec3F))0x8034AF48)                 /* func_8034AF48 */
#define GAME_DIFFERENCE     ((void (*)(Vec3F, Vec3F, Vec3F *))0x8034B268)      /* func_8034B268 */
#define GAME_LENGTH         ((f32(*)(Vec3F *))0x8034B2B0)                      /* func_8034B2B0 */
#define GAME_ANGLES         ((void (*)(Vec3F, Vec3F *))0x8034A9E8)             /* func_8034A9E8 */
#define GAME_CURVE          ((f32(*)(const void *, f32))0x80313F08)            /* func_80313F08 */
#define GAME_SQRT           ((f32(*)(f32))0x80229EA0)                          /* uvSqrtF */
#define GAME_EXTRAS         ((void (*)(s32, s32 *, s32 *, f32 *, f32 *, s32 *))0x803386A4) /* func_803386A4 */
#define GAME_HUD_TEXT       ((void (*)(s32, f32, f32))0x8031D8E0)              /* hudText_8031D8E0 */
#define GAME_CAMERA_STATE   ((void (*)(s32))0x8031DF68)                        /* hudSetCameraState */
#define GAME_SFX            ((void (*)(s32))0x8033F7F8)                        /* sndPlaySfx */
#define GAME_PRINTF         ((void (*)(const char *, ...))0x8022ED14)          /* _uvDebugPrintf */
#define GAME_SIN            ((f32(*)(f32))0x80229EC0)                          /* uvSinF */
#define GAME_COS            ((f32(*)(f32))0x8022A080)                          /* uvCosF */
#define GAME_EXAMPLES_VIEW  ((s32(*)(s32, u8 *, s32 *))0x8033DDD8)            /* func_8033DDD8 */
#define GAME_TEXT           ((const u16 *(*)(s32))0x80342198)                 /* textGetDataByIdx */

/* The game's sample photos per island (snapInit): Crescent (map 3), Little States (5), Ever-Frost (10). */
#define GAME_EXAMPLES_CRESCENT ((u8 *)0x80373630) /* D_80373630 */
#define GAME_EXAMPLES_LSTATES  ((u8 *)0x803738D0) /* D_803738D0 */
#define GAME_EXAMPLES_EFROST   ((u8 *)0x80373B70) /* D_80373B70 */
#define GAME_COUNT_CRESCENT    (*(volatile s32 *)0x80350538) /* D_80350538 */
#define GAME_COUNT_LSTATES     (*(volatile s32 *)0x8035053C) /* D_8035053C */
#define GAME_COUNT_EFROST      (*(volatile s32 *)0x80350540) /* D_80350540 */
#define SAMPLE_PHOTOS          ((u8 *)0x807F0460) /* HighDataArea.SamplePhotoTable */
#define CAPTION_OWN            0x7F00            /* textId of a module caption: 0x7F00 + type */
#define GAME_SNAP_VIEW         ((s32(*)(s32))0x8033E3A8)  /* func_8033E3A8 */
#define TEST_MENU_TEST         (*(volatile s32 *)0x803509B4) /* sCurTestIdx */

#define GAME_WHALE_ACTIVE   ((s32(*)(void))0x8034B820)          /* whaleIsActive */
#define GAME_WHALE_POS      ((void (*)(Vec3F *))0x8034C16C)     /* whaleGetPos */
#define GAME_WHALE_TIME     ((f32(*)(void))0x8034C154)          /* whaleGetInterval */
#define GAME_MISSI_ACTIVE   ((s32(*)(void))0x80314B50)          /* missiIsActive */
#define GAME_MISSI_POS      ((void (*)(Vec3F *))0x803153AC)     /* missiGetPos */
#define GAME_MISSI_TIME     ((f32(*)(void))0x80315394)          /* missiGetInterval */
#define GAME_SHUTTLE_ACTIVE ((s32(*)(void))0x80334CC0)          /* shuttle_80334CC0 */
#define GAME_SHUTTLE_POS    ((void (*)(Vec3F *))0x80335F24)     /* shuttle_80335F24 */
#define GAME_SHUTTLE_TIME   ((f32(*)(void))0x80335EE4)          /* shuttle_80335EE4 */
#define GAME_FERRY_ACTIVE   ((s32(*)(void))0x802E77A0)          /* ferryIsActive */
#define GAME_FERRY_POS      ((void (*)(Vec3F *))0x802E7CB8)     /* ferryGetPos */
#define GAME_FERRY_TIME     ((f32(*)(void))0x802E7CA0)          /* ferryGetInterval */
#define GAME_FLAME_ACTIVE   ((s32(*)(void))0x8031555C)          /* oilPlantInProximity */
#define GAME_FLAME_POS      ((void (*)(Vec3F *))0x803159F4)     /* oilPlantGetPos */
#define GAME_FOUNTAIN_ACTIVE ((s32(*)(void))0x802EADA0)         /* fountainIsActive */
#define GAME_FOUNTAIN_POS   ((void (*)(Vec3F *))0x802EB300)     /* fountainGetPos */
#define GAME_FOUNTAIN_TIME  ((f32(*)(void))0x802EB2E8)          /* fountainGetInterval */
typedef void (*ObjectData)(s32, s32 *, f32 *, Vec3F *);
#define GAME_POD_DATA       ((ObjectData)0x8034CC10)            /* whalePodGetObjState */
#define GAME_PLANE_DATA     ((ObjectData)0x80320848)            /* planesGetObjData */
#define GAME_GLIDER_DATA    ((ObjectData)0x802F205C)            /* gliderToyGetObjData */
#define GAME_BOAT_DATA      ((ObjectData)0x802D2124)            /* boatsGetObjData */
typedef void (*ObjectTime)(s32, f32);
#define GAME_POD_TIME       ((ObjectTime)0x8034CCC4)            /* whalePodSetInterval */
#define GAME_PLANE_TIME     ((ObjectTime)0x80320900)            /* planesUpdateInterval */
#define GAME_GLIDER_TIME    ((ObjectTime)0x802F2110)            /* gliderToySetInterval */
#define GAME_BOAT_TIME      ((ObjectTime)0x802D21D8)            /* boatsUpdateInterval */
#define POD_F32(i, offset)  (*(volatile f32 *)(0x8037F450 + (i) * 0x24 + (offset))) /* gWhalePod[i] */
#define POD_DEPTH(i)        POD_F32(i, 0x20) /* .depth: 0 surfaced ... -25 */
#define GAME_ADD_WAYPOINT   ((s32(*)(f32, f32, f32))0x8031A6C8)  /* hudAddWaypoint */
#define GAME_MOVE_WAYPOINT  ((void (*)(s32, f32, f32, f32))0x8031A794) /* hudMoveWaypoint */
#define GAME_TASK_PHTS      ((s32(*)(void **))0x80345C3C)        /* taskGetPHTS */
#define GAME_MAP_MAIN       ((void (*)(void *, s32))0x8030FE80)  /* map3dMain */
#define GAME_MAP_ITEM       ((void (*)(s32, f32, f32, f32))0x80312684) /* map3dAddItem */
#define GAME_MAP_SCALE      (*(volatile f32 *)0x8036AA50)        /* sMapGlobalScale */

#define FLIGHT_COUNT        (*(volatile s32 *)0x8035052C) /* D_8035052C */
#define SHUTTER_FULL        (*(volatile s32 *)0x80350534) /* D_80350534 */
#define FLIGHT_PHOTOS       ((u8 *)0x807F0000)            /* HighDataArea.FlightPhotoTable */
#define FLIGHT_PHOTO_MAX    10

/* The photo table (PhotoSubjects.cs): 'HHPS', count, then per mission vehicle, class, test, number of subjects and the
   subjects (SUBJECT_SIZE bytes each). */
#define PHOTO_TABLE         ((const u8 *)0x807F1000)
#define PHOTO_TABLE_SIZE    0x3000
#define PHOTO_TABLE_MAGIC   0x48485053u
#define SUBJECT_SIZE        0x74
#define SUBJECTS_MAX        10

/* Subject types (unk43 of a photo). */
#define KIND_SHUTTLE   1
#define KIND_FERRY     2
#define KIND_MISSILE   3 /* Missi, the lake monster of Little States Island */
#define KIND_WHALE     4
#define KIND_FOUNTAIN  5
#define KIND_FLAME     6
#define KIND_POD       7
#define KIND_PLANE     8
#define KIND_GLIDER    9
#define KIND_BOAT      10
#define KIND_NOTHING   11

#define HUD_CAM_RENDER_SHUTTER 0x40000000
#define TEXT_PHOTO     0x13C /* "Photo OK" */
#define SFX_SNAP       0x3A  /* SFX_UI_CAMERA_SNAP */
#define NO_OBJECT      0xFFFF

/* Photo (Unk80373060). */
#define PH_POS       0x00
#define PH_ANGLE     0x0C
#define PH_CLIP_X0   0x18
#define PH_CLIP_X1   0x1C
#define PH_CLIP_Y1   0x20
#define PH_CLIP_Y0   0x24
#define PH_NEAR      0x28
#define PH_FAR       0x2C
#define PH_CLASS     0x30
#define PH_TEST      0x32
#define PH_VEHICLE   0x34
#define PH_TAKEN     0x36
#define PH_SUBJECT   0x37 /* the module's: number of the mission's subject + 1, 0 = none */
#define PH_VALUE     0x38
#define PH_FACTOR    0x3C
#define PH_OBJECTS   0x42
#define PH_KINDS     0x43
#define PH_TIMES     0x4C
#define PH_INDEXES   0x64
#define PH_BEST      0x6C

/* Camera: clip values at +0x2C, the view matrix at +0x108. */
#define CAM_CLIP_X0  0x2C
#define CAM_CLIP_X1  0x30
#define CAM_CLIP_Y0  0x34
#define CAM_CLIP_Y1  0x38
#define CAM_NEAR     0x3C
#define CAM_FAR      0x40
#define CAM_MATRIX   0x108

#define F32(p, o) (*(f32 *)((u8 *)(p) + (o)))
#define U16(p, o) (*(u16 *)((u8 *)(p) + (o)))

/* ------------------------------------------------------------------ the photo table */

static u32 be32(const u8 *p) {
    return ((u32)p[0] << 24) | ((u32)p[1] << 16) | ((u32)p[2] << 8) | p[3];
}

/* The subjects of a mission (*count of them, maybe 0: no photo gives points), or 0 if it has none of its own. */
static const u8 *subjects_of(u32 veh, u32 cls, u32 test, s32 *count) {
    const u8 *p = PHOTO_TABLE, *end = PHOTO_TABLE + PHOTO_TABLE_SIZE;
    u32 n, i;
    if (be32(p) != PHOTO_TABLE_MAGIC) {
        return 0;
    }
    n = be32(p + 4);
    p += 8;
    for (i = 0; i < n && p + 4 <= end; i++) {
        u32 subjects = p[3];
        if (subjects > SUBJECTS_MAX || p + 4 + subjects * SUBJECT_SIZE > end) {
            return 0;
        }
        if (p[0] == veh && p[1] == cls && p[2] == test) {
            *count = (s32)subjects;
            return p + 4;
        }
        p += 4 + subjects * SUBJECT_SIZE;
    }
    return 0;
}

/* The player's vehicle, class and test (Unk80362690_Unk0: +2, +4, +6). */
static const u8 *current_subjects(s32 *count) {
    u8 *state = GAME_STATE;
    u8 *player;
    if (state == 0) {
        return 0;
    }
    player = state + 0x0C + *(s32 *)(state + 0x9C) * 0x8C;
    return subjects_of(U16(player, 2), U16(player, 4), U16(player, 6), count);
}

/* For missions.c (the album's points for Birdman photos): 1 if the mission has subjects of its own. */
s32 module_has_photo_subjects(u32 veh, u32 cls, u32 test) {
    s32 count;
    return subjects_of(veh, cls, test, &count) != 0;
}

/* ------------------------------------------------------------------ the objects */

/* How many objects of a type there can be (the game's lists). */
static s32 object_count(s32 kind) {
    switch (kind) {
    case KIND_POD:
        return 5;
    case KIND_PLANE:
        return 2;
    case KIND_GLIDER:
        return 4;
    case KIND_BOAT:
        return 2;
    default:
        return 1;
    }
}

/* Object "index" of a type: 1 with its position and the time of its animation if it is there (and active). */
static s32 object_at(s32 kind, s32 index, Vec3F *pos, f32 *time) {
    s32 id = NO_OBJECT;
    *time = 0.0f;
    switch (kind) {
    case KIND_WHALE:
        if (!GAME_WHALE_ACTIVE()) {
            return 0;
        }
        GAME_WHALE_POS(pos);
        *time = GAME_WHALE_TIME();
        return 1;
    case KIND_MISSILE:
        if (!GAME_MISSI_ACTIVE()) {
            return 0;
        }
        GAME_MISSI_POS(pos);
        *time = GAME_MISSI_TIME();
        return 1;
    case KIND_SHUTTLE:
        if (!GAME_SHUTTLE_ACTIVE()) {
            return 0;
        }
        GAME_SHUTTLE_POS(pos);
        *time = GAME_SHUTTLE_TIME();
        return 1;
    case KIND_FERRY:
        if (!GAME_FERRY_ACTIVE()) {
            return 0;
        }
        GAME_FERRY_POS(pos);
        *time = GAME_FERRY_TIME();
        return 1;
    case KIND_FLAME:
        if (!GAME_FLAME_ACTIVE()) {
            return 0;
        }
        GAME_FLAME_POS(pos);
        return 1;
    case KIND_FOUNTAIN:
        if (!GAME_FOUNTAIN_ACTIVE()) {
            return 0;
        }
        GAME_FOUNTAIN_POS(pos);
        *time = GAME_FOUNTAIN_TIME();
        return 1;
    case KIND_POD:
        GAME_POD_DATA(index, &id, time, pos);
        return id != NO_OBJECT;
    case KIND_PLANE:
        GAME_PLANE_DATA(index, &id, time, pos);
        return id != NO_OBJECT;
    case KIND_GLIDER:
        GAME_GLIDER_DATA(index, &id, time, pos);
        return id != NO_OBJECT;
    case KIND_BOAT:
        GAME_BOAT_DATA(index, &id, time, pos);
        return id != NO_OBJECT;
    default:
        return 0;
    }
}

/* The game's test whether an object can be the photo's subject: in front of the camera and its projection within
   -50 ... 370 x -50 ... 290. */
static s32 visible(Vec3F camera, Vec3F pos, Mtx4F *view) {
    Vec3F screen;
    if (!GAME_IN_FRONT(camera, pos, view)) {
        return 0;
    }
    GAME_PROJECT(0, pos, &screen);
    return !(screen.x < -50.0f || screen.x > 370.0f || screen.y < -50.0f || screen.y > 290.0f);
}

/* ------------------------------------------------------------------ snapPhoto */

/* The game's order of the six main subjects (D_80350510). */
static const s32 sMainOrder[6] = { KIND_WHALE, KIND_MISSILE, KIND_SHUTTLE, KIND_FERRY, KIND_FLAME, KIND_FOUNTAIN };

/* A whale of the pod counts as a photo subject only while it is (nearly) above the water: its dive goes from 0 down
   to -25 (gWhalePod[i].depth); at -5 it is still well to be seen, at -10 hardly. */
#define POD_SURFACED (-8.0f)

/* An object of a subject for a photo: there, placed (not at 0, 0, 0 like a moving object the game has not posed yet)
   and, for a whale of the pod, above the water. */
static s32 subject_object(s32 kind, s32 index, Vec3F *pos, f32 *time) {
    if (!object_at(kind, index, pos, time) || (pos->x == 0.0f && pos->y == 0.0f && pos->z == 0.0f)) {
        return 0;
    }
    return kind != KIND_POD || POD_DEPTH(index) > POD_SURFACED;
}

/* The points of a photo of a subject's object at pos, as snapPhoto counts them: in the middle half of the picture and
   (if the subject has one) in its direction, the subject's points x centring (value) and the share of its distance
   curve (factor). 1 if it is in the middle half and in the direction, else 0 (value and factor 0). */
static s32 rate(const u8 *subject, Vec3F pos, Vec3F camera, f32 *value, f32 *factor) {
    Vec3F screen, difference, angles;
    f32 x, y, distance, centre;
    *value = 0.0f;
    *factor = 0.0f;
    GAME_PROJECT(0, pos, &screen);
    x = screen.x / 320.0f;
    y = screen.y / 240.0f;
    if (x < 0.25f || x > 0.75f || y < 0.25f || y > 0.75f || screen.z < 0.0f) {
        return 0;
    }
    GAME_DIFFERENCE(pos, camera, &difference);
    distance = GAME_LENGTH(&difference);
    if (subject[2] == 1) {
        f32 direction = F32(subject, 4), tolerance = F32(subject, 8), off;
        GAME_ANGLES(difference, &angles);
        if (direction - angles.x > 180.0f) {
            angles.x += 360.0f;
        } else if (angles.x - direction > 180.0f) {
            angles.x -= 360.0f;
        }
        off = direction - angles.x;
        if ((off < 0.0f ? -off : off) >= tolerance * 0.5f) {
            return 0;
        }
    }
    x = (x * 4.0f) - 1.0f;
    y = (y * 4.0f) - 1.0f;
    centre = 1.1f - GAME_SQRT(((1.0f - x) * (1.0f - x)) + ((1.0f - y) * (1.0f - y)));
    if (centre > 1.0f) {
        centre = 1.0f;
    }
    *value = F32(subject, 0x0C) * centre;
    *factor = GAME_CURVE(subject + 0x10, distance) * 0.01f; /* the curve is in percent */
    return 1;
}

/* Takes the place of snapPhoto (func_80338A14) in the two shutters. */
void module_snap_photo(void) {
    u8 *state = GAME_STATE;
    u8 *player, *camera, *photo;
    Mtx4F *view;
    const u8 *subjects, *chosen = 0;
    s32 count = 0, i, k, found = 0, chosenNumber = -1, chosenKind = 0, chosenIndex = 0, scored = 0, photoMission;
    f32 nearest = 1e7f, chosenTime = 0.0f, chosenValue = 0.0f, chosenFactor = 0.0f;
    s32 extras = 0, extraKinds[10], extraIndexes[10];
    f32 extraTimes[10], extraDepth[10];
    f32 best;
    s32 bestPhoto;

    subjects = current_subjects(&count);
    if (subjects == 0 || state == 0) {
        GAME_SNAP_PHOTO();
        return;
    }
    if (FLIGHT_COUNT >= FLIGHT_PHOTO_MAX) {
        GAME_PRINTF("snapPhoto: %d pictures already taken\n", FLIGHT_COUNT);
        SHUTTER_FULL = 0;
        return;
    }

    player = state + 0x0C + *(s32 *)(state + 0x9C) * 0x8C;
    camera = *(u8 **)(player + 0x70);
    view = (Mtx4F *)(camera + CAM_MATRIX);
    GAME_CAMERA_STATE(HUD_CAM_RENDER_SHUTTER);
    photo = FLIGHT_PHOTOS + FLIGHT_COUNT * 0x70;
    GAME_CAMERA_POSE(view, (Vec3F *)(photo + PH_POS), (Vec3F *)(photo + PH_ANGLE));
    photo[PH_TAKEN] = 1;
    GAME_CLS_VEH_TEST((u16 *)(photo + PH_CLASS), (u16 *)(photo + PH_VEHICLE), (u16 *)(photo + PH_TEST));
    photoMission = module_photo_record(photo);
    F32(photo, PH_NEAR) = F32(camera, CAM_NEAR);
    F32(photo, PH_FAR) = F32(camera, CAM_FAR);
    F32(photo, PH_CLIP_X0) = (f32)(F32(camera, CAM_CLIP_X0) * 0.45);
    F32(photo, PH_CLIP_X1) = (f32)(F32(camera, CAM_CLIP_X1) * 0.45);
    F32(photo, PH_CLIP_Y1) = (f32)(F32(camera, CAM_CLIP_Y1) * 0.45);
    F32(photo, PH_CLIP_Y0) = (f32)(F32(camera, CAM_CLIP_Y0) * 0.45);
    photo[PH_OBJECTS] = 0;
    photo[PH_KINDS] = KIND_NOTHING;
    photo[PH_SUBJECT] = 0;
    *(s32 *)(photo + PH_BEST) = 0;
    F32(photo, PH_VALUE) = 0.0f;
    F32(photo, PH_FACTOR) = 0.0f;
    F32(photo, PH_TIMES) = 0.0f;

    /* The subject: of the visible objects of the mission's subjects the one whose photo gives the most points (then
       the one in the middle half in the subject's direction, then the nearest). The game takes the nearest visible
       object, so a whale of the pod in front of the one in the middle (or one under water) took the points away. */
    for (i = 0; i < count; i++) {
        const u8 *s = subjects + i * SUBJECT_SIZE;
        s32 kind = s[0], only = s[1], n = object_count(kind);
        for (k = 0; k < n; k++) {
            Vec3F pos;
            f32 time, distance, value, factor;
            s32 rated;
            if ((only != 0xFF && only != k) || !subject_object(kind, k, &pos, &time)) {
                continue;
            }
            if (!visible(*(Vec3F *)(photo + PH_POS), pos, view)) {
                continue;
            }
            distance = GAME_DISTANCE(pos, *(Vec3F *)(photo + PH_POS));
            rated = rate(s, pos, *(Vec3F *)(photo + PH_POS), &value, &factor);
            if (!found || value * factor > chosenValue * chosenFactor
                || (value * factor == chosenValue * chosenFactor
                    && (rated > scored || (rated == scored && distance < nearest)))) {
                found = 1;
                nearest = distance;
                chosen = s;
                chosenNumber = i;
                chosenKind = kind;
                chosenIndex = k;
                chosenTime = time;
                chosenValue = value;
                chosenFactor = factor;
                scored = rated;
            }
        }
    }

    /* None of them: the nearest of the six main subjects, as the game takes it (no points). */
    if (!found) {
        for (i = 0; i < 6; i++) {
            Vec3F pos;
            f32 time, distance;
            if (!object_at(sMainOrder[i], 0, &pos, &time) || !visible(*(Vec3F *)(photo + PH_POS), pos, view)) {
                continue;
            }
            distance = GAME_DISTANCE(pos, *(Vec3F *)(photo + PH_POS));
            if (!found || distance < nearest) {
                found = 1;
                nearest = distance;
                chosenKind = sMainOrder[i];
                chosenIndex = 0;
                chosenTime = time;
            }
        }
    }

    if (found) {
        photo[PH_OBJECTS] = 1;
        photo[PH_KINDS] = (u8)chosenKind;
        F32(photo, PH_TIMES) = chosenTime;
        photo[PH_INDEXES] = (u8)chosenIndex;
    }

    if (chosen != 0) {
        photo[PH_SUBJECT] = (u8)(chosenNumber + 1);
        if (scored) {
            F32(photo, PH_VALUE) = chosenValue;
            F32(photo, PH_FACTOR) = chosenFactor;
        }
    }

    if (photoMission && scored) {
        GAME_HUD_TEXT(TEXT_PHOTO, 3.0f, 0.0f);
    }

    /* The best photo of each subject (photos without one: of each object type, as the game). */
    FLIGHT_COUNT = FLIGHT_COUNT + 1;
    best = 0.0f;
    bestPhoto = -1;
    for (i = 0; i < FLIGHT_COUNT; i++) {
        u8 *other = FLIGHT_PHOTOS + i * 0x70;
        s32 same = photo[PH_SUBJECT] != 0 ? other[PH_SUBJECT] == photo[PH_SUBJECT]
                                          : other[PH_SUBJECT] == 0 && other[PH_KINDS] == photo[PH_KINDS];
        if (same) {
            f32 value = F32(other, PH_FACTOR) * F32(other, PH_VALUE);
            *(s32 *)(other + PH_BEST) = 0;
            if (best < value) {
                best = value;
                bestPhoto = i;
            }
        }
    }
    if (bestPhoto >= 0) {
        *(s32 *)(FLIGHT_PHOTOS + bestPhoto * 0x70 + PH_BEST) = 1;
    }

    /* The other moving objects in the picture, nearest last, as the game records them (up to six objects). */
    GAME_EXTRAS(KIND_BOAT, &extras, extraKinds, extraTimes, extraDepth, extraIndexes);
    GAME_EXTRAS(KIND_GLIDER, &extras, extraKinds, extraTimes, extraDepth, extraIndexes);
    GAME_EXTRAS(KIND_PLANE, &extras, extraKinds, extraTimes, extraDepth, extraIndexes);
    GAME_EXTRAS(KIND_POD, &extras, extraKinds, extraTimes, extraDepth, extraIndexes);
    for (i = 0; i < extras; i++) {
        s32 pick = -1, j;
        for (j = 0; j < extras; j++) {
            if (extraDepth[j] < 1e8f && extraDepth[j] >= 0.0f) {
                pick = j;
            }
        }
        if (pick < 0) {
            break;
        }
        extraDepth[pick] = -1.0f;
        if (photo[PH_OBJECTS] < 6) {
            s32 at = photo[PH_OBJECTS];
            photo[PH_KINDS + at] = (u8)extraKinds[pick];
            F32(photo, PH_TIMES + at * 4) = extraTimes[pick];
            photo[PH_INDEXES + at] = (u8)extraIndexes[pick];
            photo[PH_OBJECTS] = (u8)(at + 1);
        }
    }
    GAME_SFX(SFX_SNAP);
}

/* ------------------------------------------------------------------ sample photos per mission */

/* The game's captions of its six photo subjects (US text numbers). */
static const s16 sCaptions[7] = { 0, 0x0D1 /* Space Shuttle */, 0x075 /* Passenger Boat */, 0x040 /* Missi the Monster */,
                                  0x130 /* Whale */, 0x049 /* Fountain */, 0x156 /* Flame */ };

static s32 sSampleCount; /* sample photos of the current mission in SAMPLE_PHOTOS (0: the game's of the island) */

/* The sample photos of a mission with own subjects, one per subject with points: the camera at the subject's distance,
   direction (the camera's heading) and height angle from the object as it is now (on the test screen: as the island is
   loaded for it; in the pause menu: during the flight), looking at it (the game's sample photos in snapInit are made the
   same way, from the objects at the start). */
static void make_samples(u32 veh, u32 cls, u32 test) {
    const u8 *subjects;
    s32 count = 0, i;
    sSampleCount = 0;
    subjects = subjects_of(veh, cls, test, &count);
    if (subjects == 0) {
        return;
    }
    for (i = 0; i < count && sSampleCount < SUBJECTS_MAX; i++) {
        const u8 *s = subjects + i * SUBJECT_SIZE;
        u8 *photo;
        if (F32(s, 0x0C) <= 0.0f || object_count(s[0]) == 0) {
            continue;
        }
        photo = SAMPLE_PHOTOS + sSampleCount * 0x70;
        F32(photo, PH_CLIP_X0) = -0.220794f;
        F32(photo, PH_CLIP_X1) = 0.220794f;
        F32(photo, PH_CLIP_Y0) = -0.1575f;
        F32(photo, PH_CLIP_Y1) = 0.1575f;
        F32(photo, PH_NEAR) = 1.0f;
        F32(photo, PH_FAR) = 2000.0f;
        U16(photo, PH_CLASS) = (u16)cls;
        U16(photo, PH_VEHICLE) = (u16)veh;
        U16(photo, PH_TEST) = (u16)test;
        photo[PH_TAKEN] = 1;
        photo[PH_SUBJECT] = (u8)(i + 1);
        *(s16 *)(photo + 0x40) = s[0] <= KIND_FLAME ? sCaptions[s[0]] : (s16)(CAPTION_OWN + s[0]);
        photo[PH_OBJECTS] = 1;
        photo[PH_KINDS] = s[0];
        photo[PH_INDEXES] = s[1] != 0xFF ? s[1] : 0;
        *(s32 *)(photo + PH_BEST) = 0;
        /* Until module_place_sample: the camera's distance, heading and height angle, and the object wanted. */
        F32(photo, PH_VALUE) = F32(s, 0x64);
        F32(photo, PH_FACTOR) = F32(s, 0x68);
        F32(photo, PH_TIMES + 4) = F32(s, 0x6C);
        photo[PH_INDEXES + 1] = s[1];
        F32(photo, PH_POS + 0) = 0.0f;
        F32(photo, PH_POS + 4) = 0.0f;
        F32(photo, PH_POS + 8) = 0.0f;
        sSampleCount++;
    }
}

/* The position of an object for a sample photo: as object_at, but the six main subjects also when they are not active
   (the test screen: the player is far away; their positions are fixed or start from the loaded state). */
static s32 sample_object(s32 kind, s32 index, Vec3F *pos, f32 *time) {
    *time = 0.0f;
    switch (kind) {
    /* The moving main subjects as the game's sample photos take them: at the start of their animation (time 0; the
       whale and Missi above the water), from the place their GetPos functions give when the object is not loaded
       (whaleGetPos, missiGetPos, ferryGetPos, shuttle_80335F24). Their pose in memory is only valid while the player
       is near, so it is not read. */
    case KIND_WHALE:
        pos->x = 825.0f;
        pos->y = 100.0f;
        pos->z = 4.5f;
        return 1;
    case KIND_MISSILE:
        pos->x = 1745.0f;
        pos->y = -659.0f;
        pos->z = 57.0f;
        return 1;
    case KIND_SHUTTLE:
        pos->x = 2870.0f;
        pos->y = -2230.0f;
        pos->z = 57.51f;
        return 1;
    case KIND_FERRY:
        pos->x = 1350.0f;
        pos->y = -2150.0f;
        pos->z = 8.8f;
        return 1;
    case KIND_FLAME:
        GAME_FLAME_POS(pos);
        return 1;
    case KIND_FOUNTAIN:
        GAME_FOUNTAIN_POS(pos);
        *time = 1.0f; /* as the game's sample photo: the fountain at full height */
        return 1;
    default:
        return object_at(kind, index, pos, time);
    }
}

/* After a sample photo's test is set up (photos.c, module_photo_setup), its objects are there: the camera now looks at
   the object where it is, and the photo keeps the object's animation time, so the picture shows it at that place. */
void module_place_sample(u8 *photo) {
    s32 kind, wanted, index, n, found = 0;
    Vec3F pos;
    f32 time = 0.0f, distance, heading, height, flat;
    if (photo < SAMPLE_PHOTOS || photo >= SAMPLE_PHOTOS + sSampleCount * 0x70 || F32(photo, PH_VALUE) <= 0.0f) {
        return;
    }
    kind = photo[PH_KINDS];
    wanted = photo[PH_INDEXES + 1];
    n = object_count(kind);
    if (wanted != 0xFF) {
        index = wanted;
        found = sample_object(kind, index, &pos, &time);
    } else {
        for (index = 0; index < n && !found; index++) {
            found = sample_object(kind, index, &pos, &time);
        }
        index--;
    }
    if (!found) {
        return;
    }
    /* The moving objects: their pose follows their animation time only when they are updated (right after the test is
       loaded they are not yet): set the time they have, which places them, and read the position again. */
    if (kind >= KIND_POD && kind <= KIND_BOAT) {
        ObjectTime place = kind == KIND_POD ? GAME_POD_TIME : kind == KIND_PLANE ? GAME_PLANE_TIME
                         : kind == KIND_GLIDER ? GAME_GLIDER_TIME : GAME_BOAT_TIME;
        if (kind == KIND_POD) {
            POD_DEPTH(index) = 0.0f; /* the whale at the surface (its dive goes from 0 to -25) */
        }
        time = 0.0f; /* the start of its path: the same picture on the test screen and in the pause menu */
        place(index, time);
        if (!object_at(kind, index, &pos, &time)) {
            return;
        }
    }
    distance = F32(photo, PH_VALUE);
    heading = F32(photo, PH_FACTOR);
    height = F32(photo, PH_TIMES + 4);
    flat = GAME_COS(height * 0.0174533f) * distance;
    F32(photo, PH_POS + 0) = pos.x + GAME_SIN(heading * 0.0174533f) * flat;
    F32(photo, PH_POS + 4) = pos.y - GAME_COS(heading * 0.0174533f) * flat;
    F32(photo, PH_POS + 8) = pos.z + GAME_SIN(height * 0.0174533f) * distance;
    F32(photo, PH_ANGLE + 0) = heading;
    F32(photo, PH_ANGLE + 4) = -height;
    F32(photo, PH_ANGLE + 8) = 0.0f;
    F32(photo, PH_TIMES) = time;
    photo[PH_INDEXES] = (u8)index;
}

/* The calls of the sample photos (func_8033E3A8 mode 3 from the test screen, mode 1 from the pause menu): first the
   mission's own sample photos (the test screen's test, sCurTestIdx; the flight's test), then the game's function. */
s32 module_examples_prepare(s32 mode) {
    u8 *state = GAME_STATE;
    sSampleCount = 0;
    if (state != 0) {
        u8 *player = state + 0x0C + *(s32 *)(state + 0x9C) * 0x8C;
        make_samples(U16(player, 2), U16(player, 4), mode == 3 ? (u32)TEST_MENU_TEST : U16(player, 6));
    }
    return GAME_SNAP_VIEW(mode);
}

/* Takes the place of the three calls of func_8033DDD8 in func_8033E3A8 (the sample photos, mode 1): the mission's own
   sample photos, otherwise the game's of the island (the switch over the map, here as in the game). Returns what
   func_8033DDD8 returns (1 = leave), or 1 for an island without sample photos. */
s32 module_examples_view(s32 map, s32 *cursor) {
    if (sSampleCount > 0) {
        if (*cursor >= sSampleCount) {
            *cursor = 0;
        }
        return GAME_EXAMPLES_VIEW(sSampleCount, SAMPLE_PHOTOS, cursor);
    }
    switch (map) {
    case 3:
        return GAME_EXAMPLES_VIEW(GAME_COUNT_CRESCENT, GAME_EXAMPLES_CRESCENT, cursor);
    case 5:
        return GAME_EXAMPLES_VIEW(GAME_COUNT_LSTATES, GAME_EXAMPLES_LSTATES, cursor);
    case 10:
        return GAME_EXAMPLES_VIEW(GAME_COUNT_EFROST, GAME_EXAMPLES_EFROST, cursor);
    default:
        return 1;
    }
}

/* textGetDataByIdx for the caption of a sample photo (func_8033ADD4): the module's captions of the types 7-10. */
const u16 *module_example_text(s32 id) {
    const u16 *own;
    if (id > CAPTION_OWN && (own = module_subject_name(id - CAPTION_OWN)) != 0) {
        return own;
    }
    return GAME_TEXT(id >= CAPTION_OWN ? 0x13C : id);
}

/* For missions.c: the mission has sample photos of its own (the test screen's button, the pause menu's photo item). */
s32 module_own_samples(void) {
    return sSampleCount;
}

/* ------------------------------------------------------------------ radar marks of the main subjects */

/* taskGetClsVehTest for the loaders: the first test of the game's photo missions if the subject gets a radar mark. */
static void radar_test(u16 *cls, u16 *veh, u16 *test, s32 kind) {
    s32 count, i, show = 0;
    const u8 *subjects;
    GAME_CLS_VEH_TEST(cls, veh, test);
    subjects = subjects_of(*veh, *cls, *test, &count);
    if (subjects != 0) {
        /* own subjects: each one with points, in every mission */
        for (i = 0; i < count; i++) {
            show |= subjects[i * SUBJECT_SIZE] == kind && F32(subjects + i * SUBJECT_SIZE, 0x0C) > 0.0f;
        }
    } else {
        show = *veh == 0 && module_is_photo_mission(*veh, *cls, *test); /* the game: the Hang Glider's photo missions */
    }
    if (show) {
        *cls = 1; /* Class A */
        *veh = 0; /* Hang Glider */
        *test = 0;
    } else {
        *veh = 0xFF;
    }
}

void module_radar_whale(u16 *cls, u16 *veh, u16 *test) { radar_test(cls, veh, test, KIND_WHALE); }
void module_radar_fountain(u16 *cls, u16 *veh, u16 *test) { radar_test(cls, veh, test, KIND_FOUNTAIN); }
void module_radar_missile(u16 *cls, u16 *veh, u16 *test) { radar_test(cls, veh, test, KIND_MISSILE); }
void module_radar_ferry(u16 *cls, u16 *veh, u16 *test) { radar_test(cls, veh, test, KIND_FERRY); }
void module_radar_shuttle(u16 *cls, u16 *veh, u16 *test) { radar_test(cls, veh, test, KIND_SHUTTLE); }
void module_radar_flame(u16 *cls, u16 *veh, u16 *test) { radar_test(cls, veh, test, KIND_FLAME); }

/* ------------------------------------------------------------------ radar marks and map labels of the moving objects */

/* Where a moving object is for its radar mark and map label: as object_at; a whale of the pod also while the player is
   farther than 1750 units (whalePodProxAnimCb poses the whales only nearer, their objects stay at 0, 0, 0) and while
   the pause menu's map has the island unloaded: from its circle (whalePodUpdateWhale: around .pos, radius
   .translateX, angle .pathAngle, .depth below). 0 if it is not there or not placed yet. */
static s32 mover_at(s32 kind, s32 index, Vec3F *pos) {
    f32 time, angle;
    if (object_at(kind, index, pos, &time) && (pos->x != 0.0f || pos->y != 0.0f)) {
        return 1;
    }
    if (kind != KIND_POD || (POD_F32(index, 0x08) == 0.0f && POD_F32(index, 0x0C) == 0.0f)) {
        return 0;
    }
    angle = POD_F32(index, 0x1C) * 0.0174533f;
    pos->x = POD_F32(index, 0x08) + GAME_COS(angle) * POD_F32(index, 0x14);
    pos->y = POD_F32(index, 0x0C) + GAME_SIN(angle) * POD_F32(index, 0x14);
    pos->z = POD_F32(index, 0x10) + POD_DEPTH(index);
    return 1;
}

#define MARKS_MAX 20
static s32 sMarkCount;
static u8 sMarkKind[MARKS_MAX], sMarkIndex[MARKS_MAX], sMarkId[MARKS_MAX];
static Vec3F sMarkPos[MARKS_MAX]; /* where the object was in the last frame (for the pause menu's map) */

/* Calls f for every object of the mission's own subjects with points (of a subject with "any" every object). */
static void each_subject_object(void (*f)(s32 kind, s32 index), s32 movingOnly) {
    s32 count = 0, i, k;
    const u8 *subjects = current_subjects(&count);
    for (i = 0; subjects != 0 && i < count; i++) {
        const u8 *s = subjects + i * SUBJECT_SIZE;
        s32 kind = s[0], n = object_count(kind);
        if (F32(s, 0x0C) <= 0.0f || (movingOnly && kind < KIND_POD)) {
            continue;
        }
        for (k = 0; k < n; k++) {
            if (s[1] == 0xFF || s[1] == k) {
                f(kind, k);
            }
        }
    }
}

static void add_mark(s32 kind, s32 index) {
    Vec3F pos;
    s32 i, id;
    for (i = 0; i < sMarkCount; i++) {
        if (sMarkKind[i] == kind && sMarkIndex[i] == index) {
            return; /* two subjects with the same object */
        }
    }
    if (sMarkCount >= MARKS_MAX || !mover_at(kind, index, &pos)) {
        return;
    }
    id = GAME_ADD_WAYPOINT(pos.x, pos.y, pos.z);
    if (id == 0xFF) {
        return;
    }
    sMarkKind[sMarkCount] = (u8)kind;
    sMarkIndex[sMarkCount] = (u8)index;
    sMarkId[sMarkCount] = (u8)id;
    sMarkPos[sMarkCount] = pos;
    sMarkCount++;
}

/* snapInit's last call (hudSetCameraState(0); taskInit, at the start of every mission, after the radar is cleared
   and before the island's objects are loaded): the radar marks of the moving objects of the mission's own subjects
   start anew (the main subjects get theirs from their loaders, module_radar_<subject>). */
void module_snap_init(s32 state) {
    GAME_CAMERA_STATE(state);
    sMarkCount = 0;
}

/* Every frame the radar is drawn (objects.c, module_hud_radar): a mark for each object of the own subjects that is
   there now (once), and the marks follow their objects. */
void module_subject_marks(void) {
    s32 i;
    each_subject_object(add_mark, 1);
    for (i = 0; i < sMarkCount; i++) {
        Vec3F pos;
        if (mover_at(sMarkKind[i], sMarkIndex[i], &pos)) {
            GAME_MOVE_WAYPOINT(sMarkId[i], pos.x, pos.y, pos.z);
            sMarkPos[i] = pos;
        }
    }
}

/* The map (map3dLoad's taskGetPHTS): with own subjects their labels instead of the PHTS entries' ("Whale" ...; the
   moving objects with "Target", the game has no label for them), only in the map view (map3dMain), as the game. */
static const s32 sMapLabel[7] = { 0x31 /* Target */, 0x35 /* Space shuttle */, 0x36 /* Passenger boat */,
                                  0x39 /* Missi the Monster */, 0x34 /* Whale */, 0x37 /* Fountain */,
                                  0x38 /* Flame */ }; /* the US blits (uv_sprite.h) */
#define MAP_TEST 1
#define MAP_PAUSE 2
static s32 sMapView; /* 0, MAP_TEST or MAP_PAUSE while map3dMain runs */

#define LABELS_MAX 12 /* of the map's 20 label slots, the rest for the player, rings, pads ... */
static s32 sLabelCount;
static u8 sLabelKind[LABELS_MAX], sLabelIndex[LABELS_MAX];

static void add_label(s32 kind, s32 index) {
    Vec3F pos;
    f32 time;
    f32 scale = GAME_MAP_SCALE;
    s32 i;
    for (i = 0; i < sLabelCount; i++) {
        if (sLabelKind[i] == kind && sLabelIndex[i] == index) {
            return; /* two subjects with the same object */
        }
    }
    if (sLabelCount >= LABELS_MAX) {
        return;
    }
    switch (kind) {
    /* as the game's map: where the object is now (its start place when it is not loaded) */
    case KIND_WHALE:
        GAME_WHALE_POS(&pos);
        break;
    case KIND_MISSILE:
        GAME_MISSI_POS(&pos);
        break;
    case KIND_SHUTTLE:
        GAME_SHUTTLE_POS(&pos);
        break;
    case KIND_FERRY:
        GAME_FERRY_POS(&pos);
        break;
    case KIND_FOUNTAIN:
    case KIND_FLAME:
        sample_object(kind, index, &pos, &time);
        break;
    default:
        if (!mover_at(kind, index, &pos)) {
            /* The pause menu unloads the island before its map (func_8032D51C, uvLevelInit) and loads it again after:
               the object where its radar mark was in the last frame of the flight. */
            for (i = 0; sMapView == MAP_PAUSE && i < sMarkCount; i++) {
                if (sMarkKind[i] == kind && sMarkIndex[i] == index) {
                    break;
                }
            }
            if (sMapView != MAP_PAUSE || i >= sMarkCount) {
                return; /* not there (the test screen's map: the objects are not loaded) */
            }
            pos = sMarkPos[i];
        }
        break;
    }
    sLabelKind[sLabelCount] = (u8)kind;
    sLabelIndex[sLabelCount] = (u8)index;
    sLabelCount++;
    GAME_MAP_ITEM(kind <= KIND_FLAME ? sMapLabel[kind] : sMapLabel[0], pos.x * scale, pos.y * scale, pos.z * scale);
}

s32 module_map_photos(void **phts) {
    s32 count = 0;
    if (current_subjects(&count) == 0) {
        return GAME_TASK_PHTS(phts);
    }
    if (sMapView) {
        sLabelCount = 0;
        each_subject_object(add_label, 0);
    }
    *phts = 0;
    return 0;
}

/* The two calls of map3dMain: "View map" on the test screen and "Check map" in the pause menu. */
void module_map_test(void *game, s32 arg) {
    sMapView = MAP_TEST;
    GAME_MAP_MAIN(game, arg);
    sMapView = 0;
}

void module_map_pause(void *game, s32 arg) {
    sMapView = MAP_PAUSE;
    GAME_MAP_MAIN(game, arg);
    sMapView = 0;
}
