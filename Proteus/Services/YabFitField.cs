using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Vec3 = Proteus.Services.SecondSkinWriter.Vec3;

namespace Proteus.Services;

internal enum YabTransfer { Default, Envelope, Surface, LongCoatEnvelope }

internal static class YabFitField
{
    internal static void Apply(YabTransfer method, IReadOnlyList<BodyRetarget.SlotPair> pairs,
                               BodyRetarget.Sets sets, Vec3[] deltas, bool[] snapped, string? slot, float? strength = null)
    {
        if (method == YabTransfer.Default || slot is not ("_top" or "_dwn")) return;
        var bodies = pairs.Where(p => p.Slot is "_top" or "_dwn").ToArray();
        if (bodies.Length == 0) return;
        Func<Vector3, Vector3?> sample;
        bool[]? panels = null;
        Envelope? coatBody = null;
        SideSeams? seams = null;
        if (method is YabTransfer.Envelope or YabTransfer.LongCoatEnvelope)
        {
            var torso = new Envelope(bodies, false);
            var thigh = new Envelope(bodies, true);
            if (method == YabTransfer.LongCoatEnvelope && slot == "_top" && bodies.Any(body => body.Slot == "_dwn"))
            {
                panels = LongPanels(sets);
                if (panels != null)
                {
                    coatBody = torso;
                    seams = new SideSeams(sets, panels);
                }
            }
            sample = p =>
            {
                var trunk = torso.Sample(p);
                float mix = Ramp((.84f-p.Y)/.15f) * Ramp(Math.Abs(p.X)/.035f);
                var leg = thigh.Sample(new(Math.Abs(p.X)-.085f,p.Y,p.Z));
                leg.X *= Math.Sign(p.X);
                return Vector3.Lerp(trunk, leg, mix) * Math.Clamp(strength ?? (slot == "_top" ? .75f : 1), 0, 1.5f);
            };
        }
        else
        {
            var surface = new Surface(bodies);
            if (!surface.Usable) return;
            sample = surface.Sample;
        }

        foreach (int index in sets.ClothNodes)
        {
            if (snapped[index]) continue;
            var at = sets.NodeAt[index];
            var point = new Vector3(at.X, at.Y, at.Z);
            if (sample(point) is not { } shift) continue;
            if (panels?[index] == true)
                shift = coatBody!.PanelShift(point, shift, Math.Clamp(strength ?? .75f, 0, 1.5f), seams!);
            deltas[index] = new(shift.X, shift.Y, shift.Z);
        }
    }

    // Identify connected cloth that spans from a hip to a loose thigh panel. Isolated
    // chains and sleeves should not cause a short jacket to acquire a coat drape.
    internal static bool[]? LongPanels(BodyRetarget.Sets sets)
    {
        var nodes = sets.NodeAt;
        bool IsHem(Vec3 p) => p.Y < .69f && (Math.Abs(p.X) > .175f || p.Z < -.155f);
        if (!sets.ClothNodes.Any(i => IsHem(nodes[i]))) return null;
        var available = new bool[nodes.Length];
        foreach (int i in sets.ClothNodes)
            available[i] = nodes[i].Y <= 1.14f && (nodes[i].Y < .92f || Math.Abs(nodes[i].X) < .32f);
        bool[]? result = null;
        var component = new List<int>();
        foreach (int start in sets.ClothNodes)
        {
            if (!available[start]) continue;
            component.Clear();
            component.Add(start);
            available[start] = false;
            bool hem = false, hip = false;
            float lowX = float.PositiveInfinity, highX = float.NegativeInfinity;
            float lowZ = float.PositiveInfinity, highZ = float.NegativeInfinity;
            for (int n = 0; n < component.Count; n++)
            {
                int i = component[n];
                var p = nodes[i];
                hip |= p.Y >= .86f;
                if (IsHem(p))
                {
                    hem = true;
                    lowX = Math.Min(lowX, p.X); highX = Math.Max(highX, p.X);
                    lowZ = Math.Min(lowZ, p.Z); highZ = Math.Max(highZ, p.Z);
                }
                foreach (int j in sets.Adj[i])
                {
                    if (!available[j]) continue;
                    available[j] = false;
                    component.Add(j);
                }
            }
            if (!hip || !hem || Math.Max(highX - lowX, highZ - lowZ) < .085f) continue;
            result ??= new bool[nodes.Length];
            foreach (int i in component) result[i] = true;
        }
        return result;
    }

