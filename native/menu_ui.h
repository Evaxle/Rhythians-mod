typedef struct { Box box; int kind; } Surface;
typedef struct { Font font; Vector position; float size, spacing; Tint tint; char text[512]; } TextRun;
typedef struct { float m0, m4, m8, m12, m1, m5, m9, m13, m2, m6, m10, m14, m3, m7, m11, m15; } Matrix;
typedef struct { float x, y, z, w; } Point;

static Surface surfaces[2048];
static int surface_count;
static TextRun previous_text, profile_text;
static Box profile_bounds, play_bounds, login_bounds;
static int profile_pending, login_visible, no_fail, no_fail_seen;
static int (*screen_width)(void), (*screen_height)(void);
static _Bool (*mouse_pressed)(int);
static Vector (*mouse_position)(void);
static Matrix (*projection_matrix)(void), (*modelview_matrix)(void), (*transform_matrix)(void);
static void (*begin_scissor)(int, int, int, int), (*end_scissor)(void);
static Box clipping;
static int clipped;
static Box current_card;
static TextRun current_title;
static TextRun selected_mapper, seek_time;
static float playback_speed=1;
static int start_nonzero;
static float detail_bottom;
typedef struct { Box bounds; float value; } SpeedChoice;
static SpeedChoice speed_choices[12], previous_speeds[12];
static int speed_choice_count, previous_speed_count, speed_popup;
static int custom_popup,custom_percent,previous_custom;
static float custom_value,previous_custom_value;
static Box custom_confirm,previous_confirm;
static Vector screen_point(Vector p);
typedef struct { Box box; long long id; } CheckButton;
static CheckButton check_buttons[256];
static int check_count;
static char visible_players[8192];
static char visible_cards[8192];
static Box player_row;
static TextRun player_name;
static int player_line;
static int rank_pending, global_leaderboard;
static Box rank_row;
typedef struct { TextRun run; Box row; Player player; } PendingPlayer;
static PendingPlayer pending_players[50];
static int pending_player_count;

static Box screen_box(Box box) {
    Vector a=screen_point((Vector){box.x,box.y}), b=screen_point((Vector){box.x+box.width,box.y+box.height});
    return (Box){a.x,a.y,b.x-a.x,b.y-a.y};
}

static void setup_ui(HMODULE library) {
    screen_width=(void *)GetProcAddress(library,"GetScreenWidth");
    screen_height=(void *)GetProcAddress(library,"GetScreenHeight");
    mouse_position=(void *)GetProcAddress(library,"GetMousePosition");
    mouse_pressed=(void *)GetProcAddress(library,"IsMouseButtonPressed");
    projection_matrix=(void *)GetProcAddress(library,"rlGetMatrixProjection");
    modelview_matrix=(void *)GetProcAddress(library,"rlGetMatrixModelview");
    transform_matrix=(void *)GetProcAddress(library,"rlGetMatrixTransform");
    begin_scissor=(void *)GetProcAddress(library,"BeginScissorMode");
    end_scissor=(void *)GetProcAddress(library,"EndScissorMode");
}

static Point transform(Point p, Matrix m) {
    return (Point){m.m0*p.x+m.m4*p.y+m.m8*p.z+m.m12*p.w, m.m1*p.x+m.m5*p.y+m.m9*p.z+m.m13*p.w, m.m2*p.x+m.m6*p.y+m.m10*p.z+m.m14*p.w, m.m3*p.x+m.m7*p.y+m.m11*p.z+m.m15*p.w};
}

static Vector screen_point(Vector p) {
    Point v = transform(transform(transform((Point){p.x,p.y,0,1}, transform_matrix()), modelview_matrix()), projection_matrix());
    return (Vector){(v.x/v.w+1)*.5f*screen_width(), (1-v.y/v.w)*.5f*screen_height()};
}

