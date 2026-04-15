// DS1ParamHelper.cpp - Minimal DLL for in-process BonfireWarp
#include <windows.h>
#include <cstdint>

extern "C" __declspec(dllexport) int32_t BonfireWarp(uint64_t chrClassBasePtr, uint64_t bonfireWarpFunc)
{
    if (!chrClassBasePtr || !bonfireWarpFunc)
        return 0;

    // Read ChrClassBase object
    uint64_t* ptrAddr = (uint64_t*)chrClassBasePtr;
    uint64_t baseObj = *ptrAddr;
    if (!baseObj)
        return 0;

    // Call BonfireWarp function
    // Function signature: void BonfireWarpFunc(void* chrClassBase, int param)
    typedef void (*BonfireWarpFn)(void*, int);
    BonfireWarpFn warpFunc = (BonfireWarpFn)bonfireWarpFunc;
    
    __try
    {
        warpFunc((void*)baseObj, 1);
        return 1;
    }
    __except(EXCEPTION_EXECUTE_HANDLER)
    {
        return 0;
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    return TRUE;
}
