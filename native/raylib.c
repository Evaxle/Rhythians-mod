#include <windows.h>
#include <shlobj.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#undef DrawTextEx
#include "raylib_abi.h"

#define MAP_LIMIT 32768
#define TABLE_SIZE 65536

typedef struct {
    float speed;
    int rewards[3];
    float rating;
} SpeedProfile;

typedef struct {
    long long id;
    char title[512];
    char mapper[256];
    float native_rating;
    int ranked;
    float rating, rankability;
    int rewards[3];
    int best[3], passed, speed_count;
    char challenge[256];
    char identity[64];
    char check[64];
    float curve[77];
    SpeedProfile speeds[40];
} Map;

static INIT_ONCE initialized = INIT_ONCE_STATIC_INIT;
typedef struct { char name[128],summary[256],score[256],map[64]; } Player;
typedef struct {
    Map *s_maps;
    int s_map_count;
    int s_table[TABLE_SIZE];
    char s_status[160];
    char s_username[128];
    char s_phase[32];
    char s_hint[256];
    int s_online;
    int s_points[5];
    int s_current_mode;
    int s_signed_in;
    int s_global_rank;
    int s_challenge_level;
    char s_rhp_rank[48];
    char s_update_status[32];
    char s_update_message[256];
    char s_update_version[64];
    char s_profile_rows[3][100][512];
    int s_profile_row_count[3];
    Player s_players[50];
    int s_player_count;
    char s_avatar_key[65];
    char s_rank_names[5][8];
    char s_daily_title[256];
    char s_daily_status[160];
    char s_daily_action[40];
} State;
static State initial_state={.s_maps=NULL,.s_map_count=0,.s_status="F8 to connect Rhythians",.s_username="Rhythians",.s_phase="offline",.s_hint="Log in to see your profile and maps",.s_online=0,.s_points={-1,-1,-1,-1,-1},.s_current_mode=0,.s_signed_in=0,.s_global_rank=0,.s_challenge_level=0,.s_player_count=0};
static State *snapshot=&initial_state;
static void *volatile pending_state, *volatile retired_state;
static unsigned state_generation;
static char daily_import[MAX_PATH];
static volatile LONG daily_import_state;
#define maps (snapshot->s_maps)
#define map_count (snapshot->s_map_count)
#define table (snapshot->s_table)
#define status (snapshot->s_status)
#define username (snapshot->s_username)
#define phase (snapshot->s_phase)
#define hint (snapshot->s_hint)
#define online (snapshot->s_online)
#define points (snapshot->s_points)
#define current_mode (snapshot->s_current_mode)
#define signed_in (snapshot->s_signed_in)
#define global_rank (snapshot->s_global_rank)
#define challenge_level (snapshot->s_challenge_level)
#define rhp_rank (snapshot->s_rhp_rank)
#define update_status (snapshot->s_update_status)
#define update_message (snapshot->s_update_message)
#define update_version (snapshot->s_update_version)
#define profile_rows (snapshot->s_profile_rows)
#define profile_row_count (snapshot->s_profile_row_count)
#define players (snapshot->s_players)
#define player_count (snapshot->s_player_count)
#define avatar_key (snapshot->s_avatar_key)
#define rank_names (snapshot->s_rank_names)
#define daily_title (snapshot->s_daily_title)
#define daily_status (snapshot->s_daily_status)
#define daily_action (snapshot->s_daily_action)
static char folder[MAX_PATH],game[MAX_PATH],dismissed_update[64];
static int panel,profile_tab,profile_page,accepted;
static Font panel_font;
static void draw_panel(void);
static void write_command(const char *name,const char *value);
static volatile ULONGLONG updated;
static int menu, menu_seen;
static char selected[512], measured[16][512];
static int selected_index=-1;
static int measure_cursor;
static Texture logo;
static Texture avatar,rank_icons[5];

static void (*unload_texture)(Texture);
static void (*draw_text)(Font, const char *, Vector, float, float, Tint);
static Vector (*measure_text)(Font, const char *, float, float);
static void (*draw_box)(Box, float, int, Tint);
static void (*draw_texture)(Texture, Box, Box, Vector, float, Tint);
static void (*draw_lines)(Box, float, int, float, Tint);
static void (*draw_circle)(Vector, float, Tint);
static Texture (*load_texture)(const char *);
static _Bool (*key_pressed)(int);
static void (*end_drawing)(void);
static const Tint white = {255, 255, 255, 216}, muted = {204, 204, 204, 255};
static void setup_ui(HMODULE library);

