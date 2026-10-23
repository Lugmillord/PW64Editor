/*
 * Hawk's Hangar game module: weather and view per mission (Missions tab, section "Weather and view").
 *
 * Built with the FPU (-mhard-float), unlike module.c: the snow and rain move up to 1200 points per frame. Only
 * integers and pointers cross into module.c; floats are passed as their bits there.
 *
 * What it does (the settings are in the mission's entry of the data area, +0x80, see DataArea.cs):
 *   - At the start of every mission (taskLoad calls func_802EB598 through module_env_start): the environment's fog
 *     colour, optionally the sky tinted with it, and the camera's far distance (view distance). Changes of the last
 *     mission are undone first, as far as the game has not loaded the data anew.
 *   - Fog start and density: uvGfxSetFogFactor (kernel) goes through module_fog_factor, which uses the mission's
 *     fog range instead of factor × 1000 … 1000 while a mission with one is flown.
 *   - Snow and rain: the game's snow (snow.c: snowUpdate, snowDraw, switched on by COMM byte 9) is replaced by
 *     module_effect_update and module_effect_draw, with the mission's kind, fall speed, number and colour. Without
 *     settings it is the game's snow (800 white flakes, 15 units per second), computed the same way.
 *   - Sky Diving: below the clouds the game loaded condition 1 of the island; module_sd_below loads the mission's.
 *   - The times of day / weather the islands lack (dark overcast on Holiday and Ever-Frost Island, sunset on Crescent and
 *     Ever-Frost Island): environments the editor adds, chosen through module_env_id (envGetCurrentId).
 *
 * Function names from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).
 */

typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef signed int s32;
typedef float f32;

/* ------------------------------------------------------------------ module.c */

const void *module_weather_entry(void);
void module_set_frame_callback(void *callback);
void module_message_int(const char *fmt, s32 value);

/* ------------------------------------------------------------------ game addresses (US) */

#define GAME_STATE        (*(u8 *volatile *)0x80362690)  /* D_80362690 */
#define GAME_DT           (*(volatile f32 *)0x8034F854)  /* D_8034F854: seconds since the last frame */
#define GAME_LEVEL_DATA   (*(u8 *volatile *)0x80248DD0)  /* gGfxUnkPtrs (uvLevelData *) */
#define GAME_DL_HEAD      (*(u32 *volatile *)0x80298AB0) /* gGfxDisplayListHead */
#define GAME_FOG_FACTOR   (*(volatile f32 *)0x80249208)  /* gGfxFogFactor */

#define GAME_ORIGINAL_TASK_START ((void (*)(void))0x802EB598)  /* func_802EB598: fire, smoke, snow on */
#define GAME_ORIGINAL_TASK_END   ((void (*)(void))0x802EB5E4)  /* func_802EB5E4: their end */
#define GAME_ALLOC          ((void *(*)(u32))0x8022B078)        /* _uvMemAllocAlign8 */
#define GAME_CHAN_MATRIX    ((void (*)(s32, s32, f32 *))0x80205724) /* uvChan_80205724 */
#define GAME_MAT4_INVERT    ((void (*)(f32 *, f32 *))0x80229878)    /* uvMat4InvertTranslationRotation */
#define GAME_WIND_AT        ((void (*)(f32, f32, f32, f32 *))0x802E1754) /* env_802E1754 */
#define GAME_RAND           ((f32(*)(void))0x802315EC)          /* uvRandF_RANLUX */
#define GAME_ENV_CURRENT    ((s32(*)(void))0x802E12B4)          /* envGetCurrentId */
#define GAME_LEVEL_APPEND   ((void (*)(s32))0x80224CE0)         /* uvLevelAppend */
#define GAME_SNOW_DRAW      ((void *)0x8033FF30)                /* snowDraw (its entry jumps to module_effect_draw) */

/* uvLevelData: environments at +0x44 (32), models at +0xC8 (400). ParsedUVEN: clear colour +0, fog colour +4,
   model table +0x30 (u16 id, u8 flags; 4 bytes each), model count +0x34. ParsedUVMD: vertices +0 (16 bytes each, colour
   at +12), vertex count +4 (u16). */