    // The outer edges of loose panels, sampled on the original triangle edges. A small allowance
    // for existing bow keeps the added hip clearance from rounding an already flared side seam.
    private sealed class SideSeams
    {
        private const float Bottom = .45f, Step = .025f;
        private const int Last = 18;
        private readonly float[,] widths = new float[2, Last+1];

        internal SideSeams(BodyRetarget.Sets sets, bool[] panels)
        {
            foreach (int i in sets.ClothNodes)
            {
                if (!panels[i]) continue;
                foreach (int j in sets.Adj[i])
                {
                    if (j <= i || !panels[j]) continue;
                    var a = sets.NodeAt[i]; var b = sets.NodeAt[j];
                    if (a.Y > b.Y) (a,b) = (b,a);
                    if (b.Y-a.Y < 1e-6f) continue;
                    int first = Math.Max(0, (int)MathF.Ceiling((a.Y-Bottom)/Step));
                    int end = Math.Min(Last, (int)MathF.Floor((b.Y-Bottom)/Step));
                    for (int k = first; k <= end; k++)
                    {
                        float x = float.Lerp(a.X,b.X,(Bottom+k*Step-a.Y)/(b.Y-a.Y));
                        int side = x < 0 ? 0 : 1;
                        widths[side,k] = Math.Max(widths[side,k], Math.Abs(x));
                    }
                }
            }
        }

        internal float Bow(Vector3 p)
        {
            int side = p.X < 0 ? 0 : 1;
            float h = (p.Y-Bottom)/Step;
            if (h <= 0 || h >= Last || widths[side,0] == 0 || widths[side,Last] == 0) return 0;
            int k = (int)h;
            float width = float.Lerp(widths[side,k], widths[side,k+1], h-k);
            float line = float.Lerp(widths[side,0], widths[side,Last], h/Last);
            return Math.Clamp(width-line,0,.006f) * Ramp((Math.Abs(p.X)-width+.05f)/.05f);
        }
    }

    private static Vector3 Point(ModelParts model, int i)
        => new(model.Positions[3*i], model.Positions[3*i+1], model.Positions[3*i+2]);
    private static float Ramp(float v)
    {
        v = Math.Clamp(v,0,1);
        return v*v*(3-2*v);
    }

    // Twelve nearest UV correspondences. Split-plane pruning bounds the search on densely tessellated bodies.
    private sealed class Surface
    {
        private readonly record struct Site(Vector3 Position, Vector3 Shift);
        private readonly Site[] sites;
        private static readonly IComparer<Site>[] Axes = Enumerable.Range(0,3)
            .Select(axis => (IComparer<Site>)Comparer<Site>.Create((a,b) => a.Position[axis].CompareTo(b.Position[axis]))).ToArray();
        internal bool Usable { get; }
        internal Surface(BodyRetarget.SlotPair[] pairs)
        {
            var samples = new Dictionary<Vector3,(Vector3 Sum,int Count)>();
            int valid = 0, total = 0;
            foreach (var pair in pairs)
            {
                var c = pair.Correspondence;
                for (int i = 0; i < c.Field.Count; i++)
                {
                    total++;
                    if (c.Field[i] is not { } d || !(d.LengthSquared() < .13f*.13f)) continue;
                    var p = Point(c.Source,i);
                    var old = samples.GetValueOrDefault(p);
                    samples[p] = (old.Sum+d,old.Count+1);
                    valid++;
                }
            }
            sites = samples.Select(s => new Site(s.Key,s.Value.Sum/s.Value.Count)).ToArray();
            Usable = sites.Length >= 12 && valid >= .85*total;
            if (Usable) Build(0,sites.Length,0);
        }
        private void Build(int lo,int hi,int axis)
        {
            if (hi-lo < 2) return;
            Array.Sort(sites,lo,hi-lo,Axes[axis]);
            int m = (lo+hi)/2, next = (axis+1)%3;
            Build(lo,m,next);
            Build(m+1,hi,next);
        }
        internal Vector3? Sample(Vector3 p)
        {
            Span<float> distances = stackalloc float[12];
            Span<int> indices = stackalloc int[12];
            distances.Fill(float.PositiveInfinity);
            Nearest(p,0,sites.Length,0,distances,indices);
            Vector3 sum = default;
            float weights = 0;
            for (int i=0;i<12;i++)
            {
                float d = distances[i]+.000025f, weight = 1/(d*d);
                sum += sites[indices[i]].Shift*weight;
                weights += weight;
            }
            return sum/weights;
        }
        private void Nearest(Vector3 p,int lo,int hi,int axis,Span<float> ds,Span<int> ids)
        {
            if (lo>=hi) return;
            int m=(lo+hi)/2, next=(axis+1)%3;
            var q=sites[m].Position;
            float d=Vector3.DistanceSquared(p,q);
            if (d<ds[^1])
            {
                int i=ds.Length-1;
                while (i>0 && d<ds[i-1]) { ds[i]=ds[i-1]; ids[i]=ids[i-1]; i--; }
                ds[i]=d;
                ids[i]=m;
            }
            float gap=p[axis]-q[axis];
            bool left=gap<0;
            Nearest(p,left?lo:m+1,left?m:hi,next,ds,ids);
            if (gap*gap<ds[^1]) Nearest(p,left?m+1:lo,left?hi:m,next,ds,ids);
        }
    }

