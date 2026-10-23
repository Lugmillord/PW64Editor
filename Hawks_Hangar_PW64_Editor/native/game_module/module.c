/*
 * Hawk's Hangar game module (US version of Pilotwings 64).
 *
 * New program code that the editor builds into a hack ROM. The game loads it from the end of the
 * ROM into the editor's code area (0x803805E0, behind the game's variables) right before the
 * game program starts (loader in the code cave, see CodeArea.cs), then calls module_init().
 *
 * What it does:
 *   - Expansion Pak check: without the Expansion Pak (only 4 MB of RAM) the module shows a black
 *     screen with "EXPANSION PAK REQUIRED" and stops (the editor's hacks require 8 MB).
 *   - Mission settings: the editor writes a table with one entry per mission that needs settings
 *     into the ROM; the module copies it at the start into the data area in front of the game's
 *     program block (DATA_AREA, see DataArea.cs).
 *   - Conditions for time points (and "a crash gives 0 points") per mission, combined freely:
 *     the scoring function calls module_conditions() instead of reading the retail condition mode.
 *   - Scoring: time points when the goal ring or the ball target ends the mission, the hard end
 *     time per mission (scored or 0 points) and, with the game patch "goal objects", the points of
 *     hopper goals and ball targets (module_conditions); the goal jingle of each vehicle.
 *   - Debug output (only if switched on in the editor):
 *   - Messages go to the IS-Viewer debug port (0xB3FF0000). Emulators show them: ares in its
 *     terminal (start ares with --terminal, or Tools > Tracer "ISViewer"), mupen64plus and
 *     simple64 in their log. Flash carts with an IS-Viewer port (e.g. SummerCart64) send them
 *     over USB. On a cartridge without such a port the writes are ignored.
 *   - Every message is also kept in a ring buffer in RAM (header field log), so it can be read
 *     with the memory viewer of an emulator even without the debug port.
 *   - The editor's own messages are also drawn on the screen (frame callback of uvGfxEnd) and
 *     disappear 15 seconds after the last one.
 *   - Startup information: the steps the game takes while starting, each with "Starting: ..."
 *     and "Done: ..." plus the time it took and details where useful. The editor redirects the
 *     calls of these steps to the hook functions below (the call sites are listed in
 *     build_module.py).
 *   - Optionally the game's own debug messages: the developers' _uvDebugPrintf calls, which
 *     print nothing in the retail game, are passed on.
 *
 * Built with build_module.py (mips-linux-gnu-gcc). The editor contains the result
 * (GameModuleImage.g.cs); this source is only needed to change the module.
 */

typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef signed int s32;
typedef unsigned long long u64;
typedef __builtin_va_list va_list;
#define va_start(ap, last) __builtin_va_start(ap, last)
#define va_arg(ap, type) __builtin_va_arg(ap, type)
#define va_end(ap) __builtin_va_end(ap)

#define REG32(addr) (*(volatile u32 *)(addr))

/* ------------------------------------------------------------------ game addresses (US) */

#define GAME_TV_TYPE        REG32(0x80000300) /* osTvType: 0 PAL, 1 NTSC, 2 MPAL */
#define GAME_RESET_TYPE     REG32(0x8000030C) /* osResetType: 0 cold, 1 warm (NMI) */
#define GAME_MEM_SIZE       REG32(0x80000318) /* osMemSize: 4 or 8 MB */
#define GAME_CONT_PATTERN   (*(volatile u8 *)0x802C3394) /* gSiContPattern: bit n = controller n+1 */
#define GAME_EEPROM_FOUND   REG32(0x802C5BB0) /* gEepromFound */
#define GAME_HEAP_NEXT      REG32(0x802B892C) /* next free heap address (uvMemAlloc) */
#define GAME_TEXT_COUNT     REG32(0x80378CCC) /* sTextDataCount */
#define GAME_TEXT_NAMES     REG32(0x80378CD0) /* sTextNameCount */

#define APP_ROM_START  0x00051E30
#define APP_ROM_END    0x000DE720
#define APP_RAM_START  0x802CA900
#define APP_BSS_START  0x803571F0
#define APP_BSS_END    0x803805E0

/* ------------------------------------------------------------------ header */

#define FLAG_STARTUP       1u /* startup information */
#define FLAG_GAME_MESSAGES 2u /* the game's own debug messages */
#define FLAG_DEBUG         4u /* debug output at all */
#define FLAG_SCREEN        8u /* the editor's messages also on the screen */
#define FLAG_LIVE         16u /* current values during missions (header field live) */
#define FLAG_REQUIRE_PAK  32u /* stop with a message without the Expansion Pak */
#define FLAG_GOAL_OBJECTS 64u /* game patch "goal objects": hopper goals and ball targets always count */
#define FLAG_LANDING     128u /* a mission changes a landing: pad radius and speed limits are set in every flight frame */

/* Values of the live display (header field live). */
#define LIVE_DISTANCE   1u /* distance flown */
#define LIVE_ALTITUDE   2u /* highest altitude of this flight */
#define LIVE_HORIZONTAL 4u /* horizontal speed */
#define LIVE_VERTICAL   8u /* vertical speed */

#define LOG_SIZE 8192

void module_init(s32 arg);
int module_game_printf(const char *fmt, ...);

extern char __image_end[];
extern char __bss_start[];
extern char __bss_end[];

static char sLog[LOG_SIZE];

/* ------------------------------------------------------------------ data area (mission settings) */

/* RAM in front of the game's program block (and of the text tables at 0x801FE000), taken from the
   heap by the editor (uvMemInitBlocks, uvMemClearRegions; see DataArea.cs). The module fills it at
   the start from the ROM (header fields dataRom, dataSize). Layout: a header of 0x100 bytes, then
   one entry of 0x100 bytes per mission with settings. */
#define DATA_AREA        0x801F5000u
#define DATA_AREA_END    0x801FE000u

/* The lower data area (DataExtension.cs), 0x801F1000-0x801F5000: 'HHMX', +4 number of missions, +8 u8[8] Birdman
   missions with points (per stage a bit per test), +0x10 u8[7][8] photo missions (per vehicle and class a bit per test),
   +0x80 u8[128] the mission list (user file numbers), +0x100 the result tables, +0x2800 the class table. Its image lies
   0x9000 bytes behind the data area's in the ROM; its size is at data area header +0x24. */
#define EXT_AREA         0x801F1000u
#define EXT_AREA_END     0x801F5000u
#define EXT_MAGIC        0x48484D58u /* 'HHMX' */
#define EXT_ROM_OFFSET   0x9000u
#define EXT_OK           (*(volatile u32 *)EXT_AREA == EXT_MAGIC)
#define EXT_BIRDMAN(cls) (((volatile u8 *)(EXT_AREA + 0x08))[cls])
#define EXT_PHOTO(v, c)  (((volatile u8 *)(EXT_AREA + 0x10))[(v) * 8 + (c)])
#define MAX_CLASSES      8
#define MAX_TESTS        5
#define DATA_MAGIC       0x48484D44u /* 'HHMD' */
#define DATA_ENTRY_SIZE  0x100
#define MAX_CONDITIONS   128

typedef struct {
    u32 magic;      /* +0x00 'HHMD' */
    u32 version;    /* +0x04 layout version (1) */
    u32 missions;   /* +0x08 entries used */
    u32 entrySize;  /* +0x0C bytes per entry (0x100) */
} DataHeader;

#define DATA_HEADER ((volatile DataHeader *)DATA_AREA)

/* ------------------------------------------------------------------ conditions table */

#define COND_CRASH_ZERO 1u /* a crash gives 0 points in total */
#define COND_ALTITUDE   2u /* time points only if the highest altitude reached "altitude" */
#define COND_DISTANCE   4u /* time points only if the distance flown reached "distance" */
#define COND_FUEL       8u /* Rocket Belt, Gyrocopter: the fuel never runs out */
#define COND_END_SCORED 16u /* hard end time reached: scored like a flight that ends in the air */
#define COND_END_ZERO   32u /* hard end time reached: 0 points */
#define COND_CRASH_TIME 64u /* after a crash the time points count too (time of the crash) */
#define COND_TOUCHDOWN 128u /* Gyrocopter: own curve for the touchdown point (entry +0x18) */
#define COND_POINTS (COND_CRASH_ZERO | COND_ALTITUDE | COND_DISTANCE | COND_CRASH_TIME) /* flags about the points */

#define LANDING_PAD_RADIUS 1u /* entry +0x6C: radius of the landing pads */
#define LANDING_LIMITS     2u /* entry +0x70: speed limits of the vehicle */

/* True if an entry has conditions for the points (an entry may only be there for the fuel). */
#define HAS_POINT_CONDITIONS(c) (((c)->flags & COND_POINTS) || has_required(c))

#define OBJECT_TYPES 7      /* rings, balloons, targets, hover pads, Meca Hawks, ball targets, hopper goals */
#define REQUIRED_ALL 0xFF
/* The settings of one mission (one entry of the data area, 0x100 bytes). required[type]: 0 = not
   required, 0xFF = all, otherwise at least this many. Written by the editor (DataArea.cs). */
typedef struct {
    u8 vehicle;   /* +0x00 COMM byte 1 */
    u8 cls;       /* +0x01 COMM byte 0 */
    u8 test;      /* +0x02 COMM byte 2 */
    u8 flags;     /* +0x03 COND_... */
    u8 required[OBJECT_TYPES]; /* +0x04 */
    u8 pad;       /* +0x0B */
    u32 altitude; /* +0x0C f32 bits, game units above sea level */
    u32 distance; /* +0x10 f32 bits, game units */
    u8 hardEndMusic; /* +0x14 jingle when the hard end time is reached (music number), 0 = none */
    u8 landing;      /* +0x15 LANDING_... */
    u8 pad16[2];
    u8 touchdown[0x54]; /* +0x18 curve of the touchdown point (s32 count, 10 x {f32 x, f32 y}), like the mission file */
    u32 padRadius;   /* +0x6C f32 bits, game units */
    u32 limits[4];   /* +0x70 f32 bits: HG landing vertical/forward, good vertical/forward (km/h); GC crash, crash
                        beside a pad; RB crash, hard landing, hard landing sideways (units per second); Birdman landing
                        vertical/forward (km/h) */
    /* Weather and view (weather.c): */
    u8 weather;      /* +0x80 WEATHER_... */
    u8 effect;       /* +0x81 1 snow, 2 rain (with COMM byte 9 = 1) */
    u16 effectColor; /* +0x82 RGBA 5551 */
    u8 fogColor[4];  /* +0x84 r, g, b, 0 */
    u32 viewDistance; /* +0x88 f32 bits, game units (camera's far distance) */
    s32 fogMul;      /* +0x8C gSPFogPosition multiplier 128000 / (max - min), depth 0-1000 */
    s32 fogOffset;   /* +0x90 its offset (500 - min) * 256 / (max - min) */
    u32 fallSpeed;   /* +0x94 f32 bits, units per second */
    u16 density;     /* +0x98 snow flakes or rain drops */
    u8 reserved[DATA_ENTRY_SIZE - 0x9A];
} Condition;

#define WEATHER_FOG_COLOR 1u  /* the environment's fog colour */
#define WEATHER_SKY_TINT  2u  /* the sky (and the clear colour) tinted with it */
#define WEATHER_VIEW      4u  /* own view distance */
#define WEATHER_FOG_RANGE 8u  /* own fog start and density */
#define WEATHER_EFFECT   16u  /* own snow/rain settings */

#define gConditions ((const Condition *)(DATA_AREA + 0x100))

/* Fixed layout, read by the loader (init), the printf gate (magic, printf) and the editor
   (flags, rom, info). See GameModule.cs. */
struct Header {
    u32 magic;          /* +0x00 'HHGM' */
    void *printf;       /* +0x04 module_game_printf */
    void *init;         /* +0x08 module_init */
    u32 flags;          /* +0x0C FLAG_..., written by the editor */
    u32 romOffset;      /* +0x10 where the module lies in the ROM, written by the editor */
    u32 imageSize;      /* +0x14 bytes loaded from the ROM, written by the editor */
    char *log;          /* +0x18 ring buffer of all messages */
    u32 logSize;        /* +0x1C its size */
    u32 logPos;         /* +0x20 bytes written so far (position = logPos % logSize) */
    u32 version;        /* +0x24 module version */
    void *conditions;   /* +0x28 table of the missions with settings (in the data area) */
    u32 conditionCount; /* +0x2C entries used, written by the editor */
    char info[96];      /* +0x30 "project ..., built ...", written by the editor */
    u32 live;           /* +0x90 LIVE_... values shown during missions, written by the editor */
    u16 metText[32];    /* +0x94 "Time requirement met" in the game's text codes as in RAM (end 0xFFFF), or 0 */
    u16 digits[10];     /* +0xD4 codes of the bold digits 0-9 (penalty messages), or 0 */
    u32 dataRom;        /* +0xE8 ROM offset of the data area image, written by the editor */
    u32 dataSize;       /* +0xEC its size (a multiple of 8, 0 = none), written by the editor */
    u16 countdown[6][12]; /* +0xF0 "5" ... "1", "Time up!" in the game's text codes (end 0xFFFF), or 0 */
};

__attribute__((section(".header"), used)) struct Header gHeader = {
    0x4848474D, (void *)module_game_printf, (void *)module_init, 0, 0, 0,
    sLog, LOG_SIZE, 0, 6, (void *)(DATA_AREA + 0x100), 0, "", 0, { 0 }, { 0 }, 0, 0, { { 0 } },
};

/* ------------------------------------------------------------------ low level */

static inline u32 disable_interrupts(void) {
    u32 sr;
    __asm__ volatile("mfc0 %0, $12" : "=r"(sr));
    __asm__ volatile("mtc0 %0, $12\n\tnop\n\tnop" : : "r"(sr & ~1u));
    return sr;
}

static inline void restore_interrupts(u32 sr) {
    __asm__ volatile("mtc0 %0, $12\n\tnop\n\tnop" : : "r"(sr));
}

static inline u32 cpu_count(void) {
    u32 c;
    __asm__ volatile("mfc0 %0, $9" : "=r"(c));
    return c;
}

/* IS-Viewer: text at +0x20, then the length into +0x14 sends it. */
#define ISV_BASE   0xB3FF0000
#define ISV_TEXT   0x20
#define ISV_LENGTH 0x14
#define PI_STATUS  REG32(0xA4600010)

static void pi_write(u32 address, u32 value) {
    while (PI_STATUS & 3) {
    }
    REG32(address) = value;
}

static char sLine[512];

/* Sends a finished message (interrupts are off). */
static void output(const char *text, u32 length) {
    u32 i;
    for (i = 0; i < length; i++) {
        sLog[(gHeader.logPos + i) % LOG_SIZE] = text[i];
    }
    gHeader.logPos += length;

    for (i = 0; i < length; i += 4) {
        u32 word = 0;
        u32 k;
        for (k = 0; k < 4; k++) {
            word = (word << 8) | (i + k < length ? (u8)text[i + k] : 0);
        }
        pi_write(ISV_BASE + ISV_TEXT + i, word);
    }
    pi_write(ISV_BASE + ISV_LENGTH, length);
}

/* ------------------------------------------------------------------ formatting */

typedef struct {
    char *buf;
    u32 pos;
    u32 cap;
} Out;

static void put(Out *o, char c) {
    if (o->pos < o->cap) {
        o->buf[o->pos++] = c;
    }
}

static void put_padded(Out *o, const char *s, u32 len, s32 width, int left, char pad) {
    s32 n = width - (s32)len;
    if (!left) {
        while (n-- > 0) {
            put(o, pad);
        }
    }
    while (len--) {
        put(o, *s++);
    }
    if (left) {
        while (n-- > 0) {
            put(o, ' ');
        }
    }
}

static u32 utoa(u32 value, char *end, u32 base, int upper) {
    const char *digits = upper ? "0123456789ABCDEF" : "0123456789abcdef";
    u32 n = 0;
    do {
        *--end = digits[value % base];
        value /= base;
        n++;
    } while (value != 0);
    return n;
}

/* 64-bit shift right by a variable amount, with 32-bit halves (no libgcc). */
static u64 shr64(u64 v, int n) {
    u32 hi = (u32)(v >> 32), lo = (u32)v;
    if (n <= 0) {
        return v;
    }
    if (n >= 64) {
        return 0;
    }
    if (n >= 32) {
        return hi >> (n - 32);
    }
    return ((u64)(hi >> n) << 32) | ((lo >> n) | (hi << (32 - n)));
}

/* x << n for 0 <= n < 64, with 32-bit halves (no libgcc). */
static u64 shl64(u64 v, int n) {
    u32 hi = (u32)(v >> 32), lo = (u32)v;
    if (n <= 0) {
        return v;
    }
    if (n >= 32) {
        return (u64)(lo << (n - 32)) << 32;
    }
    return ((u64)((hi << n) | (lo >> (32 - n))) << 32) | (lo << n);
}