#define LEVEL_ENV(i)   (((u8 **)(GAME_LEVEL_DATA + 0x44))[i])
#define LEVEL_MODEL(i) (((u8 **)(GAME_LEVEL_DATA + 0xC8))[i])
#define ENV_MODEL_FOLLOWS_CAMERA 8 /* the sky dome */

/* The player's flight data (Unk80362690_Unk0): +8 u16 a flag (no snow, no mission wind), +0x70 the camera. */
static u8 *player_data(void) {
    u8 *state = GAME_STATE;
    return state != 0 ? state + 0x0C + *(s32 *)(state + 0x9C) * 0x8C : 0;
}

#define WEATHER_FOG_COLOR 1u
#define WEATHER_SKY_TINT  2u
#define WEATHER_VIEW      4u
#define WEATHER_FOG_RANGE 8u
#define WEATHER_EFFECT   16u

/* The weather fields of a mission entry (module.c, Condition +0x80). */
typedef struct {
    u8 flags;
    u8 effect;
    u16 effectColor;
    u8 fogColor[4];
    f32 viewDistance;
    s32 fogMul;    /* gSPFogPosition's multiplier: 128000 / (max - min) */
    s32 fogOffset; /* its offset: (500 - min) * 256 / (max - min) */
    f32 fallSpeed;
    u16 density;
} Weather;

static const Weather *weather(void) {
    const u8 *entry = (const u8 *)module_weather_entry();
    return entry != 0 ? (const Weather *)(entry + 0x80) : 0;
}

/* ------------------------------------------------------------------ fog colour, sky, view distance */

#define SKY_VERTICES 1024

static u8 *sFogEnv;            /* environment whose fog colour was changed */
static u8 sFogOriginal[4], sFogWritten[4]; /* fog r, g, b and clear r, g, b of it: before and after */
static u8 sClearOriginal[3], sClearWritten[3];

static u8 *sSkyModel, *sSkyVertices; /* the sky model that was tinted and its vertices */
static u32 sSkyCount;
static u8 sSkyOriginal[SKY_VERTICES][3];
static u8 sSkyTint[3];

/* The view distance cameraInit gives a camera (its "2000.0f" is patched to load this, DataArea.LiveViewAddress). */
#define LIVE_VIEW (*(volatile f32 *)0x801F5060)

static int sFogActive;
static s32 sFogMul, sFogOffset;

static u8 tinted(u8 value, u8 tint) {
    return (u8)((value * tint) / 255);
}

/* The tint of a colour: the colour brightened until its strongest channel is 255 (black stays black). */
static void tint_of(const u8 *color, u8 *tint) {
    u32 high = color[0], i;
    if (color[1] > high) {
        high = color[1];
    }
    if (color[2] > high) {
        high = color[2];
    }
    for (i = 0; i < 3; i++) {
        tint[i] = high == 0 ? 0 : (u8)((color[i] * 255) / high);
    }
}

/* Undoes the changes of the last mission where the data is still the one that was changed. */
static void undo_last(void) {
    u32 i;
    if (sFogEnv != 0) {
        for (i = 0; i < 32 && LEVEL_ENV(i) != sFogEnv; i++) {
        }
        if (i < 32 && sFogEnv[4] == sFogWritten[0] && sFogEnv[5] == sFogWritten[1] && sFogEnv[6] == sFogWritten[2]) {
            sFogEnv[4] = sFogOriginal[0];
            sFogEnv[5] = sFogOriginal[1];
            sFogEnv[6] = sFogOriginal[2];
            if (sFogEnv[0] == sClearWritten[0] && sFogEnv[1] == sClearWritten[1] && sFogEnv[2] == sClearWritten[2]) {
                sFogEnv[0] = sClearOriginal[0];
                sFogEnv[1] = sClearOriginal[1];
                sFogEnv[2] = sClearOriginal[2];
            }
        }
        sFogEnv = 0;
    }
    if (sSkyModel != 0) {
        for (i = 0; i < 400 && LEVEL_MODEL(i) != sSkyModel; i++) {
        }
        if (i < 400 && *(u8 **)sSkyModel == sSkyVertices && *(u16 *)(sSkyModel + 4) == sSkyCount) {
            u32 v, same = 1;
            for (v = 0; v < sSkyCount && same; v++) {
                u8 *c = sSkyVertices + v * 16 + 12;
                same = c[0] == tinted(sSkyOriginal[v][0], sSkyTint[0]) && c[1] == tinted(sSkyOriginal[v][1], sSkyTint[1])
                       && c[2] == tinted(sSkyOriginal[v][2], sSkyTint[2]);
            }
            if (same) {
                for (v = 0; v < sSkyCount; v++) {
                    u8 *c = sSkyVertices + v * 16 + 12;
                    c[0] = sSkyOriginal[v][0];
                    c[1] = sSkyOriginal[v][1];
                    c[2] = sSkyOriginal[v][2];
                }
            }
        }
        sSkyModel = 0;
    }
}

