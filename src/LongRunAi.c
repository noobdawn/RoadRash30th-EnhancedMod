/* Freestanding x86, no imports, globals, allocation or game-state mutation.
   Steering memory lives in the launcher's private table, never in game actors.
   Regenerate LauncherAiNative.cs with scripts/build-ai.py after editing.
   Time is the native simulation tick (nominally 60 ticks/second), not render FPS.
   fastcall: ECX=bike, EDX=config; EAX=speed, EDX=lateral chase offset (8.8). */
typedef unsigned int u32;
typedef unsigned long long u64;
typedef unsigned char byte;
typedef struct { byte *bike,*rider; u32 tick,planned; int goal,desired,slow,reserved; } LaneState;
typedef struct { u32 *tick; byte **player; int speed, rhythm, sprint, chase, weave; u32 seed;
    LaneState *lanes; int lead; } Config;
#define I(p,o) (*(int *)((byte *)(p)+(o)))
#define P(p,o) (*(byte **)((byte *)(p)+(o)))
static __attribute__((always_inline)) inline u32 mix(u32 x) {
    x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; return x ^ (x >> 16);
}
static __attribute__((always_inline)) inline int clamp(int n,int lo,int hi) { return n<lo?lo:(n>hi?hi:n); }
static __attribute__((always_inline)) inline int absolute(int n) { return n<0?-n:n; }
static __attribute__((always_inline)) inline int smooth(int x) { return x*x*(3072-2*x)/1048576; }
static __attribute__((always_inline)) inline int noise(u32 tick,u32 seed,int period,int amplitude) {
    u32 shifted=tick+(seed&1023), step=shifted/(u32)period;
    int f=smooth((shifted%(u32)period)*1024/(u32)period);
    int a=(int)(mix(seed+step)%(u32)(2*amplitude+1))-amplitude;
    int b=(int)(mix(seed+step+1)%(u32)(2*amplitude+1))-amplitude;
    return a+(b-a)*f/1024;
}
static __attribute__((always_inline)) inline int pulse(int t,int start,int length) {
    t-=start; if(t<=0||t>=length)return 0;
    if(t<30)return smooth(t*1024/30);
    if(t>length-30)return smooth((length-t)*1024/30);
    return 1024;
}
typedef enum { SolidObstacle, RunningPeer, RoadTraffic } ObstacleType;
typedef struct { int x,z,width,length,forward; ObstacleType type; } Obstacle;
static __attribute__((always_inline)) inline int steer(byte *bike,byte *rider,Config *c,u32 seed,u32 tick,int travel,int *slow) {
    byte *road=P(rider,0x2c);
    int lo,hi,x=I(rider,0x28),width,margin,desired,goal,dt=1;
    int rate=clamp(travel/16,128,2048)*(c->weave+1)/3;
    LaneState *state=0;
    if(!c->weave||!road||!P(rider,0x140))return 0;
    /* Native road builder 0x74D5D: paved edges = origin +/- lane widths.
       Native surface classifier 0x4E180 uses these, NOT 0x14C/0x158. */
    lo=I(road,0xe0);hi=I(road,0xe4);
    if(lo<=-100000||hi>=100000||hi-lo<4)return 0;
    lo*=256;hi*=256;width=hi-lo;
    margin=clamp(I(rider,0x174),12*256,80*256)+8*256;
    margin=clamp(margin,0,width/2-256);lo+=margin;hi-=margin;width=hi-lo;
    /* Riding target 0x1ABC0: origin + lane*250 + profile lane offset.
       Use the CURRENT segment: no curve look-ahead, curve speed limit or attacks.
       Independent decisions periodically leave the preferred riding lane. */
    {
        byte *profile=P(rider,0x1d8);
        int origin=I(road,0xcc),right=I(road,0x2c)/256,left=I(road,0x30)/256;
        int period=(16-c->weave*3)*60+(int)(mix(seed+19)%181);
        u32 phase=(tick+mix(seed+103)%(u32)period)/(u32)period,choice=mix(seed+phase*97);
        int first=-left,last=right-1,lane=0,offset=125*256;
        if(right>0&&right<=64&&left>=0&&left<=64&&origin> -100000&&origin<100000) {
            if(profile) {
                int mode=profile[0x18c];
                lane=mode==2?right-1:(mode==1?(right-1)/2:0);
                offset=clamp(I(profile,0x190),-250*256,250*256);
            }
            if(choice%3)lane=first+(int)(mix(choice+7)%(u32)(last-first+1));
            desired=(origin+lane*250)*256+offset;
        } else {
            /* Synthetic/narrow road fallback still uses distinct periodic lines. */
            int fraction=64+choice%897;
            desired=lo+(width/1024)*fraction+(width%1024)*fraction/1024;
        }
        desired=clamp(desired+noise(tick,seed^0xa53c9e71U,360+mix(seed+31)%241,c->weave*8*256),lo,hi);
    }
    goal=clamp(x,lo,hi);
    if(c->lanes) {
        u32 slot=mix((u32)bike^mix((u32)rider))&32767;
        LaneState *vacant=0,*oldest=&c->lanes[slot];
        for(int probe=0;probe<32;probe++) {
            LaneState *s=&c->lanes[(slot+(u32)probe)&32767];
            if(s->bike==bike&&s->rider==rider){state=s;break;}
            if(!s->bike){vacant=s;break;}
            if(tick-s->tick>tick-oldest->tick)oldest=s;
        }
        if(!state) {
            state=vacant?vacant:oldest;state->bike=bike;state->rider=rider;
            state->tick=tick-1;state->planned=tick-6;state->goal=goal;state->desired=goal;state->slow=1000;
        }
        /* Tick rollback/new race or re-entry starts steering at the current position. */
        if(tick-state->tick>120) {state->goal=goal;state->planned=tick-6;state->tick=tick-1;}
        dt=(int)(tick-state->tick);goal=clamp(state->goal,lo,hi);
        if(tick-state->planned<6) {desired=clamp(state->desired,lo,hi);*slow=state->slow;goto move;}
    }
    {
        Obstacle nearby[96];int count=0,horizon=clamp(travel/256*60,512,4096);
        int selfWidth=clamp(I(rider,0x174),12*256,80*256);
        /* The native actor chain is sorted by longitudinal position. Search locally
           in both directions; a hard bound keeps a 10,000-racer pack affordable. */
        for(int direction=0;direction<2;direction++) {
            byte *node=P(rider,direction*4);
            for(int n=0;node&&node!=rider&&n<48;n++) {
                byte *next=P(node,direction*4),*nr;int kind,z;
                if(!next)break; /* native end sentinel has no actor payload */
                z=I(node,0x1c)-I(rider,0x1c);
                if((direction==0&&z>horizon+256)||(direction==1&&z< -280))break;
                nr=P(node,0x2c);kind=I(node,0xc4);
                if(node!=bike&&nr&&(node[0xc0]&3)&&
                   (nr==road||(nr[0x36]&road[0x36]&3))&&
                   (kind==1||kind==2||kind==4||kind==8||kind==16||kind==32)) {
                    /* Hidden chase bikes must never become a moving obstacle wall. */
                    int ignore=kind==4&&I(node,0x2f4)!=0;
                    ObstacleType type=kind==16?RoadTraffic:SolidObstacle;
                    if(kind==8) {
                        byte *owner=P(node,0x1c4);
                        if(owner) {
                            int state=I(owner,0x2f4);
                            if(state==0)ignore=1;
                            if(state==2&&!(node[0xc0]&16)&&I(owner,0xc4)==4)type=RunningPeer;
                        }
                    }
                    if(!ignore&&z>=-clamp(I(node,0x178),4,256)-24&&z<=horizon+256) {
                        nearby[count].x=I(node,0x28);nearby[count].z=z;
                        nearby[count].width=clamp(I(node,0x174),12*256,1000*256)+selfWidth+8*256;
                        nearby[count].length=clamp(I(node,0x178),4,256)+clamp(I(rider,0x178),4,128);
                        nearby[count].forward=I(node,0x108);
                        nearby[count].type=type;
                        count++;
                    }
                }
                node=next;
            }
        }
        int best=desired,bestCost=0x7fffffff;
        for(int candidate=0;candidate<11;candidate++) {
            int trial=candidate==0?desired:(candidate==1?goal:lo+width*(candidate-2)/8);
            int risk=0,cost=absolute(trial-desired)/256+absolute(trial-goal)/512;
            for(int n=0;n<count;n++) {
                Obstacle *o=&nearby[n];
                /* Traffic gets a short, imperfect reaction window. Do not turn
                   the whole road into a car-free corridor far in advance. */
                if(o->type==RoadTraffic&&o->z>clamp(travel/256*6,48,192)+o->length)continue;
                int proximity=clamp(horizon-o->z+o->length,0,horizon);
                int overlap=clamp(o->width-absolute(trial-o->x),0,o->width)/256;
                /* Prefer full passing clearance from the first plan. A cheap partial
                   gap can otherwise win early, then force a late side reversal. */
                int penalty=proximity>0?overlap*(64+proximity*32/horizon):0;
                if(o->type==RoadTraffic)penalty/=24;
                if(o->forward>=travel&&o->z>o->length+16)penalty/=8;
                risk+=penalty;
            }
            cost+=risk;
            /* Ties retain the prior goal/preference rather than always choosing left. */
            if(cost<bestCost){bestCost=cost;best=trial;}
        }
        desired=best;
        *slow=1000;
        for(int n=0;n<count;n++) {
            Obstacle *o=&nearby[n];int clearance=o->width-absolute(x-o->x);
            if(o->type==RoadTraffic)continue; /* No guaranteed safe braking for road traffic. */
            // Side-by-side runners can steer apart. Braking for each other locks a packed row.
            if(o->type==RunningPeer&&o->z<=0)continue;
            if(clearance>0&&o->z> -o->length) {
                /* Allow time to reach the free lane: selecting a safe destination
                   alone does not mean the runner has already cleared the obstacle. */
                int ticks=clearance/rate+12;
                int safeStep=clamp(o->z-o->length,0,horizon)/ticks;
                int allowed,base=travel,pace;
                /* Equal-speed runners can stay together. A moving runner is not a
                   stationary wall; only the closing speed consumes the available gap. */
                if(o->forward>=base)continue;
                allowed=clamp(o->forward,-2000000,base);
                if(allowed>=0&&base-allowed<=safeStep*256)continue;
                allowed+=safeStep*256;
                // Native motion discards fractional forward distance every tick.
                // Two road units before steering leave a whole unit after turning.
                // The ratio cap below still respects a slower configured pace.
                if(o->type==RunningPeer&&allowed<2*256)allowed=2*256;
                while(base>2000000){base/=2;allowed/=2;}
                pace=clamp(allowed*1000/base,150,1000);
                if(pace<*slow)*slow=pace;
            }
        }
        if(state){state->desired=desired;state->slow=*slow;state->planned=tick;}
    }
move:
    /* A fixed road target moves smoothly; the rider is never teleported. Limit the
       final pursuit angle separately, including when returning from a shoulder. */
    goal+=clamp(desired-goal,-dt*rate,dt*rate);
    goal=clamp(goal,lo,hi);
    if(state){state->goal=goal;state->tick=tick;}
    return clamp(goal-x,-c->lead*128,c->lead*128);
}
u64 __attribute__((fastcall)) LongRunAi(byte *bike,Config *c) {
    byte *player=*c->player,*rider,*pr=0;
    int ratio=1000, lateral=0, recovering=0, gap=0,slow=1000;
    u32 tick=*c->tick, seed, speed;
    if(!bike||bike==player||I(bike,0xc4)!=4||I(bike,0x2f4)!=2)return (u32)c->speed;
    rider=P(bike,0x2fc);
    if(!rider||I(rider,0xc4)!=8||(I(rider,0xc0)&16))return (u32)c->speed;
    seed=mix((u32)bike ^ mix((u32)rider) ^ c->seed);
    if(c->rhythm)ratio+=noise(tick,seed,180+seed%301,c->rhythm*75);
    if(c->sprint) {
        u32 cycle=(u32)(36-c->sprint*8)*60+mix(seed+7)%481;
        u32 clock=tick+mix(seed+11)%cycle, lap=clock/cycle;
        int phase=clock%cycle, burst=120+mix(seed+lap+31)%121;
        int rest=180+mix(seed+lap+47)%121;
        ratio+=(200+mix(seed+lap+61)%151)*pulse(phase,0,burst)/1024;
        ratio-=(100+mix(seed+lap+79)%101)*pulse(phase,burst,rest)/1024;
        recovering=phase>=burst&&phase<burst+rest;
    }
    if(player&&I(player,0xc4)==4)pr=P(player,0x2fc);
    if(pr&&I(pr,0xc4)==8&&!(I(pr,0xc0)&16)) {
        /* Rank distance is valid across unloaded/loaded coordinates and junctions. */
        if(I(rider,0x1f8)>0&&I(pr,0x1f8)>0) {
            gap=I(rider,0x1f8)-I(pr,0x1f8);
            if(c->chase) {
                int amount=clamp(gap>0?gap-128:-gap-128,0,1024)*(c->chase*75)/1024;
                if(recovering)amount/=2;
                ratio+=gap>=0?amount:-amount;
            }
        }
    } else pr=0;
    ratio=clamp(ratio,650,1450);
    /* Split before multiplying to avoid native-speed overflow, even at UI max. */
    speed=(u32)c->speed/1000*(u32)ratio+((u32)c->speed%1000)*(u32)ratio/1000;
    if(speed>0x7fffffffU)speed=0x7fffffffU;
    lateral=steer(bike,rider,c,seed,tick,(int)speed,&slow);
    speed=speed/1000*(u32)slow+(speed%1000)*(u32)slow/1000;
    return ((u64)(u32)lateral<<32)|speed;
}