static Vector local_point(Vector p) {
    Vector a = screen_point((Vector){0,0}), b = screen_point((Vector){1,0}), c = screen_point((Vector){0,1});
    b.x-=a.x; b.y-=a.y; c.x-=a.x; c.y-=a.y; p.x-=a.x; p.y-=a.y;
    float determinant=b.x*c.y-b.y*c.x;
    if (fabsf(determinant)<.000001f) return (Vector){0,0};
    return (Vector){(p.x*c.y-p.y*c.x)/determinant, (b.x*p.y-b.y*p.x)/determinant};
}

static int contains(Box box, Vector p) {
    return p.x>=box.x && p.y>=box.y && p.x<=box.x+box.width && p.y<=box.y+box.height;
}

static void surface(Box box, int kind) {
    if (surface_count<2048 && box.width>0 && box.height>0) surfaces[surface_count++]=(Surface){box,kind};
}

static Box container(Vector p, float size, int card) {
    Box result={0};
    float area=INFINITY;
    for (int i=surface_count-1;i>=0;i--) {
        Box b=surfaces[i].box;
        if (!contains(b,p)) continue;
        if (card) {
            float aspect=b.width/b.height, row=(p.y-b.y)/b.height;
            int grid=aspect>.9f && aspect<1.1f && size>b.width*.10f && size<b.width*.17f && row>.55f && row<.85f;
            int list=aspect>3 && size>b.height*.24f && size<b.height*.4f && row>.07f && row<.4f;
            if (!grid && !list) continue;
        } else if (surfaces[i].kind!=1 || b.height<size*1.4f || b.width<size*2) continue;
        if (b.width*b.height<area) { result=b; area=b.width*b.height; }
    }
    return result;
}

static void fitted(Font font, const char *text, Vector p, float size, float spacing, Tint tint, float width) {
    if (width<=0) return;
    char value[512];
    snprintf(value,sizeof(value),"%s",text);
    if (measure_text(font,value,size,spacing).x<=width) { draw_text(font,value,p,size,spacing,tint); return; }
    size_t length=strlen(value);
    while (length) {
        length--;
        while (length && ((unsigned char)value[length]&0xc0)==0x80) length--;
        value[length]=0;
        char trial[516];
        snprintf(trial,sizeof(trial),"%s\xe2\x80\xa6",value);
        if (measure_text(font,trial,size,spacing).x<=width) { draw_text(font,trial,p,size,spacing,tint); return; }
    }
}

static void thumbnail_text(Font font, const char *text, Vector p, float size, Tint tint, float width) {
    float measured=measure_text(font,text,size,0).x;
    if (measured>width && measured>0) size*=width/measured;
    Vector dimensions=measure_text(font,text,size,0);
    float pad=size*.22f;
    draw_box((Box){p.x-pad,p.y-pad,dimensions.x+pad*2,dimensions.y+pad*2},.18f,4,(Tint){0,0,0,125});
    draw_text(font,text,p,size,0,tint);
}

static int reward(const Map *map,int mode) {
    if (no_fail || start_nonzero || playback_speed<.5f || playback_speed>2) return 0;
    if (!map || map->ranked<0) return -1;
    float value=map->rewards[mode];
    const SpeedProfile *lower=NULL,*upper=NULL;
    for (int i=0;i<map->speed_count;i++) {
        const SpeedProfile *profile=&map->speeds[i];
        if (profile->speed<=playback_speed && (!lower || profile->speed>lower->speed)) lower=profile;
        if (profile->speed>=playback_speed && (!upper || profile->speed<upper->speed)) upper=profile;
    }
    if (!lower) lower=upper;
    if (!upper) upper=lower;
    if (lower && upper) {
        float amount=upper->speed==lower->speed ? 0 : (playback_speed-lower->speed)/(upper->speed-lower->speed);
        value=lower->rewards[mode]+amount*(upper->rewards[mode]-lower->rewards[mode]);
    }
    return (int)fmaxf(0,roundf(value)-map->best[mode]);
}

