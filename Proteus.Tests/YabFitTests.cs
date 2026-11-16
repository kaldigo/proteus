using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Proteus.Services;
using Xunit;
using Vec3 = Proteus.Services.SecondSkinWriter.Vec3;

namespace Proteus.Tests;

public class YabFitTests
{
    [Fact]
    public void Source_check_peels_stacked_refits_and_remembers_a_disappearing_racial_path()
    {
        var checks = new FitSourceChecks();
        const string wearer = "c0201e9137_glv", fallback = "c0101e9137_glv";
        checks.Revealed(wearer, "resize", 1, 1500);
        Assert.True(checks.NeedsCheck(wearer, 1));
        Assert.False(checks.WasRevealed(wearer, "base", 1, out _));
        checks.Revealed(wearer, "base", 1, 1600);
        Assert.True(checks.WasRevealed(wearer, "resize", 1, out long until));
        Assert.Equal(1500L, until);
        Assert.True(checks.NeedsCheck(fallback, 1));
        checks.Checked(fallback, 1);
        checks.Settled();
        Assert.False(checks.NeedsCheck(wearer, 1));
        Assert.False(checks.NeedsCheck(fallback, 1));
        Assert.True(checks.NeedsCheck(wearer, 2));
    }

    [Fact]
    public void Existing_fit_only_needs_applying_when_the_collection_would_change()
    {
        var settings = new Proteus.Interop.PenumbraBridge.ModSettingsSnapshot(true, -1,
            new() { ["Fit"] = ["Medium", "Extra"] }, false, false);
        bool Selected(bool multi, string? cut = null, int priority = -1) =>
            AutoRefitDecisions.FitSelected(settings, "Fit", "Medium", cut, multi, priority);
        Assert.True(Selected(true));
        Assert.False(Selected(true, "Extra"));
        Assert.False(Selected(false));
        Assert.False(Selected(true, priority: 4));
        settings.Options["Fit"] = ["Medium"];
        Assert.True(Selected(false));
        Assert.False(AutoRefitDecisions.FitSelected(settings with { Enabled = false }, "Fit", "Medium", null, false, -1));
    }

    [Fact]
    public void Chat_summary_keeps_completed_items_after_the_batch_is_cleared()
    {
        System.Collections.Generic.List<(string Item, string Body)> batch = [("Tekko #55", "YAB+"), ("Tekko #55", "YAB+")];
        string message = AutoRefitDecisions.BatchSummary(batch, "{0}: {1}");
        batch.Clear();
        Assert.Equal("YAB+: Tekko #55", message);
    }

    [Fact]
    public void Routes_and_cache_keys_follow_sources_and_solver_settings()
    {
        var config = new Configuration { BaseFitPriority = -1, SizeFitPriority = 2 };
        YabOutput Fit(string source, byte value = 1) =>
            YabOutput.For(config, "body index", "Yet Another Body+", source, "gear/model.mdl", [value], 6)!;
        Assert.Equal(-1, Fit("Vanilla").Priority);
        Assert.Equal(YabOutput.Base, Fit("Bibo+").Layer);
        Assert.Equal(7, Fit("YAB+").Priority);
        Assert.Equal(YabOutput.Resize, Fit("YAB+").Layer);
        Assert.NotEqual(Fit("Vanilla").Stamp, Fit("Vanilla",2).Stamp);
        string improved = Fit("Vanilla").Stamp;
        config.UseYabField = false;
        Assert.NotEqual(improved, Fit("Vanilla").Stamp);
        Assert.Null(YabOutput.For(config,"","Other body","Vanilla","item.mdl",[],0));
    }

    [Fact]
    public void Generated_gear_keeps_its_storage_identity_under_an_item_label()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            const string item = "Proteus Fit c0201e0771_top";
            string key = RefitModService.NameFor(item, YabOutput.Base);
            string root = Path.Combine(temp.FullName, RefitModService.DirFor(key)!);
            Directory.CreateDirectory(Path.Combine(root, "Proteus"));
            RefitModService.WriteScaffold(root, "Lunar Envoy's Justaucorps of Healing — Yet Another Body+", "Refit");
            Assert.Null(RefitModService.Find(temp.FullName, item, YabOutput.Base));
            File.WriteAllText(Path.Combine(root, "Proteus", RefitModService.DisplayKeyFile), key);
            Assert.Equal(root, RefitModService.Find(temp.FullName, item, YabOutput.Base));
            Assert.Equal("Proteus Upscales/Base refits", new YabOutput(item, YabOutput.Base, 0, "").Folder);
        }
        finally { temp.Delete(true); }
    }

    [Theory]
    [InlineData("../outside","source.mdl")]
    [InlineData("inside","../outside.mdl")]
    [InlineData("inside","C:/outside.mdl")]
    public void Malformed_origin_cannot_leave_the_mod_root(string dir, string file)
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            YabOutput.Remember(temp.FullName, "fit.mdl", new("game.mdl",dir,file));
            Assert.Null(YabOutput.Read(temp.FullName, "fit.mdl","game.mdl"));
        }
        finally { temp.Delete(true); }
    }

    [Theory]
    [InlineData("_top",.75f)]
    [InlineData("_dwn",1f)]
    public void Envelope_moves_cloth_by_the_change_in_radius(string slot, float strength)
    {
        var source = Cylinder(.1f);
        var target = Cylinder(.13f);
        Assert.True(IdentityCorrespondence.TryBuild(source,target,slot,out var match,out var refusal),refusal);
        var sets = BodyRetarget.Sets.From(Cylinder(.104f), null);
        var delta = new Vec3[sets.NodeCount];
        YabFitField.Apply(YabTransfer.Envelope, [new(slot,match!,target)], sets, delta, new bool[delta.Length], slot);
        int i = Array.FindIndex(sets.NodeAt,p => Math.Abs(p.Y-1) < .001 && p.Z > .103);
        Assert.InRange(delta[i].Z,.028f*strength,.032f*strength);
        Assert.Equal(0,delta[i].Y);
        YabFitField.Apply(YabTransfer.Surface,[new(slot,match!,target)], sets, delta,new bool[delta.Length],slot);
        Assert.InRange(delta[i].Z,.026f,.032f);
    }

    private static ModelParts Cylinder(float radius)
    {
        const int sides=32;
        float[] xyz=new float[9*sides];
        int[] tri=new int[12*sides];
        for(int ring=0;ring<3;ring++)
            for(int j=0;j<sides;j++)
            {
                int at=ring*sides+j, next=ring*sides+(j+1)%sides;
                xyz[3*at]=radius*MathF.Sin(MathF.Tau*j/sides);
                xyz[3*at+1]=.5f+.5f*ring;
                xyz[3*at+2]=radius*MathF.Cos(MathF.Tau*j/sides);
                if(ring<2) new[]{at,next,at+sides,next,next+sides,at+sides}.CopyTo(tri,6*at);
            }
        var part=new ModelPart { Mesh=0,Submesh=0,Island=-1,Triangles=tri,Ordinals=[],Label="cloth",
            Material="/mt_cloth.mtrl",AttributeMask=0,Toggleable=true,Min=default,Max=default };
        return new ModelParts { Positions=xyz, Normals=xyz, MeshSpans=[new(0,0,3*sides)], Parts=[part],
            ShatteredSubmeshes=new System.Collections.Generic.Dictionary<string,int>(), AttributeNames=[],Min=default,Max=default };
    }
}