    private sealed class Envelope
    {
        private const int Rows=213, Angles=144;
        private const float Bottom=.34f, Step=.005f;
        private readonly float[,] radius, shift;
        private static readonly Vector2[] Rays=Enumerable.Range(0,Angles)
            .Select(i => new Vector2(MathF.Sin(i*MathF.Tau/Angles), MathF.Cos(i*MathF.Tau/Angles))).ToArray();
        internal Envelope(BodyRetarget.SlotPair[] pairs,bool thigh)
        {
            radius=Slice(pairs.Select(p=>p.Correspondence.Source),thigh);
            var target=Slice(pairs.Select(p=>p.Target),thigh);
            var diff=new float[Rows,Angles];
            for(int i=0;i<Rows;i++)
                for(int j=0;j<Angles;j++)
                    if(radius[i,j]>.01f && target[i,j]>.01f) diff[i,j]=target[i,j]-radius[i,j];
            shift=new float[Rows,Angles];
            for(int y=-3;y<=3;y++)
                for(int x=-4;x<=4;x++)
                {
                    float weight=MathF.Exp(-(x*x+y*y)/4.5f);
                    for(int i=0;i<Rows;i++)
                        for(int j=0;j<Angles;j++)
                            shift[i,j]+=weight*diff[Math.Clamp(i+y,0,Rows-1),(j+x+Angles)%Angles];
                }
            float norm=0;
            for(int y=-3;y<=3;y++)
                for(int x=-4;x<=4;x++) norm+=MathF.Exp(-(x*x+y*y)/4.5f);
            for(int i=0;i<Rows;i++)
                for(int j=0;j<Angles;j++) shift[i,j]/=norm;
        }
        private static float Cross(Vector2 a,Vector2 b)=>a.X*b.Y-a.Y*b.X;
        private static float[,] Slice(IEnumerable<ModelParts> models,bool thigh)
        {
            var grid=new float[Rows,Angles];
            Span<Vector3> tri=stackalloc Vector3[3];
            Span<Vector2> cut=stackalloc Vector2[2];
            foreach(var model in models)
            foreach(var part in model.Parts)
            {
                if(part.Island>=0 || part.Material.Contains("piercing",StringComparison.OrdinalIgnoreCase)
                    || part.Material.Contains("pube",StringComparison.OrdinalIgnoreCase)) continue;
                for(int t=0;t+2<part.Triangles.Length;t+=3)
                {
                    for(int k=0;k<3;k++) tri[k]=Point(model,part.Triangles[t+k]);
                    if(thigh && tri[0].X+tri[1].X+tri[2].X<=0) continue;
                    float low=Math.Min(tri[0].Y,Math.Min(tri[1].Y,tri[2].Y));
                    float high=Math.Max(tri[0].Y,Math.Max(tri[1].Y,tri[2].Y));
                    for(int row=Math.Max(0,(int)MathF.Ceiling((low-Bottom)/Step));row<Rows && Bottom+row*Step<high;row++)
                    {
                        float h=Bottom+row*Step;
                        int count=0;
                        for(int k=0;k<3;k++)
                        {
                            var a=tri[k]; var b=tri[(k+1)%3];
                            if((a.Y>h)==(b.Y>h)) continue;
                            var v=Vector3.Lerp(a,b,(h-a.Y)/(b.Y-a.Y));
                            cut[count++]=new(v.X-(thigh?.085f:0),v.Z);
                        }
                        if(count!=2 || Math.Max(Math.Abs(cut[0].X),Math.Abs(cut[1].X))>=.205f) continue;
                        var edge=cut[1]-cut[0];
                        float numerator=Cross(cut[0],edge);
                        for(int j=0;j<Angles;j++)
                        {
                            float den=Cross(Rays[j],edge);
                            if(Math.Abs(den)<1e-10f) continue;
                            float u=Cross(cut[0],Rays[j])/den;
                            if(u is >=0 and <=1) grid[row,j]=Math.Max(grid[row,j],numerator/den);
                        }
                    }
                }
            }
            return grid;
        }
        private float[]? hipGrowth;