static float difficulty(const Map *map) {
    if(!map || map->rating<0) return -1;
    float step=playback_speed*20-4;
    int lower=(int)floorf(step),upper=(int)ceilf(step);
    if(lower>=0 && upper<77 && map->curve[lower]>=0 && map->curve[upper]>=0) return map->curve[lower]+(map->curve[upper]-map->curve[lower])*(step-lower);
    const SpeedProfile *a=NULL,*b=NULL;
    for(int i=0;i<map->speed_count;i++) {
        const SpeedProfile *s=&map->speeds[i];
        if(s->speed<=playback_speed+.00001f && (!a || s->speed>a->speed)) a=s;
        if(s->speed>=playback_speed-.00001f && (!b || s->speed<b->speed)) b=s;
    }
    if(a && b) return a->rating+(b->rating-a->rating)*(b->speed==a->speed ? 0 : (playback_speed-a->speed)/(b->speed-a->speed));
    return fabsf(playback_speed-1)<.0001f ? map->rating : -1;
}

static void rewards(const Map *map, char *text, size_t capacity) {
    if (no_fail || start_nonzero || (map && map->ranked>=0)) snprintf(text,capacity,"RPL %d  RPS %d  RPVR %d",reward(map,0),reward(map,1),reward(map,2));
    else snprintf(text,capacity,"RPL --  RPS --  RPVR --");
}

static void play_rewards(Font font,const Map *map,Box box,float size,Tint tint) {
    const char *names[]={"RPL","RPS","RPVR"};
    char text[3][48];
    float sizes[3],widths[3],total=0,gap=size*.65f;
    for(int i=0;i<3;i++) {
        int value=reward(map,i);
        if(value<0) snprintf(text[i],sizeof(text[i]),"%s --",names[i]);
        else snprintf(text[i],sizeof(text[i]),"%s %d",names[i],value);
        sizes[i]=size*(current_mode==i ? 1.13f : 1);
        widths[i]=measure_text(font,text[i],sizes[i],0).x;
        total+=widths[i];
    }
    total+=gap*2;
    float scale=fminf(1,box.width*.94f/total);
    float x=box.x+(box.width-total*scale)*.5f;
    float y=box.y+box.height*.75f;
    for(int i=0;i<3;i++) {
        label(font,text[i],x,y+(current_mode==i ? -size*.06f : 0),sizes[i]*scale,current_mode==i ? (Tint){126,201,138,tint.a} : tint);
        x+=(widths[i]+gap)*scale;
    }
}

static float visible_right(Box card) {
    float screen_right=clipped ? clipping.x+clipping.width : (float)screen_width();
    Vector edge=local_point((Vector){screen_right,0});
    return fminf(card.x+card.width,edge.x);
}

