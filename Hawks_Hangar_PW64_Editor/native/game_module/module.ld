/* The game module is loaded to the start of the editor's code area (behind the game's
   variables, see CodeArea.cs) and must fit in front of its end. */
OUTPUT_FORMAT("elf32-tradbigmips")
OUTPUT_ARCH(mips)
ENTRY(module_init)
SECTIONS
{
    . = 0x803805E0;
    .header : { KEEP(*(.header)) }
    .text : { *(.text .text.*) }
    .rodata : { *(.rodata .rodata.* .rodata1) }
    .data : { *(.data .data.* .sdata .sdata.*) }
    . = ALIGN(8);
    __image_end = .;
    .bss (NOLOAD) : { __bss_start = .; *(.bss .bss.* .sbss .sbss.* COMMON) ; . = ALIGN(8); __bss_end = .; }
    /DISCARD/ : { *(.MIPS.abiflags) *(.reginfo) *(.pdr) *(.comment) *(.gnu.attributes) *(.note*) *(.mdebug*) }
}
