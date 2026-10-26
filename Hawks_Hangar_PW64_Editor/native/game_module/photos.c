/*
 * Hawk's Hangar game module, part "photos" (stage 8).
 *
 *   - Ten photos per flight: the flight's photos (D_80373390, 6) move to the high data area with room for ten
 *     (FlightPhotos.cs). The album stays at six (the save file has no room for more).
 *   - The photo grid (func_8033D3EC, used for the album and for the photos of a flight) is replaced by
 *     module_photo_grid: the same screen, but with pages of six photos. Moving right from the right column (left from
 *     the left column) turns to the next (previous) page; arrows in the title bar show that there are more pages. With
 *     six photos or fewer it behaves as the game's.
 *   - "Please wait..." while photos are drawn: the game draws every photo by setting up its test (for photos of other
 *     tests the mission is loaded again) and drawing the island, which takes seconds. The grid shows the text in its
 *     title bar (where "Check which photo ?" is shown afterwards) until all six are drawn; the single photo views
 *     (func_8033DDD8, func_8033DFD0) show it in the middle of the photo area, where the picture then replaces it; when the
 *     photo's test has to be loaded first (gameUpdateStateTestSetup clears the photo area before it loads), the text is
 *     drawn again on the cleared area and stays there while the island loads.
 *
 * Built with the FPU (-mhard-float) like weather.c; only integers and pointers cross into module.c. Function and
 * variable names from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).
 */

typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef signed int s32;
typedef float f32;
typedef f32 Mtx4F[4][4];

/* ------------------------------------------------------------------ module.c */

const u16 *module_busy_text(void);

/* ------------------------------------------------------------------ game addresses (US) */

#define GAME_CLEAR_SCREENS  ((void (*)(void))0x8033A6B8)                     /* func_8033A6B8 */
#define GAME_FRAME_TIME     ((void (*)(f32))0x80223408)                      /* uvGfxSetFrameTime */
#define GAME_FILM_STRIP     ((void (*)(void))0x8033A72C)                     /* func_8033A72C */
#define GAME_CLK_RESET      ((void (*)(s32))0x80206150)                      /* uvClkReset */
#define GAME_CLK_SEC        ((double (*)(s32))0x80206058)                    /* uvClkGetSec */
#define GAME_PHOTO_VIEWPORT ((void (*)(s32, s32, s32, s32))0x8033A610)       /* func_8033A610 */
#define GAME_PHOTO_SETUP    ((void (*)(s32, u8 *))0x8033A128)                /* func_8033A128 */
#define GAME_PHOTO_RENDER   ((void (*)(s32, u8 *))0x8033A244)                /* func_8033A244 */
#define GAME_PHOTO_FRAME    ((void (*)(s32, s32))0x8033CBD0)                 /* func_8033CBD0 */
#define GAME_SCREEN_VIEWPORT ((void (*)(s32, s32, s32, s32))0x8033A664)      /* func_8033A664 */
#define GAME_SFX            ((void (*)(s32))0x8033F7F8)                      /* sndPlaySfx */
#define GAME_SFX_VPP        ((void (*)(s32, f32, f32, f32))0x8033F758)       /* sndPlaySfxVolPitchPan */
#define GAME_SND_UPDATE     ((void (*)(void))0x8033FB14)                     /* func_8033FB14 */
#define GAME_COPY_FB        ((void (*)(s32))0x80223DEC)                      /* uvCopyFrameBuf */
#define GAME_CHAN_VIEWPORT  ((void (*)(s32, s32, s32, s32, s32))0x80204D94)  /* uvChan_80204D94 */
#define GAME_GFX_BEGIN      ((void (*)(void))0x80220B88)                     /* uvGfxBegin */
#define GAME_GFX_END        ((void (*)(void))0x8022217C)                     /* uvGfxEnd */
#define GAME_DEMO_UPDATE    ((s32(*)(void))0x80323020)                       /* demo_80323020 */
#define GAME_BUTTON_PRESS   ((s32(*)(s32, s32))0x803231E0)                   /* demoButtonPress */
#define GAME_INPUT          ((f32(*)(s32, s32))0x803230B0)                   /* demoGetInputs */
#define GAME_FONT_SET       ((void (*)(s32))0x802194D8)                      /* uvFontSet */
#define GAME_FONT_SCALE     ((void (*)(double, double))0x80219550)           /* uvFontScale */
#define GAME_FONT_COLOR     ((void (*)(s32, s32, s32, s32))0x8021956C)       /* uvFontColor */
#define GAME_FONT_WIDTH16   ((s32(*)(const u16 *))0x802196B0)                /* uvFontStr16Width */
#define GAME_FONT_PRINT16   ((s32(*)(s32, s32, const u16 *, s32, s32))0x80219874) /* uvFontPrintStr16 */
#define GAME_FONT_DLIST     ((void (*)(void))0x80219EA8)                     /* uvFontGenDlist */
#define GAME_TEXT           ((const u16 *(*)(s32))0x80342198)                /* textGetDataByIdx */
#define GAME_ORTHO          ((void (*)(Mtx4F *, f32, f32, f32, f32))0x80229CD4) /* uvMat4SetOrtho */
#define GAME_PROJ_PUSH      ((void (*)(Mtx4F *))0x80222100)                  /* uvGfxMtxProjPushF */
#define GAME_IDENTITY       ((void (*)(Mtx4F *))0x80228954)                  /* uvMat4SetIdentity */
#define GAME_VIEW_LOAD      ((void (*)(Mtx4F *, s32))0x8022345C)             /* uvGfxMtxViewLoad */
#define GAME_VIEW_POP       ((void (*)(void))0x802236A8)                     /* uvGfxMtxViewPop */
#define GAME_POLY_BEGIN     ((void (*)(void))0x8021F368)                     /* uvVtxBeginPoly */
#define GAME_VTX            ((s32(*)(s32, s32, s32, s32, s32, s32, s32, s32, s32))0x8021F264) /* uvVtx */
#define GAME_POLY_END       ((void (*)(void))0x802206C0)                     /* uvVtxEndPoly */

