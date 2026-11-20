/* Road Rash single-player grid. The constructor saves the full grid index
   before the original game reuses its byte-sized character and network IDs. */
typedef unsigned char Byte;
typedef unsigned int Word;
typedef struct { Word profile[132], stats[16]; } GridSlot;
typedef struct {
    Byte *image;
    GridSlot *slots;
    int identity;
    Word initialStats[16];
} OpponentConfig;

#define FIELD(type, base, offset) (*(type *)((Byte *)(base) + (offset)))
#define FAST __attribute__((fastcall))

Byte * FAST PrepareOpponent(OpponentConfig *config, Byte *rider, unsigned int index)
{
    if (!index || index > 10000) return 0;
    GridSlot *slot = &config->slots[index];
    Byte *previous = FIELD(Byte *, rider, 0x1d8);
    if (!previous) return 0;
    Word *profile = (Word *)previous;
    /* Vanilla points into a shared template bank. The expanded grid already
       has private profiles with twelve additional bytes of bookkeeping. */
    unsigned int relative = (unsigned int)previous - (unsigned int)config->image;
    if (relative >= 0x218800 && relative < 0x21aec8) profile = slot->profile;
    Word variation = FIELD(Word, previous, 0x18c);
    Word position = FIELD(Word, previous, 0x190);
    Word lane = FIELD(Word, previous, 0x194);
    int personality = config->identity == 15 ? 9 : config->identity;
    const Word *preset = (Word *)(config->image + 0x18f360 + personality * 0x204);
    for (int i = 0; i < 0x204 / 4; ++i) profile[i] = preset[i];
    FIELD(Word, profile, 0x18c) = variation;
    FIELD(Word, profile, 0x190) = position;
    FIELD(Word, profile, 0x194) = lane;
    FIELD(Word *, rider, 0x1d8) = profile;
    for (int i = 0; i < 16; ++i) slot->stats[i] = config->initialStats[i];
    if (personality == 9 && !FIELD(Word, config->image, 0x18e88c))
        FIELD(Word, config->image, 0x18e88c) = FIELD(Word, config->image, 0x18f2fc);
    return (Byte *)slot->stats;
}

/* The original neighborhood search uses the riders' position, road flags
   and a strict distance bound. Direct lookup keeps a crowded race linear
   per frame and lets the player be found behind another nearby opponent. */
Byte * FAST OpponentTarget(OpponentConfig *config, Byte *bike, int direction, int limit)
{
    Byte *player = FIELD(Byte *, config->image, 0x1a69ec);
    if (!player || player == bike || limit <= 0) return 0;
    Byte *rider = FIELD(Byte *, bike, 0x2fc);
    Byte *target = FIELD(Byte *, player, 0x2fc);
    if (!rider || !target || !(FIELD(Word, target, 0xc4) & 8)) return 0;
    Byte *road = FIELD(Byte *, rider, 0x2c);
    Byte *other = FIELD(Byte *, target, 0x2c);
    if (!road || !other || !(road[0x36] & other[0x36] & 3)) return 0;
    long long delta = (long long)FIELD(int, target, 0x1c) - FIELD(int, rider, 0x1c);
    if (delta <= -limit || delta >= limit) return 0;
    if (direction != 2) {
        if ((direction == 1 && delta < 0) || (direction == 0 && delta > 0)) return 0;
        int lateral = FIELD(int, target, 0x28) >> 8;
        if (lateral <= FIELD(int, other, 0xe0) || lateral >= FIELD(int, other, 0xe4)) return 0;
    }
    return player;
}