/* Tints the sky dome of an environment (its models that follow the camera). */
static void tint_sky(u8 *env, const u8 *tint) {
    u8 *table = *(u8 **)(env + 0x30);
    u32 count = env[0x34], m;
    for (m = 0; m < count && table != 0; m++) {
        u16 id = *(u16 *)(table + m * 4);
        u8 *model;
        u8 *vertices;
        u32 n, v;
        if (!(table[m * 4 + 2] & ENV_MODEL_FOLLOWS_CAMERA) || id >= 400 || (model = LEVEL_MODEL(id)) == 0) {
            continue;
        }
        vertices = *(u8 **)model;
        n = *(u16 *)(model + 4);
        if (n > SKY_VERTICES || sSkyModel != 0) {
            module_message_int("Weather: the sky model has %d vertices, too many to tint.\n", (s32)n);
            continue;
        }
        sSkyModel = model;
        sSkyVertices = vertices;
        sSkyCount = n;
        sSkyTint[0] = tint[0];
        sSkyTint[1] = tint[1];
        sSkyTint[2] = tint[2];
        for (v = 0; v < n; v++) {
            u8 *c = vertices + v * 16 + 12;
            sSkyOriginal[v][0] = c[0];
            sSkyOriginal[v][1] = c[1];
            sSkyOriginal[v][2] = c[2];
            c[0] = tinted(c[0], tint[0]);
            c[1] = tinted(c[1], tint[1]);
            c[2] = tinted(c[2], tint[2]);
        }
    }
}

/* Called by taskLoad instead of func_802EB598, at the start of every mission (and after the hidden night switch). */
void module_env_start(void) {
    const Weather *w;
    u8 *state = GAME_STATE;
    u8 *player = player_data();
    u8 *camera;
    f32 view, old;

    GAME_ORIGINAL_TASK_START();
    if (state == 0 || player == 0) {
        return;
    }
    undo_last();
    w = weather();

    /* Fog colour (and the sky) of the mission's environment. */
    if (w != 0 && (w->flags & WEATHER_FOG_COLOR) && *(u16 *)(state + 8) < 32 && LEVEL_ENV(*(u16 *)(state + 8)) != 0) {
        u8 *env = LEVEL_ENV(*(u16 *)(state + 8));
        u32 i;
        sFogEnv = env;
        for (i = 0; i < 3; i++) {
            sFogOriginal[i] = env[4 + i];
            sFogWritten[i] = w->fogColor[i];
            env[4 + i] = w->fogColor[i];
            sClearOriginal[i] = env[i];
            sClearWritten[i] = env[i];
        }
        if (w->flags & WEATHER_SKY_TINT) {
            u8 tint[3];
            tint_of(w->fogColor, tint);
            for (i = 0; i < 3; i++) {
                sClearWritten[i] = tinted(sClearOriginal[i], tint[i]);
                env[i] = sClearWritten[i];
            }
            tint_sky(env, tint);
        }
    }

    /* View distance: cameraInit, which follows, gives the camera LIVE_VIEW; a camera that is not set up again (the next
       Cannonball target) gets the difference, so the 100 the cannon adds stay. */
    camera = *(u8 **)(player + 0x70);
    view = w != 0 && (w->flags & WEATHER_VIEW) ? w->viewDistance : 2000.0f;
    old = LIVE_VIEW > 100.0f ? LIVE_VIEW : 2000.0f;
    if (camera != 0) {
        *(f32 *)(camera + 0x40) += view - old;
    }
    LIVE_VIEW = view;

    /* Fog range. */
    sFogActive = w != 0 && (w->flags & WEATHER_FOG_RANGE) && w->fogMul > 0;
    sFogMul = sFogActive ? w->fogMul : 0;
    sFogOffset = sFogActive ? w->fogOffset : 0;
}

