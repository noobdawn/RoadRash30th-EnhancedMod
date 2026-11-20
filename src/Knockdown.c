/* Single-player Road Rash HUD and race rules. All callbacks run on the race thread. */
#define COBJMACROS
#include <windows.h>
#include <ddraw.h>
#include <dsound.h>

typedef unsigned int U32;
typedef unsigned char Byte;
#define FAST __attribute__((fastcall))
#define INLINE static __inline__ __attribute__((always_inline))
#define FIELD(type, p, off) (*(type *)((Byte *)(p) + (off)))
enum { RESET, CONTACT, DAMAGE, FALL, PAUSE_CLOCK, DRAW, END_RACE, FALL_COMPLETE, GROUND_CONTACT, RACE_TICK };
enum { RIDING, AIRBORNE, ON_GROUND, WRECKED };
enum { WRECK_HOLD_MS = 3000, RAMPAGE = 5, HOLY_SHIT = 13 };
enum { HIDE_METER = 1, HARDCORE = 2, OFFICER = 4, VOICE_QUEUE = 65536 };
enum { TAGS = 32768, GRID = 400, WIDTH = 640, METER_WIDTH = 128, METER_HEIGHT = 50,
       BANNER_ROWS = 140 };
typedef struct { Byte *bike, *rider; U32 epoch, stamp; int armed; } Tag;
typedef struct { U32 offset, size; } Clip;
typedef struct { U32 magic; Clip clips[]; } VoiceBank;
typedef struct {
    U32 meter[METER_HEIGHT][METER_WIDTH];
    Byte banner[BANNER_ROWS][GRID];
} Canvas;
enum { COLOR_SLOTS = 16384 };
typedef struct { Byte palette[256][4]; U32 cache[COLOR_SLOTS]; } ColorMap;
_Static_assert(sizeof(ColorMap) == 1024 + 4 * COLOR_SLOTS, "launcher palette storage");
typedef struct {
    Byte *image;
    const U32 *atlas;
    const VoiceBank *bank;
    Canvas *canvas;
    Tag *tags;
    ColorMap *colors;
    U32 generation, total, spree, chain, clock, lastFall, lastWall, ticking;
    U32 pending, queued;
    IDirectSoundBuffer *speaker;
    U32 draws, collisionTags, damageTags, falls, voices, lastVoice, blits, active;
    IDirectDraw *spriteDevice;
    IDirectDrawSurface *spriteSurface;
    U32 creditMs, windowMs, perVoice, flags;
    Byte *queue;
    U32 clipCount, queueHead, sinceVoice, wreckPhase, retired, landedAt;
} Hud;
_Static_assert(__builtin_offsetof(Hud, wreckPhase) == 140, "launcher recovery/physics listener");
_Static_assert(sizeof(Hud) <= 0x1000, "HUD state must fit before the atlas");

INLINE void silence(Hud *h) {
    if (h->speaker) {
        IDirectSoundBuffer_Stop(h->speaker);
        IDirectSoundBuffer_Release(h->speaker);
        h->speaker = 0;
    }
}

INLINE void advance(Hud *h, U32 wall) {
    if (h->ticking) h->clock += wall - h->lastWall;
    h->lastWall = wall;
    h->ticking = 1;
    if (h->chain && h->clock - h->lastFall >= h->windowMs) h->chain = 0;
}

INLINE Byte *bikeOf(Byte *actor) {
    if (!actor) return 0;
    int kind = FIELD(int, actor, 0xc4);
    if (kind == 8) actor = FIELD(Byte *, actor, 0x1c4);
    else if (kind != 4) return 0;
    if (!actor || FIELD(int, actor, 0xc4) != 4) return 0;
    return actor;
}