#define PHOTO_POS           ((const s32(*)[2])0x80350560) /* D_80350560: the six places of the grid (x, y) */
#define ALBUM_COUNT         (*(volatile s32 *)0x80350528) /* D_80350528 */
#define FLIGHT_COUNT        (*(volatile s32 *)0x8035052C) /* D_8035052C */

#define PHOTO_SIZE    0x70 /* Unk80373060 */
#define PHOTO_SUBJECT 0x43 /* unk43[0]: 0 = no photo */
#define PER_PAGE      6
#define MODE_ALBUM    0
#define MODE_FLIGHT   2

#define CLOCK_APP     4    /* UV_CLKID_APP */
#define BUTTON_A      0x8000
#define BUTTON_B      0x4000
#define BUTTON_START  0x1000
#define SFX_MOVE      0x00 /* SFX_UI_MOVE_CHIME */
#define SFX_CANCEL    0x01 /* SFX_UI_CANCEL */
#define SFX_CONFIRM   0x6E /* SFX_UI_CONFIRM */
#define SFX_PHOTO     0x54 /* the sound of each photo appearing */
#define TEXT_CHOOSE   0x139 /* TEXT_VIEW_PHOTO1 "Check which photo ?" */

#define BAR_X0 28 /* the film strip's bar at the top of the screen, behind "Check which photo ?" (func_8033A72C) */
#define BAR_X1 284
#define BAR_Y0 191
#define BAR_Y1 213

/* ------------------------------------------------------------------ drawing */

static void screen_begin(void) {
    Mtx4F projection, view;
    GAME_SCREEN_VIEWPORT(0, 319, 0, 239);
    GAME_GFX_BEGIN();
    GAME_ORTHO(&projection, 0.0f, 319.0f, 0.0f, 239.0f);
    GAME_PROJ_PUSH(&projection);
    GAME_IDENTITY(&view);
    GAME_VIEW_LOAD(&view, 1);
}