static unsigned hash(const char *s) {
    unsigned h = 2166136261u;
    while (*s) h = (h ^ (unsigned char)*s++) * 16777619u;
    return h;
}

static int lookup(const char *title) {
    unsigned slot = hash(title) & (TABLE_SIZE - 1);
    while (table[slot]) {
        int index = abs(table[slot]) - 1;
        if (!strcmp(maps[index].title, title)) return table[slot] > 0 ? index : -1;
        slot = (slot + 1) & (TABLE_SIZE - 1);
    }
    return -1;
}

static void index_maps(State *snapshot) {
    memset(table, 0, sizeof(snapshot->s_table));
    for (int i = 0; i < map_count; i++) {
        unsigned slot = hash(maps[i].title) & (TABLE_SIZE - 1);
        while (table[slot] && strcmp(maps[abs(table[slot]) - 1].title, maps[i].title)) slot = (slot + 1) & (TABLE_SIZE - 1);
        if (!table[slot]) table[slot] = i + 1;
        else if (maps[abs(table[slot]) - 1].id != maps[i].id && (!maps[i].identity[0] || strcmp(maps[abs(table[slot]) - 1].identity,maps[i].identity))) table[slot] = -abs(table[slot]);
    }
}

static void release_state(State *value) {
    if(value && value!=&initial_state) { free(value->s_maps); free(value); }
}