/* Called by taskDeinitLevel instead of func_802EB5E4, at the end of a mission. */
void module_env_end(void) {
    u8 *player = player_data();
    u8 *camera = player != 0 ? *(u8 **)(player + 0x70) : 0;
    GAME_ORIGINAL_TASK_END();
    sFogActive = 0;
    if (LIVE_VIEW > 100.0f && camera != 0) {
        *(f32 *)(camera + 0x40) += 2000.0f - LIVE_VIEW;
    }
    LIVE_VIEW = 2000.0f;
}

/* uvGfxSetFogFactor (its entry jumps here): with the mission's fog range while a mission with one is flown, otherwise the
   original (fog_factor_original runs its first two instructions and goes on). */
void fog_factor_original(f32 factor);

void module_fog_factor(f32 factor) {
    u32 *dl;
    if (!sFogActive || !(factor > 0.0f)) {
        fog_factor_original(factor);
        return;
    }
    if (factor > 0.996f) {
        factor = 0.996f;
    }
    GAME_FOG_FACTOR = factor;
    dl = GAME_DL_HEAD;
    dl[0] = 0xB7000000u; /* gSPSetGeometryMode(G_FOG) */
    dl[1] = 0x00010000u;
    dl[2] = 0xBC000008u; /* gSPFogPosition: G_MOVEWORD G_MW_FOG, multiplier and offset */
    dl[3] = ((u32)(sFogMul & 0xFFFF) << 16) | (u32)(sFogOffset & 0xFFFF);
    GAME_DL_HEAD = dl + 4;
}

__asm__(".section .text\n"
        ".set push\n"
        ".set noreorder\n"
        ".globl fog_factor_original\n"
        "fog_factor_original:\n"
        "    mtc1 $zero, $f0\n"
        "    lui $t0, 0x802A\n"
        "    j 0x80220CA8\n"
        "    nop\n"
        ".set pop\n");

/* ------------------------------------------------------------------ Sky Diving below the clouds */

/* skydivingBelow: instead of condition 1 of the island, the mission's environment (returned in a1 for uvChanEnv). */
s32 module_sd_below(void) {
    s32 env = GAME_ENV_CURRENT();
    GAME_LEVEL_APPEND(0x70 + env);
    return env;
}

/* ------------------------------------------------------------------ the times of day / weather the islands lack */

/* The game has no environment for a dark overcast on Holiday Island, a sunset on Crescent Island, and neither on
   Ever-Frost Island (envGetCurrentId gives 22, "no sky"). The editor appends them to the UVEN and UVLV files (numbers 24
   and up, level 0x70 + number) and writes an island table into the data area (WeatherEnvironments.cs): per island its
   map number and the environment of each condition (0xFF: none), and per new environment the environments whose tint
   (env_802E1C1C) and terrain palette (envLoadTerrainPal) it takes. */

#define GAME_TASK_CONDITION ((u8 * (*)(void))0x80345CB0) /* taskGet_80345CB0: [0] the condition, [1] snow */
#define GAME_ENV_LEVEL      ((void (*)(s32))0x802E1A80)   /* env_802E1A80: uvLevelAppend of the environment's level */
#define GAME_ENV_TINT       ((void (*)(s32))0x802E1C1C)   /* env_802E1C1C: the colour changes of the terrain */
#define ENV_TABLE_ADDRESS   (*(volatile u32 *)0x801F5020)
#define DATA_MAGIC_WORD     (*(volatile u32 *)0x801F5000)
#define ENV_NONE            22
#define ENV_FIRST_ADDED     24
#define ENV_MAX             32
#define ENV_ISLANDS         8

typedef struct {
    u8 map;    /* D_80362690->map of the island (0 = unused) */
    u8 env[6]; /* the environment of each condition, 0xFF = none */
    u8 pad;
} EnvIsland;

typedef struct {
    u8 tint;    /* the environment whose tint it uses */
    u8 palette; /* the environment whose terrain palette it uses */
    u8 pad[2];
} EnvAdded;

typedef struct {
    EnvIsland islands[ENV_ISLANDS];
    EnvAdded added[ENV_MAX - ENV_FIRST_ADDED];
} EnvTable;

