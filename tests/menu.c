#include "../native/raylib.c"
#include <assert.h>

static int draws, green;
static float largest_green, largest_plain;
static BOOL CALLBACK ready(PINIT_ONCE once,void *parameter,void **context) { return TRUE; }
static void text_stub(Font font,const char *text,Vector p,float size,float spacing,Tint color) {
    draws++;
    if(color.g==201) { green++; largest_green=fmaxf(largest_green,size); }
    else largest_plain=fmaxf(largest_plain,size);
}
static Vector measure_stub(Font font,const char *text,float size,float spacing) { return (Vector){strlen(text)*size*.5f,size}; }
static void box_stub(Box box,float roundness,int segments,Tint tint) {}
static void texture_stub(Texture texture,Box source,Box destination,Vector origin,float rotation,Tint tint) {}
static Texture load_stub(const char *path) { return (Texture){0}; }
static int width_stub(void) { return 1920; }
static int height_stub(void) { return 1080; }
static Matrix identity(void) { return (Matrix){.m0=1,.m5=1,.m10=1,.m15=1}; }
static Matrix projection(void) { return (Matrix){.m0=2.0f/1920,.m5=-2.0f/1080,.m10=1,.m12=-1,.m13=1,.m15=1}; }

int main(void) {
    InitOnceExecuteOnce(&initialized,ready,NULL,NULL);
    InitializeCriticalSection(&data_lock);
    draw_text=text_stub; measure_text=measure_stub; draw_box=box_stub;
    draw_texture=texture_stub; load_texture=load_stub;
    screen_width=width_stub; screen_height=height_stub;
    projection_matrix=projection; modelview_matrix=identity; transform_matrix=identity;
    menu=1; accepted=1; map_count=1;
    maps[0]=(Map){.id=1,.title="Map title",.mapper="Someone",.ranked=1,.rating=6,.rankability=4,.rewards={600,300,450},.best={200,0,100}};
    index_maps();
    Font font={0};
    surface((Box){1000,200,900,160},3);
    DrawTextEx(font,"Unrelated button",(Vector){1170,225},48,0,white);
    DrawTextEx(font,"Mapped by Someone",(Vector){1170,280},34,0,white);
    assert(draws==2);
    draws=0;
    DrawTextEx(font,"Map title",(Vector){1170,225},48,0,white);
    assert(draws==1);
    DrawTextEx(font,"Mapped by Someone",(Vector){1170,280},34,0,white);
    assert(draws>2);
    draws=0;
    DrawTextEx(font,"Mapped by Someone",(Vector){400,150},20,0,white);
    assert(draws==1);
    assert(reward(&maps[0],0)==400);
    no_fail=1;
    assert(reward(&maps[0],0)==0);
    no_fail=0; start_nonzero=1;
    assert(reward(&maps[0],1)==0);
    start_nonzero=0;
    surface((Box){300,800,250,70},1);
    DrawTextEx(font,"PRACTICE",(Vector){350,805},26,0,white);
    assert(menu_seen && start_nonzero);
    DrawTextEx(font,"PLAY",(Vector){380,805},26,0,white);
    assert(menu_seen && !start_nonzero);
    maps[0].speeds[0]=(SpeedProfile){.speed=1,.rewards={600,300,450}};
    maps[0].speeds[1]=(SpeedProfile){.speed=1.5f,.rewards={1000,500,800}};
    maps[0].speed_count=2; playback_speed=1.25f;
    assert(reward(&maps[0],0)==600);
    for(int mode=0;mode<3;mode++) {
        current_mode=mode; green=0; largest_green=largest_plain=0;
        play_rewards(font,&maps[0],(Box){100,100,250,70},13,white);
        assert(green==1 && largest_green>largest_plain);
    }
    map_count=2;
    strcpy(maps[0].identity,"same-map");
    maps[1]=maps[0]; maps[1].id=2;
    index_maps();
    assert(lookup("Map title")>=0);
    strcpy(maps[1].identity,"another-map");
    index_maps();
    assert(lookup("Map title")==-1);
    puts("Card boundaries, reward eligibility, speed interpolation, active-mode styling, and duplicate identity passed.");
    return 0;
}
