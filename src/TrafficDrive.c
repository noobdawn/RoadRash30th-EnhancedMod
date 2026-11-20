/* Freestanding x86 traffic target hook. Only ordinary traffic (type 16,
   direction 0/2), never motorcycles or cross-street traffic. Native movement,
   collision and despawning remain in the game. No coordinate teleportation.
   ECX=car, EDX=config, stack=speed,acceleration; EDX:EAX=new acceleration:speed. */
typedef unsigned int u32;
typedef unsigned long long u64;
typedef unsigned char byte;
typedef struct { byte *car;u32 identity,planned;int choice; } Driver;
typedef struct { u32 *tick;Driver *drivers;u32 sameWhole,sameFraction,oppositeWhole,oppositeFraction,crazy,seed; } Config;
#define I(p,o) (*(int *)((byte *)(p)+(o)))
#define P(p,o) (*(byte **)((byte *)(p)+(o)))
static __attribute__((always_inline)) inline u32 mix(u32 x) {
    x^=x>>16;x*=0x7feb352dU;x^=x>>15;x*=0x846ca68bU;return x^(x>>16);
}
static __attribute__((always_inline)) inline int clamp(int x,int lo,int hi) {return x<lo?lo:(x>hi?hi:x);}
static __attribute__((always_inline)) inline u32 scale(int base,u32 whole,u32 fraction) {
    u32 b=(u32)(base>0?base:0);
    u64 value=(u64)b*whole+(b/1000)*fraction+(b%1000)*fraction/1000;
    return value>0x7fffffffU?0x7fffffffU:(u32)value;
}
u64 __attribute__((fastcall)) TrafficDrive(byte *car,Config *c,int speed,int acceleration) {
    int direction;u32 whole,fraction,s,a;
    if(!car||I(car,0xc4)!=16)return ((u64)(u32)acceleration<<32)|(u32)speed;
    direction=I(car,0x1cc);
    if(direction!=0&&direction!=2)return ((u64)(u32)acceleration<<32)|(u32)speed;
    whole=direction==0?c->sameWhole:c->oppositeWhole;
    fraction=direction==0?c->sameFraction:c->oppositeFraction;
    s=scale(speed,whole,fraction);a=scale(acceleration,whole,fraction);
    if(s&&acceleration>0&&!a)a=1; /* Sub-1x cars must still accelerate from rest. */
    /* The native acceleration step uses a signed 32-bit add and multiply. */
    if(whole!=1||fraction) {
        int dt=I(car,0x18),current=I(car,0xec);
        u32 headroom=(u32)(0x7fffffff-clamp(current,0,0x7fffffff));
        if(dt>0&&a>headroom/(u32)dt)a=headroom/(u32)dt;
    }
    if(c->crazy&&c->drivers&&P(car,0x2c)&&P(car,0x140)) {
        byte *road=P(car,0x2c);int lo=I(road,0xe0),hi=I(road,0xe4);
        if(lo> -100000&&hi<100000&&hi-lo>=4) {
            u32 tick=*c->tick,id=(u32)I(car,0x208),seed=mix((u32)car^id^c->seed);
            u32 slot=mix((u32)car)&4095;
            Driver *d=&c->drivers[slot],*oldest=d;
            for(int n=0;n<16;n++) {
                Driver *q=&c->drivers[(slot+(u32)n)&4095];
                if(q->car==car||!q->car){d=q;break;}
                if(tick-q->planned>tick-oldest->planned)oldest=q;
                d=oldest;
            }
            int interval=96+(int)(seed%145); /* 1.6 to 4 seconds, independent phases. */
            u32 phase=(tick+seed%(u32)interval)/(u32)interval;
            if(d->car!=car||d->identity!=id||d->planned!=phase) {
                d->car=car;d->identity=id;d->planned=phase;d->choice=(int)(mix(seed+phase*97)%16);
            }
            int choice=d->choice,half=clamp(I(car,0x174)/256,12,180),target;
            /* 25% shoulder targets, split between both sides; the remaining
               targets span lane centers on BOTH halves of the pavement. */
            if(choice<2)target=lo-96;
            else if(choice<4)target=hi+96;
            else {
                int left=I(road,0x30)/256,right=I(road,0x2c)/256,origin=I(road,0xcc);
                if(left>=0&&left<=64&&right>0&&right<=64&&origin> -100000&&origin<100000)
                    target=origin+((int)(mix(seed+phase*193)%(u32)(left+right))-left)*250+125;
                else target=lo+(hi-lo)*(choice-4)/11;
                int margin=half<(hi-lo)/2?half:(hi-lo)/2;
                target=clamp(target,lo+margin,hi-margin);
            }
            /* Direction vector consumed immediately by native 0x62980. */
            I(car,0x1dc)=clamp(target*256-I(car,0x28),-180*256,180*256);
            I(car,0x1e0)=direction==0?800:-800;
        }
    }
    return ((u64)a<<32)|s;
}