/* A double with "prec" decimals (cut, not rounded); values of 2^31 and more are not shown exactly. */
static void put_double(Out *o, u64 bits, int prec, s32 width, int left) {
    char tmp[48];
    char *p = tmp;
    int exp = (int)((bits >> 52) & 0x7FF);
    u64 mant = bits & 0x000FFFFFFFFFFFFFull;
    u32 whole = 0;
    u64 frac = 0;
    int shift = 0;
    char digits[12];
    u32 n;
    int i;

    if (prec < 0) {
        prec = 6;
    }
    if (prec > 9) {
        prec = 9;
    }
    if (bits >> 63) {
        *p++ = '-';
    }
    if (exp == 0x7FF || exp - 1023 >= 31) {
        const char *s = exp == 0x7FF ? (mant ? "nan" : "inf") : "(too big)";
        while (*s) {
            *p++ = *s++;
        }
        put_padded(o, tmp, (u32)(p - tmp), width, left, ' ');
        return;
    }
    if (exp != 0 && exp - 1023 > -12) {
        /* value = mant * 2^(exp - 1023 - 52): "shift" bits behind the point */
        mant |= 1ull << 52;
        shift = 52 - (exp - 1023);
        whole = (u32)shr64(mant, shift);
        frac = mant - shl64((u64)whole, shift);
    }
    n = utoa(whole, digits + sizeof(digits), 10, 0);
    for (i = 0; i < (int)n; i++) {
        *p++ = digits[sizeof(digits) - n + i];
    }
    if (prec > 0) {
        *p++ = '.';
        for (i = 0; i < prec; i++) {
            u32 digit = 0;
            if (shift > 0) {
                /* keep frac below 2^59, so frac * 10 fits into 64 bits */
                while (shift > 59) {
                    frac = shr64(frac, 1);
                    shift--;
                }
                frac = shl64(frac, 3) + shl64(frac, 1);
                digit = (u32)shr64(frac, shift);
                frac -= shl64((u64)digit, shift);
            }
            *p++ = (char)('0' + digit);
        }
    }
    put_padded(o, tmp, (u32)(p - tmp), width, left, ' ');
}

/* A small printf: %d %i %u %x %X %o %c %s %p %f %e %g %%, flags - 0 + space #, width, precision, l/h. */
static void format(Out *o, const char *fmt, va_list ap) {
    while (*fmt) {
        char c = *fmt++;
        int left = 0, zero = 0, plus = 0, space = 0, alt = 0;
        s32 width = 0;
        int prec = -1;
        char tmp[16];
        char *end = tmp + sizeof(tmp);
        u32 n;

        if (c != '%') {
            put(o, c);
            continue;
        }
        for (;; fmt++) {
            if (*fmt == '-') {
                left = 1;
            } else if (*fmt == '0') {
                zero = 1;
            } else if (*fmt == '+') {
                plus = 1;
            } else if (*fmt == ' ') {
                space = 1;
            } else if (*fmt == '#') {
                alt = 1;
            } else {
                break;
            }
        }
        if (*fmt == '*') {
            width = va_arg(ap, s32);
            fmt++;
        } else {
            while (*fmt >= '0' && *fmt <= '9') {
                width = width * 10 + (*fmt++ - '0');
            }
        }
        if (*fmt == '.') {
            fmt++;
            prec = 0;
            if (*fmt == '*') {
                prec = va_arg(ap, s32);
                fmt++;
            } else {
                while (*fmt >= '0' && *fmt <= '9') {
                    prec = prec * 10 + (*fmt++ - '0');
                }
            }
        }
        while (*fmt == 'l' || *fmt == 'h') {
            fmt++;
        }
        c = *fmt++;
        switch (c) {
        case 'd':
        case 'i': {
            s32 v = va_arg(ap, s32);
            u32 u = v < 0 ? (u32)(-v) : (u32)v;
            n = utoa(u, end, 10, 0);
            if (v < 0 || plus || space) {
                char sign = v < 0 ? '-' : (plus ? '+' : ' ');
                if (zero && !left) {
                    put(o, sign);
                    put_padded(o, end - n, n, width - 1, 0, '0');
                    break;
                }
                *(end - n - 1) = sign;
                n++;
            }
            put_padded(o, end - n, n, width, left, zero && !left ? '0' : ' ');
            break;
        }
        case 'u':
        case 'x':
        case 'X':
        case 'o': {
            u32 v = va_arg(ap, u32);
            n = utoa(v, end, c == 'u' ? 10 : c == 'o' ? 8 : 16, c == 'X');
            if (alt && c != 'u' && v != 0) {
                put(o, '0');
                if (c != 'o') {
                    put(o, c);
                }
                width -= c == 'o' ? 1 : 2;
            }
            put_padded(o, end - n, n, width, left, zero && !left ? '0' : ' ');
            break;
        }
        case 'p': {
            u32 v = (u32)va_arg(ap, void *);
            n = utoa(v, end, 16, 0);
            put(o, '0');
            put(o, 'x');
            put_padded(o, end - n, n, 8, 0, '0');
            break;
        }
        case 'c':
            tmp[0] = (char)va_arg(ap, s32);
            put_padded(o, tmp, 1, width, left, ' ');
            break;
        case 's': {
            const char *s = va_arg(ap, const char *);
            u32 len = 0;
            if (s == 0 || ((u32)s & 0x80000000u) == 0) {
                s = "(null)";
            }
            while (s[len] && (prec < 0 || len < (u32)prec) && len < 256) {
                len++;
            }
            put_padded(o, s, len, width, left, ' ');
            break;
        }
        case 'f':
        case 'F':
        case 'e':
        case 'g':
        case 'G': {
            double d = va_arg(ap, double);
            u64 bits;
            __builtin_memcpy(&bits, &d, 8);
            put_double(o, bits, prec, width, left);
            break;
        }
        case '%':
            put(o, '%');
            break;
        case 0:
            return;
        default:
            put(o, '%');
            put(o, c);
            break;
        }
    }
}

/* 1 while the last message ended with a line break. */
static int sAtLineStart = 1;

/* Formats and sends one message. "prefix" starts a new line; a message that continues an
   unfinished line of the same kind gets no prefix ("[game] initializing ... done"). */
static void vmessage(const char *prefix, const char *fmt, va_list ap) {
    static const char *lastPrefix;
    u32 sr = disable_interrupts();
    Out o;
    o.buf = sLine;
    o.pos = 0;
    o.cap = sizeof(sLine) - 1;
    if (!sAtLineStart && lastPrefix != prefix) {
        put(&o, '\n');
        sAtLineStart = 1;
    }
    if (sAtLineStart) {
        const char *p = prefix;
        while (*p) {
            put(&o, *p++);
        }
    }
    lastPrefix = prefix;
    format(&o, fmt, ap);
    if (o.pos > 0) {
        sAtLineStart = sLine[o.pos - 1] == '\n';
        output(sLine, o.pos);
    }
    restore_interrupts(sr);
}

static void screen_add(const char *text, u32 length);

/* A message of the editor: to the debug port, the log and (if switched on) the screen. */
static void message(const char *fmt, ...) {
    va_list ap;
    u32 start;
    if (!(gHeader.flags & FLAG_DEBUG)) {
        return;
    }
    start = gHeader.logPos;
    va_start(ap, fmt);
    vmessage("[HH] ", fmt, ap);
    va_end(ap);
    if (gHeader.flags & FLAG_SCREEN) {
        /* the text as written (without the prefix, which takes room on the screen) */
        u32 skip = 0;
        while (skip < 5 && sLine[skip] == "[HH] "[skip]) {
            skip++;
        }
        screen_add(sLine + skip, gHeader.logPos - start - skip);
    }
}

/* A message of the editor only to the debug port and the log (not to the screen). */
static void message_quiet(const char *fmt, ...) {
    va_list ap;
    if (!(gHeader.flags & FLAG_DEBUG)) {
        return;
    }
    va_start(ap, fmt);
    vmessage("[HH] ", fmt, ap);
    va_end(ap);
}

/* Takes the place of the game's _uvDebugPrintf (through the gate in the code cave). */
int module_game_printf(const char *fmt, ...) {
    va_list ap;
    if (!(gHeader.flags & FLAG_GAME_MESSAGES) || fmt == 0) {
        return 0;
    }
    va_start(ap, fmt);
    vmessage("[game] ", fmt, ap);
    va_end(ap);
    return 0;
}

/* ------------------------------------------------------------------ on-screen output */

#include "font6x9.h"

#define SCREEN_W 320
#define SCREEN_H 240
#define OVL_COLS 50                  /* 6 pixels each, from x = 10 */
#define OVL_ROWS 14                  /* 9 pixels each */
#define OVL_X 10
#define OVL_BOTTOM 226               /* last pixel row used (inside the visible area of a TV) */
#define OVL_TICKS (15u * 46875000u)  /* 15 seconds of CPU count ticks */

#define GAME_GFX_CALLBACK (*(void *volatile *)0x80249230) /* gGfxCallback, called by uvGfxEnd */

typedef void (*FrameCallback)(u16 *frame, void *depth);

static char sScreen[OVL_ROWS][OVL_COLS + 1];
static int sScreenCount;   /* lines used */
static u32 sScreenTime;    /* CPU count of the last message */
static FrameCallback sOtherCallback;

static char sLiveLine[OVL_COLS + 1]; /* current values during a mission (see live_update) */
static u32 sLiveTime;                /* CPU count of the last flight frame */
#define LIVE_Y 30                    /* top pixel row of the live line (below the HUD's clock) */
#define LIVE_TICKS (46875000u / 2)   /* the line goes half a second after the last flight frame */

static void overlay(u16 *frame, void *depth);

/* Puts the overlay into the frame callback (keeping the one the game uses, e.g. for snow). */
static void overlay_install(void) {
    if (GAME_GFX_CALLBACK != (void *)overlay) {
        sOtherCallback = (FrameCallback)GAME_GFX_CALLBACK;
        GAME_GFX_CALLBACK = (void *)overlay;
    }
}

/* For weather.c: sets the game's frame callback (the snow), keeping the overlay in front of it while it is shown. */
void module_set_frame_callback(void *callback) {
    if (GAME_GFX_CALLBACK == (void *)overlay) {
        sOtherCallback = (FrameCallback)callback;
    } else {
        GAME_GFX_CALLBACK = callback;
    }
}

static void screen_newline(void) {
    int i;
    if (sScreenCount == OVL_ROWS) {
        for (i = 1; i < OVL_ROWS; i++) {
            int k;
            for (k = 0; k <= OVL_COLS; k++) {
                sScreen[i - 1][k] = sScreen[i][k];
            }
        }
        sScreenCount--;
    }
    sScreen[sScreenCount][0] = 0;
    sScreenCount++;
}

/* Adds text to the screen lines: breaks at '\n' and after OVL_COLS characters (continuation
   lines are indented), tabs become spaces. */
static void screen_add(const char *text, u32 length) {
    u32 i;
    int col = OVL_COLS; /* force a new line for the first character */
    for (i = 0; i < length; i++) {
        char c = text[i];
        if (c == '\n') {
            col = OVL_COLS;
            continue;
        }
        if (col >= OVL_COLS) {
            int wrapped = col == OVL_COLS && i > 0 && text[i - 1] != '\n';
            screen_newline();
            col = 0;
            if (wrapped) {
                sScreen[sScreenCount - 1][0] = ' ';
                sScreen[sScreenCount - 1][1] = ' ';
                col = 2;
            }
        }
        if (c == '\t') {
            c = ' ';
        }
        sScreen[sScreenCount - 1][col++] = (c >= 32 && c < 127) ? c : '?';
        sScreen[sScreenCount - 1][col] = 0;
    }

    sScreenTime = cpu_count();
    overlay_install();
}

/* One line of text at a pixel row, on a darkened background. */
static void draw_line(u16 *frame, int top, const char *line) {
    int col, y, x;
    int width = 0;
    while (line[width]) {
        width++;
    }
    for (y = 0; y < 9; y++) {
        u16 *p = frame + (top + y) * SCREEN_W + OVL_X - 2;
        for (x = 0; x < width * 6 + 4; x++) {
            p[x] = ((p[x] >> 2) & 0x39CE) | 1; /* a quarter of the brightness */
        }
    }
    for (col = 0; col < width; col++) {
        const u8 *glyph = sFont6x9[line[col] - 32];
        for (y = 0; y < 9; y++) {
            u8 bits = glyph[y];
            u16 *p = frame + (top + y) * SCREEN_W + OVL_X + col * 6;
            for (x = 0; x < 6; x++) {
                if (bits & (0x20 >> x)) {
                    p[x] = 0xFFFF;
                }
            }
        }
    }
}

/* Called by uvGfxEnd with the frame that was just drawn (RGBA 5551, 320 x 240): darkens the
   lines' background and draws the text. After 15 seconds without a new message the callback
   the game had before is put back. */
static void overlay(u16 *frame, void *depth) {
    int row;
    int top;
    u32 now = cpu_count();
    int messages, live;
    if (sOtherCallback != 0 && sOtherCallback != overlay) {
        sOtherCallback(frame, depth);
    }
    messages = sScreenCount > 0 && now - sScreenTime <= OVL_TICKS;
    live = sLiveLine[0] != 0 && now - sLiveTime <= LIVE_TICKS;
    if (!messages) {
        sScreenCount = 0;
    }
    if (!messages && !live) {
        if (GAME_GFX_CALLBACK == (void *)overlay) {
            GAME_GFX_CALLBACK = (void *)sOtherCallback;
        }
        return;
    }

    if (live) {
        draw_line(frame, LIVE_Y, sLiveLine);
    }
    top = OVL_BOTTOM + 1 - sScreenCount * 9;
    for (row = 0; row < sScreenCount; row++) {
        draw_line(frame, top + row * 9, sScreen[row]);
    }
}

/* ------------------------------------------------------------------ conditions */

#define GAME_TASK_SCORING ((u8 * (*)(void))0x80345C80)  /* taskGet_80345C80: COMM + 0x48 */
#define GAME_TASK_MODE    ((s32(*)(void))0x80346364)    /* taskGet_80346364: COMM byte 4 */

typedef s32 (*CountFunction)(void);
typedef s32 (*TotalFunction)(void **);

/* Cleared objects and their number, per type (as in func_8032BF54). */
static const CountFunction sCleared[OBJECT_TYPES] = {
    0,                         /* rings: counted below */
    0,                         /* balloons: counted below */
    (CountFunction)0x803448F4, /* targets destroyed */
    (CountFunction)0x8030A080, /* hover pads used */
    (CountFunction)0x802E57C4, /* Meca Hawks destroyed */
    (CountFunction)0x802D30B4, /* ball targets */
    (CountFunction)0x802FB5A0, /* hopper goals reached */
};
static const TotalFunction sTotal[OBJECT_TYPES] = {
    0,
    0,
    (TotalFunction)0x80345BDC, /* taskGetTARG */
    (TotalFunction)0x80345BFC, /* taskGetHPAD */
    0,                         /* Meca Hawks: D_8035A5F0 */
    (TotalFunction)0x80345C1C, /* taskGetBTGT */
    (TotalFunction)0x80346474, /* taskGetHOPD */
};

#define GAME_BALL_COUNT  (*(volatile u8 *)0x80357210)
#define GAME_BALL_SPLITS (*(volatile u8 *)0x80357211)
#define GAME_BALLS       ((volatile u8 *)0x80357218) /* gBalls, 0x1AC bytes each, hasPopped at +0x94 */
#define GAME_FALCO_COUNT REG32(0x8035A5F0)

#define GAME_RING_COUNT  (*(volatile u8 *)0x8036DA74) /* sRingsCount */
#define GAME_RINGS       ((volatile u8 *)0x8036DA78) /* gRings, 0x1CC bytes each */
#define RING_SIZE        0x1CC
#define RING_SCORE_TYPE  0x146 /* 2: invisible check ring */
#define RING_CONSUMED    0x1B4

static void count_objects(int type, s32 *cleared, s32 *total) {
    void *unused = 0;
    if (type == 0) {
        /* As ringsGetCleared, but without the invisible check rings (score type 2): the
           player cannot see them, they only unlock other rings. */
        int i, n = GAME_RING_COUNT;
        *cleared = 0;
        *total = 0;
        for (i = 0; i < n && i < 30; i++) {
            volatile u8 *ring = GAME_RINGS + i * RING_SIZE;
            if (ring[RING_SCORE_TYPE] == 2) {
                continue;
            }
            (*total)++;
            if (ring[RING_CONSUMED]) {
                (*cleared)++;
            }
        }
        return;
    }
    if (type == 1) {
        int i, n = GAME_BALL_COUNT + GAME_BALL_SPLITS;
        *cleared = 0;
        for (i = 0; i < n; i++) {
            if (GAME_BALLS[i * 0x1AC + 0x94]) {
                (*cleared)++;
            }
        }
        *total = n;
        return;
    }
    *cleared = sCleared[type]() & 0xFF;
    *total = type == 4 ? (s32)GAME_FALCO_COUNT : (sTotal[type](&unused) & 0xFF);
}

static const char *const sTypeNames[OBJECT_TYPES] = {
    "rings", "balloons", "targets", "hover pads", "Meca Hawks", "ball targets", "hopper goals",
};