        internal Vector3 PanelShift(Vector3 p, Vector3 local, float strength, SideSeams seams)
        {
            if (p.Y >= 1.10f || strength == 0) return local;
            if (hipGrowth == null)
            {
                hipGrowth = new float[Angles];
                // A hanging panel spans both thighs. Use the change at the seat as
                // its anchor and carry it down the panel without tracing each leg.
                for (int a = 0; a < Angles; a++)
                    for (int row = (int)((.82f-Bottom)/Step); row <= (int)((.94f-Bottom)/Step); row++)
                        hipGrowth[a] = Math.Max(hipGrowth[a], Math.Clamp(shift[row,a], 0, .05f));
            }
            float radius = MathF.Sqrt(p.X*p.X + p.Z*p.Z);
            if (radius < .001f) return local;
            var direction = new Vector3(p.X/radius, 0, p.Z/radius);
            float angle = (MathF.Atan2(p.X,p.Z)/MathF.Tau+1)%1*Angles;
            int i = (int)angle % Angles;
            float growth = float.Lerp(hipGrowth[i], hipGrowth[(i+1)%Angles], angle-(int)angle);
            // Seat ease wraps around to the side seams, tailing off toward the front.
            float rear = (hipGrowth[Angles/2-6] + hipGrowth[Angles/2] + hipGrowth[Angles/2+6])/3;
            float ease = Math.Max(growth, rear * (.35f + .65f*Math.Max(0,-direction.Z)) * (1-Math.Max(0,direction.Z)));
            if (ease <= 0) return local;
            float drop = Math.Clamp((p.Y-.30f)/.60f,0,1);
            var drape = direction * (strength * drop * ease);
            // Side seams clear the hip later in the fall than the broad back panel.
            drape.X *= Ramp((p.Y-.59f)/.30f);
            drape.X += strength * rear * direction.X * .55f * Ramp((p.Y-.76f)/.15f);
            drape.X -= strength * Math.Sign(p.X) * Math.Min(ease, seams.Bow(p));
            float back = Ramp(-direction.Z);
            drape.Z -= strength * drop * Math.Min(.035f, 1.25f*rear) * back;
            float blend = Ramp((1.10f-p.Y)/.16f) * Ramp((.32f-Math.Abs(p.X))/.06f);
            return Vector3.Lerp(local, drape, blend);
        }

        internal Vector3 Sample(Vector3 p)
        {
            float angle=(MathF.Atan2(p.X,p.Z)/MathF.Tau+1)%1*Angles;
            float height=Math.Clamp((p.Y-Bottom)/Step,0,Rows-1.001f);
            int i=(int)height,j=(int)angle%Angles,k=(j+1)%Angles;
            float Read(float[,] data)=>float.Lerp(float.Lerp(data[i,j],data[i,k],angle-j),
                float.Lerp(data[i+1,j],data[i+1,k],angle-j),height-i);
            var direction=new Vector3(p.X,0,p.Z);
            float rad=direction.Length();
            float fade=Ramp((1.395f-p.Y)/.055f)*Ramp((p.Y-Bottom)/.08f)*Ramp((.24f-Math.Abs(p.X))/.055f)
                       *Ramp(1-Math.Max(rad-Read(radius),0)/.18f);
            return direction*(Read(shift)*fade/Math.Max(rad,.001f));
        }
    }
}
