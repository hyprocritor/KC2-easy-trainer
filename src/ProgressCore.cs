using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
// BepInEx renames the original sound-alert controller 音による警戒度加算制御.
using SoundAlertController = global::MonoBehaviourPublicStSiStUpVoStVo1;

namespace KanzenChikan2.ProgressPanel;

[BepInPlugin(Id, "KanzenChikan2 Progress Panel", "1.3.0")]
[BepInProcess("KanzenChikan2.exe")]
public sealed class ProgressPlugin : BasePlugin
{
    internal const string Id = "local.kanzenchikan2.progresspanel";
    internal static ManualLogSource LogSource { get; private set; } = null;

    public override void Load()
    {
        LogSource = Log;
        AddComponent<ProgressPanel>();
        try
        {
            new Harmony(Id).CreateClassProcessor(typeof(ExperienceGainPatch)).Patch();
            ExperienceGainPatch.Available = true;
        }
        catch (Exception error)
        {
            Log.LogError($"Could not enable the experience multiplier: {error}");
        }

        try
        {
            new Harmony(Id).CreateClassProcessor(typeof(SoundAlertPatch)).Patch();
            SoundAlertPatch.Available = true;
        }
        catch (Exception error)
        {
            Log.LogError($"Could not enable the sound alert multiplier: {error}");
        }

        try
        {
            new Harmony(Id).CreateClassProcessor(typeof(PleasureGainPatch)).Patch();
            PleasureGainPatch.Available = true;
        }
        catch (Exception error)
        {
            Log.LogError($"Could not enable the pleasure multiplier: {error}");
        }

        Log.LogInfo("Progress panel ready. F8 opens it. All multipliers start at 1.0x.");
    }
}

internal static class Stats
{
    internal const string AlertName = "警戒度";
    internal const string DetectionRiskName = "バレやすさ";
    internal const string VisualCountName = "視覚数";
    internal const string VisualNamePrefix = "視覚";
    internal const int MaxVisualSlots = 5;
    internal const string VoiceSignalName = "発声度";
    internal const string SoundSignalName = "聴覚度";
    internal const string PleasureName = "興奮度";
    internal const string PleasureLimitName = "興奮度上限";
    internal const string EjaculationName = "射精度";
    internal const string ResistanceName = "抵抗度";
    internal const float AlertLimit = 100f;
    internal const float MeterLimit = 1000f;
    internal const string ExperienceName = "経験値";
    internal const string EarnedExperienceName = "取得経験値";
    private static readonly HashSet<string> DevelopmentExperienceNames = new(StringComparer.Ordinal)
    {
        "耳取得経験値", "口取得経験値", "胸取得経験値", "乳首取得経験値", "クリ取得経験値",
        "膣取得経験値", "子宮取得経験値", "尻取得経験値", "アナル取得経験値", "太股取得経験値"
    };
    internal const string LevelName = "淫乱度";
    internal const int NormalRate = 10;
    internal const int MinRate = 1;
    internal const int MaxRate = 200;

    internal static int RateTenths { get; private set; } = NormalRate;
    internal static int DevelopmentRateTenths { get; private set; } = NormalRate;
    internal static int SoundRateTenths { get; private set; } = NormalRate;
    internal static int PleasureRateTenths { get; private set; } = NormalRate;

    internal static void AdjustDevelopmentRate(int step) =>
        DevelopmentRateTenths = Math.Clamp(DevelopmentRateTenths + step, 0, MaxRate);

    internal static int EarnedExperienceRate(string variableName)
    {
        if (variableName == EarnedExperienceName)
            return RateTenths;

        return DevelopmentExperienceNames.Contains(variableName) ? DevelopmentRateTenths : NormalRate;
    }

    internal static void AdjustPleasureRate(int step) =>
        PleasureRateTenths = Math.Clamp(PleasureRateTenths + step, 0, MaxRate);

    internal static int VisualSlotCount(int count) => Math.Clamp(count, 0, MaxVisualSlots);
    internal static void AdjustSoundRate(int step) =>
        SoundRateTenths = Math.Clamp(SoundRateTenths + step, 0, MaxRate);
    internal static void AdjustRate(int step) =>
        RateTenths = Math.Clamp(RateTenths + step, MinRate, MaxRate);