INLINE Tag *tagSlot(Hud *h, Byte *bike) {
    U32 hash = ((U32)(ULONG_PTR)bike >> 3) * 2654435761u;
    U32 slot = (hash ^ (hash >> 16)) & (TAGS - 1);
    for (U32 n = 0; n < TAGS; ++n, slot = (slot + 1) & (TAGS - 1)) {
        Tag *tag = &h->tags[slot];
        if (tag->epoch != h->generation || tag->bike == bike) return tag;
    }
    return 0;
}

INLINE Tag *tagFor(Hud *h, Byte *bike, int create) {
    Byte *rider = FIELD(Byte *, bike, 0x2fc);
    if (!rider || FIELD(int, rider, 0xc4) != 8 || FIELD(Byte *, rider, 0x1c4) != bike ||
        (FIELD(U32, rider, 0xc0) & 16)) return 0;
    Tag *tag = tagSlot(h, bike);
    if (tag && (tag->epoch != h->generation || tag->rider != rider)) {
        if (!create) return 0;
        tag->bike = bike; tag->rider = rider; tag->epoch = h->generation; tag->armed = 0;
    }
    return tag;
}

INLINE int speakerReady(Hud *h) {
    if (h->speaker) {
        DWORD status = 0;
        if (SUCCEEDED(IDirectSoundBuffer_GetStatus(h->speaker, &status)) && (status & DSBSTATUS_PLAYING)) return 0;
        silence(h);
    }
    return 1;
}

INLINE void playVoice(Hud *h, U32 id);

INLINE void credit(Hud *h) {
    if (h->total < 99999) h->total++;
    h->spree++;
    h->chain++;
    h->lastFall = h->clock;
    if (!h->clipCount || ++h->sinceVoice < h->perVoice) return;
    h->sinceVoice = 0;
    U32 multi = h->chain >= 5 ? 5 : (h->chain >= 2 ? h->chain : 0);
    U32 spree = h->spree >= 10 ? 13 : (h->spree >= 3 ? h->spree + 3 : 0);
    U32 id = h->total == 1 ? 1 : (multi && (!spree || h->chain <= 5) ? multi : spree);
    /* Ownage is a standalone exclamation. Legacy banks end with another
       standalone exclamation and contain no neutral single-knockdown line. */
    if (!id) id = h->clipCount;
    if (id == RAMPAGE || id == HOLY_SHIT) {
        if (!h->pending && speakerReady(h)) playVoice(h, id);
        return;
    }
    if (!h->pending) h->pending = id;
    else if (h->queued < VOICE_QUEUE) {
        h->queue[(h->queueHead + h->queued) & (VOICE_QUEUE - 1)] = (Byte)id;
        ++h->queued;
    }
}

INLINE void retireRacer(Hud *h, Tag *tag) {
    typedef void (FAST *ParkActor)(Byte *);
    ParkActor park = (ParkActor)(h->image + 0x15f5);
    /* Park uses the world's deferred unlink pass. Bit 3 would put the
       object back into its memory pool, invalidating the race roster. */
    Byte *actor = tag->rider;
    for (int part = 0; part < 2; ++part, actor = tag->bike) {
        FIELD(Byte, actor, 0xc0) &= (Byte)~11;
        FIELD(void *, actor, 0xdc) = 0;  /* sprite */
        FIELD(void *, actor, 0xe0) = 0;  /* control */
        FIELD(void *, actor, 0xe4) = 0;  /* physics and virtual progress */
        FIELD(ParkActor, actor, 0xe8) = park; /* a same-tick streaming callback */
        park(actor);
    }
    FIELD(int, tag->rider, 0x1f8) = 0x3fffffff; /* distance remaining for placement */
    tag->armed = -2;
    ++h->retired;
}