static void screen_end(void) {
    GAME_VIEW_POP();
    GAME_GFX_END();
}

static void rect(s32 x0, s32 y0, s32 x1, s32 y1, s32 r, s32 g, s32 b) {
    GAME_POLY_BEGIN();
    GAME_VTX(x0, y0, 0, 0, 0, r, g, b, 255);
    GAME_VTX(x1, y0, 0, 0, 0, r, g, b, 255);
    GAME_VTX(x1, y1, 0, 0, 0, r, g, b, 255);
    GAME_VTX(x0, y1, 0, 0, 0, r, g, b, 255);
    GAME_POLY_END();
}

/* "Please wait..." centred at x, in both frame buffers (each uvGfxBegin/uvGfxEnd draws into the other one). */
static void draw_busy(s32 x, s32 y, s32 r, s32 g, s32 b) {
    const u16 *text = module_busy_text();
    s32 k;
    if (text == 0) {
        return;
    }
    for (k = 0; k < 2; k++) {
        GAME_GFX_BEGIN();
        GAME_FONT_SET(6);
        GAME_FONT_SCALE(1.0, 1.0);
        GAME_FONT_COLOR(r, g, b, 0xFF);
        GAME_FONT_PRINT16(x - GAME_FONT_WIDTH16(text) / 2, y, text, 40, 0xFFE);
        GAME_FONT_DLIST();
        GAME_GFX_END();
    }
}

/* The grid's title bar again (without "Please wait..."), with arrows at both ends if there are more pages. */
static void draw_bar(s32 arrows) {
    s32 k;
    for (k = 0; k < 2; k++) {
        screen_begin();
        rect(BAR_X0, BAR_Y0, BAR_X1, BAR_Y1, 0, 0, 100);
        if (arrows) {
            GAME_POLY_BEGIN();
            GAME_VTX(44, 195, 0, 0, 0, 255, 240, 0, 255);
            GAME_VTX(44, 209, 0, 0, 0, 255, 240, 0, 255);
            GAME_VTX(35, 202, 0, 0, 0, 255, 240, 0, 255);
            GAME_POLY_END();
            GAME_POLY_BEGIN();
            GAME_VTX(268, 195, 0, 0, 0, 255, 240, 0, 255);
            GAME_VTX(277, 202, 0, 0, 0, 255, 240, 0, 255);
            GAME_VTX(268, 209, 0, 0, 0, 255, 240, 0, 255);
            GAME_POLY_END();
        }
        screen_end();
    }
}

/* ------------------------------------------------------------------ the grid */

/* The game's move of the cursor within the six places (func_8033D3EC; 0-2 the lower row, 3-5 the upper one). */
static s32 grid_move(s32 i, f32 x, f32 y, s32 horizontal, s32 vertical) {
    if (horizontal && vertical) {
        if (x > 0.0f && y > 0.0f) {
            i -= 2;
            if (i < 0) {
                i += 6;
            } else if (i == 3) {
                i = 0;
            } else if (i == 0) {
                i = 3;
            }
        } else if (x > 0.0f && y < 0.0f) {
            i += 4;
            if (i >= 6) {
                i -= 6;
            }
        } else if (x < 0.0f && y > 0.0f) {
            i -= 4;
            if (i < 0) {
                i += 6;
            }
        } else {
            i += 2;
            if (i >= 6) {
                i -= 6;
            } else if (i == 2) {
                i = 5;
            } else if (i == 5) {
                i = 3;
            }
        }
    } else if (vertical) {
        if (y > 0.0f) {
            i -= 3;
            if (i < 0) {
                i += 6;
            }
        } else {
            i += 3;
            if (i >= 6) {
                i -= 6;
            }
        }
    } else if (x > 0.0f) {
        i += 1;
        if (i == 3) {
            i = 0;
        } else if (i == 6) {
            i = 3;
        }
    } else {
        i -= 1;
        if (i == -1) {
            i = 2;
        } else if (i == 2) {
            i = 5;
        }
    }
    return i;
}