/* A float (given as its bits) times 256, without the FPU (fixed point with 8 fraction bits). */
static s32 float_to_fixed8(s32 bits) {
    int exp = ((bits >> 23) & 0xFF) - 127 - 15; /* the mantissa has 23 fraction bits, we keep 8 */
    u32 mant = ((u32)bits & 0x7FFFFF) | 0x800000;
    s32 value;
    if (((bits >> 23) & 0xFF) == 0) {
        return 0;
    }
    if (exp > 7) {
        return bits < 0 ? -0x7FFFFFFF : 0x7FFFFFFF;
    }
    value = (s32)(exp >= 0 ? mant << exp : (exp > -31 ? mant >> -exp : 0));
    return bits < 0 ? -value : value;
}

/* Game units (float bits) to the meters the game shows (x 0.7), rounded. */
static s32 units_to_meters(s32 bits) {
    s32 f = float_to_fixed8(bits);
    s32 m = (f / 16) * 7 / 160; /* f / 256 * 0.7, without overflow up to 300 km */
    return m;
}

/* The player's flight data (Unk80362690_Unk0 of the active player): distance flown at +0x0C,
   highest altitude at +0x10, position matrix at +0x2C (x, y, z at +0x5C, +0x60, +0x64). */
#define GAME_STATE (*(u8 *volatile *)0x80362690) /* D_80362690 */
#define GAME_RESULT (*(u8 *volatile *)0x8034FBD0) /* D_8034FBD0: the flight result (crash at +0x3C) */
static u8 *player_flight(void) {
    u8 *state = GAME_STATE;
    if (state == 0) {
        return 0;
    }
    return state + 0x0C + *(s32 *)(state + 0x9C) * 0x8C;
}

/* Called by the scoring function (func_8032B560) instead of taskGet_80346364, after the crash
   and hard end overrides. "result" is the flight result (flags at +0, highest altitude at
   +0x18, crash at +0x3C). Missions without an entry keep the retail condition mode. */
/* The table entry of the mission being flown, or 0. */
static const Condition *current_condition(void) {
    u8 *scoring = GAME_TASK_SCORING();
    u8 *comm = scoring - 0x48;
    u32 i;
    /* Outside a mission (title screen, menus) there is no mission data: the pointer is not in RAM. */
    if (DATA_HEADER->magic != DATA_MAGIC || (u32)scoring < 0x80000400u + 0x48 || (u32)scoring >= 0x80800000u) {
        return 0;
    }
    for (i = 0; i < DATA_HEADER->missions && i < MAX_CONDITIONS; i++) {
        if (gConditions[i].cls == comm[0] && gConditions[i].vehicle == comm[1] && gConditions[i].test == comm[2]) {
            return &gConditions[i];
        }
    }
    return 0;
}

/* For weather.c: the entry of the mission being flown if it has weather settings, or 0. */
const void *module_weather_entry(void) {
    const Condition *c = current_condition();
    return c != 0 && c->weather != 0 ? (const void *)c : 0;
}

/* For weather.c: a debug message with one number. */
void module_message_int(const char *fmt, s32 value) {
    if (gHeader.flags & FLAG_DEBUG) {
        message(fmt, value);
    }
}

static int has_required(const Condition *c) {
    int t;
    for (t = 0; t < OBJECT_TYPES; t++) {
        if (c->required[t]) {
            return 1;
        }
    }
    return 0;
}

#define GAME_TASK_GOAL ((u8(*)(void))0x80346468) /* taskGet_80346468: the goal ring or the ball target ended the mission */
#define SCORE_TIME      0x800u
#define SCORE_GOALS     0x30000u /* ball target 0x10000, hopper goal 0x20000 */
#define SCORE_IN_THE_AIR 0x3FFF8u /* everything but landing accuracy (1, 2) and impact (4) */
#define SCORE_NO_LANDING 0xF7F8u  /* the vehicles' flags after a flight without a good landing */

/* The time of the crash of the current flight (f32 bits, the vehicle's flight time at the impact), set by
   module_flight_frame; sImpactSeen: the vehicle has hit something and is tumbling or has crashed. */
static u32 sCrashTime;
static int sCrashTimeSet;
static int sImpactSeen;

s32 module_conditions(u8 *result) {
    const Condition *c = current_condition();
    u32 *flags = (u32 *)result;
    u8 *state = GAME_STATE;
    /* A crash: marked by the game, or an impact while the vehicle still tumbles (the hard end time can come
       first; the game would then score it like a flight without a crash). */
    int crashed = result[0x3C] != 0 || sImpactSeen;
    int hardEnd = state != 0 && state[0xA1] != 0;
    u32 i;
    int met = 1;

    message("Scoring: flags 0x%X%s%s%s.\n", *flags, crashed ? ", crash" : "", hardEnd ? ", hard end time" : "",
            GAME_TASK_GOAL() ? ", goal reached" : "");

    /* The hard end time came while the vehicle tumbled after an impact: scored as the crash it is. */
    if (crashed && result[0x3C] == 0) {
        result[0x3C] = 1;
        *flags = 0x3F7F8u;
        message("Crash before the hard end time: scored as a crash.\n");
    }

    /* Hard end time (the game has set the flags to "objects only"): per mission 0 points or
       scored like a flight that ends in the air. A crash keeps the crash rules. */
    if (hardEnd && !crashed && c != 0 && (c->flags & (COND_END_SCORED | COND_END_ZERO))) {
        if (c->flags & COND_END_ZERO) {
            message("Hard end time reached: 0 points.\n");
            *flags = 0;
            return 0;
        }
        *flags = SCORE_IN_THE_AIR;
        message("Hard end time reached: scored without landing.\n");
    }

    /* The goal ring or the ball target ended the mission: time points count (0 without a time curve).
       Hang Glider and Birdman set their flags only when they land, so in the air they are still 0:
       then the flags of a flight without a good landing (objects, deductions, altitude, distance). */
    if (!crashed && !hardEnd && GAME_TASK_GOAL()) {
        if (*flags == 0) {
            *flags = SCORE_NO_LANDING;
        }
        *flags |= SCORE_TIME;
        message("Goal reached: time points count.\n");
    }

    /* Game patch "goal objects": the vehicles pass only 16 bits of flags, so these two sources
       would only count after a crash or the hard end. */
    if ((gHeader.flags & FLAG_GOAL_OBJECTS) && *flags != 0) {
        *flags |= SCORE_GOALS;
    }

    if (c == 0 || !HAS_POINT_CONDITIONS(c)) {
        return GAME_TASK_MODE();
    }

    if ((c->flags & COND_CRASH_ZERO) && result[0x3C]) {
        message("Conditions: crash, 0 points.\n");
        *flags = 0;
        return 0;
    }
    /* Time points after a crash: the time curve gets the time of the crash (recorded in the flight frame;
       the game would use the time the wreck came to rest). The conditions below still apply. */
    if ((c->flags & COND_CRASH_TIME) && crashed) {
        *flags |= SCORE_TIME;
        if (sCrashTimeSet) {
            *(u32 *)(result + 0x1C) = sCrashTime;
        }
        message("Conditions: crash at %d.%d s, time points count.\n", float_to_fixed8((s32) * (u32 *)(result + 0x1C)) >> 8,
                ((float_to_fixed8((s32) * (u32 *)(result + 0x1C)) & 0xFF) * 10) >> 8);
    }
    for (i = 0; i < OBJECT_TYPES; i++) {
        s32 cleared, total, need;
        if (c->required[i] == 0) {
            continue;
        }
        count_objects((int)i, &cleared, &total);
        need = c->required[i] == REQUIRED_ALL ? total : c->required[i];
        message("Conditions: %s %d of %d, %d needed.\n", sTypeNames[i], cleared, total, need);
        if (cleared < need) {
            met = 0;
        }
    }
    if (c->flags & COND_ALTITUDE) {
        s32 reached = *(s32 *)(result + 0x18);
        s32 limit = (s32)c->altitude;
        /* non-negative floats compare like integers */
        int high = reached >= 0 && reached >= limit;
        message("Conditions: highest altitude %d m, %d m needed: %s.\n", units_to_meters(reached), units_to_meters(limit),
                high ? "reached" : "not reached");
        if (!high) {
            met = 0;
        }
    }
    if (c->flags & COND_DISTANCE) {
        s32 flown = *(s32 *)(result + 0x14);
        s32 limit = (s32)c->distance;
        int far = flown >= 0 && flown >= limit;
        message("Conditions: distance %d m, %d m needed: %s.\n", units_to_meters(flown), units_to_meters(limit),
                far ? "reached" : "not reached");
        if (!far) {
            met = 0;
        }
    }
    if (!met) {
        *flags &= ~0x800u;
    }
    message("Conditions: time points %s.\n", met ? "count" : "do not count");
    return 0;
}

/* The goal ring or the ball target ends the mission (taskFrameUpdate, game patch "goal objects"):
   the jingle of the vehicle being flown. The game played the Rocket Belt's for every vehicle (and
   changed the vehicle to the Rocket Belt, which the patch removes). */
#define GAME_SND_SET_MUSIC ((void (*)(s32))0x8033F748) /* sndSetMusic */
static const u8 sGoalJingle[7] = {
    0x05, /* Hang Glider: landing OK */
    0x09, /* Rocket Belt: landing OK */
    0x0D, /* Gyrocopter: landing OK */
    0x11, /* Cannonball: hit */
    0x14, /* Sky Diving: landing OK */
    0x18, /* Jumble Hopper: goal */
    0x1A, /* Birdman: landing OK */
};

void module_goal_jingle(s32 music) {
    u8 *state = GAME_STATE;
    u32 vehicle = state != 0 ? *(u16 *)(state + 0x0E) : 1;
    (void)music;
    GAME_SND_SET_MUSIC(vehicle < 7 ? sGoalJingle[vehicle] : 0x09);
}

/* Game patch "Bonus games and Birdman": the Birdman missions with points (lower data area +8: per stage a bit per test,
   written by the editor). The patched result, total and test screens read it too (module_bm_* below). */

/* The missions with points of a Birdman stage (bit = test), 0 for none. */
static u32 birdman_stage(u32 cls) {
    return EXT_OK && cls < MAX_CLASSES ? EXT_BIRDMAN(cls) & 0x1Fu : 0;
}

static u32 bit_count(u32 mask) {
    u32 n = 0;
    for (; mask != 0; mask >>= 1) {
        n += mask & 1;
    }
    return n;
}

/* The test of the n-th mission with points (counting from 0). */
static u32 nth_bit(u32 mask, u32 n) {
    u32 t;
    for (t = 0; t < MAX_TESTS; t++) {
        if ((mask >> t) & 1) {
            if (n == 0) {
                return t;
            }
            n--;
        }
    }
    return n + MAX_TESTS; /* not there: past the missions, as the game would count */
}

/* The stage of the current player if it is a Birdman stage with points, else 0 (mask of its missions with points). */
static u32 birdman_player_stage(void);

/* ------------------------------------------------------------------ medal limits per vehicle and class */

/* The Classes tab: medal limits per vehicle and class (data area header +0x18 = address of the class table, 0 = none).
   Table (lower data area): u16 limits[7 vehicles][8 classes][3] (bronze, silver, gold), then u8 flags[7][8]: 1 own limits, 2 own medal
   texts of the total screen. Without a table the game's table counts (gMedalPointRequirements, 0x8034FBD4: entry =
   class for the main vehicles, vehicle + 1 for the others). A Birdman stage with points and without own limits has
   70/80/90 per mission with points. (Names from the decompilation, https://github.com/gcsmith/Pilotwings64Decomp,
   MIT license.) */
#define CLASS_TABLE_ADDRESS (*(volatile u32 *)(DATA_AREA + 0x18))
#define GAME_MEDAL_TABLE    ((volatile u16 *)0x8034FBD4) /* gMedalPointRequirements */
#define GAME_TEST_COUNT     ((s32(*)(s32, s32))0x80344EF0) /* taskGetTestCount(class, vehicle) */
#define GAME_LEVEL_TOTAL    ((s32(*)(void *, u32, u32))0x8032BD20) /* levelGetTotalPoints(results, class, vehicle) */

static const u16 *class_table(void) {
    u32 a = CLASS_TABLE_ADDRESS;
    return DATA_HEADER->magic == DATA_MAGIC && a >= EXT_AREA && a + 7 * 8 * 7 <= EXT_AREA_END && EXT_OK ? (const u16 *)a : 0;
}

static u32 class_flags(u32 veh, u32 cls) {
    const u16 *t = class_table();
    return t != 0 && veh < 7 && cls < MAX_CLASSES ? ((const u8 *)t + 7 * 8 * 6)[veh * 8 + cls] : 0;
}

static void class_limits(u32 veh, u32 cls, s32 out[3]) {
    const u16 *t = class_table();
    u32 stage;
    s32 i;
    if (veh > 6 || cls >= MAX_CLASSES) {
        out[0] = out[1] = out[2] = 0x7FFF;
        return;
    }
    if (t != 0 && (class_flags(veh, cls) & 1)) {
        for (i = 0; i < 3; i++) {
            out[i] = t[(veh * 8 + cls) * 3 + i];
        }
        return;
    }
    if (veh == 6 && (stage = birdman_stage(cls)) != 0) {
        s32 n = (s32)bit_count(stage);
        out[0] = 70 * n;
        out[1] = 80 * n;
        out[2] = 90 * n;
        return;
    }
    if (t != 0) {
        for (i = 0; i < 3; i++) {
            out[i] = t[(veh * 8 + cls) * 3 + i];
        }
        return;
    }
    for (i = 0; i < 3; i++) {
        out[i] = GAME_MEDAL_TABLE[(veh < 3 ? (cls < 4 ? cls : 3) : veh + 1) * 3 + i];
    }
}

/* Points for a perfect score: 100 per test (bonus games 100 per level, a Birdman stage with points 100 per mission with
   points). */
static s32 class_perfect(u32 veh, u32 cls) {
    u32 stage = veh == 6 ? birdman_stage(cls) : 0;
    if (stage != 0) {
        return 100 * (s32)bit_count(stage);
    }
    if (veh >= 3 && veh <= 5) {
        return 100;
    }
    return 100 * GAME_TEST_COUNT((s32)cls, (s32)veh);
}

/* The medal of a total: 0 none, 1 bronze, 2 silver, 3 gold (no check that every test was played: only the sums
   count, as the editor's unlock rules). */
static s32 class_medal_of(u32 veh, u32 cls, s32 total) {
    s32 lim[3];
    class_limits(veh, cls, lim);
    return total >= lim[2] ? 3 : total >= lim[1] ? 2 : total >= lim[0] ? 1 : 0;
}

static u8 *player_results(void) {
    u8 *state = GAME_STATE;
    return (u8 *)0x80364210 + (state != 0 ? *(s32 *)(state + 0x9C) : 0) * 0x34E0; /* D_80364210[player] */
}

static s32 class_medal(u8 *results, u32 veh, u32 cls) {
    return class_medal_of(veh, cls, GAME_LEVEL_TOTAL(results, cls, veh));
}

/* "Points to the next medal" (levelSetPointsToNextMedal, 0x8032C174: s32 f(s32 *out, u16 points, u8 medal); returns 0
   bronze, 1 silver, 2 gold to get, 3 all reached, with *out = the points still missing). Every caller asks about the
   class being played, so the limits are those of the player's vehicle and class; the medal index of the caller only
   counts without a player. The original wrote "100 x tests - points" as the points to a perfect score only for the
   main vehicles (and the bonus games had one test); here every class gets its own perfect score (class_perfect).
   Called through the game patch "Bonus games and Birdman" (its eight calls) and, with a class table, from the
   function's entry. */
s32 module_points_to_medal(s32 *out, u32 points, u32 medal) {
    u8 *p = player_flight();
    s32 v = (s32)(points & 0xFFFF);
    s32 lim[3];
    u32 veh, cls;
    s32 i;
    medal &= 0xFF;
    if (p != 0) {
        veh = *(u16 *)(p + 2);
        cls = *(u16 *)(p + 4);
    } else if (medal < 4) {
        veh = 0;
        cls = medal;
    } else {
        veh = medal - 1;
        cls = 0;
    }
    class_limits(veh, cls, lim);
    for (i = 0; i < 3; i++) {
        if (v < lim[i]) {
            *out = lim[i] - v;
            return i;
        }
    }
    *out = class_perfect(veh, cls) - v;
    return 3;
}

/* The lowest medal over the classes (func_8032C3C4, used by the ending): flags 1 from the Hang Glider on (else from the
   Cannonball), 2 up to all vehicles (else up to the Cannonball); the Birdman is left out. Returns the game's MEDAL_*
   of the first medal still missing: 0 bronze, 1 silver, 2 gold, 3 none (every gold reached). */
s32 module_lowest_medal(u8 *results, u32 flags) {
    u32 first = (flags & 1) ? 0 : 3;
    u32 last = (flags & 2) ? 7 : 3;
    s32 lowest = 3;
    u32 veh, cls;
    if (first == last) {
        return 0;
    }
    for (veh = first; veh < last; veh++) {
        if (veh == 6) {
            continue;
        }
        for (cls = 0; cls < (veh < 3 ? 4u : 3u); cls++) {
            s32 m = class_medal(results, veh, cls);
            if (m < lowest) {
                lowest = m;
            }
        }
    }
    return lowest;
}