static void card_labels(TextRun run, const Map *map, Box card) {
    char card_id[40]; snprintf(card_id,sizeof(card_id),"%lld\n",map->id);
    if(!strstr(visible_cards,card_id) && strlen(visible_cards)+strlen(card_id)<sizeof(visible_cards)) strcat(visible_cards,card_id);
    int grid=card.width<card.height*1.2f;
    float padding=grid ? run.position.x-card.x : card.height*.075f;
    float right=visible_right(card)-padding;
    float size=grid ? run.size*.6154f : run.size*.5f;
    float x=grid ? run.position.x : run.position.x+(right-run.position.x)*.48f;
    size=fminf(size,(right-x)/11);
    float y=grid ? card.y+padding+size*2.05f : run.position.y;
    if(map && map->ranked<0) {
        const char *check=map->check[0] ? map->check : "Check map";
        float check_size=size*.9f;
        if(grid) thumbnail_text(run.font,check,(Vector){x,y},check_size,run.tint,right-x);
        else fitted(run.font,check,(Vector){x,y},check_size,0,run.tint,right-x);
        if(check_count<256) check_buttons[check_count++]=(CheckButton){screen_box((Box){x,y,fminf(right-x,measure_text(run.font,check,check_size,0).x),check_size*1.5f}),map->id};
        return;
    }
    float rating_width=size*3.5f;
    char text[128];
    const char *ranked=!map || map->ranked<0 ? "--" : map->ranked==2 ? "Legacy" : map->ranked ? "Ranked" : "Unranked";
    if (grid) {
        float width=measure_text(run.font,ranked,size,0).x+size*1.5f;
        draw_box((Box){x-size*.2f,y-size*.2f,width,size*1.45f},.18f,4,(Tint){0,0,0,125});
    }
    icon(x,y,size*1.1f);
    label(run.font,ranked,x+size*1.4f,y,size,run.tint);
    if (map && map->rankability>=0) snprintf(text,sizeof(text),"%.2f/5",map->rankability);
    else strcpy(text,"--/5");
    if (grid) thumbnail_text(run.font,text,(Vector){x+size*1.4f,y+size*1.35f},size*.875f,muted,right-x-rating_width);
    else label(run.font,text,x+size*1.4f,y+size*1.35f,size*.875f,muted);
    float rating=difficulty(map);
    if (rating>=0) snprintf(text,sizeof(text),"%.2f",rating);
    else strcpy(text,"--");
    float rating_size=grid ? size*1.22f : size;
    float rating_x=right-measure_text(run.font,text,rating_size,0).x;
    if (grid) thumbnail_text(run.font,text,(Vector){rating_x,y},rating_size,run.tint,rating_width*1.22f);
    else label(run.font,text,rating_x,y,rating_size,run.tint);
    rewards(map,text,sizeof(text));
    float reward_size=grid ? size*.875f : size*.75f;
    float reward_y=grid ? run.position.y-reward_size*1.5f : run.position.y+run.size*1.22f;
    if (grid) thumbnail_text(run.font,text,(Vector){x,reward_y},reward_size,run.tint,right-x);
    if (map && (map->challenge[0] || map->passed)) {
        if (map->challenge[0]) snprintf(text,sizeof(text),"%s",map->challenge);
        else snprintf(text,sizeof(text),"%s",current_mode>=0 && current_mode<3 && (map->passed&(1<<current_mode)) ? "Passed" : "Passed in another mode");
        float badge_y=grid ? reward_y-reward_size*1.5f : y+size*1.35f;
        if(grid) thumbnail_text(run.font,text,(Vector){x,badge_y},reward_size,run.tint,right-x);
        else fitted(run.font,text,(Vector){x+size*5,badge_y},size*.65f,0,muted,right-x-size*5);
    }
}

static int observe_player(TextRun run) {
    if(!strcmp(run.text,"Global player rankings")) global_leaderboard=1;
    char *end;
    int rank=run.text[0]=='#' && strtol(run.text+1,&end,10)>0 && !*end;
    if(rank) { rank_row=container(run.position,run.size,0); rank_pending=rank_row.width>0; return 0; }
    Box row=rank_row;
    if(rank_pending && !contains(row,run.position)) rank_pending=0;
    if(rank_pending && run.text[0]=='[') return 0;
    if(rank_pending && row.width>run.size*8 && row.height<run.size*5 && row.height>run.size*1.5f && strlen(run.text)<100) {
        rank_pending=0;
        char needle[132]; snprintf(needle,sizeof(needle),"%s\n",run.text);
        if(!strstr(visible_players,needle) && strlen(visible_players)+strlen(needle)<sizeof(visible_players)) strcat(visible_players,needle);
        EnterCriticalSection(&data_lock);
        Player *found=NULL;
        for(int i=0;i<player_count;i++) if(!_stricmp(players[i].name,run.text)) { found=&players[i]; break; }
        if(found) {
            const char *extra=found->summary;
            if(!global_leaderboard && menu) {
                if(pending_player_count<50) pending_players[pending_player_count++]=(PendingPlayer){run,row,*found};
                extra="";
            }
            float width=row.x+row.width-run.position.x-run.size*1.8f;
            fitted(run.font,run.text,(Vector){run.position.x,row.y+row.height*.07f},run.size*.87f,run.spacing,run.tint,width);
            fitted(run.font,extra,(Vector){run.position.x,row.y+row.height*.73f},fminf(run.size*.56f,row.height*.2f),0,muted,width);
            player_row=row; player_name=run; player_line=1;
            LeaveCriticalSection(&data_lock);
            return 1;
        }
        LeaveCriticalSection(&data_lock);
    } else if(player_line) {
        player_line=0;
        if(contains(player_row,run.position) && fabsf(run.position.x-player_name.position.x)<run.size && run.position.y>player_name.position.y && (strstr(run.text,"rp") || strstr(run.text," RP") || strstr(run.text,"%"))) {
            fitted(run.font,run.text,(Vector){run.position.x,player_row.y+player_row.height*.39f},run.size*.85f,run.spacing,run.tint,player_row.x+player_row.width-run.position.x-run.size*2);
            return 1;
        }
    }
    return 0;
}