static s32 tilted(f32 v) {
    return (v < 0.0f ? -v : v) >= 0.3f;
}

static f32 sPulse = 0.0f;
static f32 sPulseStep = 0.025f;

/*
 * Takes the place of func_8033D3EC(mode, count, photos, page, cursor) (its two calls in func_8033E3A8): the grid of the
 * album (mode 0) or of the flight's photos (mode 2). *page is the page shown (the game passes a start index that is
 * always 0), *cursor the photo chosen. Returns 1 when B (or Start without photos) leaves the grid, 0 when a photo was
 * chosen.
 */
s32 module_photo_grid(s32 mode, s32 count, u8 *photos, s32 *page, s32 *cursor) {
    s32 pages = count > PER_PAGE ? (count + PER_PAGE - 1) / PER_PAGE : 1;
    s32 cancel = 0, confirm = 0, held = 0;
    if (*page < 0 || *page >= pages) {
        *page = 0;
    }
    if (*cursor < *page * PER_PAGE || *cursor >= (*page + 1) * PER_PAGE) {
        *cursor = *page * PER_PAGE;
    }
    while (!cancel && !confirm) {
        s32 first = *page * PER_PAGE;
        s32 i, turn = 0;
        GAME_CLEAR_SCREENS();
        GAME_FRAME_TIME(0.00001f);
        GAME_FILM_STRIP();
        GAME_FILM_STRIP();
        draw_busy(160, BAR_Y0 + 1, 0xFF, 0xFF, 0xFF);
        for (i = first; i < first + PER_PAGE; i++) {
            const s32 *pos = PHOTO_POS[i - first];
            u8 *photo = i < count && photos[i * PHOTO_SIZE + PHOTO_SUBJECT] != 0 ? photos + i * PHOTO_SIZE : 0;
            GAME_CLK_RESET(CLOCK_APP);
            while (GAME_CLK_SEC(CLOCK_APP) < 0.1) {
            }
            if (photo != 0) {
                GAME_PHOTO_VIEWPORT(pos[0], pos[0] + 80, pos[1], pos[1] + 59);
            } else {
                GAME_CHAN_VIEWPORT(0, pos[0], pos[0] + 80, pos[1] - 1, pos[1] + 59);
            }
            GAME_PHOTO_SETUP(mode, photo);
            GAME_SFX(SFX_PHOTO);
            GAME_SND_UPDATE();
            GAME_COPY_FB(0);
            GAME_PHOTO_RENDER(mode, photo);
            GAME_PHOTO_RENDER(mode, photo);
            GAME_PHOTO_RENDER(mode, photo);
        }
        GAME_GFX_BEGIN();
        GAME_GFX_END();
        draw_bar(pages > 1);
        if (count != 0) {
            GAME_PHOTO_FRAME(*cursor - first, 0);
            GAME_PHOTO_FRAME(*cursor - first, 0);
        }
        sPulse = 0.0f;

        while (!turn) {
            f32 r, g, b;
            GAME_DEMO_UPDATE();
            GAME_GFX_BEGIN();
            sPulse += sPulseStep;
            if (sPulse > 1.0f) {
                sPulse = 1.0f;
                sPulseStep = -sPulseStep;
            } else if (sPulse < 0.0f) {
                sPulse = 0.0f;
                sPulseStep = -sPulseStep;
            }
            r = 44.0f + sPulse * (145.0f - 44.0f);
            g = 214.0f + sPulse * (183.0f - 214.0f);
            b = 44.0f + sPulse * (255.0f - 44.0f);
            GAME_FONT_SET(6);
            GAME_FONT_COLOR((s32)r, (s32)g, (s32)b, 0xFF);
            GAME_FONT_SCALE(1.0, 1.0);
            GAME_FONT_PRINT16(70, 190, GAME_TEXT(TEXT_CHOOSE), 100, 0xFFE);
            GAME_FONT_DLIST();
            GAME_GFX_END();

            if (GAME_BUTTON_PRESS(0, BUTTON_B) || (count == 0 && GAME_BUTTON_PRESS(0, BUTTON_START))) {
                cancel = 1;
                GAME_SFX(SFX_CANCEL);
                break;
            }
            if (count != 0 && GAME_BUTTON_PRESS(0, BUTTON_A | BUTTON_START)) {
                if (((mode == MODE_ALBUM && *cursor < ALBUM_COUNT) || (mode == MODE_FLIGHT && *cursor < FLIGHT_COUNT))
                    && photos[*cursor * PHOTO_SIZE + PHOTO_SUBJECT] != 0) {
                    confirm = 1;
                    GAME_SFX(SFX_CONFIRM);
                    break;
                }
                GAME_SFX(SFX_CANCEL);
            }

            if (count != 0) {
                f32 x = GAME_INPUT(0, 0);
                f32 y = GAME_INPUT(0, 1);
                s32 horizontal = tilted(x), vertical = tilted(y);
                if (!held && (horizontal || vertical)) {
                    s32 place = *cursor - first;
                    held = 1;
                    if (pages > 1 && horizontal && ((x > 0.0f && place % 3 == 2) || (x < 0.0f && place % 3 == 0))) {
                        /* over the edge: the next or previous page, the other end of the row */
                        s32 next = place - place % 3 + (x > 0.0f ? 0 : 2);
                        if (vertical) {
                            next = (next + 3) % 6;
                        }
                        *page = x > 0.0f ? (*page + 1) % pages : (*page + pages - 1) % pages;
                        *cursor = *page * PER_PAGE + next;
                        GAME_SFX_VPP(SFX_MOVE, 1.0f, 1.03f, 0.0f);
                        turn = 1;
                    } else {
                        s32 next = grid_move(place, x, y, horizontal, vertical);
                        if (next != place) {
                            GAME_SFX_VPP(SFX_MOVE, 1.0f, 1.03f, 0.0f);
                            GAME_PHOTO_FRAME(place, 1);
                            GAME_PHOTO_FRAME(place, 1);
                            *cursor = first + next;
                            GAME_PHOTO_FRAME(next, 0);
                            GAME_PHOTO_FRAME(next, 0);
                        }
                    }
                } else if (held && !horizontal && !vertical) {
                    held = 0;
                }
            }
        }
    }
    GAME_CLEAR_SCREENS();
    return cancel;
}