/* The level select (level_select.c): func_8030D9C8 (page 1, the license classes) and func_8030DED0 (page 2, bonus
   games and Birdman) jump here. They set the medal sprites of the fields and the arrays the cursor and the drawing
   use: D_8036A8CC[3] highest open class per vehicle, D_8036A8D0[3] highest open level per bonus game (-1 closed),
   D_8036A8D8[4] silver with all three vehicles in a class ([0] at the end: some Birdman stage open), D_8036A8D4[4]
   Birdman stage open. The rules are the original ones, with the limits of each vehicle and class and without the
   check that every test was played. */
#define GAME_SPRITE    ((void (*)(s32, ...))0x80231158) /* uvSprtProps(id, props..., 0) */
#define LS_CLASS_OPEN  ((volatile u8 *)0x8036A8CC)
#define LS_LEVEL_OPEN  ((volatile signed char *)0x8036A8D0)
#define LS_STAGE_OPEN  ((volatile u8 *)0x8036A8D4)
#define LS_SILVER      ((volatile u8 *)0x8036A8D8)
#define LS_ALL_OPEN    (*(volatile u8 *)0x8034F7BC) /* debug flag of the developers, always 0 */
#define LS_UNUSED      (*(volatile signed char *)0x8034F7B4)
#define SP_END 0
#define SP_DIM 1
#define SP_POS 2
#define SP_ENABLED 3
#define SP_TEX 5
#define SP_COLOR 7

static s32 menu_texture(s32 medal) {
    return medal == 3 ? 0x14E : medal == 2 ? 0x14F : medal == 1 ? 0x150 : 0x14D;
}

/* Unlock conditions of the 25 menu fields (Menu tab; data area header +0x1C = address of the table, 0 = the original
   rules). Fields: 0-11 license classes (vehicle * 4 + class), 12-20 bonus levels (12 + (vehicle - 3) * 3 + level),
   21-24 Birdman stages. Table: u8 count[25] (padded to 0x20), then per field 8 conditions of 3 bytes: field | 0x80 for a
   medal, u16 value (points, or the medal: 1 bronze, 2 silver, 3 gold, 4 perfect). A field is open when all its
   conditions are met; only the sums of the fields count. */
#define UNLOCK_TABLE_ADDRESS (*(volatile u32 *)(DATA_AREA + 0x1C))
#define FIELDS 25

static const u8 kFieldVehicle[FIELDS] = { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6, 6 };
static const u8 kFieldClass[FIELDS] = { 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 3 };

static const u8 *unlock_table(void) {
    u32 a = UNLOCK_TABLE_ADDRESS;
    return DATA_HEADER->magic == DATA_MAGIC && a >= DATA_AREA && a + 0x20 + FIELDS * 24 <= DATA_AREA_END ? (const u8 *)a : 0;
}

static u32 field_of(u32 veh, u32 cls) {
    return veh < 3 ? veh * 4 + cls : veh < 6 ? 12 + (veh - 3) * 3 + cls : 21 + cls;
}

/* The medal of a field: 0 none, 1 bronze, 2 silver, 3 gold, 4 perfect. */
static s32 field_medal(u8 *r, u32 f, s32 *total) {
    u32 veh = kFieldVehicle[f], cls = kFieldClass[f];
    s32 t = GAME_LEVEL_TOTAL(r, cls, veh);
    s32 m = class_medal_of(veh, cls, t);
    if (m == 3 && t >= class_perfect(veh, cls)) {
        m = 4;
    }
    *total = t;
    return m;
}

static s32 field_open(u8 *r, u32 f) {
    const u8 *t = unlock_table();
    u32 i, n;
    if (t == 0 || f >= FIELDS) {
        return 1;
    }
    n = t[f] > 8 ? 8 : t[f];
    for (i = 0; i < n; i++) {
        const u8 *e = t + 0x20 + (f * 8 + i) * 3;
        u32 g = e[0] & 0x7F;
        s32 value = (s32)((e[1] << 8) | e[2]);
        s32 total, medal;
        if (g >= FIELDS) {
            continue;
        }
        medal = field_medal(r, g, &total);
        if ((e[0] & 0x80) ? medal < value : total < value) {
            return 0;
        }
    }
    return 1;
}

/* The arrays the drawing of the level select and the file menu use, from the conditions. */
static void unlock_arrays(u8 *r) {
    s32 g, l, v, c, s;
    for (v = 0; v < 3; v++) {
        s32 top = 0;
        for (c = 0; c < 4; c++) {
            if (field_open(r, field_of((u32)v, (u32)c))) {
                top = c;
            }
        }
        LS_CLASS_OPEN[v] = (u8)top;
    }
    for (g = 0; g < 3; g++) {
        s32 top = -1, any = 0;
        for (l = 0; l < 3; l++) {
            if (field_open(r, field_of((u32)g + 3, (u32)l))) {
                top = l;
                any = 1;
            }
        }
        LS_LEVEL_OPEN[g] = (signed char)top;
        LS_SILVER[g + 1] = (u8)any;
    }
    for (s = 0; s < 4; s++) {
        LS_STAGE_OPEN[s] = (u8)field_open(r, (u32)(21 + s));
    }
    LS_SILVER[0] = (u8)(LS_STAGE_OPEN[0] | LS_STAGE_OPEN[1] | LS_STAGE_OPEN[2] | LS_STAGE_OPEN[3]);
}

static void menu_common(u8 *r) {
    s32 i, v;
    for (i = 0; i < 4; i++) {
        s32 ok = 1;
        for (v = 0; v < 3; v++) {
            if (class_medal(r, (u32)v, (u32)i) < 2) {
                ok = 0;
                break;
            }
        }
        LS_SILVER[i] = (u8)ok;
    }
    LS_STAGE_OPEN[0] = LS_SILVER[0];
    for (i = 1; i < 4; i++) {
        s32 ok = 0;
        if (LS_SILVER[i] != 0) {
            s32 l;
            ok = 1;
            for (l = 0; l < 3; l++) {
                if (class_medal(r, (u32)i + 2, (u32)l) < 2) {
                    ok = 0;
                    break;
                }
            }
        }
        LS_STAGE_OPEN[i] = (u8)ok;
    }
    LS_SILVER[0] = (u8)(LS_STAGE_OPEN[0] | LS_STAGE_OPEN[1] | LS_STAGE_OPEN[2] | LS_STAGE_OPEN[3]);
}

void module_menu_page1(void) {
    u8 *r = player_results();
    s32 i, v, c;
    if (unlock_table() != 0) {
        for (i = 0; i < 12; i++) {
            GAME_SPRITE(i, SP_ENABLED, 1, SP_DIM, 46, 31, SP_POS, (i % 3) * 48 + 78, 161 - (i / 3) * 34, SP_TEX, 332,
                        SP_COLOR, 255, 255, 255, 255, SP_END);
        }
        for (i = 0; i < 3; i++) {
            GAME_SPRITE(i + 12, SP_ENABLED, 1, SP_DIM, 46, 31, SP_POS, 236, 126 - i * 34, SP_COLOR, 255, 255, 255, 255,
                        SP_TEX, 332, SP_END);
        }
        for (v = 0; v < 3; v++) {
            for (c = 0; c < 4; c++) {
                GAME_SPRITE(v + c * 3, SP_TEX, field_open(r, field_of((u32)v, (u32)c)) ? menu_texture(class_medal(r, (u32)v, (u32)c)) : 0x14C,
                            SP_END);
            }
        }
        unlock_arrays(r);
        return;
    }
    for (i = 0; i < 12; i++) {
        GAME_SPRITE(i, SP_ENABLED, 1, SP_DIM, 46, 31, SP_POS, (i % 3) * 48 + 78, 161 - (i / 3) * 34, SP_TEX, 332,
                    SP_COLOR, 255, 255, 255, 255, SP_END);
    }
    for (i = 0; i < 3; i++) {
        GAME_SPRITE(i + 12, SP_ENABLED, 1, SP_DIM, 46, 31, SP_POS, 236, 126 - i * 34, SP_COLOR, 255, 255, 255, 255,
                    SP_TEX, 332, SP_END);
    }
    for (v = 0; v < 3; v++) {
        for (c = 0; c < 4; c++) {
            s32 tex = menu_texture(class_medal(r, (u32)v, (u32)c));
            GAME_SPRITE(v + c * 3, SP_TEX, tex, SP_END);
            if (tex == 0x14D) {
                break;
            }
        }
        LS_CLASS_OPEN[v] = (u8)(LS_ALL_OPEN != 0 ? 3 : (c > 3 ? 3 : c));
    }
    if (LS_CLASS_OPEN[0] == 0 || LS_CLASS_OPEN[1] == 0 || LS_CLASS_OPEN[2] == 0) {
        for (i = 0; i < 3; i++) {
            if (LS_CLASS_OPEN[i] > 0) {
                LS_UNUSED = 0;
                GAME_SPRITE(LS_CLASS_OPEN[i] * 3 + i, SP_TEX, 0x14C, SP_END);
                if (LS_ALL_OPEN == 0) {
                    LS_CLASS_OPEN[i] = 0;
                }
            }
        }
    }
    menu_common(r);
}

void module_menu_page2(void) {
    u8 *r = player_results();
    s32 i, g, l;
    for (i = 0; i < 9; i++) {
        GAME_SPRITE(i, SP_ENABLED, 1, SP_DIM, 46, 31, SP_POS, (i % 3) * 48 + 109, 127 - (i / 3) * 34, SP_TEX, 332,
                    SP_COLOR, 255, 255, 255, 255, SP_END);
    }
    for (i = 0; i < 4; i++) {
        GAME_SPRITE(i + 9, SP_ENABLED, 1, SP_DIM, 33, 26, SP_POS, 109 + i * 36, 195, SP_COLOR, 255, 255, 255, 255,
                    SP_TEX, 332, SP_END);
    }
    for (i = 0; i < 3; i++) {
        GAME_SPRITE(i + 13, SP_ENABLED, 1, SP_DIM, 46, 31, SP_POS, 61, 127 - i * 34, SP_COLOR, 255, 255, 255, 255,
                    SP_TEX, 332, SP_END);
    }
    if (unlock_table() != 0) {
        for (g = 0; g < 3; g++) {
            for (l = 0; l < 3; l++) {
                GAME_SPRITE(g * 3 + l, SP_TEX, field_open(r, field_of((u32)g + 3, (u32)l)) ? menu_texture(class_medal(r, (u32)g + 3, (u32)l)) : 0x14C,
                            SP_END);
            }
        }
        unlock_arrays(r);
        return;
    }
    menu_common(r); /* [1..3] of LS_SILVER stay the silver of Class A/B/Pilot, which opens the bonus games */
    for (g = 0; g < 3; g++) {
        signed char open;
        if (LS_SILVER[g + 1] != 0) {
            for (l = 0; l < 3; l++) {
                s32 tex = menu_texture(class_medal(r, (u32)g + 3, (u32)l));
                GAME_SPRITE(g * 3 + l, SP_TEX, tex, SP_END);
                if (tex == 0x14D) {
                    break;
                }
            }
            open = (signed char)(LS_ALL_OPEN != 0 ? 2 : l);
        } else {
            open = (signed char)(LS_ALL_OPEN != 0 ? 2 : -1);
        }
        if ((u8)open > 2) {
            open = 2;
        }
        LS_LEVEL_OPEN[g] = open;
    }
}

/* The file menu (fileMenu_802E8AF0): the medals of the license classes, the locks of the bonus games
   (sFileMenu_803624E0[3], 1 = closed) and whether the Birdman icon shows (sFileMenu_803624E3). */
#define FM_GAME_CLOSED ((volatile u8 *)0x803624E0)
#define FM_BIRDMAN     (*(volatile u8 *)0x803624E3)

void module_file_menu(void) {
    u8 *r = player_results();
    s32 i, j, k, any;
    for (i = 0; i < 12; i++) {
        GAME_SPRITE(i, SP_ENABLED, 1, SP_DIM, 38, 22, SP_POS, (i % 3) * 40 + 93, 123 - (i / 3) * 25,
                    SP_COLOR, 255, 255, 255, 255, SP_TEX, 0x148, SP_END);
    }
    for (i = 0; i < 3; i++) {
        GAME_SPRITE(i + 12, SP_ENABLED, 1, SP_DIM, 38, 22, SP_POS, 229, 98 - 25 * i, SP_COLOR, 255, 255, 255, 255,
                    SP_TEX, 0x148, SP_END);
    }
    for (j = 0; j < 3; j++) {
        for (k = 0; k < 4; k++) {
            s32 m = class_medal(r, (u32)j, (u32)k);
            GAME_SPRITE(k * 3 + j, SP_TEX, m == 3 ? 0x14B : m == 2 ? 0x14A : m == 1 ? 0x149 : 0x148, SP_END);
        }
    }
    if (unlock_table() != 0) {
        for (i = 0; i < 3; i++) {
            GAME_SPRITE(i + 12, SP_ENABLED, 1, SP_TEX, 0x148, SP_END);
            FM_GAME_CLOSED[i] = (u8)!(field_open(r, field_of((u32)i + 3, 0)) || field_open(r, field_of((u32)i + 3, 1))
                                     || field_open(r, field_of((u32)i + 3, 2)));
        }
        FM_BIRDMAN = (u8)(field_open(r, 21) || field_open(r, 22) || field_open(r, 23) || field_open(r, 24));
        return;
    }
    for (i = 0; i < 3; i++) {
        GAME_SPRITE(i + 12, SP_ENABLED, 1, SP_TEX, 0x148, SP_END);
        FM_GAME_CLOSED[i] = (u8)!(class_medal(r, 0, (u32)i + 1) >= 2 && class_medal(r, 1, (u32)i + 1) >= 2
                                 && class_medal(r, 2, (u32)i + 1) >= 2);
    }
    any = class_medal(r, 0, 0) >= 2 && class_medal(r, 1, 0) >= 2 && class_medal(r, 2, 0) >= 2;
    if (!any) {
        for (i = 0; i < 3 && !any; i++) {
            if (FM_GAME_CLOSED[i] == 0 && class_medal(r, (u32)i + 3, 0) >= 2 && class_medal(r, (u32)i + 3, 1) >= 2
                && class_medal(r, (u32)i + 3, 2) >= 2) {
                any = 1;
            }
        }
    }
    FM_BIRDMAN = (u8)any;
}

/* The cursor of the level select with unlock conditions (func_8030EA54, ROM 0x95F84, jumps here): the original
   moves only through prefix-open classes and levels and reaches the bonus games through the Gyrocopter's column, so with
   free conditions the module moves the cursor itself, from open field to open field: in the direction of the stick to
   the nearest open field (nearest row or column first, then the nearest one across); past the right edge of page 1 to
   page 2 and past the left edge of page 2 back. A locked field cannot be chosen; if the cursor stands on one (the
   first field when the menu opens), it moves to the first open field. State as the original: D_8034F7A0 column,
   D_8034F7A4 row, D_8034F7B0 page, D_8034F7B8 Birdman row, D_8034F7A8 cell (column + row * 3, returned when a field
   is chosen), D_8034F7AC "first frame" (no sound), D_8036A8C0/C4 cursor position (f32), D_8036A8C8-CA stick state. */
#define LS_COL    (*(volatile signed char *)0x8034F7A0)
#define LS_ROW    (*(volatile signed char *)0x8034F7A4)
#define LS_CELL   (*(volatile u8 *)0x8034F7A8)
#define LS_FIRST  (*(volatile u8 *)0x8034F7AC)
#define LS_PAGE   (*(volatile u8 *)0x8034F7B0)
#define LS_BIRD   (*(volatile u8 *)0x8034F7B8)
#define LS_X      (*(volatile u32 *)0x8036A8C0)
#define LS_Y      (*(volatile u32 *)0x8036A8C4)
#define LS_DX     (*(volatile signed char *)0x8036A8C8)
#define LS_DY     (*(volatile signed char *)0x8036A8C9)
#define LS_HELD   (*(volatile signed char *)0x8036A8CA)
#define GAME_DEMO_UPDATE   ((void (*)(void))0x80323020)          /* demo_80323020 */
#define GAME_BUTTON_PRESS  ((s32(*)(s32, s32))0x803231E0)        /* demoButtonPress(player, buttons) */
#define GAME_SFX           ((void (*)(s32))0x8033F7F8)           /* sndPlaySfx */
s32 stick_dir(s32 player, s32 axis);
u32 int_to_float(s32 value);
static s32 sLastField = -1;

/* A cell of the menu: page 0 (x vehicle 0-2, y class 0-3) or page 1 (y -1 Birdman row x stage 0-3; y 0-2 bonus game,
   x level 0-2). */
static s32 cell_field(s32 page, s32 x, s32 y) {
    if (page == 0) {
        return x >= 0 && x < 3 && y >= 0 && y < 4 ? (s32)field_of((u32)x, (u32)y) : -1;
    }
    if (y == -1) {
        return x >= 0 && x < 4 ? 21 + x : -1;
    }
    return x >= 0 && x < 3 && y >= 0 && y < 3 ? (s32)field_of((u32)y + 3, (u32)x) : -1;
}

static s32 cell_x(s32 page, s32 x, s32 y) {
    return page == 1 && y == -1 ? x * 36 : x * 48;
}

