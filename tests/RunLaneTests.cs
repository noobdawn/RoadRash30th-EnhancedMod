using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class RunLaneTests
{
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr at,UIntPtr size,uint type,uint protection);
    [DllImport("kernel32.dll")] static extern bool FlushInstructionCache(IntPtr process,IntPtr at,UIntPtr size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong Evaluate(IntPtr bike,IntPtr config);
    static long Alloc(int n) {return (uint)VirtualAlloc(IntPtr.Zero,(UIntPtr)n,0x3000,0x40).ToInt32();}
    static void Put(long p,long v) {Marshal.WriteInt32(new IntPtr(p),unchecked((int)v));}
    static int Get(long p) {return Marshal.ReadInt32(new IntPtr(p));}
    static int Lane(ulong v) {return unchecked((int)(v>>32));}
    static int Speed(ulong v) {return (int)(v&0xffffffff);}
    static void Check(bool ok,string name) {if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    static int passed,tick;
    static long image,config,road,actors;
    static Evaluate run;
    static long Bike(int i) {return actors+i*0x1000;}
    static long Rider(int i) {return Bike(i)+0x800;}
    static ulong Sample(int i) {Put(image+0x2036E8,tick);return run(new IntPtr(Bike(i)),new IntPtr(config));}
    static void Bounds(int left,int right) {Put(road+0xE0,left);Put(road+0xE4,right);}
    static void Reset(int count,int x) {
        tick+=1000;
        for(int i=0;i<count;i++) {Put(Rider(i)+0x28,x*256);Put(Rider(i),0);Put(Rider(i)+4,0);Put(Rider(i)+0x1C,0);}
    }
    // This model follows the actual native target with a bounded response. Native
    // road classification and chase direction are tested separately in RaceTests.
    static void Follow(int i,ulong value) {Put(Rider(i)+0x28,Get(Rider(i)+0x28)+Lane(value)/4);}
    static long State(int i) {
        long table=(uint)Get(config+32);
        for(int n=0;n<32768;n++)if((uint)Get(table+n*32)==Bike(i)&&(uint)Get(table+n*32+4)==Rider(i))return table+n*32;
        throw new Exception("missing private steering state");
    }
    static long Obstacle(int kind,int x,int z,int halfWidth) {
        long o=Alloc(0x1000);Put(o+0xC4,kind);Put(o+0xC0,7);Put(o+0x2C,road);
        Put(o+0x28,x*256);Put(o+0x1C,z);Put(o+0x174,halfWidth*256);Put(o+0x178,12);return o;
    }
    static void Ahead(int i,params long[] obstacles) {
        long last=Rider(i),end=Alloc(16);Put(last+4,0);
        foreach(long o in obstacles){Put(last,o);Put(o+4,last);last=o;}
        Put(last,end);Put(end+4,last);
    }
    static int Main()
    {
        try {
            image=Alloc(0x240000);long entry=RuntimePatch.PrepareRunAi(Process.GetCurrentProcess().Handle,image,6,0,0,0,2,delegate(string s){},out config);
            Put(config+28,1234567);long bridge=Alloc(0x1000);
            List<byte> code=new List<byte>{0x8B,0x4C,0x24,4,0x8B,0x54,0x24,8,0xE9};code.AddRange(BitConverter.GetBytes((int)(entry-bridge-13)));
            byte[] bytes=code.ToArray();Marshal.Copy(bytes,0,new IntPtr(bridge),bytes.Length);FlushInstructionCache(new IntPtr(-1),new IntPtr(bridge),(UIntPtr)bytes.Length);
            run=(Evaluate)Marshal.GetDelegateForFunctionPointer(new IntPtr(bridge),typeof(Evaluate));
            road=Alloc(0x1000);actors=Alloc(512*0x1000);Put(road+0x36,3);Bounds(-500,500);
            for(int i=0;i<512;i++) {
                Put(Bike(i)+0xC4,4);Put(Bike(i)+0x2F4,2);Put(Bike(i)+0x2FC,Rider(i));
                Put(Rider(i)+0xC4,8);Put(Rider(i)+0xC0,7);Put(Rider(i)+0x1C4,Bike(i));
                Put(Rider(i)+0x2C,road);Put(Rider(i)+0x140,road);Put(Rider(i)+0x174,12*256);Put(Rider(i)+0x178,8);
            }
            Reset(512,0);bool inside=true;int maxJump=0;
            long[] states=new long[512];int[] previous=new int[512];
            for(int t=0;t<1200;t++,tick++)for(int i=0;i<512;i++) {
                ulong v=Sample(i);Follow(i,v);int x=Get(Rider(i)+0x28);
                inside &= x>=-480*256&&x<=480*256;
                if(t==0)states[i]=State(i);
                int goal=Get(states[i]+16);if(t>0)maxJump=Math.Max(maxJump,Math.Abs(goal-previous[i]));previous[i]=goal;
            }
            int left=0,right=0;double mean=0;HashSet<int> bins=new HashSet<int>();
            for(int i=0;i<512;i++){int x=Get(Rider(i)+0x28)/256;mean+=x;if(x< -100)left++;if(x>100)right++;bins.Add((x+500)/50);}
            mean/=512;
            Check(inside&&left>120&&right>120&&bins.Count>=14&&Math.Abs(mean)<65,"512 runner pursuit model spreads across paved road; left="+left+" right="+right+" mean="+mean.ToString("F1"));
            Check(maxJump<=450,"private lane goals move smoothly at medium rate, including planning ticks");
            int[] snapshot=new int[512];for(int i=0;i<512;i++){Put(states[i]+12,tick-6);Sample(i);snapshot[i]=Get(states[i]+20);}
            Put(road+0x14C,10000);Put(road+0x158,20000); // reproduce previous erroneous right-hand attraction
            // Replan at the same simulation time so a periodic lane change cannot
            // be mistaken for terrain-field dependence.
            for(int i=0;i<512;i++)Put(states[i]+12,tick-6);
            for(int i=0;i<512;i++)Sample(i);
            bool ignored=true;for(int i=0;i<512;i++)ignored &= Math.Abs(Get(states[i]+20)-snapshot[i])<3*256;
            Check(ignored,"special terrain extents do not redirect runners to the right");
            long player=Bike(511);Put(image+0x1A69EC,player);Put(Rider(511)+0x28,450*256);
            tick+=6;Sample(0);int withoutPlayer=Get(State(0)+20);Put(Rider(511)+0x28,-450*256);Put(State(0)+12,tick-6);Sample(0);
            Check(Math.Abs(Get(State(0)+20)-withoutPlayer)<3*256,"unlinked player's lane does not attract the whole pack");Put(image+0x1A69EC,0);
            Put(Rider(0)+0x2C,0);Put(Rider(0)+0x140,0);
            tick+=500;Check(Lane(Sample(0))==0&&Speed(Sample(0))==7200,"offscreen keeps base pacing and skips road and object access");
            Put(Rider(0)+0x2C,road);Put(Rider(0)+0x140,road);Put(Rider(0)+0x28,200*256);Sample(0);
            Check(Math.Abs(Get(State(0)+16)-200*256)<=450,"re-entry resumes near actual position without a stale lateral jump");
            foreach(int center in new int[]{-900,750})foreach(int width in new int[]{30,200,1400}) {
                Bounds(center-width/2,center+width/2);Reset(32,center);
                for(int t=0;t<400;t++,tick++)for(int i=0;i<32;i++)Follow(i,Sample(i));
                bool valid=true;for(int i=0;i<32;i++){int x=Get(Rider(i)+0x28);valid &= x-12*256>(center-width/2)*256&&x+12*256<(center+width/2)*256;}
                Check(valid,"offset and narrow paved corridor contains whole runner bodies center="+center+" width="+width);
            }
            Bounds(-500,500);Reset(1,700);Sample(0);Check(Lane(Sample(0))<0,"right shoulder recovery points back toward pavement");
            Reset(1,-700);Sample(0);Check(Lane(Sample(0))>0,"left shoulder recovery points back toward pavement");
            foreach(int start in new int[]{-120,120}) {
                Bounds(-180,180);Reset(1,start);long car=Obstacle(2,start,80,45);Ahead(0,car);
                for(int t=0;t<80;t++,tick++)Sample(0);
                int target=Get(State(0)+20);Check(start<0?target>start*256:target<start*256,"static obstacle bypass chooses available interior side start="+start);
            }
            Bounds(-180,180);Reset(1,0);long wall=Obstacle(2,0,35,400);Ahead(0,wall);
            ulong blocked=Sample(0);Check(Speed(blocked)<7200&&Speed(blocked)>=1080,"blocked corridor yields speed while retaining a road-bound target");
            Put(Rider(0),0);Put(Rider(0)+4,0);tick+=6;Check(Speed(Sample(0))==7200,"pace recovers once congestion clears");
            Reset(1,0);long ghost=Obstacle(4,0,35,400);Put(ghost+0x2F4,2);Ahead(0,ghost);
            Check(Speed(Sample(0))==7200,"hidden chase motorcycles do not create a wall of obstacles");
            Reset(1,0);long peer=Obstacle(8,0,60,12);Ahead(0,peer);Sample(0);
            Check(Math.Abs(Get(State(0)+20))>=31*256,"nearby on-foot runner receives body clearance plus a margin");
            Put(peer+0x108,7200);tick+=6;
            Check(Speed(Sample(0))==7200,"same-speed runner ahead does not trigger stationary-obstacle braking");
            Put(peer+0x108,3600);tick+=6;
            Check(Speed(Sample(0))<7200&&Speed(Sample(0))>=3600,"slower runner ahead limits closing speed rather than stopping the pack");
            Put(peer+0x108,0);
            long fork=Alloc(0x1000);Put(fork+0x36,2);Put(road+0x36,1);Put(peer+0x2C,fork);Put(peer+0x174,1000*256);tick+=6;
            Check(Speed(Sample(0))==7200,"objects on a separate branch do not block this corridor");Put(road+0x36,3);
            // Traffic is intentionally risky: close the pursuit loop and verify
            // progress AND actual overlap opportunities, without promising immunity.
            int trafficContacts=0;
            for(int layout=0;layout<16;layout++)foreach(int testSpeed in new int[]{1200,6600,12000,6601}) {
                Put(config+28,1234567+layout*7919);
                int baseSpeed=testSpeed==6601?6600:testSpeed;
                Put(config+16,testSpeed==6601?3:0);
                Put(config+8,baseSpeed);Bounds(-300,300);Reset(1,0);
                for(int t=0;t<600;t++,tick++)Follow(0,Sample(0));
                int carX=Get(Rider(0)+0x28)/256;long car=Obstacle(16,carX,1800,45);Ahead(0,car);
                double x=Get(Rider(0)+0x28),z=0;bool collided=false;
                for(int t=0;t<2500&&z<1950;t++,tick++) {
                    Put(Rider(0)+0x28,(int)x);Put(Rider(0)+0x1C,(int)z);ulong v=Sample(0);
                    double angle=Math.Atan2(Lane(v)/256.0,96),step=Speed(v)/256.0;
                    x+=Speed(v)*Math.Sin(angle);z+=step*Math.Cos(angle);
                    if(Math.Abs(z-1800)<24&&Math.Abs(x/256.0-carX)<57) {
                        collided=true;
                    }
                }
                if(collided)trafficContacts++;
                Check(z>=1950,"traffic pursuit model maintains progress speed="+baseSpeed+" sprint="+(testSpeed==6601)+" layout="+layout);
            }
            Check(trafficContacts>0,"traffic remains hittable in closed pursuit model; contacts="+trafficContacts);
            Put(config+16,0);
            Put(config+8,7200);
            Reset(1,0);long cycle=Obstacle(16,0,40,40);Put(Rider(0),cycle);Put(cycle,cycle);
            Stopwatch bounded=Stopwatch.StartNew();for(int n=0;n<10000;n++){tick+=6;Sample(0);}bounded.Stop();
            Check(bounded.ElapsedMilliseconds<5000,"bounded neighbor walk terminates even for a cyclic chain; 10000 plans ms="+bounded.ElapsedMilliseconds);
            // Dense sorted pack: all actors share the road but have distributed lateral positions.
            Reset(512,0);long head=Alloc(16),tail=Alloc(16);Put(head,Rider(0));Put(tail+4,Rider(511));
            for(int i=0;i<512;i++) {Put(Rider(i),i==511?tail:Rider(i+1));Put(Rider(i)+4,i==0?head:Rider(i-1));Put(Rider(i)+0x1C,i*8);Put(Rider(i)+0x28,((i%12)-6)*24*256);Put(Rider(i)+0x108,7200);}
            bool packPace=true;
            Stopwatch load=Stopwatch.StartNew();for(int t=0;t<120;t++,tick++)for(int i=0;i<512;i++)packPace &= Speed(Sample(i))==7200;load.Stop();
            Check(load.ElapsedMilliseconds<10000,"512 dense runners, 120 simulated ticks with bounded local avoidance; ms="+load.ElapsedMilliseconds);
            Check(packPace,"dense equal-speed pack retains its pace instead of braking for each other");
            tick=1;Sample(0);Check(Math.Abs(Get(State(0)+16)-Get(Rider(0)+0x28))<=450,"new-race tick rollback clears stale steering motion");
            Put(config+24,0);Check(Lane(Sample(0))==0&&Speed(Sample(0))==7200,"turning off lateral behavior also disables local yielding");
            Put(config+24,2);Put(config+16,0);Put(config+8,7200);Bounds(-500,500);
            Put(road+0x2c,512);Put(road+0x30,512);Put(road+0xcc,0);
            long profile=Alloc(0x1000);Put(Rider(0)+0x1d8,profile);Put(profile+0x190,125*256);
            // Production intentionally mixes actor addresses into identity. Cancel
            // that variable for this single-runner comparison so ASLR cannot pick
            // a short random sequence that happens to omit a lane or preference.
            uint riderIdentity=unchecked((uint)Rider(0));
            unchecked { riderIdentity^=riderIdentity>>16;riderIdentity*=0x7feb352dU;riderIdentity^=riderIdentity>>15;riderIdentity*=0x846ca68bU;riderIdentity^=riderIdentity>>16; }
            Put(config+28,unchecked((uint)Bike(0))^riderIdentity^1234567U);
            Reset(1,0);bool nativePreference=false;HashSet<int> routes=new HashSet<int>();
            for(int t=0;t<36000;t+=60) {
                tick+=60;Put(profile+0x18c,0);Put(State(0)+12,tick-6);Sample(0);int first=Get(State(0)+20);
                routes.Add((first/256+500)/100);
                Put(profile+0x18c,2);Put(State(0)+12,tick-6);Sample(0);int last=Get(State(0)+20);
                nativePreference |= last-first==250*256;
            }
            Check(nativePreference&&routes.Count>=4,"real road lane counts and riding profile influence changing traffic-lane targets; preferred="+nativePreference+" routes="+routes.Count);
            Put(profile+0x18c,0);Reset(1,0);Sample(0);int beforeTraffic=Get(State(0)+20);
            long distantCar=Obstacle(16,0,800,400);Ahead(0,distantCar);Put(State(0)+12,tick-6);Sample(0);
            Check(Get(State(0)+20)==beforeTraffic,"distant traffic does not reserve an empty corridor ahead of the runner");
            Put(distantCar+0x1c,25);Put(State(0)+12,tick-6);
            Check(Speed(Sample(0))==7200,"close traffic does not trigger guaranteed-safe braking");
            Console.WriteLine("Run lane tests: "+passed+" passed");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