INLINE void riderEvent(Hud *h, U32 event, Byte *a, Byte *b) {
    Byte *player = FIELD(Byte *, h->image, 0x1a69ec);
    if (!player) return;
    if (event == GROUND_CONTACT) {
        if (h->wreckPhase == AIRBORNE && a == FIELD(Byte *, player, 0x2fc)) {
            h->wreckPhase = ON_GROUND;
            h->landedAt = h->clock;
        }
        return;
    }
    if (event == RACE_TICK) {
        if (h->wreckPhase == AIRBORNE || h->wreckPhase == ON_GROUND) {
            /* This callback precedes the stock pending-verdict countdown. */
            FIELD(U32, h->image, 0x1cd4e4) = 0;
            if (h->wreckPhase == ON_GROUND && h->clock - h->landedAt >= WRECK_HOLD_MS) {
                typedef void (FAST *EndRace)(int, int);
                h->wreckPhase = WRECKED;
                ((EndRace)(h->image + 0x5a01))(5, 0);
            }
        }
        return;
    }
    if (event == FALL_COMPLETE) {
        if (h->flags & OFFICER) {
            /* Common epilogues also run when the game declined a fall. Only
               dereference actors that actually passed the state-1 write. */
            Tag *tag = tagSlot(h, a);
            if (tag && tag->epoch == h->generation && tag->armed == -1) retireRacer(h, tag);
        }
        return;
    }
    if (event == FALL) {
        ++h->falls;
        if (a == player) {
            h->spree = h->chain = 0;
            Byte *rider = FIELD(Byte *, player, 0x2fc);
            if ((h->flags & (HARDCORE | OFFICER)) && h->wreckPhase == RIDING && rider &&
                !(rider[0xc0] & 16) && !FIELD(Byte, h->image, 0x1cd47f) &&
                (!FIELD(U32, h->image, 0x1cd4e4) || FIELD(int, h->image, 0x2185bc) == 5))
                h->wreckPhase = AIRBORNE;
        } else if ((!(h->flags & HIDE_METER) || (h->flags & OFFICER)) && a && FIELD(int, a, 0xc4) == 4) {
            int retire = (h->flags & OFFICER) && FIELD(int, a, 0x324) == 1;
            Tag *tag = tagFor(h, a, retire);
            if (tag && tag->armed >= 0) {
                if (tag->armed && h->clock - tag->stamp < h->creditMs) credit(h);
                tag->armed = retire ? -1 : 0;
            }
        }
        return;
    }
    if (event == CONTACT) {
        Byte *left = bikeOf(a), *right = bikeOf(b);
        if (!left || !right || left == right) return;
        if (right == player) { Byte *tmp = a; a = b; b = tmp; right = left; }
        else if (left != player) return;
        /* An abandoned or long-run guide motorcycle isn't the player. */
        if (a == player && FIELD(int, player, 0x2f4) != 0) return;
        b = right;
    } else if (a != player || !b || b == player || FIELD(int, b, 0xc4) != 4) return;
    if (FIELD(int, b, 0x2f4) != 0) return;
    Tag *tag = tagFor(h, b, 1);
    if (tag && tag->armed >= 0) {
        tag->stamp = h->clock; tag->armed = 1;
        if (event == CONTACT) ++h->collisionTags; else ++h->damageTags;
    }
}