/* ------------------------------------------------------------------ single photos */

static s32 sBusyX, sBusyY; /* where module_photo_busy put the text */
static s32 sSettingUp;     /* module_photo_setup is running */

/*
 * Takes the place of func_8033A610 (the photo's viewport) in func_8033DDD8 (the example photos) and func_8033DFD0 (one
 * photo of the album or the flight): "Please wait..." in the middle of the (still white) photo area first; the picture
 * drawn there afterwards covers it.
 */
void module_photo_busy(s32 x0, s32 x1, s32 y0, s32 y1) {
    sBusyX = (x0 + x1) / 2;
    sBusyY = (y0 + y1) / 2 - 6;
    draw_busy(sBusyX, sBusyY, 0x20, 0x20, 0x20);
    GAME_PHOTO_VIEWPORT(x0, x1, y0, y1);
}

/*
 * Takes the place of func_8033A128 (the photo's test is set up) in the same two functions. For a photo of another test
 * this is gameUpdateStateTestSetup, which first clears the photo area twice (black) and then loads the island and the
 * mission for seconds; module_setup_frame draws the text again after those two frames.
 */
void module_photo_setup(s32 mode, u8 *photo) {
    sSettingUp = 1;
    GAME_PHOTO_SETUP(mode, photo);
    sSettingUp = 0;
}

/* The second uvGfxEnd of gameUpdateStateTestSetup (after its two clears): "Please wait..." in white on the black photo
   area while module_photo_setup runs, otherwise nothing more than the game's uvGfxEnd. */
void module_setup_frame(void) {
    GAME_GFX_END();
    if (sSettingUp) {
        draw_busy(sBusyX, sBusyY, 0xFF, 0xFF, 0xFF);
    }
}
