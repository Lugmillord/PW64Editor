#!/usr/bin/env python3
"""Builds the Hawk's Hangar game module and writes it into the editor's source code.

    python3 build_module.py [--rom Pilotwings_64_USA.z64]

Needs a MIPS cross compiler (mips-linux-gnu-gcc and binutils, e.g. the Debian/Ubuntu packages
gcc-mips-linux-gnu and binutils-mips-linux-gnu). Writes:
  - hooks.g.h (table of the startup steps, included by module.c)
  - ../../src/PW64Editor.Core/Code/GameModuleImage.g.cs (the machine code and the call sites)
With --rom the call sites are checked against a clean US ROM first.

The list below is the only place where the startup steps are defined: each entry is a call
("jal") in the game code whose target the editor replaces by the hook function of the same
index. Names are shown in the debug output.
"""
import argparse
import base64
import os
import struct
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
CS_OUT = os.path.join(HERE, '..', '..', 'src', 'PW64Editor.Core', 'Code', 'GameModuleImage.g.cs')
LOAD_ADDRESS = 0x803805E0
AREA_END = 0x803905E0
CONDITIONS_CAPACITY = 160  # MAX_CONDITIONS in module.c

# (ROM offset of the jal, original target, name, detail, startup only, last step of the startup)
# detail: NONE, EEPROM, CONTROLLERS, HEAP, LEVEL, TEXTS, RETURN (see module.c)
HOOKS = [
    # app_entrypoint (src/app/map3d.c)
    (0x097358, 0x8022E0E0, 'system start (uvSysInit)', 'HEAP', 0, 0),
    (0x097360, 0x80223BB8, 'gamma setting (uvGfxEnableGamma)', 'NONE', 0, 0),
    (0x097368, 0x802EB9C0, 'game start (gameInit)', 'HEAP', 0, 0),
    # uvSysInit (src/kernel/system.c)
    (0x02F1C4, 0x8023A920, 'video filter (osViSetSpecialFeatures)', 'NONE', 0, 0),
    (0x02F1D0, 0x80220960, 'graphics (uvGfxInit)', 'NONE', 0, 0),
    (0x02F1D8, 0x80205FD0, 'clocks (uvClkInit)', 'NONE', 0, 0),
    (0x02F1E0, 0x802246A0, 'file block headers (uvMemInitBlockHdr)', 'NONE', 0, 0),
    (0x02F1E8, 0x80204930, 'channels (uvChanInit)', 'NONE', 0, 0),
    (0x02F1F0, 0x80218700, 'camera view (uvEnvResetModelView)', 'NONE', 0, 0),
    (0x02F1F8, 0x8020F9F4, 'kernel tables (func_8020F9F4)', 'NONE', 0, 0),
    (0x02F200, 0x80217398, 'display objects (uvDobjInit)', 'NONE', 0, 0),
    (0x02F208, 0x802242A0, 'controllers (uvControllerInit)', 'CONTROLLERS', 0, 0),
    (0x02F210, 0x80218BA0, 'sequences (uvSeqInit)', 'NONE', 0, 0),
    (0x02F218, 0x80219FD0, 'effects (uvFxInit)', 'NONE', 0, 0),
    (0x02F220, 0x8021F100, 'vertex buffers (uvVtxInit)', 'NONE', 0, 0),
    (0x02F228, 0x80230130, 'sprite tables (uvSprt_80230130)', 'NONE', 0, 0),
    (0x02F230, 0x80230954, 'sprites (uvSprtInit)', 'NONE', 0, 0),
    (0x02F238, 0x8022A3C0, 'memory blocks (uvMemInitBlocks)', 'NONE', 0, 0),
    (0x02F240, 0x802038A0, 'audio (uvaManagerInit)', 'NONE', 0, 0),
    (0x02F24C, 0x8023B9B0, 'save memory (osEepromProbe)', 'EEPROM', 0, 0),
    # gameInit (src/app/game.c)
    (0x072F1C, 0x80222E90, 'lighting (uvGfxEnableLighting)', 'NONE', 0, 0),
    (0x072F7C, 0x80324C70, 'Rocket Belt (rocketBeltInit)', 'NONE', 0, 0),
    (0x072F84, 0x802D5A90, 'Cannonball (cannonInit)', 'NONE', 0, 0),
    (0x072F8C, 0x8032FAB0, 'Sky Diving (skydivingInit)', 'NONE', 0, 0),
    (0x072F94, 0x802F9F60, 'Jumble Hopper (jumbleHopperInit)', 'NONE', 0, 0),
    (0x072F9C, 0x802CC1B0, 'Birdman (birdInit)', 'NONE', 0, 0),
    (0x072FA4, 0x802EF140, 'Hang Glider (hangGliderInit)', 'NONE', 0, 0),
    (0x072FAC, 0x802F5A00, 'Gyrocopter (gyrocopterInit)', 'NONE', 0, 0),
    (0x072FB4, 0x802F88D0, 'vehicle tables (func_802F88D0)', 'NONE', 0, 0),
    (0x072FBC, 0x802E6570, 'flight recorder (fdrInit)', 'NONE', 0, 0),
    (0x072FC4, 0x803449B0, 'mission objects (taskInit)', 'NONE', 0, 0),
    (0x072FCC, 0x802E1238, 'environment (envInit)', 'NONE', 0, 0),
    (0x072FD4, 0x8033F640, 'sound (sndInit)', 'NONE', 0, 0),
    (0x072FDC, 0x8034E080, 'wind objects (windObjInit)', 'NONE', 0, 0),
    (0x072FE4, 0x80347BD0, 'toys (toyInit)', 'NONE', 0, 0),
    (0x072FEC, 0x80321100, 'animations near the player (proxAnimInit)', 'NONE', 0, 0),
    (0x072FF4, 0x802E37B0, 'Meca Hawk (falcoInit)', 'NONE', 0, 0),
    (0x072FFC, 0x8034A450, 'user paths (userPathInit)', 'NONE', 0, 0),
    (0x073004, 0x80337D50, 'photo album (func_80337D50)', 'NONE', 0, 0),
    (0x07300C, 0x802E8304, 'saved games (saveModuleInit)', 'NONE', 0, 0),
    (0x073014, 0x80335B94, 'space shuttle (shuttleInit)', 'NONE', 0, 0),
    (0x07301C, 0x802F1FA0, 'glider toys (gliderToyInit)', 'NONE', 0, 0),
    (0x07307C, 0x802D3170, 'camera (cameraInit)', 'NONE', 0, 0),
    (0x073084, 0x8032B3D0, 'results (func_8032B3D0)', 'NONE', 0, 0),
    (0x073090, 0x8034B5E0, 'player view (func_8034B5E0)', 'NONE', 0, 0),
    # gameUpdateStateTitle (src/app/game.c): also called later, reported only while starting
    (0x07320C, 0x8022AF04, 'level memory (uvLevelInit)', 'HEAP', 1, 0),
    (0x073214, 0x80224CE0, 'title level (uvLevelAppend)', 'LEVEL', 1, 0),
    (0x07321C, 0x80224CE0, 'title level (uvLevelAppend)', 'LEVEL', 1, 0),
    (0x073224, 0x80224CE0, 'title level (uvLevelAppend)', 'LEVEL', 1, 0),
    (0x07322C, 0x80341F10, 'texts (textLoadBlock)', 'TEXTS', 1, 1),
]

