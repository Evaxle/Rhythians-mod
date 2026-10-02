typedef struct { Vector offset,target; float rotation,zoom; } Camera2;
static int modal_action;

static void write_command(const char *name,const char *value) {
    char path[MAX_PATH]; snprintf(path,sizeof(path),"%s\\%s",folder,name);
    HANDLE file=CreateFileA(path,GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,NULL,CREATE_ALWAYS,0,NULL);
    if(file!=INVALID_HANDLE_VALUE) { DWORD written; WriteFile(file,value,(DWORD)strlen(value),&written,NULL); CloseHandle(file); }
}

static int setting_exists(const char *name) {
    char path[MAX_PATH]; snprintf(path,sizeof(path),"%s\\%s",folder,name);
    return GetFileAttributesA(path)!=INVALID_FILE_ATTRIBUTES;
}

static void toggle_setting(const char *name) {
    char path[MAX_PATH]; snprintf(path,sizeof(path),"%s\\%s",folder,name);
    if(setting_exists(name)) DeleteFileA(path); else write_command(name,"1");
}

static void modal_button(const char *text,Box box,float size,int action) {
    int hover=contains(box,mouse_position());
    int active=panel==3 && action>=10 && action<=13 && profile_tab==action-10;
    draw_box(box,.14f,5,(Tint){255,255,255,active ? 48 : hover ? 40 : 22});
    if(active) draw_box((Box){box.x+size*.6f,box.y+box.height-2,box.width-size*1.2f,2},0,1,white);
    float width=measure_text(panel_font,text,size,0).x;
    label(panel_font,text,box.x+(box.width-width)*.5f,box.y+(box.height-size)*.5f,size,white);
    if(hover && mouse_pressed(0)) modal_action=action;
}

