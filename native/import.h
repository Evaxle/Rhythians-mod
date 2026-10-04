typedef struct { unsigned capacity,count; char **paths; } FilePathList;
static char *daily_paths[]={daily_import};

__declspec(dllexport) _Bool IsFileDropped(void) {
    InitOnceExecuteOnce(&initialized,setup,NULL,NULL);
    static _Bool (*original)(void);
    if(!original) original=(void *)GetProcAddress(GetModuleHandleA("RhythiansRaylib.dll"),"IsFileDropped");
    return original() || (menu && !panel && daily_import_state==1);
}

__declspec(dllexport) FilePathList LoadDroppedFiles(void) {
    InitOnceExecuteOnce(&initialized,setup,NULL,NULL);
    static FilePathList (*original)(void);
    if(!original) original=(void *)GetProcAddress(GetModuleHandleA("RhythiansRaylib.dll"),"LoadDroppedFiles");
    FilePathList files=original();
    if(files.count || !menu || panel || daily_import_state!=1) return files;
    static void (*unload)(FilePathList);
    if(!unload) unload=(void *)GetProcAddress(GetModuleHandleA("RhythiansRaylib.dll"),"UnloadDroppedFiles");
    unload(files);
    return (FilePathList){1,1,daily_paths};
}

__declspec(dllexport) void UnloadDroppedFiles(FilePathList files) {
    if(files.paths==daily_paths) { InterlockedExchange(&daily_import_state,2); return; }
    InitOnceExecuteOnce(&initialized,setup,NULL,NULL);
    static void (*original)(FilePathList);
    if(!original) original=(void *)GetProcAddress(GetModuleHandleA("RhythiansRaylib.dll"),"UnloadDroppedFiles");
    original(files);
}