INLINE void playVoice(Hud *h, U32 id) {
    LPDIRECTSOUND device = FIELD(LPDIRECTSOUND, h->image, 0x1abc98);
    if (!h->bank || !device) return;
    int volume = FIELD(int, h->image, 0x1c5544);
    if (volume <= 0) return;
    if (volume > 100) volume = 100;
    const Clip *clip = &h->bank->clips[id - 1];
    WAVEFORMATEX format = {WAVE_FORMAT_PCM, 1, 22050, 44100, 2, 16, 0};
    DSBUFFERDESC desc = {0};
    desc.dwSize = sizeof(desc); desc.dwFlags = DSBCAPS_CTRLVOLUME | DSBCAPS_STATIC;
    desc.dwBufferBytes = clip->size; desc.lpwfxFormat = &format;
    if (FAILED(IDirectSound_CreateSoundBuffer(device, &desc, &h->speaker, 0))) return;
    void *dst = 0; DWORD size = 0;
    if (SUCCEEDED(IDirectSoundBuffer_Lock(h->speaker, 0, clip->size, &dst, &size, 0, 0, 0))) {
        int ready = dst && size == clip->size;
        if (ready) {
            const short *pcm = (const short *)((const Byte *)h->bank + clip->offset);
            /* The game's effects control is a linear amplitude percentage. */
            for (U32 i = 0; i < size / 2; ++i) ((short *)dst)[i] = (short)(pcm[i] * volume / 100);
        }
        HRESULT unlocked = IDirectSoundBuffer_Unlock(h->speaker, dst, size, 0, 0);
        if (ready && SUCCEEDED(unlocked)) {
            IDirectSoundBuffer_SetVolume(h->speaker, DSBVOLUME_MAX);
            if (SUCCEEDED(IDirectSoundBuffer_Play(h->speaker, 0, 0, 0))) {
                ++h->voices; h->lastVoice = id; return;
            }
        }
    }
    silence(h);
}

INLINE void pumpVoice(Hud *h) {
    if (!h->bank || !FIELD(LPDIRECTSOUND, h->image, 0x1abc98) || !speakerReady(h)) return;
    U32 id = h->pending;
    if (!id || id > h->clipCount) return;
    h->pending = 0;
    if (h->queued) {
        h->pending = h->queue[h->queueHead];
        h->queueHead = (h->queueHead + 1) & (VOICE_QUEUE - 1);
        --h->queued;
    }
    playVoice(h, id);
}

/* Four independent channels, multiplied with round-to-nearest. All layer
   pixels are premultiplied; zero alpha always has zero color. */
INLINE U32 scaled(U32 value, U32 factor) {
    U32 rb = (value & 0x00ff00ff) * factor + 0x00800080;
    U32 ag = ((value >> 8) & 0x00ff00ff) * factor + 0x00800080;
    rb = ((rb + ((rb >> 8) & 0x00ff00ff)) >> 8) & 0x00ff00ff;
    ag = ((ag + ((ag >> 8) & 0x00ff00ff)) >> 8) & 0x00ff00ff;
    return rb | (ag << 8);
}

INLINE void pixel(Canvas *c, int x, int y, U32 rgb, U32 alpha) {
    if ((unsigned)x >= METER_WIDTH || (unsigned)y >= METER_HEIGHT || !alpha) return;
    U32 *dst = &c->meter[y][x];
    *dst = alpha == 255 ? (0xff000000 | rgb) :
        ((alpha << 24) | scaled(rgb & 0xffffff, alpha)) + scaled(*dst, 255 - alpha);
}

INLINE void sprite(Hud *h, int x, int y, int sx, int sy, int w, int height) {
    for (int dy = 0; dy < height; ++dy)
        for (int dx = 0; dx < w; ++dx) {
            U32 p = h->atlas[(sy + dy) * 320 + sx + dx];
            pixel(h->canvas, x + dx, y + dy, p & 0xffffff, p >> 24);
        }
}

INLINE int digits(U32 value, Byte out[10]) {
    int n = 0;
    do { out[n++] = (Byte)(value % 10); value /= 10; } while (value);
    return n;
}

/* Fold the phase into a continuous 0..16..0 ramp. Adjacent columns share
   the same flow, so tongues travel coherently instead of sparkling at random. */
INLINE int triangle(U32 phase) {
    int t = (int)(phase & 31);
    return t <= 16 ? t : 32 - t;
}

/* The same single-line masks feed a small indexed sprite by the LCD. */
typedef struct { int width, left, base, numberStart, slot; } BannerLine;

INLINE int clamp(int value, int low, int high) {
    return value < low ? low : (value > high ? high : value);
}