static void draw_panel(void) {
    if(!panel || !panel_font.texture.id) return;
    HMODULE library=GetModuleHandleA("RhythiansRaylib.dll");
    void (*begin)(Camera2)=(void *)GetProcAddress(library,"BeginMode2D");
    void (*end)(void)=(void *)GetProcAddress(library,"EndMode2D");
    begin((Camera2){.zoom=1});
    float sw=(float)screen_width(),sh=(float)screen_height();
    float scale=fminf(sw/1100,sh/760),size=20*scale;
    Box box={(sw-850*scale)*.5f,(sh-600*scale)*.5f,850*scale,600*scale};
    float x=box.x+32*scale,y=box.y+30*scale,w=box.width-64*scale;
    draw_box((Box){0,0,sw,sh},0,1,(Tint){0,0,0,170});
    draw_box(box,.03f,8,(Tint){14,14,17,250});
    modal_action=0;
    EnterCriticalSection(&data_lock);
    if(panel==1) {
        label(panel_font,"Welcome to Rhythians",x,y,size*1.4f,white);
        y+=size*2.4f;
        label(panel_font,"Rhythians is NOT a cheat. It is a ranked system",x,y,size*1.02f,(Tint){126,201,138,255}); y+=size*1.45f;
        label(panel_font,"for your profile, map ratings, and completed runs.",x,y,size,white); y+=size*2;
        const char *lines[]={"It does not aim, play maps, or change your hits or game scores.","Rhythians is independent of Rhythia and Steam.","By accepting, you agree to submit only your own genuine runs.","Do not forge scores, impersonate players, or tamper with submissions.","After sign-in, eligible new scores and chart identifiers are sent to", "Rhythians. Your linked profile and public scores may appear in rankings.","You can pause submissions, sign out, or uninstall at any time.","This beta may contain bugs. Points may be corrected or invalidated.","Acceptance is saved on this Windows account and is not shown again."};
        for(int i=0;i<9;i++) { label(panel_font,lines[i],x,y,size*.83f,muted); y+=size*1.35f; }
        modal_button("Decline",(Box){x,box.y+box.height-76*scale,w*.32f,44*scale},size,1);
        modal_button("Accept and continue",(Box){x+w*.38f,box.y+box.height-76*scale,w*.62f,44*scale},size,2);
    } else if(panel==2) {
        label(panel_font,"Connect to Rhythians",x,y,size*1.4f,white);
        y+=size*3;
        int busy=!strcmp(phase,"authorizing");
        label(panel_font,busy ? "Waiting for approval in your browser" : "Sign in to see your profile and submit eligible scores.",x,y,size,white);
        y+=size*2;
        fitted(panel_font,busy ? "This attempt expires after two minutes. You can retry or cancel." : hint,(Vector){x,y},size*.9f,0,muted,w);
        modal_button(busy ? "Cancel login" : "Not now",(Box){x,box.y+box.height-90*scale,w*.4f,48*scale},size,3);
        modal_button(busy ? "Retry login" : "Sign in with website",(Box){x+w*.45f,box.y+box.height-90*scale,w*.55f,48*scale},size,4);
        if(signed_in && online && !busy) panel=3;
    } else if(panel==4) {
        label(panel_font,"Rhythians updates",x,y,size*1.4f,white);
        fitted(panel_font,update_message,(Vector){x,y+size*3},size*.9f,0,muted,w);
        modal_button("Cancel",(Box){x,box.y+box.height-90*scale,w*.4f,48*scale},size,40);
        if(!strcmp(update_status,"available")) modal_button("Update and restart",(Box){x+w*.45f,box.y+box.height-90*scale,w*.55f,48*scale},size,41);
        if(!strcmp(update_status,"error")) modal_button("Retry",(Box){x+w*.45f,box.y+box.height-90*scale,w*.55f,48*scale},size,31);
    } else if(panel==3) {
        profile_texture(-1,x,y,size*2.4f);
        label(panel_font,username,x+size*3,y,size*1.25f,white);
        char summary[200]; snprintf(summary,sizeof(summary),"%s  |  Global #%d  |  Challenge L%d",rhp_rank,global_rank,challenge_level);
        fitted(panel_font,summary,(Vector){x+size*3,y+size*1.6f},size*.85f,0,muted,w-size*3);
        y+=size*3.7f;
        const char *tabs[]={"Overview","Scores","Passes","Settings"};
        for(int i=0;i<4;i++) modal_button(tabs[i],(Box){x+i*w/4,y,w/4-8*scale,38*scale},size*.85f,10+i);
        y+=size*2.8f;
        if(profile_tab==0) {
            const char *names[]={"RHP","RBP","RPL","RPS","RPVR"};
            for(int i=0;i<5;i++) { char value[64]; snprintf(value,sizeof(value),"%s %d",names[i],points[i]); profile_texture(i,x+i*w/5,y,size*1.8f); label(panel_font,value,x+i*w/5,y+size*2,size*.85f,white); }
            y+=size*4;
            for(int i=0;i<profile_row_count[0] && i<6;i++) { label(panel_font,profile_rows[0][i],x,y,size*.9f,muted); y+=size*1.45f; }
        } else if(profile_tab<3) {
            int count=profile_row_count[profile_tab],start=profile_page*8;
            if(!count) label(panel_font,"No entries yet. Refresh to check again.",x,y,size*.9f,muted);
            for(int i=start;i<count && i<start+8;i++) { fitted(panel_font,profile_rows[profile_tab][i],(Vector){x,y},size*.8f,0,white,w); y+=size*1.55f; }
            if(profile_page>0) modal_button("Previous",(Box){x,box.y+box.height-135*scale,130*scale,34*scale},size*.8f,20);
            if(start+8<count) modal_button("Next",(Box){x+w-130*scale,box.y+box.height-135*scale,130*scale,34*scale},size*.8f,21);
            if(count) { char pages[32]; snprintf(pages,sizeof(pages),"%d / %d",profile_page+1,(count+7)/8); label(panel_font,pages,x+(w-measure_text(panel_font,pages,size*.8f,0).x)*.5f,box.y+box.height-126*scale,size*.8f,muted); }
        } else {
            modal_button(setting_exists("scores-paused") ? "Score submissions: paused" : "Score submissions: on",(Box){x,y,w,42*scale},size*.9f,30); y+=size*3;
            modal_button("Check for updates",(Box){x,y,w,42*scale},size*.9f,31); y+=size*3;
            modal_button("Sign out",(Box){x,y,w,42*scale},size*.9f,32);
        }
        modal_button("Refresh",(Box){x,box.y+box.height-70*scale,140*scale,40*scale},size*.85f,22);
        modal_button("Close",(Box){x+w-140*scale,box.y+box.height-70*scale,140*scale,40*scale},size*.85f,3);
    }
    LeaveCriticalSection(&data_lock);
    switch(modal_action) {
        case 1: panel=0; break;
        case 2: write_command("terms-accepted","1"); accepted=1; panel=signed_in ? 0 : 2; break;
        case 3: if(panel==2 && !strcmp(phase,"authorizing")) write_command("cancel-login","1"); panel=0; break;
        case 4: connect_account(); break;
        case 10: case 11: case 12: case 13: profile_tab=modal_action-10; profile_page=0; break;
        case 20: profile_page--; break;
        case 21: profile_page++; break;
        case 22: write_command("command.txt","profile"); break;
        case 30: toggle_setting("scores-paused"); break;
        case 31: write_command("command.txt","updates"); panel=4; break;
        case 40: snprintf(dismissed_update,64,"%s",update_version); panel=0; break;
        case 41: write_command("command.txt","install-update"); snprintf(update_status,32,"downloading"); snprintf(update_message,256,"Preparing the update..."); break;
        case 32: write_command("command.txt","logout"); panel=2; break;
    }
    void (*triangle)(Vector,Vector,Vector,Tint)=(void *)GetProcAddress(library,"DrawTriangle");
    Vector pointer=mouse_position();
    float cursor=fmaxf(1,scale);
    triangle(pointer,(Vector){pointer.x,pointer.y+22*cursor},(Vector){pointer.x+16*cursor,pointer.y+16*cursor},(Tint){0,0,0,255});
    triangle((Vector){pointer.x+2*cursor,pointer.y+4*cursor},(Vector){pointer.x+2*cursor,pointer.y+19*cursor},(Vector){pointer.x+13*cursor,pointer.y+14*cursor},(Tint){255,255,255,255});
    end();
}