/* The nearest open cell of a page from (x, y) in direction (dx, dy); with dx = dy = 0 the nearest open cell at all.
   Returns 1 and the cell, or 0. */
static s32 nearest_open(u8 *r, s32 page, s32 x, s32 y, s32 dx, s32 dy, s32 *outX, s32 *outY) {
    s32 best = 0x7FFFFFFF, found = 0;
    s32 cy, cx;
    for (cy = page == 1 ? -1 : 0; cy < (page == 1 ? 3 : 4); cy++) {
        for (cx = 0; cx < (page == 1 && cy == -1 ? 4 : 3); cx++) {
            s32 f = cell_field(page, cx, cy);
            s32 along, across, score;
            if (f < 0 || !field_open(r, (u32)f)) {
                continue;
            }
            if (dx != 0) {
                along = (cx - x) * dx;
                across = cy - y;
            } else if (dy != 0) {
                along = (cy - y) * dy;
                across = cell_x(page, cx, cy) - cell_x(page, x, y);
            } else {
                along = cy - y < 0 ? y - cy : cy - y;
                across = cell_x(page, cx, cy) - cell_x(page, x, y);
            }
            if ((dx != 0 || dy != 0) && along <= 0) {
                continue;
            }
            if (across < 0) {
                across = -across;
            }
            score = along * 1000 + across;
            if (score < best) {
                best = score;
                found = 1;
                *outX = cx;
                *outY = cy;
            }
        }
    }
    return found;
}

static void cursor_get(s32 *page, s32 *x, s32 *y) {
    *page = LS_PAGE != 0 ? 1 : 0;
    *x = LS_COL;
    *y = *page == 1 && LS_BIRD != 0 ? -1 : LS_ROW;
}

static void cursor_set(s32 page, s32 x, s32 y) {
    if (page != (LS_PAGE != 0 ? 1 : 0)) {
        LS_PAGE = (u8)page;
        if (page == 0) {
            module_menu_page1();
        } else {
            module_menu_page2();
        }
    }
    LS_BIRD = (u8)(page == 1 && y == -1);
    LS_COL = (signed char)x;
    LS_ROW = (signed char)(y < 0 ? 0 : y);
}

/* Moves the cursor in a direction; returns 1 if it moved. */
static s32 cursor_move(u8 *r, s32 dx, s32 dy) {
    s32 page, x, y, nx, ny;
    cursor_get(&page, &x, &y);
    if (nearest_open(r, page, x, y, dx, dy, &nx, &ny)) {
        cursor_set(page, nx, ny);
        return 1;
    }
    /* Past the edge to the other page: right of page 1 to the bonus games (class row 0 = the Birdman row), left of
       page 2 back to the Gyrocopter's column. */
    if (page == 0 && dx > 0 && nearest_open(r, 1, 0, y == 0 ? -1 : y - 1, 0, 0, &nx, &ny)) {
        cursor_set(1, nx, ny);
        return 1;
    }
    if (page == 1 && dx < 0 && nearest_open(r, 0, 2, y + 1, 0, 0, &nx, &ny)) {
        cursor_set(0, nx, ny);
        return 1;
    }
    return 0;
}

s32 module_menu_update(void) {
    u8 *state = GAME_STATE;
    s32 player = *(s32 *)(state + 0x9C);
    u8 *r = player_results();
    s32 sx, sy, page, x, y, f, cell;
    GAME_DEMO_UPDATE();
    sx = stick_dir(player, 0);
    sy = stick_dir(player, 1);
    LS_DX = 0;
    LS_DY = 0;
    if (sx == 0 && sy == 0) {
        LS_HELD = 0;
    } else if (LS_HELD == 0) {
        LS_DX = (signed char)sx;
        LS_DY = (signed char)-sy;
        LS_HELD = 1;
    }

    /* On a locked field (the start of the menu): the nearest open field, else the first open one of the pages. */
    cursor_get(&page, &x, &y);
    f = cell_field(page, x, y);
    if (f < 0 || !field_open(r, (u32)f)) {
        s32 nx, ny;
        if (nearest_open(r, page, x, y, 0, 0, &nx, &ny)) {
            cursor_set(page, nx, ny);
        } else if (nearest_open(r, 1 - page, 0, page == 0 ? -1 : 0, 0, 0, &nx, &ny)) {
            cursor_set(1 - page, nx, ny);
        }
    }

    if (LS_DX != 0) {
        cursor_move(r, LS_DX, 0);
    }
    if (LS_DY != 0) {
        cursor_move(r, 0, LS_DY);
    }

    cursor_get(&page, &x, &y);
    if (page == 0) {
        LS_X = int_to_float(x * 48 + 75);
        LS_Y = int_to_float(127 - y * 34);
    } else if (y >= 0) {
        LS_X = int_to_float(x * 48 + 106);
        LS_Y = int_to_float(93 - y * 34);
    } else {
        LS_X = int_to_float(x * 36 + 106);
        LS_Y = int_to_float(167);
    }
    cell = LS_COL + LS_ROW * 3;
    f = cell_field(page, x, y);
    if (f != sLastField || LS_FIRST != 0) {
        if (LS_FIRST != 0) {
            LS_FIRST = 0;
        } else {
            GAME_SFX(0x74);
        }
        sLastField = f;
    }
    LS_CELL = (u8)cell;

    if (GAME_BUTTON_PRESS(player, 0x9000) != 0 && f >= 0 && field_open(r, (u32)f)) {
        u8 *p = player_flight();
        GAME_SFX(0x6E);
        *(u16 *)(p + 2) = kFieldVehicle[f];
        *(u16 *)(p + 4) = kFieldClass[f];
        return cell;
    }
    if (GAME_BUTTON_PRESS(player, 0x4000) != 0) {
        GAME_SFX(0x01);
        return -2;
    }
    return -1;
}

/* The points under a field of the level select (func_8030F818) show from bronze on: "lhu t2, gMed[...].bronze" at ROM
   0x971F4 becomes a call of this stub (vehicle in s3, class in s2), which returns the field's bronze limit in t2. */
u32 class_bronze(u32 veh, u32 cls) {
    s32 lim[3];
    class_limits(veh, cls, lim);
    return (u32)lim[0];
}

__asm__(".section .text\n"
        ".set push\n"
        ".set noreorder\n"
        ".set noat\n"
        ".globl module_bronze_t2\n"
        "module_bronze_t2:\n"
        "    addiu $sp, $sp, -64\n"
        "    sw $ra, 60($sp)\n"
        "    sw $v0, 56($sp)\n"
        "    sw $v1, 52($sp)\n"
        "    sw $a0, 48($sp)\n"
        "    sw $a1, 44($sp)\n"
        "    sw $a2, 40($sp)\n"
        "    sw $a3, 36($sp)\n"
        "    sw $t0, 32($sp)\n"
        "    sw $t1, 28($sp)\n"
        "    sw $t3, 24($sp)\n"
        "    sw $t4, 20($sp)\n"
        "    sw $at, 16($sp)\n"
        "    move $a0, $s3\n"
        "    jal class_bronze\n"
        "    move $a1, $s2\n"
        "    move $t2, $v0\n"
        "    lw $ra, 60($sp)\n"
        "    lw $v0, 56($sp)\n"
        "    lw $v1, 52($sp)\n"
        "    lw $a0, 48($sp)\n"
        "    lw $a1, 44($sp)\n"
        "    lw $a2, 40($sp)\n"
        "    lw $a3, 36($sp)\n"
        "    lw $t0, 32($sp)\n"
        "    lw $t1, 28($sp)\n"
        "    lw $t3, 24($sp)\n"
        "    lw $t4, 20($sp)\n"
        "    lw $at, 16($sp)\n"
        "    jr $ra\n"
        "    addiu $sp, $sp, 64\n"
        ".set pop\n");

/* The hard end time is reached (game.c, ROM 0x75004: "arg0->unkA1 = 1" goes through here):
   the game goes straight to the results and plays nothing itself, except the Hang Glider, whose
   result frames play its landing jingle. Missions with a hard end setting play the jingle chosen
   in the editor right now (or none), and the Hang Glider's own jingle is skipped. */
#define GAME_SND_SET_MUSIC_STATE ((void (*)(s32))0x8033F964) /* sndSetMusicState (0 = play) */
#define GAME_SND_VEHICLE         ((void (*)(s32))0x8033FCD0) /* func_8033FCD0(vehicle) */

static const Condition *hard_end_setting(void) {
    const Condition *c = current_condition();
    return c != 0 && (c->flags & (COND_END_SCORED | COND_END_ZERO)) ? c : 0;
}

void module_hard_end(u8 *state) {
    const Condition *c;
    state[0xA1] = 1;
    c = hard_end_setting();
    if (c == 0 || sImpactSeen) {
        return; /* after an impact the mission ends as a crash: no jingle */
    }
    if (c->hardEndMusic != 0) {
        GAME_SND_SET_MUSIC(c->hardEndMusic);
        GAME_SND_SET_MUSIC_STATE(0);
        GAME_SND_VEHICLE(*(u16 *)(state + 0x0E));
    }
    message("Hard end time reached: jingle %d.\n", c->hardEndMusic);
}

/* The Hang Glider's landing jingle in its result frames (func_802EEB00): skipped after a hard end
   with a setting, because module_hard_end has played the chosen one. */
static int hard_end_music_taken(void) {
    u8 *state = GAME_STATE;
    return state != 0 && state[0xA1] != 0 && hard_end_setting() != 0;
}

void module_hg_music(s32 music) {
    if (!hard_end_music_taken()) {
        GAME_SND_SET_MUSIC(music);
    } else {
        message("Hang Glider landing jingle %d skipped (hard end time).\n", music);
    }
}

void module_hg_music_state(s32 musicState) {
    if (!hard_end_music_taken()) {
        GAME_SND_SET_MUSIC_STATE(musicState);
    }
}

/* Countdown of the hard end time: in the last 5 seconds a red "5" ... "1" in the middle of the
   picture, each with the sound of the photo reticle, then "Time up!" with the Jumble Hopper's goal gong. hudMainRender calls hudDrawStartText through here (ROM 0x9FBD4), so the
   text is drawn with the game's own font (font 6, as the HUD messages) and shows in every emulator. */
#define GAME_HUD_START_TEXT ((void (*)(void *))0x8031DC00)             /* hudDrawStartText */
#define GAME_FONT_SET       ((void (*)(s32))0x802194D8)                /* uvFontSet */
#define GAME_FONT_COLOR     ((void (*)(s32, s32, s32, s32))0x8021956C) /* uvFontColor */
#define GAME_FONT_WIDTH16   ((s32(*)(const u16 *))0x802196B0)          /* uvFontStr16Width */
#define GAME_FONT_PRINT16   ((s32(*)(s32, s32, const u16 *, s32, s32))0x80219874) /* uvFontPrintStr16 */
#define GAME_FONT_SCALE_X   REG32(0x80248E7C)                          /* sFontScaleX (f32) */
#define GAME_FONT_SCALE_Y   REG32(0x80248E80)                          /* sFontScaleY (f32) */
#define GAME_CLOCK          REG32(0x8034F850)                          /* D_8034F850: flight clock (f32 s) */
#define FLOAT_1 0x3F800000u
#define FLOAT_2 0x40000000u
#define GAME_SND_PLAY_SFX   ((void (*)(s32))0x8033F7F8)                /* sndPlaySfx */
#define SFX_RETICLE 0x43 /* the photo reticle appearing (Z held, hudDrawCamera) */
#define SFX_GONG    0x11 /* SFX_GOAL_GONG (Jumble Hopper reaching the goal) */

static s32 sCountdownShown = -1; /* text shown last frame, so each one sounds once */

void module_hud_text(void *hud) {
    const Condition *c;
    u8 *state = GAME_STATE;
    const u16 *text;
    s32 index = -1;
    s32 width;

    GAME_HUD_START_TEXT(hud);
    c = hard_end_setting();
    if (c == 0 || state == 0 || gHeader.countdown[0][0] == 0) {
        return;
    }
    /* After the impact of a crash the flight is over: no countdown, no sounds. The vehicle first tumbles for
       3.75 seconds (flight state 3, the clock keeps running) before the game marks the crash (state 4,
       result +0x3C); state 5 is the end of a landing. */
    if (sImpactSeen || (GAME_RESULT != 0 && GAME_RESULT[0x3C] != 0)) {
        sCountdownShown = -1;
        return;
    }
    if (state[0xA1]) {
        index = 5; /* "Time up!" */
    } else {
        s32 end = float_to_fixed8(*(s32 *)(GAME_TASK_SCORING() + 0x3C0));
        s32 left = end - float_to_fixed8((s32)GAME_CLOCK);
        if (left > 0 && left <= 5 * 256) {
            index = 5 - ((left + 255) >> 8); /* 5 seconds or less left: "5" ... "1" */
        }
    }
    if (index != sCountdownShown) {
        sCountdownShown = index;
        if (index >= 0) {
            GAME_SND_PLAY_SFX(index == 5 ? SFX_GONG : SFX_RETICLE);
            message("Countdown: %s (sound %d).\n", index == 5 ? "time up" : "number", index == 5 ? SFX_GONG : SFX_RETICLE);
        }
    }
    if (index < 0) {
        return;
    }
    text = gHeader.countdown[index];
    GAME_FONT_SET(6);
    GAME_FONT_COLOR(0xFF, 0x10, 0x10, 0xFF);
    GAME_FONT_SCALE_X = FLOAT_2;
    GAME_FONT_SCALE_Y = FLOAT_2;
    width = GAME_FONT_WIDTH16(text);
    GAME_FONT_PRINT16(SCREEN_W / 2 - (width - 16) / 2, 70, text, 0x28, 0xFFE);
    GAME_FONT_SCALE_X = FLOAT_1;
    GAME_FONT_SCALE_Y = FLOAT_1;
}

/* "n to go" on the HUD. The ring and target code ask func_8032C080 how many objects are still
   needed (before counting the one just cleared) and show one less; the retail game answers only
   in condition mode 3, otherwise -1 (no message). For a mission in the table that needs a number
   of rings or targets, the module answers with what is still missing. It never reports "reached",
   so the game does not say "please land" or remove the other objects. */
#define GAME_OBJECTS_LEFT ((s32(*)(s32 *))0x8032C080) /* func_8032C080 */

#define GAME_HUD_WARNING ((void (*)(s32, u32, u32))0x8031D9B8)  /* hudWarningText(textId, 3.0f, 8.0f) */
#define GAME_HUD_WARNING_TEXT ((volatile u16 *)(0x8036C5A8 + 0xB40)) /* sHudState.unkB40[0x3C] */
#define TEXT_LEFT_CNT 0x29
#define FLOAT_3 0x40400000u
#define FLOAT_8 0x41000000u

/* True if all conditions of the time points are met, counting "extra" more objects of "type". */
static int time_requirement_met(const Condition *c, int type, int extra) {
    int t;
    u8 *flight = player_flight();
    for (t = 0; t < OBJECT_TYPES; t++) {
        s32 cleared, total, need;
        if (c->required[t] == 0) {
            continue;
        }
        count_objects(t, &cleared, &total);
        if (t == type) {
            cleared += extra;
        }
        need = c->required[t] == REQUIRED_ALL ? total : c->required[t];
        if (cleared < need) {
            return 0;
        }
    }
    if ((c->flags & COND_ALTITUDE) && (flight == 0 || *(s32 *)(flight + 0x10) < (s32)c->altitude)) {
        return 0;
    }
    if ((c->flags & COND_DISTANCE) && (flight == 0 || *(s32 *)(flight + 0x0C) < (s32)c->distance)) {
        return 0;
    }
    return 1;
}

/* "Time requirement met" as the second HUD line (the text comes from the editor, in the
   game's text codes): the game's own message with the "to go" text, whose copy is then replaced. */
static void show_requirement_met(void) {
    int i;
    if (gHeader.metText[0] == 0) {
        return;
    }
    GAME_HUD_WARNING(TEXT_LEFT_CNT, FLOAT_3, FLOAT_8);
    for (i = 0; i < 32 && i < 0x3C; i++) {
        GAME_HUD_WARNING_TEXT[i] = gHeader.metText[i];
        if (gHeader.metText[i] == 0xFFFF) {
            break;
        }
    }
}

static s32 objects_left(int type, s32 *left) {
    const Condition *c = current_condition();
    s32 cleared, total, need;
    if (c == 0 || c->required[type] == 0) {
        return GAME_OBJECTS_LEFT(left);
    }
    count_objects(type, &cleared, &total);
    need = c->required[type] == REQUIRED_ALL ? total : c->required[type];
    /* "cleared" does not include the object just cleared yet. */
    message("HUD: %s %d of %d cleared, %d needed.\n", sTypeNames[type], cleared + 1, total, need);
    if (left != 0) {
        /* The game shows one less; with 1 left (this object reaches the number) nothing. */
        *left = need - cleared > 1 ? need - cleared : 0;
    }
    if (!time_requirement_met(c, type, 0) && time_requirement_met(c, type, 1)) {
        message("HUD: time requirement met.\n");
        show_requirement_met();
    }
    return 0;
}

/* Called by rings_80323FFC (a ring was passed) instead of func_8032C080. */
s32 module_rings_left(s32 *left) {
    return objects_left(0, left);
}