INLINE void ember(Canvas *c, int x, int y, Byte color) {
    if ((unsigned)x < GRID && (unsigned)y < BANNER_ROWS && !c->banner[y][x])
        c->banner[y][x] = color;
}

/* The fire bed spans the entire line, including word spaces and letter
   counters. Its rising edge is advected sideways; the lower edge rolls at
   a different speed. Paint the whole bed before the ink. */
INLINE void fireLine(Canvas *c, BannerLine line, const Byte plume[512], U32 tick, int heat) {
    int end = line.width + 7;
    int leftPad = clamp(line.left - 3, 1, 16);
    int rightPad = clamp(GRID - 4 - line.left - end, 1, 16);
    for (int local = -leftPad; local <= end + rightPad; ++local) {
        int x = line.left + local;
        int along = clamp(local, 0, end);
        int outside = local < 0 ? -local : local - along;
        int taper = outside * 18 / (local < 0 ? leftPad : rightPad);
        int cap = taper * taper / 4;
        int base = line.base - along / 11;
        int shoulder = clamp((along - line.numberStart + 28) / 2, 0, 14);
        int roof = base - 41 - shoulder + cap;
        int floor = base + 5 + (triangle(local + tick * 2) + triangle(local / 2 - tick)) * heat / 48 - cap / 2;
        int last = floor < BANNER_ROWS ? floor : BANNER_ROWS - 1;
        for (int y = clamp(roof - 35, 0, BANNER_ROWS); y <= last; ++y) {
            int rise = roof - y;
            int drift = rise > 0 ? rise / 3 + (triangle(tick + rise) - 8) * rise / 50 : 0;
            int q = local + 64 - drift;
            int depth = plume[(U32)q & 511] - rise;
            if (depth < 0) continue;
            if (depth > floor - y) depth = floor - y;
            int curl = triangle(q / 2 + y / 2 - 2 * tick + triangle(q / 3 - y + tick))
                     + triangle(q / 3 - y + tick + triangle(y + tick) / 2);
            int energy = (depth < 16 ? depth : 16) + curl + heat - 14;
            Byte color = depth < 1 ? 52 : (energy < 12 ? 50 : (energy < 18 ? 37 :
                         (energy < 26 ? 36 : (energy < 34 ? 187 : (energy < 40 ? 185 :
                         (energy < 45 ? 201 : 229))))));
            c->banner[y][x] = color;
        }
        if (heat >= 7 && local > 0 && local < end && local % 19 == 7) {
            int life = (tick + local * 5) & 31;
            if (life >= 3 && life < 23) {
                int dx = x + life / 3, dy = roof - plume[(local + 64) & 511] - life / 2 - heat / 3;
                ember(c, dx, dy, life < 14 ? 229 : 36);
                if (life < 16) ember(c, dx - 1, dy + 1, 187);
            }
        }
    }
}

INLINE void letters(Hud *h, BannerLine line, const Byte number[10], int count, int edge) {
    for (int pass = 0; pass < 2; ++pass) {
        for (int x = 0; x < line.width; ++x) {
            int sx, height, titleWidth = line.numberStart - 4;
            if (x < titleWidth) {
                sx = x * 155 / titleWidth; height = 38;
            } else if (x >= line.numberStart) {
                int col = x - line.numberStart;
                sx = 168 + 15 * number[count - 1 - col / line.slot] + (col % line.slot) * 15 / line.slot;
                height = 52;
            } else continue;
            for (int y = 0; y < height; ++y) {
                int row = y * 26 / height;
                if (h->atlas[(57 + row) * 320 + sx] < 0x80000000u) continue;
                int dx = line.left + x + (height - 1 - y) / 6;
                int dy = line.base - x / 11 - height + y;
                if (!pass) {
                    for (int oy = -1; oy <= 3; ++oy)
                        for (int ox = -1; ox <= 2; ++ox) h->canvas->banner[dy+oy][dx+ox] = 57;
                } else {
                    Byte ink;
                    if (x < edge) ink = row < 5 ? (x >= line.numberStart ? 255 : 183) :
                                           (row < 11 ? 184 : (row < 18 ? 186 : 188));
                    else ink = row < 10 ? 60 : 45;
                    h->canvas->banner[dy][dx] = x == edge && edge > 0 ? 255 : ink;
                }
            }
        }
    }
}