static const EnvTable *env_table(void) {
    u32 a = ENV_TABLE_ADDRESS;
    return DATA_MAGIC_WORD == 0x48484D44u && a >= 0x801F5000u && a + sizeof(EnvTable) <= 0x801FE000u ? (const EnvTable *)a : 0;
}

/* envGetCurrentId (its entry jumps here): the environment of the mission's condition on its island. */
s32 module_env_id(void) {
    const EnvTable *table = env_table();
    u32 condition = GAME_TASK_CONDITION()[0];
    u32 map = *(volatile u16 *)(GAME_STATE + 4);
    u32 i;
    if (table == 0 || condition >= 6 || map == 0) {
        return ENV_NONE;
    }
    for (i = 0; i < ENV_ISLANDS; i++) {
        if (table->islands[i].map == map) {
            u32 env = table->islands[i].env[condition];
            return env < ENV_MAX ? (s32)env : ENV_NONE;
        }
    }
    return ENV_NONE;
}

/* env_802E1444's call of env_802E1A80: a new environment's level is 0x70 + its number, as the game's. */
void module_env_level(s32 env) {
    if (env >= ENV_FIRST_ADDED && env < ENV_MAX) {
        GAME_LEVEL_APPEND(0x70 + env);
    } else {
        GAME_ENV_LEVEL(env);
    }
}

/* env_802E1444's call of env_802E1C1C: a new environment takes the tint of its template. */
void module_env_tint(s32 env) {
    const EnvTable *table = env_table();
    if (env >= ENV_FIRST_ADDED && env < ENV_MAX && table != 0) {
        env = table->added[env - ENV_FIRST_ADDED].tint;
    }
    GAME_ENV_TINT(env);
}

/* envLoadTerrainPal (its entry jumps here): a new environment takes the terrain palette of its template (Ever-Frost
   Island has own palettes for day, overcast and night). env_palette_original runs the first two instructions. */
void env_palette_original(s32 env);

void module_env_palette(s32 env) {
    const EnvTable *table = env_table();
    if (env >= ENV_FIRST_ADDED && env < ENV_MAX && table != 0) {
        env = table->added[env - ENV_FIRST_ADDED].palette;
    }
    env_palette_original(env);
}

__asm__(".section .text\n"
        ".set push\n"
        ".set noreorder\n"
        ".globl env_palette_original\n"
        "env_palette_original:\n"
        "    addiu $sp, $sp, -0x18\n"
        "    j 0x802E1998\n"
        "    sw $ra, 0x14($sp)\n"
        ".set pop\n");

/* ------------------------------------------------------------------ snow and rain */

#define FLAKES_MAX 1200
#define EFFECT_SNOW 1
#define EFFECT_RAIN 2
#define SNOW_DEFAULT_COUNT 800
#define SNOW_DEFAULT_SPEED 15.0f
#define RAIN_SECONDS 0.15f /* a drop's streak: the way it falls in this time */
#define RAIN_MIN_LENGTH 3  /* pixels */
#define RAIN_MAX_LENGTH 24 /* pixels */

typedef struct EffectData {
    struct EffectData *self;
    u32 count;            /* flakes in use */
    f32 pos[FLAKES_MAX][3];
    u8 visible[FLAKES_MAX];
    u32 items[FLAKES_MAX * 2]; /* snow: frame buffer index; rain: x0 | y0 << 16, x1 | y1 << 16 */
    u32 itemCount;             /* words used */
    u32 rain;
    u16 color;
    s32 inBox;
} EffectData;

static EffectData *sEffect;
static f32 sLastCamera[3];

typedef struct {
    f32 min[3];
    f32 max[3];
} Box;

/* sSnowBounds of snow.c: no new flakes in these boxes (on Ever-Frost Island). */
static const Box kNoSnow[2] = {
    { { 310.0f, 34.0f, 151.0f }, { 350.6f, 62.64f, 167.0f } },
    { { 240.0f, 123.0f, 121.47f }, { 344.58f, 154.2f, 159.3f } },
};

static int effect_valid(void) {
    return sEffect != 0 && sEffect == sEffect->self;
}