/* Called by targets_8034473C (a target was hit) instead of func_8032C080. */
s32 module_targets_left(s32 *left) {
    return objects_left(2, left);
}

/* ------------------------------------------------------------------ penalty messages */

/* "5 pts. deducted" (Gyrocopter, a hit) and "2 pts. deducted" (Rocket Belt, a hit; Jumble Hopper,
   leaving the area) are fixed texts, but the points come from the mission. The calls of
   hudWarningText for them go here: the message is shown, then the number in its copy is replaced
   by the mission's value (no message if nothing is deducted). */
#define TEXT_OFF_2 0x4F
#define TEXT_OFF_5 0x148
#define HUD_TEXT_LENGTH 0x3C

static void show_penalty(s32 textId, u32 a1, u32 a2, s32 points) {
    volatile u16 *text = GAME_HUD_WARNING_TEXT;
    s32 shown = textId == TEXT_OFF_5 ? 5 : 2;
    u16 digits[3];
    int n = 0, at, end, i;
    if (points <= 0) {
        message("HUD: no points deducted, no message.\n");
        return;
    }
    GAME_HUD_WARNING(textId, a1, a2);
    if (points == shown || gHeader.digits[shown] == 0) {
        return;
    }
    if (points > 999) {
        points = 999;
    }
    for (at = 0; at < HUD_TEXT_LENGTH && text[at] != 0xFFFF && text[at] != gHeader.digits[shown]; at++) {
    }
    for (end = at; end < HUD_TEXT_LENGTH && text[end] != 0xFFFF; end++) {
    }
    if (end >= HUD_TEXT_LENGTH || at == end) {
        return; /* the text was changed and has no such number */
    }
    do {
        digits[n++] = gHeader.digits[points % 10];
        points /= 10;
    } while (points != 0);
    if (end + n - 1 >= HUD_TEXT_LENGTH) {
        return;
    }
    for (i = end; i > at; i--) {
        text[i + n - 1] = text[i];
    }
    for (i = 0; i < n; i++) {
        text[at + i] = digits[n - 1 - i];
    }
}

/* A non-negative f32 (bits) rounded to an integer, without floating point (soft-float build). */
static s32 round_float_bits(u32 bits) {
    s32 e = (s32)((bits >> 23) & 0xFF) - 127;
    u32 mantissa = (bits & 0x7FFFFF) | 0x800000, twice;
    if ((bits & 0x80000000u) || e < -1) {
        return 0;
    }
    if (e > 20) {
        return 999;
    }
    twice = e >= 22 ? mantissa << (e - 22) : mantissa >> (22 - e);
    return (s32)((twice + 1) >> 1);
}

/* Gyrocopter and Rocket Belt: a hit costs COMM+0x3B4 (s32, negative). */
void module_penalty_hit(s32 textId, u32 a1, u32 a2) {
    s32 points = -*(s32 *)(GAME_TASK_SCORING() + 0x3B4);
    message("HUD: hit, %d points deducted.\n", points);
    show_penalty(textId, a1, a2, points);
}

/* Jumble Hopper: leaving the area costs COMM+0x414 (f32). */
void module_penalty_area(s32 textId, u32 a1, u32 a2) {
    s32 points = round_float_bits(*(u32 *)(GAME_TASK_SCORING() - 0x48 + 0x414));
    message("HUD: out of the area, %d points deducted.\n", points);
    show_penalty(textId, a1, a2, points);
}

/* ------------------------------------------------------------------ distance row */

/* The result sheet (resultInit) picks the points of each row through a jump table; row type 10
   (distance) has no entry there and shows nothing. The editor points that entry here: load the
   distance points (unk8 of the test's result) like the other rows do and continue with them. */
__asm__(".section .text\n"
        ".set push\n"
        ".set noreorder\n"
        ".globl module_distance_row\n"
        "module_distance_row:\n"
        "    j 0x8032E528\n"
        "    lh $a1, 8($t0)\n"
        ".set pop\n");

/* ------------------------------------------------------------------ live values */

static u32 sLiveSampleTime;      /* CPU count of the last speed sample */
static s32 sLiveX, sLiveY, sLiveZ; /* position then (fixed point, 8 fraction bits) */
static s32 sLiveHorizontal;      /* km/h */
static s32 sLiveVertical;        /* m/s x 10 */
static u32 sLivePrintTime;       /* CPU count of the last line to the debug port */
#define SAMPLE_TICKS (46875000u / 4)

static u32 isqrt(u32 v) {
    u32 r = 0, bit = 1u << 30;
    while (bit > v) {
        bit >>= 2;
    }
    while (bit) {
        if (v >= r + bit) {
            v -= r + bit;
            r = (r >> 1) + bit;
        } else {
            r >>= 1;
        }
        bit >>= 2;
    }
    return r;
}

static void append(char *line, int *pos, const char *fmt, ...) {
    va_list ap;
    Out o;
    o.buf = line + *pos;
    o.pos = 0;
    o.cap = OVL_COLS - *pos;
    va_start(ap, fmt);
    format(&o, fmt, ap);
    va_end(ap);
    *pos += o.pos;
    line[*pos] = 0;
}

/* Once per flight frame: the values of the live display. */
static void live_update(u8 *flight) {
    u32 now = cpu_count();
    s32 x = float_to_fixed8(*(s32 *)(flight + 0x5C));
    s32 y = float_to_fixed8(*(s32 *)(flight + 0x60));
    s32 z = float_to_fixed8(*(s32 *)(flight + 0x64));
    u32 elapsed = now - sLiveSampleTime;
    char line[OVL_COLS + 1];
    int pos = 0;
    u32 live = gHeader.live;

    if (now - sLiveTime > LIVE_TICKS) {
        /* a new flight (or the first frame after a pause): start the speeds again */
        sLiveSampleTime = now;
        sLiveX = x;
        sLiveY = y;
        sLiveZ = z;
        sLiveHorizontal = 0;
        sLiveVertical = 0;
    } else if (elapsed >= SAMPLE_TICKS) {
        s32 dx = (x - sLiveX) / 4, dy = (y - sLiveY) / 4; /* /4 keeps the square in range */
        u32 ticks = elapsed >> 10;
        u32 h = isqrt((u32)(dx * dx + dy * dy)) * 4; /* units x 256 */
        /* units x 256 per (ticks x 1024) to km/h: x 0.7 x 3.6 x 46875000 / 256 / 1024 = 450.6 */
        sLiveHorizontal = (s32)(h * 451u / ticks);
        /* to m/s x 10: x 0.7 x 10 x 46875000 / 256 / 1024 = 1251.7 */
        sLiveVertical = (z - sLiveZ) * 1252 / (s32)ticks;
        sLiveSampleTime = now;
        sLiveX = x;
        sLiveY = y;
        sLiveZ = z;
    }
    sLiveTime = now;

    if (live & LIVE_DISTANCE) {
        append(line, &pos, "Dist %d m  ", units_to_meters(*(s32 *)(flight + 0x0C)));
    }
    if (live & LIVE_ALTITUDE) {
        append(line, &pos, "Max %d m  ", units_to_meters(*(s32 *)(flight + 0x10)));
    }
    if (live & LIVE_HORIZONTAL) {
        append(line, &pos, "H %d km/h  ", sLiveHorizontal);
    }
    if (live & LIVE_VERTICAL) {
        s32 v = sLiveVertical;
        append(line, &pos, "V %s%d.%d m/s", v < 0 ? "-" : "", (v < 0 ? -v : v) / 10, (v < 0 ? -v : v) % 10);
    }
    if (pos == 0) {
        return;
    }
    if (gHeader.flags & FLAG_SCREEN) {
        int i;
        for (i = 0; i <= pos; i++) {
            sLiveLine[i] = line[i];
        }
        overlay_install();
    }
    if (now - sLivePrintTime >= 46875000u) {
        sLivePrintTime = now;
        message_quiet("Live: %s\n", line);
    }
}

/* Called by the vehicles once per flight frame instead of func_802EDAF0 (it records distance,
   highest altitude and the end of the flight). All arguments are passed on as they came. */
#define GAME_FLIGHT_FRAME ((void (*)(u32, u32, u32, u32, u32, u32, u32, u32, u32, u32, u32))0x802EDAF0)
#define FLOAT_1 0x3F800000u
/* ------------------------------------------------------------------ landing values */

#define GAME_PAD_COUNT   (*(volatile u8 *)0x8036C2B9)  /* gLandingPadCount */
#define GAME_PADS        ((volatile u8 *)0x8036C2E8)   /* gLandingPads[14], 0x18 bytes: radius +0x10, used +0x14 */
#define LIVE_LANDING     ((volatile u32 *)(DATA_AREA + 0x40)) /* the Hang Glider's four speed limits (patched code loads them) */
#define FLOAT_30   0x41F00000u
#define FLOAT_50   0x42480000u
#define FLOAT_15   0x41700000u

/* The constants of the Gyrocopter's and the Rocket Belt's landing (RAM copies of the game's data). */
#define GC_CRASH        (*(volatile u32 *)0x803537C8) /* 12.698 */
#define GC_CRASH_OFFPAD (*(volatile u32 *)0x803537CC) /* 7.937 */
#define RB_CRASH_A      (*(volatile u32 *)0x80355434) /* 21.8 */
#define RB_CRASH_B      (*(volatile u32 *)0x8035543C) /* 21.8 */
#define RB_HARD         (*(volatile u32 *)0x80355444) /* 7.93, speed into the ground */
#define RB_HARD_SIDE_A  (*(volatile u32 *)0x80355448) /* 7.93, sideways */
#define RB_HARD_SIDE_B  (*(volatile u32 *)0x80355458) /* 7.93, sideways */

#define LIVE_BIRDMAN ((volatile u32 *)(DATA_AREA + 0x50)) /* the Birdman's two landing limits (patched code loads them) */

static void landing_defaults(void) {
    LIVE_LANDING[0] = FLOAT_50;
    LIVE_LANDING[1] = FLOAT_50;
    LIVE_LANDING[2] = FLOAT_15;
    LIVE_LANDING[3] = FLOAT_15;
    LIVE_BIRDMAN[0] = FLOAT_50;
    LIVE_BIRDMAN[1] = FLOAT_50;
}

/* Sets the landing values of the mission being flown (or the original ones) in every flight frame: the radius
   of the landing pads (set to 30 when a mission loads), the Hang Glider's speed limits (read by its patched
   landing code), the Gyrocopter's and the Rocket Belt's constants. */
static void landing_values(const Condition *c, u32 vehicle) {
    u32 radius = c != 0 && (c->landing & LANDING_PAD_RADIUS) ? c->padRadius : FLOAT_30;
    int own = c != 0 && (c->landing & LANDING_LIMITS);
    u32 n = GAME_PAD_COUNT;
    u32 i;
    if (n > 14) {
        n = 14;
    }
    for (i = 0; i < n; i++) {
        if (GAME_PADS[i * 0x18 + 0x14]) {
            *(volatile u32 *)(GAME_PADS + i * 0x18 + 0x10) = radius;
        }
    }

    landing_defaults();
    if (own && vehicle == 0) {
        for (i = 0; i < 4; i++) {
            LIVE_LANDING[i] = c->limits[i];
        }
    }
    if (own && vehicle == 6) {
        LIVE_BIRDMAN[0] = c->limits[0];
        LIVE_BIRDMAN[1] = c->limits[1];
    }

    GC_CRASH = own && vehicle == 2 ? c->limits[0] : 0x414B2B02u;
    GC_CRASH_OFFPAD = own && vehicle == 2 ? c->limits[1] : 0x40FDFBE7u;
    RB_CRASH_A = RB_CRASH_B = own && vehicle == 1 ? c->limits[0] : 0x41AE6666u;
    RB_HARD = own && vehicle == 1 ? c->limits[1] : 0x40FDC28Fu;
    RB_HARD_SIDE_A = RB_HARD_SIDE_B = own && vehicle == 1 ? c->limits[2] : 0x40FDC28Fu;
}

/* The scoring function evaluates the touchdown point of the Gyrocopter (landedDist) through here: with an own
   curve in the mission's entry that one, otherwise the landing accuracy curve (as the original). The module is
   built without the FPU, so a small stub keeps the float argument (a1) and the result (f0) of func_80313F08. */
__attribute__((used)) const void *module_touchdown_select(const void *curve) {
    const Condition *c = current_condition();
    return c != 0 && (c->flags & COND_TOUCHDOWN) ? (const void *)c->touchdown : curve;
}

__asm__(".section .text\n"
        ".set push\n"
        ".set noreorder\n"
        ".globl module_touchdown_curve\n"
        "module_touchdown_curve:\n"
        "    addiu $sp, $sp, -24\n"
        "    sw $ra, 20($sp)\n"
        "    jal module_touchdown_select\n"
        "    sw $a1, 16($sp)\n"
        "    lw $a1, 16($sp)\n"
        "    lw $ra, 20($sp)\n"
        "    move $a0, $v0\n"
        "    j 0x80313F08\n"
        "    addiu $sp, $sp, 24\n"
        ".set pop\n");

/* ------------------------------------------------------------------ Birdman with points */

/* Game patch "Bonus games and Birdman": the Birdman missions with points (bit class * 4 + test, data area header
   +0x14, written by the editor). The patched result and test screens read it too (module_bm_* below). */
#define FLOAT_2_52 0x402147AEu /* units per second to the km/h the HUD shows */
#define FLOAT_1_5  0x3FC00000u
#define FLOAT_8    0x41000000u
#define GAME_HUD_TEXT ((void (*)(s32, u32, u32))0x8031D8E0) /* hudText_8031D8E0(text, 1.5f, 8.0f) */
#define TEXT_LAND_OK  0xCB /* "Landing accomplished" */
#define TEXT_LAND_OUT 0xB2 /* "Landed outside target" */

/* A few single-precision operations on the FPU (the module itself is built without it). Only f0, f2, f4, f12 and
   f14 are used: the caller (the game) may keep values in f20 to f30. */
__asm__(".section .text\n"
        ".set push\n"
        ".set noreorder\n"
        ".set noat\n"
        ".set hardfloat\n"
        /* stick_dir(player, axis): demoGetInputs (0x803230B0) as -1, 0, 1 beyond +-0.75. */
        ".globl stick_dir\n"
        "stick_dir:\n"
        "    addiu $sp, $sp, -24\n"
        "    sw $ra, 20($sp)\n"
        "    jal 0x803230B0\n"
        "    nop\n"
        "    lui $at, 0x3F40\n"
        "    mtc1 $at, $f2\n"
        "    nop\n"
        "    neg.s $f4, $f2\n"
        "    c.lt.s $f2, $f0\n"
        "    nop\n"
        "    bc1t 1f\n"
        "    addiu $v0, $zero, 1\n"
        "    c.lt.s $f0, $f4\n"
        "    nop\n"
        "    bc1t 1f\n"
        "    addiu $v0, $zero, -1\n"
        "    move $v0, $zero\n"
        "1:  lw $ra, 20($sp)\n"
        "    jr $ra\n"
        "    addiu $sp, $sp, 24\n"
        /* int_to_float(value): the bits of (float) value. */
        ".globl int_to_float\n"
        "int_to_float:\n"
        "    mtc1 $a0, $f0\n"
        "    nop\n"
        "    cvt.s.w $f0, $f0\n"
        "    jr $ra\n"
        "    mfc1 $v0, $f0\n"
        ".globl f_mul\n"
        "f_mul:\n"
        "    mtc1 $a0, $f0\n"
        "    mtc1 $a1, $f2\n"
        "    nop\n"
        "    mul.s $f0, $f0, $f2\n"
        "    jr $ra\n"
        "    mfc1 $v0, $f0\n"
        /* padsLandedPadStrip(x, y, z, &index) (0x80317978): x, y in f12, f14, z in a2 (o32); the index is non-zero on a
           landing pad within its radius or on a runway. */
        ".globl birdman_pad_index\n"
        "birdman_pad_index:\n"
        "    addiu $sp, $sp, -32\n"
        "    sw $ra, 28($sp)\n"
        "    sb $zero, 24($sp)\n"
        "    mtc1 $a0, $f12\n"
        "    mtc1 $a1, $f14\n"
        "    jal 0x80317978\n"
        "    addiu $a3, $sp, 24\n"
        "    lbu $v0, 24($sp)\n"
        "    lw $ra, 28($sp)\n"
        "    jr $ra\n"
        "    addiu $sp, $sp, 32\n"
        ".set pop\n");
u32 f_mul(u32 a, u32 b);
u32 birdman_pad_index(u32 x, u32 y, u32 z);

static int birdman_points(u32 cls, u32 test) {
    return test < MAX_TESTS && ((birdman_stage(cls) >> test) & 1);
}

/* The Birdman's landing (BirdmanData: flags +0x04, flight time +0x08, position +0x40, state +0x104 (0 flying, 1 impact,
   2 crashed, 3 landed), soft touchdown +0x105, speed +0x218 (x, y forward, z vertical), its length +0x224 (the HUD shows
   it × 2.52 as km/h)). The original makes a soft touchdown (feet first, vertical and forward speed below 50 km/h) a
   1.9 s bounce, after which the Birdman flies on; every other ground contact is an impact (a crash 3.75 s later). With
   points the soft touchdown is a landing: the unused state "landed" (3, flight state 5) ends the mission at once. Like
   the Hang Glider, the place decides: on a landing pad (within its radius) "Landing accomplished" and all flags,
   otherwise "Landed outside target" and the flags of a landing beside the target. How hard the landing was only the
   impact curve decides: the impact is the speed of the last frame in the air in km/h as the HUD shows it (vertical,
   forward and sideways together; the Birdman passes 0). */