CFLAGS = ['-march=vr4300', '-mabi=32', '-mfp32', '-msoft-float', '-mno-abicalls', '-fno-pic', '-G0', '-Os',
          '-ffreestanding', '-fno-builtin', '-nostdlib', '-mno-check-zero-division', '-fno-common',
          '-fno-asynchronous-unwind-tables', '-Wall', '-Wextra', '-Werror', '-std=gnu99']


def jal(target):
    return 0x0C000000 | ((target >> 2) & 0x03FFFFFF)


def run(*args):
    result = subprocess.run(args, capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(f'{" ".join(args)} failed:\n{result.stdout}{result.stderr}')
    return result.stdout


def write_hooks_header():
    lines = ['/* Generated by build_module.py - do not edit. */', '',
             'static const Hook sHooks[] = {']
    for _, target, name, detail, startup_only, last in HOOKS:
        lines.append(f'    {{ "{name}", (Function)0x{target:08X}, DETAIL_{detail}, {startup_only}, {last} }},')
    lines += ['};', '']
    for i in range(len(HOOKS)):
        lines.append(f's32 hook_{i}(s32 a0, s32 a1, s32 a2, s32 a3);')
        lines.append(f's32 hook_{i}(s32 a0, s32 a1, s32 a2, s32 a3) {{ return run_hook({i}, a0, a1, a2, a3); }}')
    with open(os.path.join(HERE, 'hooks.g.h'), 'w', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')


def check_rom(path):
    rom = open(path, 'rb').read()
    for site, target, name, *_ in HOOKS:
        word = struct.unpack('>I', rom[site:site + 4])[0]
        if word != jal(target):
            sys.exit(f'{name}: ROM 0x{site:06X} holds 0x{word:08X}, expected jal 0x{target:08X}')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--rom')
    args = parser.parse_args()
    if args.rom:
        check_rom(args.rom)
    write_hooks_header()

    with tempfile.TemporaryDirectory() as tmp:
        obj = os.path.join(tmp, 'module.o')
        elf = os.path.join(tmp, 'module.elf')
        binary = os.path.join(tmp, 'module.bin')
        run('mips-linux-gnu-gcc', *CFLAGS, '-c', os.path.join(HERE, 'module.c'), '-o', obj)
        run('mips-linux-gnu-ld', '-T', os.path.join(HERE, 'module.ld'), '--no-warn-rwx-segments', obj, '-o', elf)
        undefined = run('mips-linux-gnu-nm', '-u', elf).strip()
        if undefined:
            sys.exit('Undefined symbols (the module must not need libgcc):\n' + undefined)
        run('mips-linux-gnu-objcopy', '-O', 'binary', '--only-section=.header', '--only-section=.text',
            '--only-section=.rodata', '--only-section=.data', elf, binary)
        symbols = {}
        for line in run('mips-linux-gnu-nm', elf).splitlines():
            parts = line.split()
            if len(parts) == 3:
                symbols[parts[2]] = int(parts[0], 16)
        image = open(binary, 'rb').read()

    image_end = symbols['__image_end']
    bss_end = symbols['__bss_end']
    if symbols['gHeader'] != LOAD_ADDRESS:
        sys.exit('The header must come first.')
    if len(image) != image_end - LOAD_ADDRESS:
        image = image.ljust(image_end - LOAD_ADDRESS, b'\0')
    if bss_end > AREA_END:
        sys.exit(f'The module needs up to 0x{bss_end:08X}, the code area ends at 0x{AREA_END:08X}.')

    sites = [f'        new(0x{site:06X}, 0x{jal(target):08X}, 0x{symbols[f"hook_{i}"]:08X}, "{name}"),'
             for i, (site, target, name, *_) in enumerate(HOOKS)]
    encoded = base64.b64encode(image).decode()
    chunks = [encoded[i:i + 100] for i in range(0, len(encoded), 100)]
    cs = [
        '// <auto-generated>',
        '// Generated by native/game_module/build_module.py from module.c - do not edit.',
        '// </auto-generated>',
        '',
        'namespace PW64Editor.Core.Code;',
        '',
        '/// <summary>The compiled game module (see native/game_module/module.c).</summary>',
        'public static partial class GameModuleImage',
        '{',
        f'    /// <summary>RAM address the module is linked for (start of the code area).</summary>',
        f'    public const uint LoadAddress = 0x{LOAD_ADDRESS:08X};',
        '',
        f'    /// <summary>End of the module in RAM including its variables (must not pass the code area end).</summary>',
        f'    public const uint EndAddress = 0x{bss_end:08X};',
        '',
        '    /// <summary>RAM address of module_conditions (called by the scoring function).</summary>',
        f'    public const uint ConditionsHookAddress = 0x{symbols["module_conditions"]:08X};',
        '',
        '    /// <summary>RAM address of module_rings_left ("n to go" when a ring is passed).</summary>',
        f'    public const uint RingsLeftHookAddress = 0x{symbols["module_rings_left"]:08X};',
        '',
        '    /// <summary>RAM address of module_targets_left ("n to go" when a target is hit).</summary>',
        f'    public const uint TargetsLeftHookAddress = 0x{symbols["module_targets_left"]:08X};',
        '',
        '    /// <summary>Offset of the conditions table (gConditions, 16 bytes per mission) in the image.</summary>',
        f'    public const int ConditionsTableOffset = 0x{symbols["gConditions"] - LOAD_ADDRESS:X};',
        '',
        '    /// <summary>Number of entries the conditions table has room for.</summary>',
        f'    public const int ConditionsCapacity = {CONDITIONS_CAPACITY};',
        '',
        '    /// <summary>The machine code and data, as loaded from the ROM.</summary>',
        '    public static byte[] Image => System.Convert.FromBase64String(',
        '        ' + ' +\n        '.join(f'"{c}"' for c in chunks) + ');',
        '',
        '    /// <summary>The calls of the startup steps and the hook function that replaces each target.</summary>',
        '    public static readonly IReadOnlyList<GameModuleCallSite> StartupSites =',
        '    [',
        *sites,
        '    ];',
        '}',
        '',
    ]
    with open(CS_OUT, 'w', newline='\n') as f:
        f.write('\n'.join(cs))
    print(f'module: {len(image)} bytes, up to 0x{bss_end:08X} with variables; {len(HOOKS)} startup steps')


if __name__ == '__main__':
    main()