__declspec(dllexport) _Bool IsMouseButtonPressed(int button) { InitOnceExecuteOnce(&initialized,setup,NULL,NULL); return panel ? 0 : mouse_pressed(button); }
__declspec(dllexport) _Bool IsMouseButtonDown(int button) { InitOnceExecuteOnce(&initialized,setup,NULL,NULL); _Bool (*fn)(int)=(void *)GetProcAddress(GetModuleHandleA("RhythiansRaylib.dll"),"IsMouseButtonDown"); return panel ? 0 : fn(button); }
__declspec(dllexport) _Bool IsMouseButtonReleased(int button) { InitOnceExecuteOnce(&initialized,setup,NULL,NULL); _Bool (*fn)(int)=(void *)GetProcAddress(GetModuleHandleA("RhythiansRaylib.dll"),"IsMouseButtonReleased"); return panel ? 0 : fn(button); }
__declspec(dllexport) _Bool IsKeyPressed(int key) { InitOnceExecuteOnce(&initialized,setup,NULL,NULL); return panel ? 0 : key_pressed(key); }
__declspec(dllexport) _Bool IsKeyDown(int key) { InitOnceExecuteOnce(&initialized,setup,NULL,NULL); _Bool (*fn)(int)=(void *)GetProcAddress(GetModuleHandleA("RhythiansRaylib.dll"),"IsKeyDown"); return panel ? 0 : fn(key); }

__declspec(dllexport) int GetCharPressed(void) { InitOnceExecuteOnce(&initialized,setup,NULL,NULL); int (*fn)(void)=(void *)GetProcAddress(GetModuleHandleA("RhythiansRaylib.dll"),"GetCharPressed"); int value=fn(); return panel ? 0 : value; }
__declspec(dllexport) float GetMouseWheelMove(void) { InitOnceExecuteOnce(&initialized,setup,NULL,NULL); float (*fn)(void)=(void *)GetProcAddress(GetModuleHandleA("RhythiansRaylib.dll"),"GetMouseWheelMove"); return panel ? 0 : fn(); }
__declspec(dllexport) Vector GetMouseWheelMoveV(void) { InitOnceExecuteOnce(&initialized,setup,NULL,NULL); Vector (*fn)(void)=(void *)GetProcAddress(GetModuleHandleA("RhythiansRaylib.dll"),"GetMouseWheelMoveV"); return panel ? (Vector){0} : fn(); }
__declspec(dllexport) _Bool IsKeyPressedRepeat(int key) { InitOnceExecuteOnce(&initialized,setup,NULL,NULL); _Bool (*fn)(int)=(void *)GetProcAddress(GetModuleHandleA("RhythiansRaylib.dll"),"IsKeyPressedRepeat"); return panel ? 0 : fn(key); }
__declspec(dllexport) _Bool IsKeyReleased(int key) { InitOnceExecuteOnce(&initialized,setup,NULL,NULL); _Bool (*fn)(int)=(void *)GetProcAddress(GetModuleHandleA("RhythiansRaylib.dll"),"IsKeyReleased"); return panel ? 0 : fn(key); }