static void connect_account(void) {
    char path[MAX_PATH];
    snprintf(path,sizeof(path),"%s\\connect",folder);
    HANDLE file=CreateFileA(path,GENERIC_WRITE,FILE_SHARE_READ,NULL,CREATE_ALWAYS,0,NULL);
    if (file!=INVALID_HANDLE_VALUE) CloseHandle(file);
}

static void profile_labels(float right) {
    TextRun r=profile_text;
    float gap=r.size*.55f;
    float left=profile_bounds.x+profile_bounds.width+gap;
    float width=right-left-gap;
    if (width<r.size*10) return;
    float height=profile_bounds.height;
    float top=profile_bounds.y;
    float avatar_size=height*.62f;
    profile_texture(-1,left,top+(height-avatar_size)*.5f,avatar_size);
    float x=left+avatar_size+gap;
    float name_size=r.size;
    float stats_size=r.size*.64f;
    float row=top+height*.54f;
    const char *names[]={"RHP","RBP","RPL","RPS","RPVR"};
    float available=right-gap-x, column=available/5;
    char text[128];
    int fresh=GetTickCount64()-updated<15000;
    int busy=fresh && (!strcmp(phase,"authorizing") || !strcmp(phase,"loading"));
    for (int i=0;i<5 && !busy;i++) {
        if (points[i]>=0) snprintf(text,sizeof(text),"%s %d",names[i],points[i]);
        else snprintf(text,sizeof(text),"%s --",names[i]);
        float size=stats_size;
        float measure=measure_text(r.font,text,size,0).x;
        float badge=height*.31f;
        if (measure>column-gap*.4f-badge) size*=(column-gap*.4f-badge)/measure;
        profile_texture(i,x+i*column,row-stats_size*.2f,badge);
        label(r.font,text,x+i*column+badge+2,row,size,r.tint);
    }
    if (busy) fitted(r.font,hint,(Vector){x,row},stats_size,0,r.tint,available);
    const char *button=!strcmp(phase,"authorizing") ? "Cancel / Retry" : signed_in ? "Account" : "Log in";
    float button_size=r.size*.8f;
    float button_width=measure_text(r.font,button,button_size,0).x;
    float button_x=right-gap-button_width;
    float name_width=available*.40f;
    fitted(r.font,username,(Vector){x,top+height*.02f},name_size,0,r.tint,name_width);
    const char *connection=fresh ? status : "Rhythians Offline";
    float connection_x=x+name_width+gap*.4f;
    fitted(r.font,connection,(Vector){connection_x,top+height*.1f},r.size*.65f,0,r.tint,button_x-connection_x-gap);
    Box button_box={button_x-gap*.5f,top,button_width+gap,height*.47f};
    Vector a=screen_point((Vector){button_box.x,button_box.y}), b=screen_point((Vector){button_box.x+button_box.width,button_box.y+button_box.height});
    login_bounds=(Box){a.x,a.y,b.x-a.x,b.y-a.y};
    login_visible=1;
    Tint color=contains(login_bounds,mouse_position()) ? (Tint){255,255,255,255} : r.tint;
    label(r.font,button,button_x,top+height*.05f,button_size,color);
}

