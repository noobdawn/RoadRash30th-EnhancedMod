/* Single-player race placement. All positions in one AI lookup come from the
   same snapshot, including the query rider. Ties never count as ahead. */
typedef unsigned char Byte;
typedef unsigned int Word;
typedef unsigned long long Counter;
#define FAST __attribute__((fastcall))
#define FIELD(type, base, offset) (*(type *)((Byte *)(base) + (offset)))
#define LIMIT 10000
#define HASH_SIZE 32768
#define TRACE_TICKS 300
typedef int (FAST *StockRank)(Byte *);
typedef int (__attribute__((stdcall)) *ReadCounter)(Counter *);

typedef struct {
    Word player, timedAI, loadedAI, scans, builds, indexed;
    Counter time;
} TickRow;
typedef struct {
    Byte *rider;
    int mask, position, remaining, rank;
} Sample;
typedef struct {
    Byte *image;
    Byte **roster;
    Word window, race, magic, activeRace, firstTick, collectStats;
    TickRow rows[TRACE_TICKS];
    Word lastTick;
    int valid, rosterCount, burst;
    Word builtAt, burstAt;
    int slots[HASH_SIZE], edges[5];
    Sample samples[LIMIT];
    int positions[LIMIT], distances[LIMIT];
} RankState;
typedef char CheckStateSize[sizeof(RankState) <= 0x80000 ? 1 : -1];
typedef char CheckTraceLayout[__builtin_offsetof(RankState, collectStats) == 28 &&
    __builtin_offsetof(RankState, rows) == 32 && sizeof(TickRow) == 32 ? 1 : -1];

static void sift(int *values, int root, int length) {
    int value = values[root];
    for (int child = root * 2 + 1; child < length; child = root * 2 + 1) {
        if (child + 1 < length && values[child] < values[child + 1]) ++child;
        if (value >= values[child]) break;
        values[root] = values[child];
        root = child;
    }
    values[root] = value;
}
static void sortValues(int *values, int length) {
    for (int i = length / 2; i > 0; --i) sift(values, i - 1, length);
    for (int end = length - 1; end > 0; --end) {
        int last = values[end];
        values[end] = values[0];
        values[0] = last;
        sift(values, 0, end);
    }
}
static int hashSlot(RankState *s, Byte *rider) {
    Word slot = ((Word)rider * 2654435761u) >> 17;
    while (s->slots[slot] && s->samples[s->slots[slot] - 1].rider != rider)
        slot = (slot + 1) & (HASH_SIZE - 1);
    return (int)slot;
}
static void snapshot(RankState *s, int count, Word now) {
    int next[4], used = 0;
    for (int i = 0; i < HASH_SIZE; ++i) s->slots[i] = 0;
    for (int m = 0; m <= 4; ++m) s->edges[m] = 0;
    for (int i = 0; i < count; ++i) {
        Byte *rider = s->roster[i];
        if (!rider) continue;
        Byte *road = FIELD(Byte *, rider, 0x2c);
        Sample *sample = &s->samples[used];
        sample->rider = rider;
        sample->mask = road ? road[0x36] & 3 : 0;
        sample->position = FIELD(int, rider, 0x1c);
        sample->remaining = FIELD(int, rider, 0x1f8);
        sample->rank = 0;
        ++s->edges[sample->mask + 1];
        s->slots[hashSlot(s, rider)] = ++used;
    }
    for (int m = 0; m < 4; ++m) {
        s->edges[m + 1] += s->edges[m];
        next[m] = s->edges[m];
    }
    for (int i = 0; i < used; ++i) {
        Sample *r = &s->samples[i];
        int at = next[r->mask]++;
        s->positions[at] = r->position;
        s->distances[at] = r->remaining;
    }
    for (int m = 0; m < 4; ++m) {
        int begin = s->edges[m], length = s->edges[m + 1] - begin;
        sortValues(s->positions + begin, length);
        sortValues(s->distances + begin, length);
    }
    s->builtAt = now;
    s->valid = 1;
    s->burst = 0;
}
static int snapshotRank(RankState *s, Byte *rider) {
    int id = s->slots[hashSlot(s, rider)];
    if (!id) return 0;
    Sample *r = &s->samples[id - 1];
    if (r->rank) return r->rank;
    int rank = 1;
    for (int m = 0; m < 4; ++m) {
        int shared = m & r->mask;
        const int *values = shared ? s->positions : s->distances;
        int key = shared ? r->position : r->remaining;
        int lo = s->edges[m], hi = s->edges[m + 1];
        while (lo < hi) {
            int mid = lo + (hi - lo) / 2;
            if (values[mid] < key || (shared && values[mid] == key)) lo = mid + 1;
            else hi = mid;
        }
        rank += shared ? s->edges[m + 1] - lo : lo - s->edges[m];
    }
    return r->rank = rank;
}
static void startRace(RankState *s, Word now) {
    s->activeRace = 0;
    if (s->collectStats)
        for (int i = 0; i < TRACE_TICKS; ++i) s->rows[i] = (TickRow){0};
    s->firstTick = now;
    s->valid = 0;
    s->burst = 0;
    s->activeRace = s->race;
}
int FAST RankBatch(Byte *rider, RankState *s) {
    Byte *game = s->image;
    StockRank stock = (StockRank)(game + 0x16940);
    if (FIELD(unsigned short, game, 0x218059)) return stock(rider);
    Word now = FIELD(Word, game, 0x2036e8);
    if (now < s->lastTick && s->activeRace == s->race) ++s->race;
    if (s->race != s->activeRace) startRace(s, now);
    s->lastTick = now;
    Word elapsed = now - s->firstTick;
    TickRow *row = s->collectStats && elapsed < TRACE_TICKS ? &s->rows[elapsed] : (TickRow *)0;
    int player = FIELD(Byte *, rider, 0x1c4) == FIELD(Byte *, game, 0x1a69ec);
    ReadCounter clock = row ? FIELD(ReadCounter, game, 0x21c3dc) : (ReadCounter)0;
    Counter before = 0, after;
    if (row) {
        if (clock) clock(&before);
        if (player) ++row->player;
        else if (__builtin_return_address(0) == game + 0x1604d) ++row->loadedAI;
        else ++row->timedAI;
    }
    int count = FIELD(int, game, 0x18f1d8) - 1, rank = 0;
    if (count != s->rosterCount) {
        s->valid = s->burst = 0;
        s->rosterCount = count;
    }
    if (!player && s->window && count >= 128 && count <= LIMIT &&
        FIELD(int, rider, 0x1f8) > 0 && !(rider[0xc0] & 0x10)) {
        if (s->valid && now - s->builtAt >= s->window) s->valid = 0;
        if (!s->valid) {
            if (!s->burst || now - s->burstAt >= s->window) {
                s->burstAt = now;
                s->burst = 0;
            }
            /* Sparse queries cost less as a direct scan. Only dense windows sort. */
            if (++s->burst >= 16) {
                snapshot(s, count, now);
                if (row) ++row->builds;
            }
        }
        if (s->valid) rank = snapshotRank(s, rider);
    }
    if (rank) {
        FIELD(int, rider, 0x1fc) = rank;
        FIELD(Word, rider, 0x1f4) = FIELD(Word, rider, 0x10) + 60;
        if (row) ++row->indexed;
    } else {
        rank = stock(rider);
        if (row) ++row->scans;
    }
    if (row && clock && before && clock(&after)) row->time += after - before;
    return rank;
}
