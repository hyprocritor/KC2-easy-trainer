using System;
using System.Globalization;
using HutongGames.PlayMaker;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace KanzenChikan2.ProgressPanel;

public sealed partial class ProgressPanel
{
    private readonly MeterLock alertLock = new();
    private readonly MeterLock riskLock = new();
    private readonly MeterLock[] visualLocks = { new(), new(), new(), new(), new() };
    private readonly FsmFloat[] visualValues = new FsmFloat[Stats.MaxVisualSlots];
    private readonly string[] visualEntries = new string[Stats.MaxVisualSlots];
    private FsmInt visualCount;
    private int selectedVisual;

    private bool HasLocks() => alertLock.Enabled || riskLock.Enabled || HasVisualLocks();

    private bool HasVisualLocks()
    {
        foreach (var tracker in visualLocks)
            if (tracker.Enabled)
                return true;
        return false;
    }

    private int CurrentVisualCount() => Stats.VisualSlotCount(visualCount?.Value ?? 0);

    private void ResetVisualBindings()
    {
        Array.Clear(visualValues, 0, visualValues.Length);
        visualCount = null;
    }

    private void BindVisualVariables(FsmVariables globals)
    {
        visualCount = globals?.FindFsmInt(Stats.VisualCountName);
        for (var index = 0; index < visualValues.Length; index++)
            visualValues[index] = globals?.FindFsmFloat(Stats.VisualNamePrefix + (index + 1).ToString(CultureInfo.InvariantCulture));
    }

    private void BindMeterLocks(bool active)
    {
        alertLock.Bind(active ? floats[Alert] : null);
        riskLock.Bind(active ? floats[DetectionRisk] : null);
        for (var index = 0; index < visualLocks.Length; index++)
            visualLocks[index].Bind(active ? visualValues[index] : null);
    }

    private void RestoreMeterLocks()
    {
        if (!HasLocks())
            return;

        var active = HasActiveGauge();
        BindMeterLocks(active);
        if (!active)
        {
            nextRefresh = 0f;
            return;
        }

        alertLock.Restore();
        riskLock.Restore();
        foreach (var tracker in visualLocks)
            tracker.Restore();
    }

    private void DisableMeterLocks()
    {
        alertLock.Disable();
        riskLock.Disable();
        foreach (var tracker in visualLocks)
            tracker.Disable();
    }

    [HideFromIl2Cpp]
    private void DrawFloat(float y, int index, string label, float max, bool active, MeterLock tracker = null)
    {
        var variable = floats[index];
        var available = active && variable != null;
        var current = variable == null ? "—" : Format(variable.Value) + (index == DetectionRisk ? "%" : "");
        if (DrawStat(y, label, current, ref entries[index], available))
        {
            if (!Stats.TryReadNumber(entries[index], max, out var value))
            {
                ShowRange(max);
            }
            else
            {
                if (index == Experience)
                    gauge.SetExp(value);
                else if (tracker != null)
                    tracker.Write(variable, value);
                else
                    variable.Value = value;
                status = tracker?.Enabled == true ? LockMessage(label, value) : "已应用。";
                entries[index] = "";
            }
        }

        if (tracker != null)
            DrawLock(y, tracker, variable, label, ref entries[index], available);
    }

    [HideFromIl2Cpp]
    private void DrawLock(float y, MeterLock tracker, FsmFloat variable, string label, ref string entry, bool available)
    {
        if (!StateButton(new Rect(426, y, 75, 27), tracker.Enabled ? "解锁" : "锁定", available || tracker.Enabled, tracker.Enabled))
            return;

        if (tracker.Enabled)
        {
            tracker.Disable();
            status = $"{label}已解锁。";
            return;
        }

        if (!Stats.TryReadPercentLock(entry, variable.Value, out var target))
        {
            ShowRange(Stats.AlertLimit);
            return;
        }

        if (tracker.TryEnable(variable, target))
        {
            entry = "";
            status = LockMessage(label, target);
        }
        else
        {
            status = "锁定加载失败，详见 BepInEx 日志。";
        }
    }

    private static string LockMessage(string label, float target) =>
        $"{label}已锁定为 {Format(target)}，关闭面板后继续生效。";

    private void DrawVisualSection(bool active)
    {
        Label(new Rect(17, 527, 190, 25), "视觉感知", 15f, Accent);
        var enabledCount = active ? CurrentVisualCount() : 0;
        var availableCount = 0;
        if (active)
            foreach (var variable in visualValues)
                if (variable != null)
                    availableCount++;

        if (active)
            Label(new Rect(117, 527, 213, 25), $"当前启用 {enabledCount} 个", 12f, Muted);
        if (StateButton(new Rect(348, 526, 72, 26), "锁定全部", availableCount > 0, false))
            LockAllVisual();
        if (StateButton(new Rect(426, 526, 75, 26), "解锁全部", HasVisualLocks(), false))
        {
            foreach (var tracker in visualLocks)
                tracker.Disable();
            status = "所有视觉感知槽已解锁。";
        }

        if (availableCount == 0)
        {
            Label(new Rect(17, 572, 484, 25), "进入游戏后可编辑当前值。", 12f, Muted);
            return;
        }

        for (var index = 0; index < visualValues.Length; index++)
        {
            var variable = visualValues[index];
            var number = variable == null ? "—" : Format(variable.Value) + "%";
            var text = $"{index + 1}  {number}" + (visualLocks[index].Enabled ? " 锁" : "");
            if (StateButton(new Rect(17 + index * 98, 561, 92, 26), text, variable != null, selectedVisual == index))
                selectedVisual = index;
        }

        var slot = visualValues[selectedVisual];
        var slotLabel = $"感知 {selectedVisual + 1}";
        var slotValue = slot == null ? "—" : Format(slot.Value) + "%";
        var meterLock = visualLocks[selectedVisual];
        if (DrawStat(602, slotLabel, slotValue, ref visualEntries[selectedVisual], slot != null))
        {
            if (Stats.TryReadNumber(visualEntries[selectedVisual], Stats.AlertLimit, out var value))
            {
                meterLock.Write(slot, value);
                visualEntries[selectedVisual] = "";
                status = meterLock.Enabled ? LockMessage(slotLabel, value) : "已应用。";
            }
            else
            {
                ShowRange(Stats.AlertLimit);
            }
        }
        DrawLock(602, meterLock, slot, slotLabel, ref visualEntries[selectedVisual], slot != null);
    }

    private void LockAllVisual()
    {
        var locked = 0;
        for (var index = 0; index < visualLocks.Length; index++)
        {
            var variable = visualValues[index];
            if (variable != null && Stats.TryReadPercentLock("", variable.Value, out var value) &&
                visualLocks[index].TryEnable(variable, value))
                locked++;
        }
        status = $"已锁定 {locked} 个视觉感知槽。";
    }

    private bool StateButton(Rect rect, string text, bool available, bool highlighted)
    {
        var previousEnabled = GUI.enabled;
        var previousColor = GUI.color;
        try
        {
            GUI.enabled = previousEnabled && available;
            if (highlighted)
                GUI.color = Accent;
            return Button(rect, text);
        }
        finally
        {
            GUI.enabled = previousEnabled;
            GUI.color = previousColor;
        }
    }
}