    internal static bool TryReadNumber(string text, float max, out float value) =>
        float.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value) && float.IsFinite(value) && value >= 0f && value <= max;

    internal static float PleasureLimit(float gameLimit) =>
        float.IsFinite(gameLimit) && gameLimit > 0f ? gameLimit : MeterLimit;

    internal static bool TryReadPercentLock(string text, float current, out float target)
    {
        if (!string.IsNullOrWhiteSpace(text))
            return TryReadNumber(text, AlertLimit, out target);

        target = current;
        return float.IsFinite(target) && target >= 0 && target <= AlertLimit;
    }

    internal static float ScalePositiveGain(float before, float after, int rateTenths)
    {
        if (rateTenths == NormalRate || !float.IsFinite(before) || !float.IsFinite(after) || after <= before)
            return after;

        return ScaleDelta(before, after, rateTenths);
    }

    internal static float ScaleDelta(float before, float after, int rateTenths)
    {
        if (rateTenths == NormalRate || !float.IsFinite(before) || !float.IsFinite(after) || before == after)
            return after;

        var adjusted = (double)before + ((double)after - before) * rateTenths / NormalRate;
        return adjusted >= -float.MaxValue && adjusted <= float.MaxValue ? (float)adjusted : after;
    }
}

[HarmonyPatch(typeof(FloatAdd), "DoFloatAdd")]
internal static class ExperienceGainPatch
{
    internal static bool Available { get; set; }
    private static bool reportedGain;
    private static bool reportedDevelopmentGain;

    [HarmonyPrefix]
    private static void Capture(FloatAdd __instance, out float __state)
    {
        __state = float.NaN;
        if (Stats.RateTenths == Stats.NormalRate && Stats.DevelopmentRateTenths == Stats.NormalRate)
            return;

        var destination = __instance.floatVariable;
        var name = destination?.Name;
        if (name == null || Stats.EarnedExperienceRate(name) == Stats.NormalRate)
            return;

        var earned = FsmVariables.GlobalVariables?.FindFsmFloat(name);
        if (earned != null && earned.Pointer == destination.Pointer)
            __state = earned.Value;
    }

    [HarmonyPostfix]
    private static void Apply(FloatAdd __instance, float __state)
    {
        if (!float.IsFinite(__state))
            return;

        var destination = __instance.floatVariable;
        var name = destination.Name;
        var current = destination.Value;
        var adjusted = Stats.ScalePositiveGain(__state, current, Stats.EarnedExperienceRate(name));
        if (adjusted == current || !float.IsFinite(adjusted))
            return;

        destination.Value = adjusted;
        if (name == Stats.EarnedExperienceName)
        {
            if (!reportedGain)
            {
                reportedGain = true;
                ProgressPlugin.LogSource.LogInfo("Applied the multiplier to a positive earned-experience gain.");
            }
        }
        else if (!reportedDevelopmentGain)
        {
            reportedDevelopmentGain = true;
            ProgressPlugin.LogSource.LogInfo("Applied the multiplier to a positive development-experience gain.");
        }
    }
}


[HarmonyPatch(typeof(SmoothFloatCalculate), "CalculateResult")]
internal static class PleasureGainPatch
{
    internal static bool Available { get; set; }
    private static bool reportedGain;

    [HarmonyPostfix]
    private static void Apply(SmoothFloatCalculate __instance, float __0, ref float __result)
    {
        var rate = Stats.PleasureRateTenths;
        if (rate == Stats.NormalRate || __instance.useIntProcessing || !(__result > __0) ||
            (__instance.calculationType != SmoothFloatCalculate.CalculationType.Add &&
             __instance.calculationType != SmoothFloatCalculate.CalculationType.Subtract))
            return;

        var destination = __instance.target;
        if (destination == null || destination.Name != Stats.PleasureName)
            return;

        var global = FsmVariables.GlobalVariables?.FindFsmFloat(Stats.PleasureName);
        if (global == null || global.Pointer != destination.Pointer)
            return;

        var adjusted = Stats.ScalePositiveGain(__0, __result, rate);
        if (adjusted == __result || !float.IsFinite(adjusted))
            return;

        // The game clamps this result and drives its normal gauge animation afterward.
        __result = adjusted;
        if (!reportedGain)
        {
            reportedGain = true;
            ProgressPlugin.LogSource.LogInfo("Applied the multiplier to a positive pleasure gain.");
        }
    }
}