static int sBmLanded;
static u32 sBmTime, sBmSpeed, sBmFlags;

static void birdman_frame(u8 *bm, const Condition *c, u32 *impact, u32 *flags, u32 *state, u32 time) {
    (void)c;
    if (sBmLanded && (s32)time < (s32)sBmTime) {
        sBmLanded = 0; /* a new flight: its time started again */
    }
    if (!sBmLanded) {
        int onPad;
        if (bm[0x104] != 0) {
            return;
        }
        if (bm[0x105] == 0) {
            /* Still in the air: the speed of this frame (the touchdown sets it to 0). */
            sBmSpeed = f_mul(*(u32 *)(bm + 0x224) & 0x7FFFFFFFu, FLOAT_2_52);
            return;
        }
        /* Soft touchdown in this frame. */
        onPad = birdman_pad_index(*(u32 *)(bm + 0x40), *(u32 *)(bm + 0x44), *(u32 *)(bm + 0x48)) != 0;
        sBmLanded = 1;
        sBmTime = time;
        sBmFlags = onPad ? 0xFFFFu : SCORE_NO_LANDING;
        GAME_HUD_TEXT(onPad ? TEXT_LAND_OK : TEXT_LAND_OUT, FLOAT_1_5, FLOAT_8);
        GAME_SND_SET_MUSIC(onPad ? 0x1A : 0x1B);
        GAME_SND_SET_MUSIC_STATE(0);
        GAME_SND_VEHICLE(6);
        message("Birdman: landed %s at %d km/h (impact).\n", onPad ? "on a landing pad" : "beside the target",
                float_to_fixed8((s32)sBmSpeed) >> 8);
    }
    bm[0x104] = 3;
    bm[0x105] = 0;
    *(u16 *)(bm + 4) = (u16)sBmFlags;
    *impact = sBmSpeed;
    *flags = sBmFlags;
    *state = 5;
}

/* testMenuInitText takes the points shown on the test screen of all vehicles but the main ones from (level, test 0); for
   the Birdman the mission's own: (class, test). */
#define GAME_POINT_COUNT ((s32(*)(void *, u32, u32, u32))0x8032BE1C) /* testGetPointCount(data, class, test, vehicle) */
s32 module_point_count(void *data, u32 a1, u32 a2, u32 vehicle) {
    u8 *p = player_flight();
    if ((vehicle & 0xFF) == 6 && p != 0) {
        return GAME_POINT_COUNT(data, *(u16 *)(p + 4), a1, vehicle);
    }
    return GAME_POINT_COUNT(data, a1, a2, vehicle);
}

/* "All perfect" (func_8032C27C) skips the Birdman; the Birdman missions with points must have 100 points too. Their
   results: D_80364210 (0x34E0 bytes per player), + class * 0x694 + vehicle * 0x30 + test * 0x150 + 0x6C (s16). */
#define GAME_ALL_PERFECT ((s32(*)(void))0x8032C27C)
s32 module_all_perfect(void) {
    s32 ok = GAME_ALL_PERFECT();
    u8 *state = GAME_STATE;
    u32 cls, test;
    if (!ok || state == 0) {
        return ok;
    }
    for (cls = 0; cls < MAX_CLASSES; cls++) {
        for (test = 0; test < MAX_TESTS; test++) {
            if (birdman_points(cls, test)) {
                u8 *results = (u8 *)0x80364210 + *(s32 *)(state + 0x9C) * 0x34E0;
                short points = *(short *)(results + cls * 0x694 + 6 * 0x30 + test * 0x150 + 0x6C);
                if (points != 100) {
                    return 0;
                }
            }
        }
    }
    return 1;
}

static u32 birdman_player_stage(void) {
    u8 *p = player_flight();
    return p != 0 && *(u16 *)(p + 2) == 6 ? birdman_stage(*(u16 *)(p + 4)) : 0;
}

/* levelGetTotalPoints (0x8032BD20) itself jumps here (its first instruction becomes "j module_level_points"), so every
   caller (stage screen, level select, scoring, medal ceremony, ...) gets the same sum: for a Birdman stage with points only
   its missions with points, otherwise as the original all tests of the class (taskGetTestCount), 0x7F (no result) as 0.
   The total screen (taskGetTestCount, testGetPointCount) is patched below. */
s32 module_level_points(void *data, u32 cls, u32 vehicle) {
    u32 stage = (vehicle & 0xFF) == 6 ? birdman_stage(cls & 0xFF) : 0;
    s32 count = stage != 0 ? MAX_TESTS : GAME_TEST_COUNT((s32)cls, (s32)vehicle);
    s32 sum = 0;
    s32 t;
    for (t = 0; t < count; t++) {
        if (stage == 0 || ((stage >> t) & 1)) {
            s32 points = GAME_POINT_COUNT(data, cls, (u32)t, vehicle);
            if (points != 127) {
                sum += points;
            }
        }
    }
    return sum;
}

/* The medal ceremony (code_A8C30, call 0xA9114) asks for the medal of every bonus vehicle with entry 4 (70/80/90, one
   test); the Birdman has its own limits (entry 7, or 70/80/90 per mission with points in a stage with points). */
s32 module_ceremony_medal(s32 *out, u32 points, u32 medal) {
    u8 *p = player_flight();
    return module_points_to_medal(out, points, p != 0 && *(u16 *)(p + 2) == 6 ? 7 : medal);
}

s32 module_tot_test_count(s32 cls, s32 vehicle) {
    u32 stage = vehicle == 6 ? birdman_stage((u32)cls) : 0;
    return stage != 0 ? (s32)bit_count(stage) : GAME_TEST_COUNT(cls, vehicle);
}

s32 module_tot_point_count(void *data, u32 cls, u32 i, u32 vehicle) {
    u32 stage = (vehicle & 0xFF) == 6 ? birdman_stage(cls & 0xFF) : 0;
    return GAME_POINT_COUNT(data, cls, stage != 0 ? nth_bit(stage, i & 0xFF) : i, vehicle);
}

/* Texts: the medal texts of a Birdman stage with points (BD_E_S3_BLONDS …, written by the editor for its number of
   missions) instead of the bonus games' BONUS_S3_…, and "N tests to go" (TRY1 … TRY4) for the stage screen. */
#define GAME_TEXT_BY_NAME ((void *(*)(const char *))0x803420F4) /* textGetDataByName */
#define GAME_TEXT_BY_IDX  ((void *(*)(u32))0x80342198)          /* textGetDataByIdx */
static char sTextName[24];

void *module_tot_medal_text(const char *name) {
    static const char bonus[] = "BONUS_S3_";
    static const char prefixes[] = "HGRPGCCBSDHP";
    u8 *p = player_flight();
    u32 i;
    /* A class with own limits has own texts for bronze, silver and gold ("HG_A_S3_GOLD", "SD_L2_S3_SILVER"), written by
       the editor from the class's text; perfect and "great" stay the game's (their numbers do not change). */
    if (p != 0 && *(u16 *)(p + 2) < 6 && (class_flags(*(u16 *)(p + 2), *(u16 *)(p + 4)) & 2)) {
        u32 veh = *(u16 *)(p + 2);
        u32 cls = *(u16 *)(p + 4) & 7;
        const char *rest = name;
        while (*rest != 0 && !(rest[0] == 'S' && rest[1] == '3' && rest[2] == '_')) {
            rest++;
        }
        /* Flag 4 (another number of tests than in the game): the perfect score's texts too. */
        if (*rest != 0 && (rest[3] == 'B' || rest[3] == 'S' || (rest[3] == 'G' && rest[4] == 'O')
                || ((class_flags(veh, *(u16 *)(p + 4)) & 4) && (rest[3] == 'P' || (rest[3] == 'G' && rest[4] == 'R'))))) {
            u32 n = 0;
            sTextName[n++] = prefixes[veh * 2];
            sTextName[n++] = prefixes[veh * 2 + 1];
            sTextName[n++] = '_';
            if (veh < 3) {
                sTextName[n++] = "EABPCDFG"[cls];
            } else {
                sTextName[n++] = 'L';
                sTextName[n++] = (char)('1' + cls);
            }
            sTextName[n++] = '_';
            for (i = 0; rest[i] != 0 && n < sizeof(sTextName) - 1; i++) {
                sTextName[n++] = rest[i];
            }
            sTextName[n] = 0;
            return GAME_TEXT_BY_NAME(sTextName);
        }
    }
    if (birdman_player_stage() != 0) {
        for (i = 0; bonus[i] != 0 && name[i] == bonus[i]; i++) {
        }
        if (bonus[i] == 0) {
            const char *rest = name + i;
            sTextName[0] = 'B';
            sTextName[1] = 'D';
            sTextName[2] = '_';
            sTextName[3] = "EABPCDFG"[*(u16 *)(p + 4) & 7];
            sTextName[4] = '_';
            sTextName[5] = 'S';
            sTextName[6] = '3';
            sTextName[7] = '_';
            for (i = 0; rest[i] != 0 && i < 15; i++) {
                sTextName[8 + i] = rest[i];
            }
            sTextName[8 + i] = 0;
            return GAME_TEXT_BY_NAME(sTextName);
        }
    }
    return GAME_TEXT_BY_NAME(name);
}

/* The stage screen's "N tests to go" (test_summary.c): the game takes TRY1 ... TRY3 by the class (Beginner 1, Class A 2,
   Class B and Pilot 3 tests). Here a main vehicle's class gets the text of its number of tests (TRY4 and TRY5 are written
   by the editor), a Birdman stage with points that of its missions with points. */
void *module_try_text(u32 index) {
    u32 stage = birdman_player_stage();
    u8 *p = player_flight();
    s32 count = 0;
    if (stage != 0) {
        count = (s32)bit_count(stage);
    } else if (p != 0 && *(u16 *)(p + 2) < 3) {
        count = GAME_TEST_COUNT(*(u16 *)(p + 4), *(u16 *)(p + 2));
    }
    if (count >= 1 && count <= MAX_TESTS) {
        sTextName[0] = 'T';
        sTextName[1] = 'R';
        sTextName[2] = 'Y';
        sTextName[3] = (char)('0' + count);
        sTextName[4] = 0;
        return GAME_TEXT_BY_NAME(sTextName);
    }
    return GAME_TEXT_BY_IDX(index);
}

/* The menu of the total screen: a Birdman stage with more than one mission with points gets "Retry", "Another test" and
   "Quit", like the license classes from Class A on (the game offers it only to Hang Glider, Rocket Belt and
   Gyrocopter); the choice is mapped the same way. A new record keeps the game's "Next" menu. */
#define GAME_TOT_MENU     ((void (*)(void))0x8034754C)                                   /* totResultCreateMenu */
#define GAME_TOT_CHOICE   ((s32(*)(s32))0x80347150)                                      /* totResult_80347150 */
#define GAME_TOT_RECORD   ((u32(*)(u8 *))0x80346FC0)                                     /* totResult_80346FC0 */
#define GAME_MENU_CREATE  ((void (*)(s32, s32, s32, u32, u32, volatile s32 *, s32))0x8030B48C) /* menuCreateItems */
#define TOT_MENU_ITEMS    ((volatile s32 *)0x80350948)                                   /* sResultsMenuItems */
#define TOT_MENU_COUNT    (*(volatile s32 *)0x80350954)                                  /* sResultsMenuCount */
#define TEXT_RETRY_SGI    0x189
#define TEXT_ANOTHER_TASK 0x166
#define TEXT_QUIT_SGI     0x1D

/* The menu the module gives the total screen: 0 the game's (a new record: "Next"; the bonus games), 2 "Retry", "Quit",
   3 "Retry", "Another test", "Quit". The game gives the main vehicles the three items from Class A on; here a class of a
   main vehicle gets them when it has more than one test (so a Beginner class with more tests too, a class with one test
   not), a Birdman stage with points when it has more than one mission with points. */
static int tot_menu_kind(void) {
    u8 *p = player_flight();
    u32 veh;
    if (p == 0 || (GAME_TOT_RECORD(p) & 0xFF) != 0) {
        return 0;
    }
    veh = *(u16 *)(p + 2);
    if (veh < 3) {
        return GAME_TEST_COUNT(*(u16 *)(p + 4), (s32)veh) > 1 ? 3 : 2;
    }
    return bit_count(birdman_player_stage()) > 1 ? 3 : 0;
}

void module_tot_menu(void) {
    int kind = tot_menu_kind();
    if (kind == 0) {
        GAME_TOT_MENU();
        return;
    }
    TOT_MENU_COUNT = 0;
    TOT_MENU_ITEMS[TOT_MENU_COUNT++] = TEXT_RETRY_SGI;
    if (kind == 3) {
        TOT_MENU_ITEMS[TOT_MENU_COUNT++] = TEXT_ANOTHER_TASK;
    }
    TOT_MENU_ITEMS[TOT_MENU_COUNT++] = TEXT_QUIT_SGI;
    GAME_MENU_CREATE(0xAA, 2, 6, FLOAT_1, FLOAT_1, TOT_MENU_ITEMS, TOT_MENU_COUNT);
}

s32 module_tot_choice(s32 item) {
    int kind = item < 0 ? 0 : tot_menu_kind();
    if (kind == 0) {
        return GAME_TOT_CHOICE(item);
    }
    if (kind == 2) {
        return item == 0 ? 3 : item == 1 ? 5 : -1; /* retry, quit */
    }
    return item == 0 ? 3 : item == 1 ? 4 : item == 2 ? 5 : -1; /* retry, another test, quit */
}

/* The HUD of a Birdman mission with points: also the radar and the time, at the places of the Hang Glider's. The HUD
   state: elapsed time +0x10, attitude +0x68 (x, y, heading, pitch, computed by hudMainRender), radar (HUDRadar) +0x90, as
   the game's own calls pass it (hudDrawHangGlider: addiu t9, s0, 0x90). */
#define GAME_HUD_BIRDMAN ((void (*)(u8 *))0x80319EFC)                                  /* hudDrawBirdman */
#define GAME_HUD_RADAR   ((void (*)(s32, s32, u32, u32, u32, u32, u8 *))0x8031AD68)     /* hudDrawRadar */
#define GAME_HUD_TIMER   ((void (*)(s32, s32, u32))0x8031D264)                         /* hudDrawTimer */
void module_hud_birdman(u8 *hud) {
    u8 *comm = GAME_TASK_SCORING() - 0x48;
    u8 *p = player_flight();
    GAME_HUD_BIRDMAN(hud);
    if (p == 0 || p[0x7B] != 0 || (u32)comm < 0x80000400u || (u32)comm >= 0x80800000u || comm[1] != 6
        || !birdman_points(comm[0], comm[2])) {
        return; /* free flight or the demo */
    }
    GAME_HUD_RADAR(215, 222, *(u32 *)(hud + 0x68), *(u32 *)(hud + 0x6C), *(u32 *)(hud + 0x70), *(u32 *)(hud + 0x74), hud + 0x90);
    GAME_HUD_TIMER(27, 222, *(u32 *)(hud + 0x10));
}

/* The result sheet and the test screen leave out the Birdman ("veh != BIRDMAN": no line above the total, no numbers,
   no top score). The patch replaces "lhu d, 2(b); li at, 6" by "jal module_bm_d_b; lhu d, 2(b)": b is the player's
   flight data (class +4, test +6), d the vehicle just loaded. For a Birdman mission with points d becomes 0x7F, so the
   check that follows (against at = 6) treats it like any other vehicle. Only v0 and v1 are used (saved). */
#define BM_CHECK(name, d, b) \
    ".globl " name "\n" name ":\n" \
    "    addiu $sp, $sp, -16\n" \
    "    sw $v0, 0($sp)\n" \
    "    sw $v1, 4($sp)\n" \
    "    addiu $at, $zero, 6\n" \
    "    bne " d ", $at, 9f\n" \
    "    lhu $v0, 4(" b ")\n" \
    "    lhu $v1, 6(" b ")\n" \
    "    or $at, $v0, $v1\n" \
    "    sltiu $at, $at, 4\n" \
    "    beqz $at, 8f\n" \
    "    sll $v0, $v0, 2\n" \
    "    addu $v0, $v0, $v1\n" \
    "    lui $v1, 0x801F\n" \
    "    lhu $v1, 0x5014($v1)\n" \
    "    srlv $v1, $v1, $v0\n" \
    "    andi $v1, $v1, 1\n" \
    "    beqz $v1, 8f\n" \
    "    nop\n" \
    "    addiu " d ", $zero, 0x7F\n" \
    "8:  addiu $at, $zero, 6\n" \
    "9:  lw $v0, 0($sp)\n" \
    "    lw $v1, 4($sp)\n" \
    "    jr $ra\n" \
    "    addiu $sp, $sp, 16\n"