INLINE int marquee(Hud *h) {
    Byte number[10]; int count = digits(h->chain, number);
    int numberStart = (count <= 5 ? 207 : 172) + 4;
    int slot = (335 - numberStart) / count;
    if (slot > 25) slot = 25;
    int width = numberStart + slot * count, age = (int)(h->clock - h->lastFall), remaining = (int)h->windowMs - age;
    int edge = width * remaining / (int)h->windowMs;
    U32 tick = h->clock / 55u, noise = (tick + h->chain * 379u) * 2654435761u;
    int force = age < 250 ? 2 : (age < 500 ? 1 : 0);
    int shakeX = (int)(noise % (2 * force + 1)) - force;
    int shakeY = (int)((noise >> 18) % (force + 1)) - force / 2;
    BannerLine line = {width, 20 + shakeX, 80 + (width-1)/11 + shakeY, numberStart, slot};
    int rows = line.base + 16;
    if (rows > BANNER_ROWS) rows = BANNER_ROWS;
    for (int y = 0; y < BANNER_ROWS; ++y)
        for (int x = 0; x < GRID; ++x) h->canvas->banner[y][x] = 0;
    int peak = h->chain >= 10 ? 14 : (h->chain >= 5 ? 11 : 8);
    int rest = remaining < 4000 ? 2 : 4;
    int heat = rest + (peak - rest) * clamp(650 - age, 0, 650) / 650;
    Byte plume[512];
    for (int i = 0; i < 512; ++i) {
        int a = triangle(i + tick * 2 + triangle(i / 3 - tick) / 2);
        int b = triangle(i * 2 - tick + triangle(i / 5 + tick));
        plume[i] = (Byte)(5 + (2*a*a + b*b + 2*a*b) * heat / 640);
    }
    fireLine(h->canvas, line, plume, tick, heat);
    letters(h, line, number, count, edge);
    return rows;
}

INLINE int raster(Hud *h) {
    for (int y = 0; y < METER_HEIGHT; ++y)
        for (int x = 0; x < METER_WIDTH; ++x) h->canvas->meter[y][x] = 0;
    sprite(h, 0, 0, 0, 0, METER_WIDTH, METER_HEIGHT);
    U32 value = h->total;
    for (int position = 4; position >= 0; --position) {
        sprite(h, 14 + 17 * position, 15, (int)(value % 10) * 16, 86, 16, 26);
        value /= 10;
    }
    return h->chain >= 2 ? marquee(h) : 0;
}

INLINE void releaseSprite(Hud *h) {
    if (h->spriteSurface) IDirectDrawSurface_Release(h->spriteSurface);
    h->spriteSurface = 0;
    h->spriteDevice = 0;
}

/* The dashboard loader creates an indexed system-memory surface and sets
   DDCKEY_SRCBLT to palette index zero (RoadRash95 RVA 5F7F0). */
