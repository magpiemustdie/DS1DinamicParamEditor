using System;

namespace DS1ParamEditor
{
    internal static class PtdAssembly
    {
        // ── ItemGet shellcode (x86) ────────────────────────────────────────
        // Template bytes, fill placeholders before Execute():
        //   offset 0x01: category (edi)
        //   offset 0x06: count (ecx)
        //   offset 0x0B: itemID (esi)
        //   offset 0x10: 0 (ebp)
        //   offset 0x22: funcAddr (eax)
        public static readonly byte[] ItemGet = new byte[]
        {
            0xBF, 0xFF, 0xFF, 0xFF, 0xFF,       // mov edi, category
            0xB9, 0xFF, 0xFF, 0xFF, 0xFF,       // mov ecx, count
            0xBE, 0xFF, 0xFF, 0xFF, 0xFF,       // mov esi, itemID
            0xBD, 0xFF, 0xFF, 0xFF, 0xFF,       // mov ebp, 0
            0xBB, 0xFF, 0xFF, 0xFF, 0xFF,       // mov ebx, 0xFFFFFFFF
            0x6A, 0x00,                          // push 0x0
            0x6A, 0x01,                          // push 0x1
            0x55,                                // push ebp
            0x56,                                // push esi
            0x51,                                // push ecx
            0x57,                                // push edi
            0xB8, 0xFF, 0xFF, 0xFF, 0xFF,       // mov eax, funcAddr
            0xFF, 0xD0,                          // call eax
            0xC3                                 // ret
        };

        // ── ItemDrop shellcode (x86) ───────────────────────────────────────
        //   offset 0x01: category (ebp)
        //   offset 0x06: itemID (ebx)
        //   offset 0x10: quantity (edx)
        //   offset 0x15: ptr lookup addr (eax)
        //   offset 0x32: ptr2 lookup addr (eax) 
        //   offset 0x38: funcAddr (call via push/ret)
        public static readonly byte[] ItemDrop = new byte[]
        {
            0xBD, 0xFF, 0xFF, 0xFF, 0xFF,       // mov ebp, category
            0xBB, 0xFF, 0xFF, 0xFF, 0xFF,       // mov ebx, itemID
            0xB9, 0xFF, 0xFF, 0xFF, 0xFF,       // mov ecx, 0xFFFFFFFF
            0xBA, 0xFF, 0xFF, 0xFF, 0xFF,       // mov edx, quantity
            0xA1, 0xFF, 0xFF, 0xFF, 0xFF,       // mov eax, [ptr1]
            0x89, 0xA8, 0x28, 0x08, 0x00, 0x00, // mov [eax+0x828], ebp
            0x89, 0x98, 0x2C, 0x08, 0x00, 0x00, // mov [eax+0x82C], ebx
            0x89, 0x88, 0x30, 0x08, 0x00, 0x00, // mov [eax+0x830], ecx
            0x89, 0x90, 0x34, 0x08, 0x00, 0x00, // mov [eax+0x834], edx
            0xA1, 0xFF, 0xFF, 0xFF, 0xFF,       // mov eax, [ptr2]
            0x50,                                // push eax
            0xB8, 0xFF, 0xFF, 0xFF, 0xFF,       // mov eax, funcAddr
            0xFF, 0xD0,                          // call eax
            0xC3                                 // ret
        };

        // ── BonfireWarp shellcode (x86) ────────────────────────────────────
        //   offset 0x01: addr of pointer to load (eax comes from [imm32])
        //   offset 0x0E: funcAddr (eax for call eax)
        public static readonly byte[] BonfireWarp = new byte[]
        {
            0xA1, 0xFF, 0xFF, 0xFF, 0xFF,       // mov eax, [ptrAddr]
            0x8B, 0xF0,                          // mov esi, eax
            0xBF, 0x01, 0x00, 0x00, 0x00,       // mov edi, 1
            0x57,                                // push edi
            0xB8, 0xFF, 0xFF, 0xFF, 0xFF,       // mov eax, funcAddr
            0xFF, 0xD0,                          // call eax
            0xC3                                 // ret
        };

        // ── LevelUp shellcode (x86) ────────────────────────────────────────
        //   offset 0x01: ptr to stats struct (eax)
        //   offset 0x06: ptr to stats struct (ecx)
        //   offset 0x0B: funcAddr (eax for call eax)
        public static readonly byte[] LevelUp = new byte[]
        {
            0xB8, 0xFF, 0xFF, 0xFF, 0xFF,       // mov eax, statsPtr
            0xB9, 0xFF, 0xFF, 0xFF, 0xFF,       // mov ecx, statsPtr
            0xB8, 0xFF, 0xFF, 0xFF, 0xFF,       // mov eax, funcAddr
            0xFF, 0xD0,                          // call eax
            0xC3                                 // ret
        };
    }
}