static State *parse_state(char *data) {
    int capacity=0;
    for(char *p=data;*p;p++) if((p==data || p[-1]=='\n') && p[0]=='M' && p[1]=='\t') capacity++;
    if(capacity>MAP_LIMIT) capacity=MAP_LIMIT;
    State *snapshot=calloc(1,sizeof(State));
    if(!snapshot) return NULL;
    maps=calloc(capacity ? capacity : 1,sizeof(Map));
    if(!maps) { free(snapshot); return NULL; }
                    map_count = 0;
                    player_count=0;
                    memset(profile_row_count,0,sizeof(profile_row_count));
                    char *line_context, *line = strtok_s(data, "\r\n", &line_context);
                    while (line) {
                        char *field[9], *field_context;
                        int count = 0;
                        for (char *part = strtok_s(line, "\t", &field_context); part && count < 9; part = strtok_s(NULL, "\t", &field_context)) field[count++] = part;
                        if (count==5 && !strcmp(field[0],"D")) { global_rank=atoi(field[1]); challenge_level=atoi(field[2]); snprintf(rhp_rank,sizeof(rhp_rank),"%s",field[3]); signed_in=atoi(field[4]); }
                        else if (count==4 && !strcmp(field[0],"W")) { snprintf(update_status,32,"%s",field[1]); snprintf(update_message,256,"%s",field[2]); snprintf(update_version,64,"%s",field[3]); }
                        else if (count==4 && !strcmp(field[0],"Y")) { snprintf(daily_title,256,"%s",field[1]); snprintf(daily_status,160,"%s",field[2]); snprintf(daily_action,40,"%s",field[3]); }
                        else if (count==3 && !strcmp(field[0],"Z")) { int tab=atoi(field[1]); if(tab>=0 && tab<3 && profile_row_count[tab]<100) snprintf(profile_rows[tab][profile_row_count[tab]++],512,"%s",field[2]); }
                        else if (count==5 && !strcmp(field[0],"U") && player_count<50) { Player *p=&players[player_count++]; snprintf(p->name,128,"%s",field[1]); snprintf(p->summary,256,"%s",field[2]); snprintf(p->score,256,"%s",field[3]); snprintf(p->map,64,"%s",field[4]); }
                        else if (count == 3 && !strcmp(field[0], "A")) {
                            if(strlen(field[1])==64 && strspn(field[1],"0123456789abcdef")==64) snprintf(avatar_key,sizeof(avatar_key),"%s",field[1]);
                            else avatar_key[0]=0;
                            char *context,*name=strtok_s(field[2],",",&context);
                            for(int i=0;i<5;i++) {
                                rank_names[i][0]=0;
                                if(name && strlen(name)<8 && strspn(name,"ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")==strlen(name)) snprintf(rank_names[i],8,"%s",name);
                                if(name) name=strtok_s(NULL,",",&context);
                            }
                        } else if (count == 3 && !strcmp(field[0], "G")) {
                            current_mode=atoi(field[1]);
                        } else if (count == 3 && !strcmp(field[0], "S")) {
                            snprintf(phase,sizeof(phase),"%s",field[1]);
                            snprintf(hint,sizeof(hint),"%s",field[2]);
                        } else if (count == 9 && !strcmp(field[0], "P")) {
                            online = atoi(field[1]);
                            snprintf(status, sizeof(status), "%s", field[2]);
                            snprintf(username, sizeof(username), "%s", field[3]);
                            for (int i = 0; i < 5; i++) points[i] = atoi(field[i + 4]);
                        } else if (count == 9 && !strcmp(field[0], "M") && map_count < MAP_LIMIT) {
                            Map *map = &maps[map_count++];
                            memset(map,0,sizeof(*map));
                            for(int j=0;j<77;j++) map->curve[j]=-1;
                            map->id = _strtoi64(field[1], NULL, 10);
                            snprintf(map->title, sizeof(map->title), "%s", field[2]);
                            map->ranked = atoi(field[3]);
                            map->rating = strtof(field[4], NULL);
                            map->rankability = strtof(field[5], NULL);
                            for (int i = 0; i < 3; i++) map->rewards[i] = atoi(field[i + 6]);
                        } else if (map_count>0 && count>=3 && maps[map_count-1].id==_strtoi64(field[1],NULL,10)) {
                            Map *map=&maps[map_count-1];
                            if(!strcmp(field[0],"T") && count==4) { snprintf(map->mapper,sizeof(map->mapper),"%s",field[2]); map->native_rating=strtof(field[3],NULL); }
                            if (!strcmp(field[0],"B") && count==6) {
                                map->passed=atoi(field[2]);
                                for(int i=0;i<3;i++) map->best[i]=atoi(field[i+3]);
                            } else if (!strcmp(field[0],"C") && count==3 && strcmp(field[2],"--")) snprintf(map->challenge,sizeof(map->challenge),"%s",field[2]);
                            else if (!strcmp(field[0],"I") && count==3) snprintf(map->identity,sizeof(map->identity),"%s",field[2]);
                            else if (!strcmp(field[0],"K") && count==3) snprintf(map->check,sizeof(map->check),"%s",field[2]);
                            else if (!strcmp(field[0],"V") && count==4) { int index=(int)roundf(strtof(field[2],NULL)*20)-4; if(index>=0 && index<77) map->curve[index]=strtof(field[3],NULL); }
                            else if (!strcmp(field[0],"R") && count==7 && map->speed_count<40) {
                                SpeedProfile *speed=&map->speeds[map->speed_count++];
                                speed->speed=strtof(field[2],NULL);
                                for(int i=0;i<3;i++) speed->rewards[i]=atoi(field[i+3]);
                                speed->rating=strtof(field[6],NULL);
                            }
                        }
                        line = strtok_s(NULL, "\r\n", &line_context);
                    }
                    index_maps(snapshot);

    return snapshot;
}

static DWORD WINAPI read_state(void *unused) {
    char path[MAX_PATH];
    snprintf(path,sizeof(path),"%s\\state.tsv",folder);
    FILETIME last={0};
    unsigned last_hash=0;
    for(;;) {
        release_state(InterlockedExchangePointer(&retired_state,NULL));
        char import_path[MAX_PATH]; snprintf(import_path,sizeof(import_path),"%s\\daily-import.txt",folder);
        if(daily_import_state==2) { DeleteFileA(import_path); InterlockedExchange(&daily_import_state,0); }
        if(!daily_import_state) {
            FILE *request=fopen(import_path,"r");
            if(request) {
                char name[80]={0}; fgets(name,sizeof(name),request); fclose(request);
                if(strlen(name)==43 && !strncmp(name,"daily-",6) && strspn(name+6,"0123456789abcdef")==32 && !strcmp(name+38,".sspm")) {
                    snprintf(daily_import,sizeof(daily_import),"%s\\%s",folder,name);
                    if(GetFileAttributesA(daily_import)!=INVALID_FILE_ATTRIBUTES) InterlockedExchange(&daily_import_state,1);
                }
            }
        }
        HANDLE file=CreateFileA(path,GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,NULL,OPEN_EXISTING,0,NULL);
        if(file!=INVALID_HANDLE_VALUE) {
            FILETIME written,now;
            GetFileTime(file,NULL,NULL,&written);
            GetSystemTimeAsFileTime(&now);
            ULARGE_INTEGER a={.LowPart=now.dwLowDateTime,.HighPart=now.dwHighDateTime};
            ULARGE_INTEGER b={.LowPart=written.dwLowDateTime,.HighPart=written.dwHighDateTime};
            if(a.QuadPart>=b.QuadPart && a.QuadPart-b.QuadPart<150000000) updated=GetTickCount64();
            if(CompareFileTime(&last,&written)) {
                DWORD size=GetFileSize(file,NULL),read=0;
                if(size>0 && size<16*1024*1024) {
                    char *data=malloc((size_t)size+1);
                    if(data && ReadFile(file,data,size,&read,NULL) && read==size) {
                        data[size]=0;
                        unsigned content_hash=hash(data);
                        if(content_hash!=last_hash) {
                            State *next=parse_state(data);
                            if(next) { release_state(InterlockedExchangePointer(&pending_state,next)); last_hash=content_hash; }
                        }
                        last=written;
                    }
                    free(data);
                }
            }
            CloseHandle(file);
        }
        Sleep(500);
    }
    return 0;
}

