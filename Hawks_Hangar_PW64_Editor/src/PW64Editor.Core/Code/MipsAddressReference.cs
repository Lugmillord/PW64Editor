namespace PW64Editor.Core.Code;

/// <summary>
/// The location of a 32-bit constant in the game code that is built from two MIPS instructions.
/// </summary>
/// <remarks>
/// <para>
/// MIPS instructions are 32 bits wide, so a 32-bit constant cannot fit into one of them.
/// The compiler splits it: <c>lui</c> ("load upper immediate") sets the upper 16 bits of a
/// register, and a following <c>addiu</c> ("add immediate unsigned") adds the lower 16 bits:
/// </para>
/// <code>
/// lui   $a1, 0x0062        ; $a1 = 0x00620000
/// addiu $a1, $a1, -0x7490  ; $a1 = 0x00620000 - 0x7490 = 0x00618B70
/// </code>
/// <para>
/// Note the trap: the 16-bit value of addiu is signed. If the lower half is 0x8000 or more,
/// it counts as negative, so the upper half must be one higher to compensate.
/// See <see cref="MipsAddressPatcher"/>.
/// </para>
/// </remarks>
/// <param name="HiOffset">ROM offset of the <c>lui</c> instruction.</param>
/// <param name="LoOffset">ROM offset of the matching <c>addiu</c> instruction.</param>
/// <param name="Description">What the constant means, for error messages and documentation.</param>
public sealed record MipsAddressReference(int HiOffset, int LoOffset, string Description);