INLINE int prepareSprite(Hud *h, IDirectDraw *device) {
    if (h->spriteSurface && h->spriteDevice == device) return 1;
    releaseSprite(h);
    if (!device) return 0;
    DDSURFACEDESC desc = {0};
    desc.dwSize = sizeof(desc);
    desc.dwFlags = DDSD_CAPS | DDSD_WIDTH | DDSD_HEIGHT | DDSD_PIXELFORMAT;
    desc.dwWidth = WIDTH; desc.dwHeight = METER_HEIGHT;
    desc.ddsCaps.dwCaps = DDSCAPS_OFFSCREENPLAIN | DDSCAPS_SYSTEMMEMORY;
    desc.ddpfPixelFormat.dwSize = sizeof(DDPIXELFORMAT);
    desc.ddpfPixelFormat.dwFlags = DDPF_RGB | DDPF_PALETTEINDEXED8;
    desc.ddpfPixelFormat.dwRGBBitCount = 8;
    if (FAILED(IDirectDraw_CreateSurface(device, &desc, &h->spriteSurface, 0))) return 0;
    DDCOLORKEY key = {0, 0};
    if (FAILED(IDirectDrawSurface_SetColorKey(h->spriteSurface, DDCKEY_SRCBLT, &key))) {
        releaseSprite(h);
        return 0;
    }
    h->spriteDevice = device;
    return 1;
}

/* Cache exact straight RGB values. Reducing colors to five bits per channel
   merges distinct BOB inks such as #070707 and opaque black. */
INLINE Byte paletteIndex(ColorMap *colors, U32 r, U32 g, U32 b) {
    U32 rgb = (r << 16) | (g << 8) | b;
    U32 first = ((rgb ^ (rgb >> 11)) * 2654435761u) >> 18;
    U32 slot = first;
    for (int n = 0; n < 8; ++n) {
        slot = (first + n) & (COLOR_SLOTS - 1);
        U32 entry = colors->cache[slot];
        if (!entry) break;
        if ((entry >> 8) == rgb) return (Byte)entry;
    }
    U32 best = ~0u;
    Byte index = 1;
    for (int i = 1; i < 256; ++i) {
        const Byte *ink = colors->palette[i];
        int dr = (int)r - ink[0], dg = (int)g - ink[1], db = (int)b - ink[2];
        int rg = dr - dg, bg = db - dg;
        U32 distance = dr*dr + 4*dg*dg + db*db + rg*rg + bg*bg;
        if (distance < best) {
            best = distance; index = (Byte)i;
            if (!distance) break;
        }
    }
    if (colors->cache[slot]) slot = first;
    colors->cache[slot] = (rgb << 8) | index;
    return index;
}

INLINE Byte spriteIndex(ColorMap *colors, U32 pixel) {
    U32 alpha = pixel >> 24;
    if (alpha < 128) return 0;
    U32 r = (pixel >> 16) & 255, g = (pixel >> 8) & 255, b = pixel & 255;
    if (alpha != 255) {
        r = (r * 255 + alpha / 2) / alpha;
        g = (g * 255 + alpha / 2) / alpha;
        b = (b * 255 + alpha / 2) / alpha;
    }
    return paletteIndex(colors, r, g, b);
}

INLINE U32 halfSize(const U32 *p) {
    U32 a = p[0], b = p[1], c = p[METER_WIDTH], d = p[METER_WIDTH+1], mask = 0x00ff00ff;
    U32 rb = (a & mask) + (b & mask) + (c & mask) + (d & mask);
    U32 ag = ((a >> 8) & mask) + ((b >> 8) & mask) + ((c >> 8) & mask) + ((d >> 8) & mask);
    return (((rb + 0x20002) >> 2) & mask) | ((((ag + 0x20002) >> 2) & mask) << 8);
}