static void effect_create(u32 count) {
    u32 i;
    if (!effect_valid()) {
        sEffect = (EffectData *)GAME_ALLOC(sizeof(EffectData));
        sEffect->self = sEffect;
        sEffect->count = 0;
        sEffect->itemCount = 0;
        sEffect->inBox = 0;
    }
    if (sEffect->count != count) {
        sEffect->count = count;
        sEffect->itemCount = 0;
        for (i = 0; i < count; i++) {
            sEffect->pos[i][0] = GAME_RAND() * 200.0f;
            sEffect->pos[i][1] = GAME_RAND() * 200.0f;
            sEffect->pos[i][2] = GAME_RAND() * 200.0f;
            sEffect->visible[i] = 1;
        }
    }
}

/* snowCheckRange: true while no new flakes may appear (the player flag, or inside a box on Ever-Frost Island). */
static int effect_blocked(const f32 *camera) {
    u8 *state = GAME_STATE;
    u8 *player = player_data();
    u32 i;
    if (player != 0 && *(u16 *)(player + 8) != 0) {
        return 1;
    }
    if (state == 0 || *(u16 *)(state + 6) != 7) {
        return 0;
    }
    for (i = 0; i < 2; i++) {
        const Box *b = &kNoSnow[i];
        if (camera[0] > b->min[0] && camera[0] < b->max[0] && camera[1] > b->min[1] && camera[1] < b->max[1]
            && camera[2] > b->min[2] && camera[2] < b->max[2]) {
            return 1;
        }
    }
    return 0;
}

/* uvMat4LocalToWorld. */
static void to_view(const f32 *m, const f32 *v, f32 *out) {
    out[0] = v[0] * m[0] + v[1] * m[4] + v[2] * m[8] + m[12];
    out[1] = v[0] * m[1] + v[1] * m[5] + v[2] * m[9] + m[13];
    out[2] = v[0] * m[2] + v[1] * m[6] + v[2] * m[10] + m[14];
}

/* The screen point of a point in view space (y forward), as snow.c projects it; 0 outside the picture. */
static int project(const f32 *p, s32 *x, s32 *y) {
    f32 sx, sz;
    if (p[1] < 0.0f) {
        return 0;
    }
    sx = p[0] * (200.0f / p[1]);
    sz = p[2] * (200.0f / p[1]);
    if (!(sx > -150.0f && sx < 150.0f && sz > -102.0f && sz < 112.0f)) {
        return 0;
    }
    *x = (s32)sx;
    *y = (s32)sz;
    return 1;
}