static BOOL CALLBACK setup(PINIT_ONCE once, void *parameter, void **context) {
    GetModuleFileNameA(NULL, game, sizeof(game));
    char *slash = strrchr(game, '\\');
    if (!slash) return FALSE;
    *slash = 0;
    char path[MAX_PATH];
    snprintf(path, sizeof(path), "%s\\RhythiansRaylib.dll", game);
    HMODULE library = LoadLibraryA(path);
    if (!library) return FALSE;
    draw_text = (void *)GetProcAddress(library, "DrawTextEx");
    measure_text = (void *)GetProcAddress(library, "MeasureTextEx");
    draw_box = (void *)GetProcAddress(library, "DrawRectangleRounded");
    draw_texture = (void *)GetProcAddress(library, "DrawTexturePro");
    draw_lines = (void *)GetProcAddress(library, "DrawRectangleRoundedLinesEx");
    draw_circle = (void *)GetProcAddress(library, "DrawCircleV");
    load_texture = (void *)GetProcAddress(library, "LoadTexture");
    unload_texture = (void *)GetProcAddress(library, "UnloadTexture");
    key_pressed = (void *)GetProcAddress(library, "IsKeyPressed");
    end_drawing = (void *)GetProcAddress(library, "EndDrawing");
    setup_ui(library);
    SHGetFolderPathA(NULL, CSIDL_LOCAL_APPDATA, NULL, 0, folder);
    strcat(folder, "\\RhythiansMod");
    CreateDirectoryA(folder, NULL);
    snprintf(path,sizeof(path),"%s\\terms-accepted",folder);
    accepted=GetFileAttributesA(path)!=INVALID_FILE_ATTRIBUTES;
    if(!accepted) panel=1;
    HANDLE thread = CreateThread(NULL, 0, read_state, NULL, 0, NULL);
    if (thread) CloseHandle(thread);
    snprintf(path, sizeof(path), "%s\\Rhythians\\Rhythians.Bridge.exe", game);
    STARTUPINFOA startup = {.cb = sizeof(startup)};
    PROCESS_INFORMATION process;
    if (CreateProcessA(path, NULL, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, game, &startup, &process)) {
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
    }
    return TRUE;
}

static void label(Font font, const char *text, float x, float y, float size, Tint color) {
    draw_text(font, text, (Vector){x, y}, size, 0, color);
}

static void icon(float x, float y, float size) {
    static int attempted;
    if (!attempted) {
        char path[MAX_PATH];
        snprintf(path, sizeof(path), "%s\\Rhythians\\assets\\logo.png", game);
        logo = load_texture(path);
        attempted = 1;
    }
    if (logo.id) draw_texture(logo, (Box){0, 0, (float)logo.width, (float)logo.height}, (Box){x, y, size, size}, (Vector){0, 0}, 0, white);
}

