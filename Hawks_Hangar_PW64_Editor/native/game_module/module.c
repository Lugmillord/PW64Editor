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
                        beside a pad; RB crash, hard landing, hard landing sideways (units per second) */
    u8 reserved[DATA_ENTRY_SIZE - 0x80];
} Condition;

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

/* "Points to the next medal" (levelSetPointsToNextMedal, 0x8032C174: s32 f(s32 *out, u16 points, u8 medal)), called
   through here by the game patch "Bonus games and Birdman". The game's medal table has no limits for the Birdman
   (entry 7 was 0, the patch sets 280/320/360), and when every medal is reached the original writes "100 x tests -
   points" only for the main vehicles and "100 - points" for all others. The Birdman has four missions per stage, so
   it gets the formula of the main vehicles, with the tests of its class. (Function names from the decompilation,
   https://github.com/gcsmith/Pilotwings64Decomp, MIT license.) */
#define GAME_POINTS_TO_MEDAL ((s32(*)(s32 *, u32, u32))0x8032C174) /* levelSetPointsToNextMedal */
#define GAME_TEST_COUNT      ((s32(*)(s32, s32))0x80344EF0)        /* taskGetTestCount(class, vehicle) */

s32 module_points_to_medal(s32 *out, u32 points, u32 medal) {
    s32 result = GAME_POINTS_TO_MEDAL(out, points, medal);
    u8 *state = GAME_STATE;
    if (result == 3 && state != 0) {
        u8 *player = state + (*(s32 *)(state + 0x9C)) * 140 + 0x0C; /* as the original: the current player */
        if (*(u16 *)(player + 2) == 6) {
            *out = 100 * GAME_TEST_COUNT(*(u16 *)(player + 4), 6) - (s32)(points & 0xFFFF);
        }
    }
    return result;
}

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

static void landing_defaults(void) {
    LIVE_LANDING[0] = FLOAT_50;
    LIVE_LANDING[1] = FLOAT_50;
    LIVE_LANDING[2] = FLOAT_15;
    LIVE_LANDING[3] = FLOAT_15;
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

    if (own && vehicle == 0) {
        for (i = 0; i < 4; i++) {
            LIVE_LANDING[i] = c->limits[i];
        }
    } else {
        landing_defaults();
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

void module_flight_frame(u32 a0, u32 a1, u32 a2, u32 a3, u32 a4, u32 a5, u32 a6, u32 a7, u32 a8, u32 a9, u32 a10) {
    const Condition *c;
    u8 *result;
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