[HarmonyPatch(typeof(FsmFloat), nameof(FsmFloat.Value), MethodType.Getter)]
internal static class MeterLockPatch
{
    private static bool getterPatched;
    private static readonly ConcurrentDictionary<IntPtr, float> targets = new();

    internal static bool TryInstall()
    {
        try
        {
            if (!getterPatched)
            {
                new Harmony(ProgressPlugin.Id).CreateClassProcessor(typeof(MeterLockPatch)).Patch();
                getterPatched = true;
            }
            return true;
        }
        catch (Exception error)
        {
            ProgressPlugin.LogSource.LogError($"Could not install the stat lock: {error}");
            return false;
        }
    }

    internal static bool IsLocked(FsmFloat variable) => targets.ContainsKey(variable.Pointer);

    internal static void Publish(IntPtr pointer, float value) => targets[pointer] = value;

    internal static void Unpublish(IntPtr pointer) => targets.TryRemove(pointer, out _);

    [HarmonyPrefix]
    private static bool ReadLockedValue(FsmFloat __instance, ref float __result)
    {
        if (!targets.TryGetValue(__instance.Pointer, out var value))
            return true;

        __result = value;
        return false;
    }
}

internal sealed class MeterLock
{
    private FsmFloat variable;
    private IntPtr pointer;
    private float? target;

    internal bool Enabled => target.HasValue;

    internal void Bind(FsmFloat value)
    {
        var currentPointer = value?.Pointer ?? IntPtr.Zero;
        if (pointer == currentPointer)
            return;

        if (pointer != IntPtr.Zero)
            MeterLockPatch.Unpublish(pointer);
        pointer = currentPointer;
        variable = value;
        if (Enabled && pointer != IntPtr.Zero)
            MeterLockPatch.Publish(pointer, target.Value);
    }

    internal bool TryEnable(FsmFloat value, float lockedValue)
    {
        if (!MeterLockPatch.TryInstall())
            return false;

        Bind(value);
        target = lockedValue;
        Write(value, lockedValue);
        return true;
    }

    internal void Write(FsmFloat value, float desired)
    {
        value.Value = desired;
        if (!Enabled)
            return;

        Bind(value);
        target = desired;
        MeterLockPatch.Publish(pointer, desired);
    }

    internal void Disable()
    {
        target = null;
        if (pointer != IntPtr.Zero)
            MeterLockPatch.Unpublish(pointer);
    }

    internal void Restore()
    {
        if (Enabled && variable != null && variable.value != target.Value)
            variable.Value = target.Value;
    }
}

[HarmonyPatch(typeof(SoundAlertController), "Update")]
internal static class SoundAlertPatch
{
    internal static bool Available { get; set; }

    private readonly struct Snapshot
    {
        internal readonly FsmFloat Variable;
        internal readonly float Before;

        internal Snapshot(FsmFloat variable)
        {
            Variable = variable;
            Before = variable.value;
        }
    }

    [HarmonyPrefix]
    private static void Capture(SoundAlertController __instance, out Snapshot __state)
    {
        __state = default;
        if (Stats.SoundRateTenths == Stats.NormalRate ||
            __instance.field_Public_String_0 != Stats.VoiceSignalName || __instance.field_Public_String_1 != Stats.SoundSignalName ||
            __instance.field_Public_String_2 != Stats.AlertName)
            return;

        var variable = FsmVariables.GlobalVariables?.FindFsmFloat(Stats.AlertName);
        if (variable != null && !MeterLockPatch.IsLocked(variable))
            __state = new Snapshot(variable);
    }

    [HarmonyPostfix]
    private static void Apply(Snapshot __state)
    {
        var variable = __state.Variable;
        if (variable == null)
            return;

        var current = variable.value;
        var adjusted = Stats.ScaleDelta(__state.Before, current, Stats.SoundRateTenths);
        if (adjusted != current && float.IsFinite(adjusted))
            variable.Value = adjusted;
    }
}

internal static class PanelLayout
{
    internal const float Width = 518f;
    internal const float Height = 673f;
    internal const float Margin = 8f;

    internal static float Scale(float screenWidth, float screenHeight) =>
        MathF.Min(1f, MathF.Min((screenWidth - Margin * 2) / Width, (screenHeight - Margin * 2) / Height));

    internal static float ClampPosition(float value, float viewport, float size) =>
        Math.Clamp(value, Margin, MathF.Max(Margin, viewport - size - Margin));
}
