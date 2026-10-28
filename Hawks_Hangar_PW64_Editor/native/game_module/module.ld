/* The game module is loaded to the start of the editor's code area (behind the game's
   variables, see CodeArea.cs) and must fit in front of its end. Its second part (.high: photos.c,
   subjects.c) runs in the high data area; module_init copies it there from behind the first part. */
OUTPUT_FORMAT("elf32-tradbigmips")
OUTPUT_ARCH(mips)
ENTRY(module_init)
SECTIONS
{
    . = 0x803805E0;
    .header : { KEEP(*(.header)) }
    .text : { *(EXCLUDE_FILE(*photos.o *subjects.o) .text EXCLUDE_FILE(*photos.o *subjects.o) .text.*) }
    .rodata : { *(EXCLUDE_FILE(*photos.o *subjects.o) .rodata EXCLUDE_FILE(*photos.o *subjects.o) .rodata.*
                  EXCLUDE_FILE(*photos.o *subjects.o) .rodata1) }
    .data : { *(EXCLUDE_FILE(*photos.o *subjects.o) .data EXCLUDE_FILE(*photos.o *subjects.o) .data.*
                EXCLUDE_FILE(*photos.o *subjects.o) .sdata EXCLUDE_FILE(*photos.o *subjects.o) .sdata.*) }
    . = ALIGN(8);
    __image_end = .;
    .bss (NOLOAD) : { __bss_start = .; *(.bss .bss.* .sbss .sbss.* COMMON) ; . = ALIGN(8); __bss_end = .; }

    /* HighDataArea.ModuleCode */
    .high 0x807F4000 : AT(0x807F4000)
    {
        __high_start = .;
        *photos.o(.text .text.* .rodata .rodata.* .rodata1 .data .data.* .sdata .sdata.*)
        *subjects.o(.text .text.* .rodata .rodata.* .rodata1 .data .data.* .sdata .sdata.*)
        . = ALIGN(8);
        __high_end = .;
    }
    /DISCARD/ : { *(.MIPS.abiflags) *(.reginfo) *(.pdr) *(.comment) *(.gnu.attributes) *(.note*) *(.mdebug*) }
}
