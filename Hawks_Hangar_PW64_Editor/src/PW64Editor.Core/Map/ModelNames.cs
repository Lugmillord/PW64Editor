namespace PW64Editor.Core.Map;

/// <summary>Descriptive names of the game's models.</summary>
/// <remarks>Taken from the model enum of include/kernel/uv_dobj.h of the decompilation
/// (https://github.com/gcsmith/Pilotwings64Decomp, MIT license), written in plain words. They are descriptions by the
/// decompilation's authors, not names from the game. Models the decompilation leaves unnamed were named by the editor's
/// author after looking at them; the few still unnamed show their number.</remarks>
public static class ModelNames
{
    private static readonly string[] Names =
    [
        "Holiday island low poly", // 0x000
        "White sail boat with red logo", // 0x001
        "Blue cuboid", // 0x002
        "Yellow speed boat", // 0x003
        "Themepark brown green building", // 0x004
        "Resort tiki hut 1", // 0x005
        "Themepark circus tent", // 0x006
        "Themepark long building", // 0x007
        "Themepark small building", // 0x008
        "Themepark merry go round", // 0x009
        "Castle", // 0x00A
        "Themepark entry building", // 0x00B
        "Themepark ferris wheel", // 0x00C
        "Village water wheel", // 0x00D
        "Village bridge brick", // 0x00E
        "Bridge brick long white", // 0x00F
        "Village large old building", // 0x010
        "Village building 1", // 0x011
        "Village building 2", // 0x012
        "Resort building near airport", // 0x013
        "Village building with chimney", // 0x014
        "Holiday lighthouse", // 0x015
        "Castle yard", // 0x016
        "Airport atc building", // 0x017
        "Palm tree", // 0x018
        "Five palm trees", // 0x019
        "Red blue sailboat", // 0x01A
        "Yellow white plane", // 0x01B
        "Beach umbrellas and towels", // 0x01C
        "Barn", // 0x01D
        "Small farm shack", // 0x01E
        "Yellow n64 flag", // 0x01F
        "Resort tiki hut 2", // 0x020
        "Carpark street lamp", // 0x021
        "Tree", // 0x022
        "Tree small", // 0x023
        "Tree tall", // 0x024
        "Themepark entrance arch", // 0x025
        "Double water fountain", // 0x026
        "Big white red plane", // 0x027
        "Crescent island low poly", // 0x028
        "Red white blue yacht", // 0x029
        "White yellow sailboat", // 0x02A
        "Rock arch", // 0x02B
        "Rock post", // 0x02C
        "Rock pillar tall", // 0x02D
        "Rock small", // 0x02E
        "Rock big", // 0x02F
        "Rock small 2", // 0x030
        "Rock pillar medium", // 0x031
        "Farmhouse 4x overgrown", // 0x032
        "Farmhouse 2x overgrown and silo", // 0x033
        "Silo 3", // 0x034
        "Farm storage 1", // 0x035
        "Cr resort building 1", // 0x036
        "Cr resort building 2", // 0x037
        "Cr resort building 3", // 0x038
        "Cr resort building 4", // 0x039
        "Cr resort building 5", // 0x03A
        "Cr resort building 6", // 0x03B
        "Cr lighthouse", // 0x03C
        "Cr wooden hut 1", // 0x03D
        "Cr wooden hut 2", // 0x03E
        "03f", // 0x03F
        "Wind sock yellow blue", // 0x040
        "Planter box", // 0x041
        "Hangglider ygw", // 0x042
        "Hangglider blue", // 0x043
        "Truck blue", // 0x044
        "Rockface small", // 0x045
        "Rockface large", // 0x046
        "Cr airport hanger", // 0x047
        "Cr seaside town centre", // 0x048
        "Cr seaside town building 1", // 0x049
        "Cr seaside town building 2", // 0x04A
        "Cr seaside town fountain", // 0x04B
        "Wooden church", // 0x04C
        "Cave pillars large", // 0x04D
        "Cave pillars", // 0x04E
        "Cave pillar single", // 0x04F
        "Cave light fitting", // 0x050
        "Little states low poly", // 0x051
        "Big cruise ship", // 0x052
        "Wind turbine", // 0x053
        "Pump jack", // 0x054
        "Stone wario head", // 0x055
        "Space shuttle", // 0x056
        "Big red square", // 0x057
        "Nessies head", // 0x058
        "Boat blue white n64 flag", // 0x059
        "Antebellum plantation house", // 0x05A
        "Boat paddle steamer", // 0x05B
        "Tree ls 1", // 0x05C
        "Tree ls 2", // 0x05D
        "Tree ls 3", // 0x05E
        "Tree ls 4", // 0x05F
        "Tree ls 5", // 0x060
        "Crain Communications Building", // 0x061
        "John Hancock Center", // 0x062
        "Sears Tower", // 0x063
        "Nyc empire state building", // 0x064
        "Nyc building un headquarters", // 0x065
        "Nyc statue of liberty", // 0x066
        "Chicago 4x office towers 1", // 0x067
        "Chicago 4x office towers 2", // 0x068
        "Chicago 4x office towers 3", // 0x069
        "Chicago 5x office towers 1", // 0x06A
        "Nyc 4x office towers 1", // 0x06B
        "Nyc 3x office towers 1", // 0x06C
        "Nyc 4x office towers 2", // 0x06D
        "Nyc 4x office towers 3", // 0x06E
        "Nyc single office tower 1", // 0x06F
        "Nyc world trade center", // 0x070
        "Silo 1", // 0x071
        "Silo 2", // 0x072
        "Barn long 1", // 0x073
        "Water tower", // 0x074
        "Fl wide building", // 0x075
        "Airport hanger double", // 0x076
        "Ksc large airplane hangar", // 0x077
        "Ksc control towers", // 0x078
        "Ksc guard tower", // 0x079
        "Barn long 2", // 0x07A
        "Barn medium", // 0x07B
        "Stl gateway arch", // 0x07C
        "Farmhouse 3x stone", // 0x07D
        "Farmhouse 4x stone and silo", // 0x07E
        "Red barn and silo", // 0x07F
        "Washington lincoln memorial", // 0x080
        "Washington whitehouse", // 0x081
        "Washington monument", // 0x082
        "Space shuttle launch tower", // 0x083
        "Gas station", // 0x084
        "Washington capitol building", // 0x085
        "Washington federal triangle", // 0x086
        "Washington rectangular building", // 0x087
        "Nuclear power plant", // 0x088
        "Bridge supports", // 0x089
        "La tower 1", // 0x08A
        "La 2x towers", // 0x08B
        "U.S. Bank Tower", // 0x08C
        "Plus-shaped skyscraper", // 0x08D
        "Triple flat building", // 0x08E
        "Long flat building", // 0x08F
        "Building pink 1", // 0x090
        "Building pink 2", // 0x091
        "Seattle space needle", // 0x092
        "Observatory", // 0x093
        "Truck delivery blue", // 0x094
        "Ls bridge support small", // 0x095
        "Ls bridge support tall", // 0x096
        "Row of 5x trees", // 0x097
        "Mobile phone tower", // 0x098
        "Stone mario head", // 0x099
        "Offshore oil rig", // 0x09A
        "Cactus", // 0x09B
        "Wind pump", // 0x09C
        "Cactus 2", // 0x09D
        "Oil derrick", // 0x09E
        "Wild West store", // 0x09F
        "Wild West gallows", // 0x0A0
        "Wild West hotel", // 0x0A1
        "Wild West broken house", // 0x0A2
        "Wild West ranch", // 0x0A3
        "Wild West mercantile", // 0x0A4
        "Ever frost island low poly", // 0x0A5
        "Oil spray", // 0x0A6
        "Ski lift chair", // 0x0A7
        "Iceberg 1", // 0x0A8
        "Iceberg 2", // 0x0A9
        "Iceberg fractured", // 0x0AA
        "Tree snow billboard", // 0x0AB
        "Snow-covered house", // 0x0AC
        "Stone church", // 0x0AD
        "Snow-covered harbour tower", // 0x0AE
        "Two snow-covered houses", // 0x0AF
        "Snow-covered house 2", // 0x0B0
        "Snow-covered house 3", // 0x0B1
        "Snow-covered house 4", // 0x0B2
        "Five snow-covered houses", // 0x0B3
        "Snow-covered garden complex", // 0x0B4
        "Airport atc building snowy", // 0x0B5
        "Airport hanger snowy", // 0x0B6
        "Snow-covered airport", // 0x0B7
        "Lighthouse, white and red", // 0x0B8
        "Igloo", // 0x0B9
        "Refinery chimney", // 0x0BA
        "Refinery chimney 2", // 0x0BB
        "Refinery chimney 3", // 0x0BC
        "Harbour crane", // 0x0BD
        "Refinery tank sphere", // 0x0BE
        "Refinery building with pipes", // 0x0BF
        "Pipeline 1", // 0x0C0
        "Ski lift start", // 0x0C1
        "Ski lift end", // 0x0C2
        "Ski lift pole", // 0x0C3
        "Igloo", // 0x0C4
        "Oil derrick 2", // 0x0C5
        "White flag n64logo", // 0x0C6
        "Map thermal cylinder", // 0x0C7
        "Map wind indicator arrow", // 0x0C8
        "Map target indicator arrow", // 0x0C9
        "Map ring target yellow cube", // 0x0CA
        "Map landing pad red cube", // 0x0CB
        "Map ring target blue cube", // 0x0CC
        "Pilotwings 3d logo", // 0x0CD
        "Map starting direction arrow", // 0x0CE
        "Cannon on tall red pedestal", // 0x0CF
        "Cannonb target 1", // 0x0D0
        "Red white goal", // 0x0D1
        "Green blue goal 0", // 0x0D2
        "Mecca hawk", // 0x0D3
        "Landing pad notarget", // 0x0D4
        "Wind wave 1", // 0x0D5
        "Wind wave 2", // 0x0D6
        "Grey whale", // 0x0D7
        "Brown box", // 0x0D8
        "Ring yellow 1", // 0x0D9
        "Ring red 1", // 0x0DA
        "Ring blue 1", // 0x0DB
        "Ring white 1", // 0x0DC
        "Ring yellow 2", // 0x0DD
        "Ring red 2", // 0x0DE
        "Ring blue 2", // 0x0DF
        "Ring white 2", // 0x0E0
        "Ring yellow 3", // 0x0E1
        "Ring red 3", // 0x0E2
        "Ring blue 3", // 0x0E3
        "Ring white 3", // 0x0E4
        "Ring yellow 4", // 0x0E5
        "Ring red 4", // 0x0E6
        "Ring blue 4", // 0x0E7
        "Ring white 4", // 0x0E8
        "Ring yellow 5", // 0x0E9
        "Ring red 5", // 0x0EA
        "Ring blue 5", // 0x0EB
        "Ring white 5", // 0x0EC
        "Ring locked yellow", // 0x0ED
        "Ring locked red", // 0x0EE
        "Ring locked blue", // 0x0EF
        "Ring locked grey", // 0x0F0
        "Ring with goal text", // 0x0F1
        "Bonus star", // 0x0F2
        "Green blue goal 1", // 0x0F3
        "Ball orange", // 0x0F4
        "Ball green", // 0x0F5
        "Ball blue", // 0x0F6
        "Missile target billboard", // 0x0F7
        "Missile target disc", // 0x0F8
        "Missile target ball", // 0x0F9
        "Rb hoverpad locked", // 0x0FA
        "Rb hoverpad standard", // 0x0FB
        "Rb hoverpad large", // 0x0FC
        "Giant transparent mecca hawk", // 0x0FD
        "Giant red thing", // 0x0FE
        "Big rock boulder", // 0x0FF
        "Iceberg giant", // 0x100
        "Hg thermal cylinder", // 0x101
        "Landing pad 1", // 0x102
        "Landing pad 2", // 0x103
        "Landing pad 3", // 0x104
        "Cannon on pillar", // 0x105
        "Cannonb target 2", // 0x106
        "Cannonb target 3", // 0x107
        "Cannonb target 4", // 0x108
        "Jh destination", // 0x109
        "Hg lark lowpoly", // 0x10A
        "Hg goose lowpoly", // 0x10B
        "Hg hawk lowpoly", // 0x10C
        "Hg kiwi lowpoly", // 0x10D
        "Hg ibis lowpoly", // 0x10E
        "Hg robin lowpoly", // 0x10F
        "Hg lark", // 0x110
        "Hg shadow", // 0x111
        "Hg shadow column", // 0x112
        "Hg lark crashed", // 0x113
        "Hg goose", // 0x114
        "Hg goose crashed", // 0x115
        "Hg hawk", // 0x116
        "Hg hawk crashed", // 0x117
        "Hg kiwi", // 0x118
        "Hg kiwi crashed", // 0x119
        "Hg ibis", // 0x11A
        "Hg ibis crashed", // 0x11B
        "Hg robin", // 0x11C
        "Hg robin crashed", // 0x11D
        "Rb lark", // 0x11E
        "Rb shadow", // 0x11F
        "Rb shadow column", // 0x120
        "Multi colored cube", // 0x121
        "Rb goose", // 0x122
        "Rb hawk", // 0x123
        "Rb kiwi", // 0x124
        "Rb ibis", // 0x125
        "Rb robin", // 0x126
        "Gyro lark", // 0x127
        "Gyro shadow", // 0x128
        "Gyro shadow column", // 0x129
        "Gyro lark crashed", // 0x12A
        "Gyro goose", // 0x12B
        "Gyro goose crashed", // 0x12C
        "Gyro hawk", // 0x12D
        "Gyro hawk crashed", // 0x12E
        "Gyro kiwi", // 0x12F
        "Gyro kiwi crashed", // 0x130
        "Gyro ibis", // 0x131
        "Gyro ibis crashed", // 0x132
        "Gyro robin", // 0x133
        "Gyro robin crashed", // 0x134
        "Cannonb lark launched", // 0x135
        "Cannonb goose launched", // 0x136
        "Cannonb hawk launched", // 0x137
        "Cannonb kiwi launched", // 0x138
        "Cannonb ibis launched", // 0x139
        "Cannonb robin launched", // 0x13A
        "Birdman lark", // 0x13B
        "Birdman shadow", // 0x13C
        "Birdman shadow column", // 0x13D
        "Birdman goose", // 0x13E
        "Birdman hawk", // 0x13F
        "Birdman kiwi", // 0x140
        "Birdman ibis", // 0x141
        "Birdman robin", // 0x142
        "Skydiving lark", // 0x143
        "Parachute lark", // 0x144
        "Skydiving kiwi", // 0x145
        "Skydiving ibis", // 0x146
        "Skydiving robin", // 0x147
        "Skydiving shadow", // 0x148
        "Skydiving goose", // 0x149
        "Parachute goose", // 0x14A
        "Skydiving hawk", // 0x14B
        "Parachute hawk", // 0x14C
        "Parachute kiwi", // 0x14D
        "Parachute ibis", // 0x14E
        "Parachute robin", // 0x14F
        "Jumbleh lark", // 0x150
        "Jumbleh shadow", // 0x151
        "Jumbleh goose", // 0x152
        "Jumbleh hawk", // 0x153
        "Jumbleh kiwi", // 0x154
        "Jumbleh ibis", // 0x155
        "Jumbleh robin", // 0x156
        "Pilotwings logo feather", // 0x157
        "Pilotwings logo six", // 0x158
        "Pilotwings logo four", // 0x159
        "Pilot lark", // 0x15A
        "Pilot goose", // 0x15B
        "Pilot hawk", // 0x15C
        "Pilot kiwi", // 0x15D
        "Pilot ibis", // 0x15E
        "Pilot robin", // 0x15F
        "Skybox intro low poly sky cloud", // 0x160
        "Skybox sunny", // 0x161
        "Skybox sunny 2", // 0x162
        "Skybox dark clouds", // 0x163
        "Skybox evening", // 0x164
        "Skybox starry night", // 0x165
        "Ocean cutout crescent island", // 0x166
        "Skybox dark gloomy clouds", // 0x167
        "Ocean cutout little states", // 0x168
        "Ocean cutout ever frost island", // 0x169
        "Skybox intro giant circular sky ocean", // 0x16A
    ];

    /// <summary>The name of a model, or an empty text if none is known.</summary>
    public static string Of(int model) => model >= 0 && model < Names.Length ? Names[model] : string.Empty;
}
