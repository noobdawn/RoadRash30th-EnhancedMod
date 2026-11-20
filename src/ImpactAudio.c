/* Nearby AI impacts. Called only from native collision callbacks, on the game
   thread. No actor-list scan, external audio device changes, or frame polling. */
typedef unsigned int u32;
typedef unsigned char byte;
typedef struct { byte *actor,*other;u32 tick; } Hit;
typedef void (__attribute__((fastcall)) *Play)(byte *,int,int,int,int);
typedef struct { byte *image;Play play;Hit *hits;u32 tick,used,ram; } Config;
#define I(p,o) (*(int *)((byte *)(p)+(o)))
#define U(p,o) (*(u32 *)((byte *)(p)+(o)))
#define P(p,o) (*(byte **)((byte *)(p)+(o)))
static __attribute__((always_inline)) inline int magnitude(int a) {
    if(a==(-2147483647-1))return 2147483647;
    return a<0?-a:a;
}
static __attribute__((always_inline)) inline int clamp(int a,int lo,int hi) {return a<lo?lo:(a>hi?hi:a);}
void __attribute__((fastcall)) ImpactAudio(byte *actor,Config *c) {
    byte *player,*listener,*other,*owner;int kind,strength,dx,dz,near,gain,pan,bank;u32 tick,slot;
    if(!actor||!c||!c->image||!c->play||!c->hits)return;
    kind=I(actor,0xc4);
    if(kind!=4&&kind!=8)return;
    player=P(c->image,0x1a69ec);if(!player)return;
    owner=kind==8?P(actor,0x1c4):actor;
    if(!owner||owner==player||I(actor,0xc0)&16)return;
    other=P(actor,0x1a0);if(!other||other==actor)return;
    /* Native collision filters have already rejected the harmless own bike.
       The ram patch already supplies player-triggered knockdown sound. */
    if(c->ram&&other==player)return;
    listener=P(player,0x2fc);if(!listener)listener=player;
    {
        long long delta=(long long)I(actor,0x1c)-I(listener,0x1c);
        if(delta<=-4096||delta>=4096)return;
        dz=magnitude((int)delta);
        delta=((long long)I(actor,0x28)-I(listener,0x28))/256;
        if(delta<=-4096||delta>=4096)return;
        dx=(int)delta;
    }
    near=dz+magnitude(dx);if(near>=4096)return;
    if(kind==4) {
        strength=magnitude(I(actor,0x184));
        if(strength<magnitude(I(actor,0x188)))strength=magnitude(I(actor,0x188));
    } else {
        strength=magnitude(I(other,0xec));
        if(strength<magnitude(I(actor,0xec)))strength=magnitude(I(actor,0xec));
    }
    if(strength<1000)return;
    gain=clamp(I(c->image,0x1c5544),0,100)*16384/100;
    gain=gain*(4096-near)/4096;if(gain<=0)return;
    tick=U(c->image,0x2036e8);
    if(c->tick!=tick){c->tick=tick;c->used=0;}
    if(c->used>=2)return;
    /* Pair suppression covers symmetric callbacks; actor suppression covers
       a continuous pile-up. Unsigned elapsed time tolerates tick wrap/reset. */
    slot=(((u32)actor>>4)^((u32)other>>4))&255;
    for(u32 n=0;n<256;n++) {
        Hit *h=&c->hits[n];
        if(h->actor&&(tick-h->tick)<12 &&
           (h->actor==actor||h->other==actor||h->actor==other||h->other==other))return;
    }
    c->hits[slot].actor=actor;c->hits[slot].other=other;c->hits[slot].tick=tick;c->used++;
    pan=clamp(dx*8,-8000,8000);
    c->play(P(c->image,0x2162b0),gain,pan,10000,0);
    if(strength>=2000) {
        bank=I(c->image,0x1c5400);if(bank!=0&&bank!=3)bank=0;
        bank+=strength>=6000?2:1;
        c->play(P(c->image,0x2162bc+bank*4),gain*3/4,pan,10000,0);
    }
}
