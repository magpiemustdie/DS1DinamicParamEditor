I'm sharing my DS1PE (Dark Souls 1 Param Editor). 
To run the program, you'll need .NET 9.0 installed on your PC. 
Before using it, please first familiarize yourself with how parameters work in DSMapStudio, then how to work with Smithbox. This program works as a companion to these programs, not as a replacement. 
It modifies parameter banks, which are already defined in the model properties in the .msb file. Since the parameters are tied to collisions in the game, you need the PTDE version of the game to display them in the editors; otherwise, working with DS1R is inconvenient.

The "Read params files" button reads parameter files from disk. 
The "Hook" button creates a pattern from this file and searches for it in the program's memory. For GameParams the first 100 bytes are usually sufficient, but for DrawParams I recommend searching the entire file. The "Save parameter" button: I recommend using it only for DrawParams (GameParams will be broken!!!!!!!!!).

If the program doesn't find the address, the value has already been changed, so the pattern doesn't match. Try resetting the addresses or exiting the game menu; the game will load the parameter bank from a file (this doesn't work with GameParam, which are always loaded).

Example: Jump to Anor Londo at the beginning of the location. Some of the environment uses m15_LightBank ID 20, but the character uses ID 30 (collision influence): adjust the EnvSpc and EnvDif sliders and you should see the result. 
Example 2: try changing LockCamParam ID 0 in Firelink Shrine.

To fill the fields, use Ctrl+Click; this is better than using sliders. 
The program constantly reads values ​​from memory, so simply minimize the table if you want to pause this feature.
GL.
