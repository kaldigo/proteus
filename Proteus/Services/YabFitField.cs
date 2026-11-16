using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Vec3 = Proteus.Services.SecondSkinWriter.Vec3;

namespace Proteus.Services;

internal enum YabTransfer { Default, Envelope, Surface }

internal static class YabFitField
{
    internal static void Apply(YabTransfer method, IReadOnlyList<BodyRetarget.SlotPair> pairs,
                               BodyRetarget.Sets sets, Vec3[] deltas, bool[] snapped, string? slot, float? strength = null)
    {
        if (method == YabTransfer.Default || slot is not ("_top" or "_dwn")) return;
        var bodies = pairs.Where(p => p.Slot is "_top" or "_dwn").ToArray();
        if (bodies.Length == 0) return;
        Func<Vector3, Vector3?> sample;
        if (method == YabTransfer.Envelope)
        {
            var torso = new Envelope(bodies, false);
            var thigh = new Envelope(bodies, true);
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
            if (sample(new(at.X, at.Y, at.Z)) is { } shift)
                deltas[index] = new(shift.X, shift.Y, shift.Z);
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