/* The same for the stage screen: there the whole stage counts (any mission with points), not the test last chosen. */
#define BM_STAGE_CHECK(name, d, b) \
    ".globl " name "\n" name ":\n" \
    "    addiu $sp, $sp, -16\n" \
    "    sw $v0, 0($sp)\n" \
    "    sw $v1, 4($sp)\n" \
    "    addiu $at, $zero, 6\n" \
    "    bne " d ", $at, 9f\n" \
    "    lhu $v0, 4(" b ")\n" \
    "    sltiu $at, $v0, 4\n" \
    "    beqz $at, 8f\n" \
    "    sll $v0, $v0, 2\n" \
    "    lui $v1, 0x801F\n" \
    "    lhu $v1, 0x5014($v1)\n" \
    "    srlv $v1, $v1, $v0\n" \
    "    andi $v1, $v1, 0xF\n" \
    "    beqz $v1, 8f\n" \
    "    nop\n" \
    "    addiu " d ", $zero, 0x7F\n" \
    "8:  addiu $at, $zero, 6\n" \
    "9:  lw $v0, 0($sp)\n" \
    "    lw $v1, 4($sp)\n" \
    "    jr $ra\n" \
    "    addiu $sp, $sp, 16\n"

__asm__(".section .text\n"
        ".set push\n"
        ".set noreorder\n"
        ".set noat\n"
        BM_CHECK("module_bm_t9_t7", "$t9", "$t7")
        BM_CHECK("module_bm_t6_t5", "$t6", "$t5")
        BM_CHECK("module_bm_t3_t2", "$t3", "$t2")
        BM_CHECK("module_bm_t9_t8", "$t9", "$t8")
        BM_CHECK("module_bm_t1_t0", "$t1", "$t0")
        BM_STAGE_CHECK("module_bm_t0_t9", "$t0", "$t9")
        /* The banner of the stage screen (test_summary.c): "v1 = 7" (Free Flight) for the Birdman becomes, for a stage
           with points, the banner of level class + 1 (stage 4: level 3). s0 is the player's flight data. */
        ".globl module_bm_banner\n"
        "module_bm_banner:\n"
        "    addiu $sp, $sp, -16\n"
        "    sw $t0, 0($sp)\n"
        "    sw $t1, 4($sp)\n"
        "    addiu $v1, $zero, 7\n"
        "    lhu $t0, 4($s0)\n"
        "    sltiu $at, $t0, 4\n"
        "    beqz $at, 9f\n"
        "    lui $t1, 0x801F\n"
        "    lhu $t1, 0x5014($t1)\n"
        "    sll $at, $t0, 2\n"
        "    srlv $t1, $t1, $at\n"
        "    andi $t1, $t1, 0xF\n"
        "    beqz $t1, 9f\n"
        "    nop\n"
        "    addiu $v1, $t0, 4\n"
        "    sltiu $at, $v1, 7\n"
        "    bnez $at, 9f\n"
        "    nop\n"
        "    addiu $v1, $zero, 6\n"
        "9:  lw $t0, 0($sp)\n"
        "    lw $t1, 4($sp)\n"
        "    jr $ra\n"
        "    addiu $sp, $sp, 16\n"
        /* "Next" on the result sheet (resultMenuChoose, 0x8032E7F4): "beql veh, 6 (at), test details" becomes "j" here
           (the delay slot sets t6 = 2, the value of test details): the Birdman goes to the test details only in a free
           flight, a mission with points to the total screen like every other vehicle. v0 = vehicle, v1 = flight data. */
        ".globl module_res_next\n"
        "module_res_next:\n"
        "    addiu $at, $zero, 6\n"
        "    bne $v0, $at, 1f\n"
        "    lhu $t5, 4($v1)\n"
        "    lhu $t7, 6($v1)\n"
        "    or $at, $t5, $t7\n"
        "    sltiu $at, $at, 4\n"
        "    beqz $at, 2f\n"
        "    sll $t5, $t5, 2\n"
        "    addu $t5, $t5, $t7\n"
        "    lui $t7, 0x801F\n"
        "    lhu $t7, 0x5014($t7)\n"
        "    srlv $t7, $t7, $t5\n"
        "    andi $t7, $t7, 1\n"
        "    bnez $t7, 1f\n"
        "    nop\n"
        "2:  j 0x8032E810\n"
        "    nop\n"
        "1:  j 0x8032E7FC\n"
        "    nop\n"
        ".set pop\n");

void module_flight_frame(u32 a0, u32 a1, u32 a2, u32 a3, u32 a4, u32 a5, u32 a6, u32 a7, u32 a8, u32 a9, u32 a10) {
    const Condition *c;
    u8 *result;
    u8 *comm = GAME_TASK_SCORING() - 0x48;

    /* A Birdman mission with points: its landing (a1 is the Birdman's matrix at +0x10). */
    if (*(u16 *)((u8 *)a0 + 2) == 6 && (u32)comm >= 0x80000400u && (u32)comm < 0x80800000u && comm[1] == 6
        && birdman_points(comm[0], comm[2])) {
        birdman_frame((u8 *)a1 - 0x10, current_condition(), &a4, &a7, &a10, a6);
    } else {
        sBmLanded = 0;
    }
    GAME_FLIGHT_FRAME(a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10);

    /* The time of a crash: the impact (flight state a10 = 3: the vehicle tumbles for 3.75 s; 4: crashed),
       a6 is the vehicle's flight time of this frame. A new flight (state 1 or 2 without a crash) clears it. */
    result = GAME_RESULT;
    if (a10 == 3 || a10 == 4 || (result != 0 && result[0x3C] != 0)) {
        if (!sCrashTimeSet) {
            sCrashTimeSet = 1;
            sCrashTime = a6;
        }
        sImpactSeen = 1;
    } else if (a10 == 1 || a10 == 2) {
        sCrashTimeSet = 0;
        sImpactSeen = 0;
    }

    /* Unlimited fuel: fill the tank again after each frame (a1 is the vehicle's matrix at +0x10;
       the fuel is at +0xF8 for the Rocket Belt and +0xD8 for the Gyrocopter, 1.0 = full). */
    c = current_condition();
    if (c != 0 && (c->flags & COND_FUEL)) {
        u16 vehicle = *(u16 *)((u8 *)a0 + 2);
        u8 *data = (u8 *)a1 - 0x10;
        if (vehicle == 1) {
            *(u32 *)(data + 0xF8) = FLOAT_1;
        } else if (vehicle == 2) {
            *(u32 *)(data + 0xD8) = FLOAT_1;
        }
    }
    if (gHeader.flags & FLAG_LANDING) {
        landing_values(c, *(u16 *)((u8 *)a0 + 2));
    }
    if (gHeader.flags & FLAG_LIVE) {
        live_update((u8 *)a0);
    }
}

/* Called by hudDrawRocketPack and hudDrawGyrocopter instead of hudDrawFuel (gauge and "FUEL"
   label): with unlimited fuel there is nothing to show. */
#define GAME_HUD_DRAW_FUEL ((void (*)(s32, s32, u32))0x8031C1E8)
void module_hud_fuel(s32 x, s32 y, u32 fuel) {
    const Condition *c = current_condition();
    if (c != 0 && (c->flags & COND_FUEL)) {
        return;
    }
    GAME_HUD_DRAW_FUEL(x, y, fuel);
}

/* ------------------------------------------------------------------ startup steps */

enum Detail {
    DETAIL_NONE,
    DETAIL_EEPROM,      /* result of osEepromProbe */
    DETAIL_CONTROLLERS, /* gSiContPattern */
    DETAIL_HEAP,        /* next free heap address */
    DETAIL_LEVEL,       /* level number (argument) and heap */
    DETAIL_TEXTS,       /* number of texts and heap */
    DETAIL_RETURN,      /* return value */
};

typedef s32 (*Function)(s32, s32, s32, s32);

typedef struct {
    const char *name;
    Function original;
    u8 detail;
    u8 startupOnly; /* 1: only reported until the title screen is reached (also called later) */
    u8 last;        /* 1: the end of the startup (the title screen is ready) */
} Hook;

static s32 run_hook(int index, s32 a0, s32 a1, s32 a2, s32 a3);

#include "hooks.g.h"

static int sDepth;
static int sStartupDone;

static void indent(char *buf) {
    int i;
    for (i = 0; i < sDepth * 2 && i < 30; i++) {
        buf[i] = ' ';
    }
    buf[i] = 0;
}

/* "1.4 ms" from CPU count ticks (46.875 MHz). */
static void duration(char *buf, u32 ticks) {
    u32 ms = ticks / 46875;
    u32 tenth = (ticks % 46875) * 10 / 46875;
    Out o;
    o.buf = buf;
    o.pos = 0;
    o.cap = 23;
    {
        char tmp[12];
        u32 n = utoa(ms, tmp + sizeof(tmp), 10, 0);
        put_padded(&o, tmp + sizeof(tmp) - n, n, 0, 0, ' ');
    }
    put(&o, '.');
    put(&o, (char)('0' + tenth));
    put(&o, ' ');
    put(&o, 'm');
    put(&o, 's');
    buf[o.pos] = 0;
}

static s32 run_hook(int index, s32 a0, s32 a1, s32 a2, s32 a3) {
    const Hook *hook = &sHooks[index];
    int report = (gHeader.flags & FLAG_STARTUP) && !(hook->startupOnly && sStartupDone);
    char pad[32];
    char time[24];
    u32 start;
    s32 result;

    if (!report) {
        return hook->original(a0, a1, a2, a3);
    }

    indent(pad);
    if (hook->detail == DETAIL_LEVEL) {
        message("%sStarting: %s, level %d\n", pad, hook->name, a0);
    } else {
        message("%sStarting: %s\n", pad, hook->name);
    }
    sDepth++;
    start = cpu_count();
    result = hook->original(a0, a1, a2, a3);
    duration(time, cpu_count() - start);
    sDepth--;

    switch (hook->detail) {
    case DETAIL_EEPROM:
        message("%sDone: %s (%s) - %s\n", pad, hook->name, time,
                result == 1 ? "EEPROM 4 Kbit found" : result == 2 ? "EEPROM 16 Kbit found" : "no EEPROM found, saving will not work");
        break;
    case DETAIL_CONTROLLERS: {
        u8 p = GAME_CONT_PATTERN;
        message("%sDone: %s (%s) - controllers plugged in: %s%s%s%s%s\n", pad, hook->name, time,
                p & 1 ? "1 " : "", p & 2 ? "2 " : "", p & 4 ? "3 " : "", p & 8 ? "4 " : "", p & 15 ? "" : "none");
        break;
    }
    case DETAIL_HEAP:
        message("%sDone: %s (%s) - next free memory at 0x%08X\n", pad, hook->name, time, GAME_HEAP_NEXT);
        break;
    case DETAIL_LEVEL:
        message("%sDone: %s, level %d (%s) - next free memory at 0x%08X\n", pad, hook->name, a0, time, GAME_HEAP_NEXT);
        break;
    case DETAIL_TEXTS:
        message("%sDone: %s (%s) - %d texts, %d names, next free memory at 0x%08X\n", pad, hook->name, time,
                GAME_TEXT_COUNT, GAME_TEXT_NAMES, GAME_HEAP_NEXT);
        break;
    case DETAIL_RETURN:
        message("%sDone: %s (%s) - result %d\n", pad, hook->name, time, result);
        break;
    default:
        message("%sDone: %s (%s)\n", pad, hook->name, time);
        break;
    }

    if (hook->last && !sStartupDone) {
        sStartupDone = 1;
        message("Startup complete: the title screen is ready.\n");
    }
    return result;
}

/* ------------------------------------------------------------------ Expansion Pak */

#define GAME_MEDIA_COPY       ((void (*)(void *, u32, u32))0x8022A760) /* _uvMediaCopy(dst, rom, size) */
void module_ext_load(u32 dataRom);
#define GAME_VI_BLACK         ((void (*)(u8))0x8023A830)               /* osViBlack */
#define GAME_VI_SWAP_BUFFER   ((void (*)(void *))0x8023AE20)           /* osViSwapBuffer */
#define GAME_WRITEBACK_DCACHE ((void (*)(void))0x8023AE70)             /* osWritebackDCacheAll */

/* Frame buffer of the message: heap memory the game has not used yet at this point. */
#define PAK_FRAME ((u16 *)0x80080000)
#define PAK_SCALE 2
#define COLOR_BLACK  0x0001
#define COLOR_WHITE  0xFFFF
#define COLOR_YELLOW 0xFFC1

/* One line of text, centred, each pixel of the font drawn as a PAK_SCALE x PAK_SCALE square. */
static void pak_line(int top, const char *line, u16 color) {
    int width = 0, col, y, x, dy, dx, left;
    while (line[width]) {
        width++;
    }
    left = (SCREEN_W - width * 6 * PAK_SCALE) / 2;
    for (col = 0; col < width; col++) {
        const u8 *glyph = sFont6x9[line[col] - 32];
        for (y = 0; y < 9; y++) {
            for (x = 0; x < 6; x++) {
                if (!(glyph[y] & (0x20 >> x))) {
                    continue;
                }
                for (dy = 0; dy < PAK_SCALE; dy++) {
                    u16 *p = PAK_FRAME + (top + y * PAK_SCALE + dy) * SCREEN_W + left + (col * 6 + x) * PAK_SCALE;
                    for (dx = 0; dx < PAK_SCALE; dx++) {
                        p[dx] = color;
                    }
                }
            }
        }
    }
}

/* Without the Expansion Pak: a black screen with the message, drawn by the CPU into a frame buffer
   of its own, then the game stops here (it never starts). The video mode is already set
   (uvSetVideoMode, before the game program starts); only the frame buffer is handed to the video
   interface. Emulators with HLE graphics may show only black, real hardware and LLE emulators
   show the message. */
static void pak_screen(void) {
    int i;
    for (i = 0; i < SCREEN_W * SCREEN_H; i++) {
        PAK_FRAME[i] = COLOR_BLACK;
    }
    pak_line(66, "EXPANSION PAK REQUIRED", COLOR_YELLOW);
    pak_line(108, "This game needs the", COLOR_WHITE);
    pak_line(132, "Nintendo 64", COLOR_WHITE);
    pak_line(156, "Expansion Pak.", COLOR_WHITE);
    GAME_WRITEBACK_DCACHE();
    GAME_VI_BLACK(0);
    GAME_VI_SWAP_BUFFER(PAK_FRAME);
    message("No Expansion Pak (%d MB of RAM): the game stops.\n", GAME_MEM_SIZE >> 20);
    for (;;) {
    }
}

/* ------------------------------------------------------------------ start */

/* Called by the loader after the module was copied into RAM, right before the game program
   starts (app_entrypoint). "arg" is the argument the game passes on. */
void module_init(s32 arg) {
    char *p;
    (void)arg;
    for (p = __bss_start; p < __bss_end; p++) {
        *p = 0;
    }

    if ((gHeader.flags & FLAG_REQUIRE_PAK) && GAME_MEM_SIZE < 0x800000) {
        pak_screen();
    }

    /* The mission settings into the data area (an empty header if there are none). */
    DATA_HEADER->magic = 0;
    GAME_WRITEBACK_DCACHE(); /* so the DMA cannot be overwritten by this line later */
    if (gHeader.dataSize != 0 && gHeader.dataSize <= DATA_AREA_END - DATA_AREA) {
        GAME_MEDIA_COPY((void *)DATA_AREA, gHeader.dataRom, gHeader.dataSize);
    }
    module_ext_load(gHeader.dataRom); /* its lower part (missions.c) */
    if (gHeader.flags & FLAG_LANDING) {
        landing_defaults(); /* the Hang Glider's patched landing code reads them from the first flight on */
    }

    if (!(gHeader.flags & FLAG_DEBUG)) {
        return;
    }
    message("Hawk's Hangar debug output%s%s\n", gHeader.info[0] ? " - " : "", gHeader.info);
    message("RAM: %d MB (%s), video: %s, start: %s\n", GAME_MEM_SIZE >> 20,
            GAME_MEM_SIZE > 0x400000 ? "Expansion Pak found" : "no Expansion Pak",
            GAME_TV_TYPE == 0 ? "PAL" : GAME_TV_TYPE == 2 ? "MPAL" : "NTSC",
            GAME_RESET_TYPE ? "reset button" : "power on");
    if (gHeader.flags & FLAG_STARTUP) {
        message("Done before the debug output could start: memory cleared, operating system started (osInitialize), "
                "kernel, PI manager and render threads started, video mode set.\n");
        message("Done: game program loaded, %d bytes from ROM 0x%06X to 0x%08X; its variables cleared "
                "(0x%08X-0x%08X).\n", APP_ROM_END - APP_ROM_START, APP_ROM_START, APP_RAM_START, APP_BSS_START,
                APP_BSS_END);
        message("Done: debug module loaded, %d bytes from ROM 0x%06X to 0x%08X.\n", gHeader.imageSize,
                gHeader.romOffset, (u32)&gHeader);
        message("Done: mission settings loaded, %d bytes from ROM 0x%06X to 0x%08X (%d missions).\n", gHeader.dataSize,
                gHeader.dataRom, DATA_AREA, DATA_HEADER->magic == DATA_MAGIC ? (int)DATA_HEADER->missions : 0);
        message("Starting: game program (app_entrypoint)\n");
    }
}