INLINE void paint(Hud *h, IDirectDrawSurface *target) {
    if (!target || !h->atlas || !h->canvas || !h->colors) return;
    int step = FIELD(Byte, h->image, 0x1a292a) ? 1 : 2;
    int viewWidth = FIELD(int, h->image, 0x1a2934), viewHeight = FIELD(int, h->image, 0x1a2938);
    /* In low detail the full-sized backing allocation can still report 640x480.
       The stock HUD draws in a 320x240 viewport at the game's origin. */
    if (step == 2 && viewWidth == 640 && viewHeight == 480) { viewWidth = 320; viewHeight = 240; }
    if (viewWidth < 300) step = 2;
    if (viewWidth < METER_WIDTH / step || viewHeight < 58 / step) return;
    if (!prepareSprite(h, FIELD(IDirectDraw *, h->image, 0x1cd4ec))) return;
    int rows = raster(h);
    int full = (rows ? WIDTH : METER_WIDTH) / step;
    int width = full < viewWidth ? full : viewWidth;
    int meterX = (width - METER_WIDTH / step) / 2;
    int dockX = meterX + (METER_WIDTH + 2) / step;
    int dockWidth = clamp(width * 9 / 40, 0, width > dockX ? width - dockX : 0);
    int height = METER_HEIGHT / step;
    int dockHeight = dockWidth * BANNER_ROWS / GRID;
    int dockY = (height - dockHeight) / 2;
    int left = FIELD(int, h->image, 0x1cd4b4) + (viewWidth - width) / 2;
    int top = FIELD(int, h->image, 0x1cd4b8) + 8 / step;

    DDSURFACEDESC lock = {0};
    lock.dwSize = sizeof(lock);
    HRESULT status = IDirectDrawSurface_Lock(h->spriteSurface, 0, &lock, DDLOCK_WAIT, 0);
    if (FAILED(status)) {
        if (status == DDERR_SURFACELOST) releaseSprite(h);
        return;
    }
    for (int y = 0; y < height; ++y) {
        Byte *row = (Byte *)lock.lpSurface + y * lock.lPitch;
        int sy = step * y;
        const Byte *fire = rows && (unsigned)(y - dockY) < (unsigned)dockHeight ?
            h->canvas->banner[((y - dockY) * 2 + 1) * BANNER_ROWS / (2 * dockHeight)] : 0;
        for (int x = 0; x < width; ++x) {
            int bx = x - dockX, sx = (x - meterX) * step;
            if (fire && (unsigned)bx < (unsigned)dockWidth)
                row[x] = fire[(2 * bx + 1) * GRID / (2 * dockWidth)];
            else if ((unsigned)sx < METER_WIDTH && sy < METER_HEIGHT) {
                const U32 *sample = &h->canvas->meter[sy][sx];
                row[x] = spriteIndex(h->colors, step == 1 ? *sample : halfSize(sample));
            }
            else row[x] = 0;
        }
    }
    if (FAILED(IDirectDrawSurface_Unlock(h->spriteSurface, 0))) { releaseSprite(h); return; }
    RECT source = {0, 0, width, height};
    status = IDirectDrawSurface_BltFast(target, left, top, h->spriteSurface, &source,
                                        DDBLTFAST_WAIT | DDBLTFAST_SRCCOLORKEY);
    if (SUCCEEDED(status)) ++h->blits;
    else if (status == DDERR_SURFACELOST) releaseSprite(h);
}

void FAST Knockdown(Hud *h, U32 event, Byte *a, Byte *b) {
    if (event == RESET || event == END_RACE) {
        silence(h);
        releaseSprite(h);
        h->active = event == RESET;
        h->generation++; h->total = h->spree = h->chain = h->clock = h->lastFall = 0;
        h->ticking = h->pending = h->queued = h->queueHead = h->sinceVoice = h->retired = 0;
        h->wreckPhase = RIDING; h->landedAt = 0;
        return;
    }
    if (!h->active || FIELD(unsigned short, h->image, 0x218059)) return;
    typedef DWORD (WINAPI *GetWallTime)(void);
    U32 wall = FIELD(GetWallTime, h->image, 0x21c414)();
    if (event == PAUSE_CLOCK || FIELD(Byte, h->image, 0x1cd4d4)) {
        if (h->ticking) advance(h, wall);
        h->ticking = 0; silence(h);
        return;
    }
    advance(h, wall);
    if (event == DRAW) {
        ++h->draws;
        if (!(h->flags & HIDE_METER)) paint(h, (IDirectDrawSurface *)a);
        pumpVoice(h);
    } else riderEvent(h, event, a, b);
}
