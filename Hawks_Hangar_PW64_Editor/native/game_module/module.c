/*
 * Hawk's Hangar game module (US version of Pilotwings 64).
 *
 * New program code that the editor builds into a hack ROM. The game loads it from the end of the
 * ROM into the editor's code area (0x803805E0, behind the game's variables) right before the
 * game program starts (loader in the code cave, see CodeArea.cs), then calls module_init().
 *
 * What it does:
 *   - Conditions for time points (and "a crash gives 0 points") per mission, combined freely:
 *     the editor writes a table of the missions that need them; the scoring function calls
 *     module_conditions() instead of reading the retail condition mode.
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

#define LOG_SIZE 8192

void module_init(s32 arg);
int module_game_printf(const char *fmt, ...);

extern char __image_end[];
extern char __bss_start[];
extern char __bss_end[];

static char sLog[LOG_SIZE];

/* ------------------------------------------------------------------ conditions table */

#define COND_CRASH_ZERO 1u /* a crash gives 0 points in total */
#define COND_ALTITUDE   2u /* time points only if the highest altitude reached "altitude" */

#define OBJECT_TYPES 7      /* rings, balloons, targets, hover pads, Meca Hawks, ball targets, hopper goals */
#define REQUIRED_ALL 0xFF
#define MAX_CONDITIONS 160

/* One mission with conditions (16 bytes). required[type]: 0 = not required, 0xFF = all,
   otherwise at least this many. Written by the editor (GameModule.cs). */
typedef struct {
    u8 vehicle;
    u8 cls;
    u8 test;
    u8 flags;
    u8 required[OBJECT_TYPES];
    u8 pad;
    u32 altitude; /* f32 bits */
} Condition;

__attribute__((section(".data"))) Condition gConditions[MAX_CONDITIONS] = { { 0xFF, 0, 0, 0, { 0 }, 0, 0 } };

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
    void *conditions;   /* +0x28 table of the missions with conditions (gConditions) */
    u32 conditionCount; /* +0x2C entries used, written by the editor */
    char info[96];      /* +0x30 "project ..., built ...", written by the editor */
};

__attribute__((section(".header"), used)) struct Header gHeader = {
    0x4848474D, (void *)module_game_printf, (void *)module_init, 0, 0, 0,
    sLog, LOG_SIZE, 0, 2, gConditions, 0, "",
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

static void overlay(u16 *frame, void *depth);

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
    if (GAME_GFX_CALLBACK != (void *)overlay) {
        sOtherCallback = (FrameCallback)GAME_GFX_CALLBACK;
        GAME_GFX_CALLBACK = (void *)overlay;
    }
}

/* Called by uvGfxEnd with the frame that was just drawn (RGBA 5551, 320 x 240): darkens the
   lines' background and draws the text. After 15 seconds without a new message the callback
   the game had before is put back. */
static void overlay(u16 *frame, void *depth) {
    int row, col, y, x;
    int top;
    if (sOtherCallback != 0 && sOtherCallback != overlay) {
        sOtherCallback(frame, depth);
    }
    if (cpu_count() - sScreenTime > OVL_TICKS) {
        if (GAME_GFX_CALLBACK == (void *)overlay) {
            GAME_GFX_CALLBACK = (void *)sOtherCallback;
        }
        sScreenCount = 0;
        return;
    }

    top = OVL_BOTTOM + 1 - sScreenCount * 9;
    for (row = 0; row < sScreenCount; row++) {
        const char *line = sScreen[row];
        int width = 0;
        while (line[width]) {
            width++;
        }
        for (y = 0; y < 9; y++) {
            u16 *p = frame + (top + row * 9 + y) * SCREEN_W + OVL_X - 2;
            for (x = 0; x < width * 6 + 4; x++) {
                p[x] = ((p[x] >> 2) & 0x39CE) | 1; /* a quarter of the brightness */
            }
        }
        for (col = 0; col < width; col++) {
            const u8 *glyph = sFont6x9[line[col] - 32];
            for (y = 0; y < 9; y++) {
                u8 bits = glyph[y];
                u16 *p = frame + (top + row * 9 + y) * SCREEN_W + OVL_X + col * 6;
                for (x = 0; x < 6; x++) {
                    if (bits & (0x20 >> x)) {
                        p[x] = 0xFFFF;
                    }
                }
            }
        }
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

/* The whole part of a float (given as its bits), without the FPU. */
static s32 float_to_int(s32 bits) {
    int exp = ((bits >> 23) & 0xFF) - 127;
    u32 mant = ((u32)bits & 0x7FFFFF) | 0x800000;
    s32 value;
    if (exp < 0) {
        return 0;
    }
    if (exp > 30) {
        return bits < 0 ? -0x7FFFFFFF : 0x7FFFFFFF;
    }
    value = (s32)(exp >= 23 ? mant << (exp - 23) : mant >> (23 - exp));
    return bits < 0 ? -value : value;
}

/* Called by the scoring function (func_8032B560) instead of taskGet_80346364, after the crash
   and hard end overrides. "result" is the flight result (flags at +0, highest altitude at
   +0x18, crash at +0x3C). Missions without an entry keep the retail condition mode. */
/* The table entry of the mission being flown, or 0. */
static const Condition *current_condition(void) {
    u8 *comm = GAME_TASK_SCORING() - 0x48;
    u32 i;
    for (i = 0; i < gHeader.conditionCount && i < MAX_CONDITIONS; i++) {
        if (gConditions[i].cls == comm[0] && gConditions[i].vehicle == comm[1] && gConditions[i].test == comm[2]) {
            return &gConditions[i];
        }
    }
    return 0;
}

s32 module_conditions(u8 *result) {
    const Condition *c = current_condition();
    u32 *flags = (u32 *)result;
    u32 i;
    int met = 1;

    if (c == 0) {
        return GAME_TASK_MODE();
    }

    if ((c->flags & COND_CRASH_ZERO) && result[0x3C]) {
        message("Conditions: crash, 0 points.\n");
        *flags = 0;
        return 0;
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
        message("Conditions: highest altitude %d m, %d m needed: %s.\n", float_to_int(reached), float_to_int(limit),
                high ? "reached" : "not reached");
        if (!high) {
            met = 0;
        }
    }
    if (!met) {
        *flags &= ~0x800u;
    }
    message("Conditions: time points %s.\n", met ? "count" : "do not count");
    return 0;
}

/* "n to go" on the HUD. The ring and target code ask func_8032C080 how many objects are still
   needed (before counting the one just cleared) and show one less; the retail game answers only
   in condition mode 3, otherwise -1 (no message). For a mission in the table that needs a number
   of rings or targets, the module answers with what is still missing. It never reports "reached",
   so the game does not say "please land" or remove the other objects. */
#define GAME_OBJECTS_LEFT ((s32(*)(s32 *))0x8032C080) /* func_8032C080 */

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
        *left = need > cleared ? need - cleared : 0;
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

/* ------------------------------------------------------------------ start */

/* Called by the loader after the module was copied into RAM, right before the game program
   starts (app_entrypoint). "arg" is the argument the game passes on. */
void module_init(s32 arg) {
    char *p;
    (void)arg;
    for (p = __bss_start; p < __bss_end; p++) {
        *p = 0;
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
        message("Starting: game program (app_entrypoint)\n");
    }
}