static int title_matches(const char *full,const char *text) {
    const char *ellipsis=strstr(text,"\xe2\x80\xa6");
    if(!ellipsis) ellipsis=strstr(text,"...");
    if(ellipsis) return !strncmp(full,text,ellipsis-text);
    return !strcmp(full,text);
}

static int identify_card(const char *text,const char *mapper) {
    const char *title=text;
    const char *recent=measured[(measure_cursor-1)&15];
    if(measure_cursor && title_matches(recent,text)) title=recent;
    int found=-1;
    for(int i=0;i<map_count;i++) {
        if(!title_matches(maps[i].title,title) || !title_matches(maps[i].mapper,mapper)) continue;
        if(found>=0 && (!maps[i].identity[0] || strcmp(maps[i].identity,maps[found].identity))) return -1;
        found=i;
    }
    return found;
}

static int title_exists(const char *text) {
    if(!strstr(text,"\xe2\x80\xa6") && !strstr(text,"...")) {
        unsigned slot=hash(text)&(TABLE_SIZE-1);
        while(table[slot]) { if(!strcmp(maps[abs(table[slot])-1].title,text)) return 1; slot=(slot+1)&(TABLE_SIZE-1); }
        return 0;
    }
    for(int i=0;i<map_count;i++) if(title_matches(maps[i].title,text)) return 1;
    return 0;
}

static int identify(const char *text) {
    int index=lookup(text);
    const char *ellipsis=strstr(text,"\xe2\x80\xa6");
    if (index>=0 || !ellipsis) return index;
    int candidate=-1;
    for (int i=0;i<map_count;i++) {
        if (!strncmp(maps[i].title,text,ellipsis-text)) {
            if (candidate>=0 && maps[i].id!=maps[candidate].id && (!maps[i].identity[0] || strcmp(maps[i].identity,maps[candidate].identity))) return -1;
            candidate=i;
        }
    }
    return candidate;
}

__declspec(dllexport) Vector MeasureTextEx(Font font,const char *text,float size,float spacing) {
    InitOnceExecuteOnce(&initialized,setup,NULL,NULL);
    if (text && strlen(text)<512) {
        EnterCriticalSection(&data_lock);
        if (title_exists(text) && !strstr(text,"\xe2\x80\xa6") && !strstr(text,"...")) snprintf(measured[measure_cursor++&15],512,"%s",text);
        LeaveCriticalSection(&data_lock);
    }
    return measure_text(font,text,size,spacing);
}

