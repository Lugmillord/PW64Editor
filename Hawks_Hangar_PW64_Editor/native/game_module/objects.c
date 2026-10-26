/*
 * Hawk's Hangar game module, part "objects": preparing the level editor (stage 8).
 *
 *   - Radar goals: hud_8031A378 (hud.c) writes the mission's hopper goals, ball targets, used landing pads and landing
 *     strips into the radar's goals[10] once at the start, without a check: more than 10 overwrite the waypoints behind
 *     them (an original bug). The editor points the function's goal list to module_radar_goals (room for
 *     RADAR_GOALS_MAX); every call of hudDrawRadar goes through module_hud_radar, which first copies the 10 goals nearest
 *     to the player into the real list. With 10 goals or fewer nothing changes for the player.
 *
 * Soft-float like module.c (floats are handled as their bits). Function and variable names from the decompilation
 * (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).
 */

typedef unsigned char u8;
typedef unsigned int u32;
typedef signed int s32;

#define GAME_HUD_RADAR ((void (*)(s32, s32, u32, u32, u32, u32, u8 *))0x8031AD68) /* hudDrawRadar */

/* ------------------------------------------------------------------ radar goals */

#define RADAR_GOALS_MAX  32 /* hopper goals 5 + ball targets 5 + landing pads 14 + landing strips 2 = 26 */
#define RADAR_GOALS_SHOWN 10 /* HUDRadar.goals[10] */
#define GOAL_SIZE        16 /* HUDRadar_Goals: x, y, angle (f32), kind (s32) */

/* Laid out like the start of HUDRadar: goalCount (s32), unk4, goals[]. hud_8031A378 fills it. */
u8 module_radar_goals[8 + (RADAR_GOALS_MAX * GOAL_SIZE)] __attribute__((aligned(8)));

/* A float (its bits) as a whole number, rounded towards zero, clamped to +-30000 (game units). */
static s32 float_to_units(u32 bits) {
    s32 exp = (s32)((bits >> 23) & 0xFF) - 127;
    u32 mant = (bits & 0x7FFFFF) | 0x800000;
    s32 value;
    if (exp < 0) {
        return 0;
    }
    if (exp > 14) {
        value = 30000;
    } else {
        value = (s32)(mant >> (23 - exp));
        if (value > 30000) {
            value = 30000;
        }
    }
    return (bits & 0x80000000u) ? -value : value;
}

/* Takes the place of hudDrawRadar (all its calls): the 10 goals nearest to the player (xOff, yOff) into the radar's
   list, then the game's function. */
void module_hud_radar(s32 x, s32 y, u32 xOff, u32 yOff, u32 heading, u32 pitch, u8 *radar) {
    s32 count = *(volatile s32 *)module_radar_goals;
    s32 px = float_to_units(xOff), py = float_to_units(yOff);
    u32 taken = 0;
    s32 shown = 0, i, k;
    if (count > RADAR_GOALS_MAX) {
        count = RADAR_GOALS_MAX;
    }
    if (count < 0) {
        count = 0;
    }
    for (k = 0; k < RADAR_GOALS_SHOWN && k < count; k++) {
        s32 best = -1;
        u32 bestDistance = 0;
        for (i = 0; i < count; i++) {
            const u8 *goal = module_radar_goals + 8 + (i * GOAL_SIZE);
            s32 dx, dy;
            u32 distance;
            if (taken & (1u << i)) {
                continue;
            }
            dx = float_to_units(*(const u32 *)goal) - px;
            dy = float_to_units(*(const u32 *)(goal + 4)) - py;
            /* halved, so the sum of the squares fits (at most 2 x 30000^2) */
            dx = (dx < 0 ? -dx : dx) >> 1;
            dy = (dy < 0 ? -dy : dy) >> 1;
            distance = ((u32)dx * (u32)dx) + ((u32)dy * (u32)dy);
            if (best < 0 || distance < bestDistance) {
                best = i;
                bestDistance = distance;
            }
        }
        taken |= 1u << best;
        {
            const u32 *from = (const u32 *)(module_radar_goals + 8 + (best * GOAL_SIZE));
            u32 *to = (u32 *)(radar + 8 + (shown * GOAL_SIZE));
            to[0] = from[0];
            to[1] = from[1];
            to[2] = from[2];
            to[3] = from[3];
        }
        shown++;
    }
    *(s32 *)radar = shown;
    GAME_HUD_RADAR(x, y, xOff, yOff, heading, pitch, radar);
}