static void profile_texture(int slot,float x,float y,float size) {
    static char loaded_avatar[65],loaded_ranks[5][8];
    Texture *texture=slot<0 ? &avatar : &rank_icons[slot];
    char *loaded=slot<0 ? loaded_avatar : loaded_ranks[slot];
    const char *name=slot<0 ? avatar_key : rank_names[slot];
    if(strcmp(loaded,name)) {
        if(texture->id) unload_texture(*texture);
        *texture=(Texture){0};
        char path[MAX_PATH];
        if(slot<0) snprintf(path,sizeof(path),"%s\\avatar-%s.png",folder,name);
        else snprintf(path,sizeof(path),"%s\\Rhythians\\assets\\ranks\\%s.png",game,name);
        if(name[0]) *texture=load_texture(path);
        strcpy(loaded,name);
    }
    if(texture->id) draw_texture(*texture,(Box){0,0,(float)texture->width,(float)texture->height},(Box){x,y,size,size},(Vector){0},0,white);
    else if(slot<0) icon(x,y,size);
}

#include "menu_ui.h"
#include "panels.h"
#include "import.h"

#ifdef RHYTHIANS_DIAGNOSTICS
static char trace[131072];
static int trace_length;
static void shape(const char *kind, Box box, Tint tint) {
    if (trace_length > (int)sizeof(trace) - 150) return;
    trace_length += snprintf(trace + trace_length, sizeof(trace) - trace_length, "%s %.2f %.2f %.2f %.2f %u %u %u %u\n", kind, box.x, box.y, box.width, box.height, tint.r, tint.g, tint.b, tint.a);
}
#endif

__declspec(dllexport) void DrawRectangleRounded(Box box, float roundness, int segments, Tint tint) {
    InitOnceExecuteOnce(&initialized, setup, NULL, NULL);
    surface(box, 1);
#ifdef RHYTHIANS_DIAGNOSTICS
    shape("box", box, tint);
#endif
    draw_box(box, roundness, segments, tint);
}

__declspec(dllexport) void DrawRectangleRoundedLinesEx(Box box, float roundness, int segments, float thickness, Tint tint) {
    InitOnceExecuteOnce(&initialized, setup, NULL, NULL);
    surface(box, 2);
#ifdef RHYTHIANS_DIAGNOSTICS
    shape("line", box, tint);
#endif
    draw_lines(box, roundness, segments, thickness, tint);
}

__declspec(dllexport) void DrawCircleV(Vector center, float radius, Tint tint) {
    InitOnceExecuteOnce(&initialized, setup, NULL, NULL);
#ifdef RHYTHIANS_DIAGNOSTICS
    shape("circle", (Box){center.x, center.y, radius, radius}, tint);
#endif
    draw_circle(center, radius, tint);
}

__declspec(dllexport) void DrawTexturePro(Texture texture, Box source, Box destination, Vector origin, float rotation, Tint tint) {
    InitOnceExecuteOnce(&initialized, setup, NULL, NULL);
    surface(destination, 3);
#ifdef RHYTHIANS_DIAGNOSTICS
    shape("texture", destination, tint);
#endif
    draw_texture(texture, source, destination, origin, rotation, tint);
    if (menu && profile_pending && destination.x>profile_bounds.x+profile_bounds.width && destination.y>=profile_bounds.y && destination.y+destination.height<=profile_bounds.y+profile_bounds.height) {
        profile_labels(destination.x);
        profile_pending=0;
    }
    if (menu && tint.r==126 && tint.g==201 && tint.b==138 && destination.width==destination.height) {
        no_fail=tint.a==255;
        no_fail_seen=1;
    }
}