/* snowUpdate (its entry jumps here): moves the flakes and lists the pixels the next frame draws. */
void module_effect_update(void) {
    f32 view[16], camera[16];
    f32 ahead[3], wind[3], move[3], step[3];
    f32 lo[3], hi[3];
    f32 fall, dt = GAME_DT;
    f32 drift = dt * 15.0f;
    const Weather *w = weather();
    u32 count = SNOW_DEFAULT_COUNT, i, n = 0;
    int rain = 0;
    u16 color = 0xFFFF;
    f32 speed = SNOW_DEFAULT_SPEED;

    if (w != 0 && (w->flags & WEATHER_EFFECT)) {
        rain = w->effect == EFFECT_RAIN;
        color = w->effectColor;
        speed = w->fallSpeed;
        count = w->density;
        if (count > FLAKES_MAX) {
            count = FLAKES_MAX;
        }
    }
    fall = dt * speed;
    effect_create(count);
    sEffect->rain = (u32)rain;
    sEffect->color = color;

    module_set_frame_callback(GAME_SNOW_DRAW);
    sEffect->itemCount = 0;
    GAME_CHAN_MATRIX(0, 3, view);
    GAME_MAT4_INVERT(camera, view);
    sEffect->inBox = effect_blocked(camera + 12);
    for (i = 0; i < 3; i++) {
        ahead[i] = camera[12 + i] + camera[4 + i] * 100.0f;
        move[i] = sLastCamera[i] - camera[12 + i];
        sLastCamera[i] = camera[12 + i];
    }
    GAME_WIND_AT(ahead[0], ahead[1], ahead[2], wind);
    if (rain) {
        /* Rain falls straight down: no drift with the wind, and not against the camera's movement (its streaks are
           vertical, a sideways movement would make them slide). */
        step[0] = 0.0f;
        step[1] = 0.0f;
        step[2] = -fall;
    } else {
        step[0] = wind[0] * drift + move[0] * 3.0f;
        step[1] = wind[1] * drift + move[1] * 3.0f;
        step[2] = move[2] - fall;
    }
    for (i = 0; i < 3; i++) {
        lo[i] = ahead[i] - 100.0f - 1.0f;
        hi[i] = ahead[i] + 100.0f + 1.0f;
    }

    for (i = 0; i < sEffect->count; i++) {
        f32 *p = sEffect->pos[i];
        f32 v[3];
        s32 x, y;
        p[0] += step[0];
        p[1] += step[1];
        p[2] += step[2];
        if (!(p[0] > lo[0]) || !(p[0] < hi[0]) || !(p[1] > lo[1]) || !(p[1] < hi[1]) || !(p[2] > lo[2]) || !(p[2] < hi[2])) {
            while (p[0] < lo[0]) {
                p[0] += 200.0f;
            }
            while (p[0] > hi[0]) {
                p[0] -= 200.0f;
            }
            while (p[1] < lo[1]) {
                p[1] += 200.0f;
            }
            while (p[1] > hi[1]) {
                p[1] -= 200.0f;
            }
            while (p[2] < lo[2]) {
                p[2] += 200.0f;
            }
            while (p[2] > hi[2]) {
                p[2] -= 200.0f;
            }
            sEffect->visible[i] = sEffect->inBox == 0;
            continue;
        }
        if (!sEffect->visible[i]) {
            continue;
        }
        to_view(view, p, v);
        if (!project(v, &x, &y)) {
            continue;
        }
        if (!rain) {
            sEffect->items[n++] = (u32)((x - y * 320) + 0x96A0);
        } else {
            /* The streak: from the drop up along the way it falls in RAIN_SECONDS. */
            f32 t[3], len = speed * RAIN_SECONDS;
            s32 x1, y1;
            t[0] = v[0] + view[8] * len;
            t[1] = v[1] + view[9] * len;
            t[2] = v[2] + view[10] * len;
            if (!project(t, &x1, &y1)) {
                x1 = x;
                y1 = y;
            }
            if (y1 - y < RAIN_MIN_LENGTH && x1 == x) {
                y1 = y + RAIN_MIN_LENGTH; /* screen y grows upwards here (120 - y below) */
            }
            sEffect->items[n++] = (u32)(x + 160) | ((u32)(120 - y) << 16);
            sEffect->items[n++] = (u32)(x1 + 160) | ((u32)(120 - y1) << 16);
        }
    }
    sEffect->itemCount = n;
}

/* snowDraw (its entry jumps here): the frame callback of uvGfxEnd, draws the pixels into the finished frame. */
s32 module_effect_draw(u16 *frame, s32 depth) {
    u32 i;
    (void)depth;
    if (!effect_valid()) {
        effect_create(SNOW_DEFAULT_COUNT);
        return 0;
    }
    if (!sEffect->rain) {
        u16 color = sEffect->color | 1;
        for (i = 0; i < sEffect->itemCount; i++) {
            frame[sEffect->items[i]] = color;
        }
        return 0;
    }
    for (i = 0; i + 1 < sEffect->itemCount; i += 2) {
        s32 x0 = (s32)(sEffect->items[i] & 0xFFFF), y0 = (s32)(sEffect->items[i] >> 16);
        s32 x1 = (s32)(sEffect->items[i + 1] & 0xFFFF), y1 = (s32)(sEffect->items[i + 1] >> 16);
        s32 dx = x1 - x0, dy = y1 - y0, len, steps, k;
        u16 half = (u16)((sEffect->color & 0xF7BEu) >> 1);
        len = dx < 0 ? -dx : dx;
        if ((dy < 0 ? -dy : dy) > len) {
            len = dy < 0 ? -dy : dy;
        }
        steps = len > RAIN_MAX_LENGTH ? RAIN_MAX_LENGTH : len;
        for (k = 0; k <= steps; k++) {
            s32 x = len == 0 ? x0 : x0 + (dx * k) / len;
            s32 y = len == 0 ? y0 : y0 + (dy * k) / len;
            u16 *p;
            if (x < 0 || x >= 320 || y < 0 || y >= 240) {
                continue;
            }
            p = frame + y * 320 + x;
            *p = (u16)((((*p & 0xF7BEu) >> 1) + half) | 1u); /* half the drop's colour, half the picture */
        }
    }
    return 0;
}