__declspec(dllexport) void DrawTextEx(Font font,const char *text,Vector p,float size,float spacing,Tint tint) {
    InitOnceExecuteOnce(&initialized,setup,NULL,NULL);
    if (!text) return;
    TextRun run={font,p,size,spacing,tint,{0}};
    snprintf(run.text,sizeof(run.text),"%s",text);
    if(font.texture.id && size>12) panel_font=font;
    if(accepted && observe_player(run)) { previous_text=run; return; }
    if (!strcmp(text,"PLAY") || !strcmp(text,"PRACTICE")) {
        menu_seen=1;
        start_nonzero=!strcmp(text,"PRACTICE");
        play_bounds=container(p,size,0);
    }
    if(!accepted) { draw_text(font,text,p,size,spacing,tint); previous_text=run; return; }
    if (!menu) { draw_text(font,text,p,size,spacing,tint); previous_text=run; return; }
    if(!strcmp(text,"Pitch Lock")) speed_popup=1;
    if(!strncmp(text,"Set speed multiplier",20)) custom_popup=1;
    if(custom_popup) {
        if(!strcmp(text,"Mode: Percent")) custom_percent=1;
        if(!strcmp(text,"Mode: Decimal")) custom_percent=0;
        char *end;
        float value=strtof(text,&end);
        if(end!=text && (!*end || !strcmp(end,"%"))) custom_value=custom_percent ? value*.01f : value;
        if(!strcmp(text,"Confirm")) custom_confirm=screen_box(container(p,size,0));
    }
    if(speed_popup && speed_choice_count<12) {
        float value; char suffix,extra;
        if(sscanf(text,"%f%c%c",&value,&suffix,&extra)==2 && suffix=='x' && value>=.5f && value<=2) {
            Box bounds=container(p,size,0);
            if(bounds.width>0 && bounds.height<size*3) speed_choices[speed_choice_count++]=(SpeedChoice){screen_box(bounds),fabsf(value-.87f)<.001f ? 1.0f/1.15f : value};
        }
    }
    EnterCriticalSection(&data_lock);
    Box card=container(p,size,1);
    int index=identify(text);
    if (index<0 && !title_exists(text)) card=(Box){0};
    Box pending_card=current_card;
    TextRun pending_title=current_title;
    current_card=(Box){0};
    if (card.width>0) {
        current_card=card;
        current_title=run;
        if (card.width>card.height*3) {
            float right=visible_right(card)-card.height*.075f;
            float end=p.x+(right-p.x)*.48f-size*.3f;
            fitted(font,text,p,size,spacing,tint,end-p.x);
        } else draw_text(font,text,p,size,spacing,tint);
    } else if (!strncmp(text,"Mapped by ",10) && pending_card.width>0 && contains(pending_card,p) && fabsf(p.x-pending_title.position.x)<pending_title.size*.25f && p.y>pending_title.position.y && p.y<pending_title.position.y+pending_title.size*1.6f) {
        int chosen=identify_card(pending_title.text,text+10);
        if (chosen>=0) card_labels(pending_title,&maps[chosen],pending_card);
        if (chosen>=0 && pending_card.width>pending_card.height*3) {
            float right=visible_right(pending_card)-pending_card.height*.075f;
            float x=pending_title.position.x+(right-pending_title.position.x)*.48f;
            fitted(font,text,p,size,spacing,tint,x-p.x-size*.3f);
            char value[128];
            rewards(&maps[chosen],value,sizeof(value));
            fitted(font,value,(Vector){x,p.y+size*.3f},size*.5f,0,tint,right-x);
        } else draw_text(font,text,p,size,spacing,tint);
    } else if ((!strcmp(text,"PLAY") || !strcmp(text,"PRACTICE")) && play_bounds.width>0) {
        float adjusted=size*.88f;
        label(font,text,play_bounds.x+(play_bounds.width-measure_text(font,text,adjusted,spacing).x)*.5f,play_bounds.y+play_bounds.height*.04f,adjusted,tint);
    } else if (!strncmp(text,"MAX ",4) && strstr(text," RP") && play_bounds.width>0 && contains(play_bounds,p)) {
        float adjusted=size*.88f;
        label(font,text,play_bounds.x+(play_bounds.width-measure_text(font,text,adjusted,spacing).x)*.5f,play_bounds.y+play_bounds.height*.51f,adjusted,tint);
        int chosen=selected_index;
        play_rewards(font,chosen>=0 ? &maps[chosen] : NULL,play_bounds,size*.82f,tint);
    } else {
        draw_text(font,text,p,size,spacing,tint);
        if (size>30 && title_exists(text)) snprintf(selected,sizeof(selected),"%s",text);
        if(selected[0] && !strncmp(text,"Mapped by ",10)) {
            selected_mapper=run;
            selected_index=identify_card(selected,text+10);
            for(int i=0;i<pending_player_count;i++) {
                PendingPlayer *entry=&pending_players[i];
                if(selected_index>=0 && !strcmp(entry->player.map,maps[selected_index].identity)) fitted(entry->run.font,entry->player.score,(Vector){entry->run.position.x,entry->row.y+entry->row.height*.73f},fminf(entry->run.size*.56f,entry->row.height*.2f),0,muted,entry->row.x+entry->row.width-entry->run.position.x-entry->run.size*1.8f);
            }
            pending_player_count=0;
        }
        if (selected[0] && !strncmp(text,"Mapped by ",10)) selected_mapper=run;
        int native_status=!strcmp(previous_text.text,"Unranked") || !strcmp(previous_text.text,"Ranked") || !strcmp(previous_text.text,"Legacy") || !strcmp(previous_text.text,"Approved") || !strcmp(previous_text.text,"Unknown");
        if (selected_index>=0 && native_status && strstr(text," stars") && fabsf(previous_text.position.y-p.y)<size*.1f && previous_text.position.x<p.x && previous_text.size==size) {
            TextRun left=previous_text;
            int chosen=selected_index;
            const Map *detail=chosen>=0 ? &maps[chosen] : NULL;
            float top=selected_mapper.position.y+selected_mapper.size*1.15f;
            float height=p.y-top-size*.25f;
            float label_size=fminf(size*.78f,height*.7f);
            if (label_size>size*.35f) {
                float y=top+(height-label_size)*.5f;
                char value[128],rating[48];
                const char *name=detail->ranked==2 ? "Legacy" : detail->ranked<0 ? "--" : detail->ranked ? "Ranked" : "Unranked";
                if(detail->rankability>=0) snprintf(value,sizeof(value),"%s  %.2f/5%s",name,detail->rankability,current_mode>=0 && current_mode<3 && detail->passed&(1<<current_mode) ? "  Passed" : "");
                else snprintf(value,sizeof(value),"%s  --/5",name);
                float adjusted=difficulty(detail);
                if(adjusted>=0) snprintf(rating,sizeof(rating),"%.2f",adjusted); else strcpy(rating,"--");
                float start=left.position.x-size*1.2f;
                float end=p.x+measure_text(font,text,size,spacing).x;
                float rating_width=measure_text(font,rating,label_size,0).x;
                float available=end-start-label_size*2.5f-rating_width;
                float text_width=fminf(available,measure_text(font,value,label_size,0).x);
                float x=start+(end-start-text_width-rating_width-label_size*2.5f)*.5f;
                icon(x,y,label_size);
                fitted(font,value,(Vector){x+label_size*1.35f,y},label_size,0,tint,available);
                label(font,rating,x+label_size*2.5f+text_width,y,label_size,tint);
            }
            detail_bottom=p.y;
        }
        int minutes,seconds; char tail;
        if(selected[0] && detail_bottom>0 && !seek_time.text[0] && p.y>detail_bottom+size*3 && sscanf(text,"%d:%d%c",&minutes,&seconds,&tail)==2) seek_time=run;
        char *end;
        strtol(text,&end,10);
        if (end!=text && !strcmp(end," RP")) {
            Box bounds=container(p,size,0);
            if (bounds.height>size*2 && bounds.height<size*4) { profile_bounds=bounds; profile_text=run; profile_pending=1; }
        }
    }
    previous_text=run;
    LeaveCriticalSection(&data_lock);
}

__declspec(dllexport) void BeginScissorMode(int x,int y,int width,int height) {
    InitOnceExecuteOnce(&initialized,setup,NULL,NULL);
    clipping=(Box){(float)x,(float)y,(float)width,(float)height}; clipped=1;
    begin_scissor(x,y,width,height);
}

__declspec(dllexport) void EndScissorMode(void) {
    InitOnceExecuteOnce(&initialized,setup,NULL,NULL);
    clipped=0;
    end_scissor();
}