__declspec(dllexport) void EndDrawing(void) {
    InitOnceExecuteOnce(&initialized, setup, NULL, NULL);
    if (menu_seen && accepted && (key_pressed(297) || (login_visible && mouse_pressed(0) && contains(login_bounds,mouse_position())))) { panel=signed_in && strcmp(phase,"authorizing") ? 3 : 2; profile_page=0; if(signed_in) write_command("command.txt","profile"); }
    for(int i=0;menu_seen && !panel && i<check_count;i++) if(mouse_pressed(0) && contains(check_buttons[i].box,mouse_position())) { if(!signed_in) { panel=2; break; } char value[64]; snprintf(value,sizeof(value),"check %lld",check_buttons[i].id); write_command("command.txt",value); }
    check_count=0;
    if(menu_seen && !panel && daily_visible && mouse_pressed(0) && contains(daily_bounds,mouse_position())) write_command("command.txt",!strcmp(daily_action,"Download map") ? "daily-download" : "daily-check");
    daily_visible=0;
    static ULONGLONG sent_players;
    if(menu_seen && GetTickCount64()-sent_players>3000) { write_command("players.txt",visible_players); write_command("cards.txt",visible_cards); visible_players[0]=visible_cards[0]=0; sent_players=GetTickCount64(); }
    static float sent_speed=-1;
    if(menu_seen && playback_speed!=sent_speed) { char value[32]; snprintf(value,sizeof(value),"%.6f",playback_speed); write_command("speed.txt",value); sent_speed=playback_speed; }
    if(menu_seen && mouse_pressed(0)) for(int i=0;i<previous_speed_count;i++) if(contains(previous_speeds[i].bounds,mouse_position())) playback_speed=previous_speeds[i].value;
    if(menu_seen && previous_custom && (key_pressed(257) || (mouse_pressed(0) && contains(previous_confirm,mouse_position()))) && previous_custom_value>=.2f && previous_custom_value<=4) playback_speed=previous_custom_value;
    previous_custom=custom_popup;
    previous_custom_value=custom_value;
    previous_confirm=custom_confirm;
    custom_popup=0;
    custom_value=0;
    custom_confirm=(Box){0};
    memcpy(previous_speeds,speed_choices,speed_choice_count*sizeof(SpeedChoice));
    previous_speed_count=speed_choice_count;
    speed_choice_count=0;
    speed_popup=0;
    if (menu_seen && selected[0]) {
        static long long last_selected_id;
        long long selected_id=selected_index>=0 && selected_index<map_count ? maps[selected_index].id : 0;
        if(selected_id!=last_selected_id) { char value[32]; snprintf(value,sizeof(value),"%lld",selected_id); write_command("selection-id.txt",value); last_selected_id=selected_id; }
        static char last_selection[512];
        if (strcmp(last_selection,selected)) {
            char path[MAX_PATH];
            snprintf(path,sizeof(path),"%s\\selection.txt",folder);
            HANDLE file=CreateFileA(path,GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,NULL,CREATE_ALWAYS,0,NULL);
            if (file!=INVALID_HANDLE_VALUE) { DWORD written; WriteFile(file,selected,(DWORD)strlen(selected),&written,NULL); CloseHandle(file); snprintf(last_selection,sizeof(last_selection),"%s",selected); }
        }
    }
    if(menu_seen) {
        static int last_mode=-2;
        if(current_mode!=last_mode) {
            char path[MAX_PATH]; snprintf(path,sizeof(path),"%s\\mode.txt",folder);
            FILE *file=fopen(path,"w");
            if(file) { fprintf(file,"%d",current_mode); fclose(file); last_mode=current_mode; }
        }
    }
#ifdef RHYTHIANS_DIAGNOSTICS
    static ULONGLONG last;
    if (menu_seen && GetTickCount64() - last > 3000) {
        char path[MAX_PATH];
        snprintf(path, sizeof(path), "%s\\shapes.txt", folder);
        FILE *file = fopen(path, "w");
        if (file) { fwrite(trace, 1, trace_length, file); fclose(file); }
        last = GetTickCount64();
    }
    trace_length = 0;
#endif
    static int sent_menu=-1;
    if(sent_menu!=menu_seen) { write_command("menu.txt",menu_seen ? "1" : "0"); sent_menu=menu_seen; }
    menu = menu_seen;
    menu_seen = 0;
    surface_count=0;
    profile_pending=0;
    login_visible=0;
    selected[0]=0;
    selected_index=-1;
    pending_player_count=0;
    card_rating_count=0;
    unresolved_card=(Box){0};
    rank_pending=global_leaderboard=0;
    play_bounds=(Box){0};
    current_card=(Box){0};
    previous_text=(TextRun){0};
    selected_mapper=(TextRun){0};
    seek_time=(TextRun){0};
    detail_bottom=0;
    if (!no_fail_seen) no_fail=0;
    no_fail_seen=0;
    if(accepted && menu && !panel && !strcmp(update_status,"available") && strcmp(dismissed_update,update_version)) panel=4;
    draw_panel();
    State *next=retired_state ? NULL : InterlockedExchangePointer(&pending_state,NULL);
    if(next) {
        State *old=snapshot;
        snapshot=next;
        state_generation++;
        InterlockedExchangePointer(&retired_state,old);
    }
    end_drawing();
}
