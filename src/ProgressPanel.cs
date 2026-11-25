using System;
using System.Globalization;
using System.Runtime.InteropServices;
using HutongGames.PlayMaker;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace KanzenChikan2.ProgressPanel;

public sealed partial class ProgressPanel : MonoBehaviour
{
    private const int OpenKey = 0x77;
    private const int DragControlHint = 0x4B433250;
    private const int Alert = 0;
    private const int Pleasure = 1;
    private const int Ejaculation = 2;
    private const int Resistance = 3;
    private const int Experience = 4;
    private const int DetectionRisk = 5;
    private const int Level = 6;

    private static readonly Color Backdrop = new(0.035f, 0.052f, 0.079f, 0.97f);
    private static readonly Color Border = new(0.22f, 0.33f, 0.43f);
    private static readonly Color Accent = new(0.42f, 0.86f, 0.81f);
    private static readonly Color Muted = new(0.69f, 0.76f, 0.82f);

    private readonly string[] entries = new string[Level + 1];
    private readonly FsmFloat[] floats = new FsmFloat[Level];
    private LewdnessLevelGauge gauge;
    private FsmFloat pleasureLimit;
    private FsmFloat voiceSignal;
    private FsmFloat soundSignal;
    private FsmInt level;
    private GUIStyle inputStyle;
    private GUIStyle buttonStyle;
    private GlyphAtlas textAtlas;
    private string status = "";
    private bool isOpen;
    private bool wasDown;
    private bool stylesInitialized;
    private bool oldCursorVisible;
    private CursorLockMode oldCursorLock;
    private float nextRefresh;
    private int maxLevel;
    private float maxExperience;
    private bool positioned;
    private Vector2 position;
    private Vector2 dragOffset;
    private int activeDragId;
    private bool releaseDrag;

    public ProgressPanel(IntPtr pointer) : base(pointer) { }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern short GetAsyncKeyState(int key);

    public void Update()
    {
        var down = (GetAsyncKeyState(OpenKey) & 0x8000) != 0;
        if (down && !wasDown && Application.isFocused)
            Toggle();
        wasDown = down;

        if ((!isOpen && !HasLocks()) || Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + 0.25f;
        RefreshBindings();
    }

    public void LateUpdate() => RestoreMeterLocks();

    public void OnApplicationFocus(bool focused)
    {
        if (!focused)
            releaseDrag = true;
    }

    public void OnDestroy()
    {
        DisableMeterLocks();
        textAtlas?.Destroy();
    }

    private void Toggle()
    {
        isOpen = !isOpen;
        status = "";
        if (isOpen)
        {
            oldCursorVisible = Cursor.visible;
            oldCursorLock = Cursor.lockState;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            nextRefresh = 0f;
            Array.Fill(entries, "");
            Array.Fill(visualEntries, "");
        }
        else
        {
            if (activeDragId != 0)
                releaseDrag = true;
            Cursor.lockState = oldCursorLock;
            Cursor.visible = oldCursorVisible;
        }
    }

    private bool HasActiveGauge() => gauge != null && gauge.isActiveAndEnabled;

    private void RefreshBindings()
    {
        if (!HasActiveGauge())
        {
            var found = UnityEngine.Object.FindFirstObjectByType(Il2CppType.Of<LewdnessLevelGauge>());
            gauge = found == null ? null : found.TryCast<LewdnessLevelGauge>();
        }

        if (!HasActiveGauge() || gauge.expVariableName != Stats.ExperienceName ||
            gauge.levelVariableName != Stats.LevelName || gauge.levelRequirements == null)
        {
            Array.Clear(floats, 0, floats.Length);
            level = null;
            pleasureLimit = null;
            voiceSignal = null;
            soundSignal = null;
            maxLevel = 0;
            maxExperience = 0;
            ResetVisualBindings();
            BindMeterLocks(false);
            return;
        }

        var globals = FsmVariables.GlobalVariables;
        floats[Alert] = globals?.FindFsmFloat(Stats.AlertName);
        floats[DetectionRisk] = globals?.FindFsmFloat(Stats.DetectionRiskName);
        BindVisualVariables(globals);
        floats[Pleasure] = globals?.FindFsmFloat(Stats.PleasureName);
        floats[Ejaculation] = globals?.FindFsmFloat(Stats.EjaculationName);
        floats[Resistance] = globals?.FindFsmFloat(Stats.ResistanceName);
        floats[Experience] = globals?.FindFsmFloat(Stats.ExperienceName);
        pleasureLimit = globals?.FindFsmFloat(Stats.PleasureLimitName);
        voiceSignal = globals?.FindFsmFloat(Stats.VoiceSignalName);
        soundSignal = globals?.FindFsmFloat(Stats.SoundSignalName);
        level = globals?.FindFsmInt(Stats.LevelName);
        maxLevel = gauge.GetMaxLevel();
        maxExperience = maxLevel > 0 ? gauge.GetRequiredExp(maxLevel - 1) : 0f;
        BindMeterLocks(true);
    }

    public void OnGUI()
    {
        if (releaseDrag)
        {
            if (activeDragId != 0 && GUIUtility.hotControl == activeDragId)
                GUIUtility.hotControl = 0;
            activeDragId = 0;
            releaseDrag = false;
        }

        if (!isOpen)
            return;

        var scale = PanelLayout.Scale(Screen.width, Screen.height);
        if (scale <= 0f)
            return;

        InitializeStyles();
        var previousColor = GUI.color;
        var previousMatrix = GUI.matrix;
        try
        {
            var width = PanelLayout.Width * scale;
            var height = PanelLayout.Height * scale;
            if (!positioned)
            {
                position = new Vector2((Screen.width - width) / 2f, (Screen.height - height) / 2f);
                positioned = true;
            }

            GUI.matrix = Matrix4x4.identity;
            HandleDrag(scale);
            position.x = PanelLayout.ClampPosition(position.x, Screen.width, width);
            position.y = PanelLayout.ClampPosition(position.y, Screen.height, height);
            GUI.matrix = Matrix4x4.TRS(new Vector3(position.x, position.y), Quaternion.identity, new Vector3(scale, scale, 1f));
            DrawPanel();
        }
        finally
        {
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }
    }

    private void HandleDrag(float scale)
    {
        var mouse = Event.current;
        var controlId = GUIUtility.GetControlID(DragControlHint, FocusType.Passive);
        var title = new Rect(position.x + 7f * scale, position.y + 6f * scale, 416f * scale, 37f * scale);
        switch (mouse.GetTypeForControl(controlId))
        {
            case EventType.MouseDown when mouse.button == 0 && GUIUtility.hotControl == 0 && title.Contains(mouse.mousePosition):
                dragOffset = mouse.mousePosition - position;
                GUIUtility.hotControl = activeDragId = controlId;
                mouse.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == controlId:
                position = mouse.mousePosition - dragOffset;
                mouse.Use();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == controlId:
                GUIUtility.hotControl = activeDragId = 0;
                mouse.Use();
                break;
        }
    }

    private void InitializeStyles()
    {
        if (stylesInitialized)
            return;

        inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = 15 };
        buttonStyle = new GUIStyle(GUI.skin.button);
        try
        {
            textAtlas = GlyphAtlas.Create();
            ProgressPlugin.LogSource.LogInfo("Simplified Chinese loaded from the embedded glyph atlas.");
        }
        catch (Exception error)
        {
            ProgressPlugin.LogSource.LogError($"Could not load the embedded Chinese glyph atlas: {error}");
        }
        stylesInitialized = true;
    }

    private void DrawPanel()
    {
        Fill(new Rect(0, 0, PanelLayout.Width, PanelLayout.Height), Border);
        Fill(new Rect(1, 1, PanelLayout.Width - 2, PanelLayout.Height - 2), Backdrop);
        Fill(new Rect(1, 1, 4, 46), Accent);
        Label(new Rect(17, 11, 270, 30), "进度修改", 19f, Accent);
        Label(new Rect(297, 14, 117, 25), "拖动标题栏移动", 12f, Muted);
        if (Button(new Rect(434, 13, 67, 25), "关闭 F8"))
        {
            Toggle();
            return;
        }

        Fill(new Rect(17, 48, 484, 1), Border);
        Label(new Rect(17, 54, 150, 23), "项目", 12f, Muted);
        Label(new Rect(154, 54, 86, 23), "当前值", 12f, Muted);
        Label(new Rect(246, 54, 92, 23), "设置值", 12f, Muted);

        var active = HasActiveGauge();
        var pleasureMax = Stats.PleasureLimit(pleasureLimit?.Value ?? 0f);
        DrawFloat(80, Alert, "警戒值", Stats.AlertLimit, active, alertLock);
        DrawFloat(116, Pleasure, "快感", pleasureMax, active);
        DrawFloat(152, Ejaculation, "射精度", Stats.MeterLimit, active);
        DrawFloat(188, Resistance, "抵抗值", Stats.MeterLimit, active);
        DrawFloat(224, Experience, "成长经验", maxExperience, active && maxExperience > 0f);

        var levelText = level == null ? "—" : $"{level.Value} / {maxLevel}";
        if (DrawStat(260, "淫乱度", levelText, ref entries[Level], active && level != null && floats[Experience] != null && maxLevel > 0))
            ApplyLevel();

        DrawFloat(296, DetectionRisk, "基础发现风险", Stats.AlertLimit, active, riskLock);

        Fill(new Rect(17, 336, 484, 1), Border);
        var rateStep = DrawRate(345, "经验倍率", Stats.RateTenths, ExperienceGainPatch.Available && GUI.enabled);
        if (rateStep != 0)
            Stats.AdjustRate(rateStep);
        var developmentStep = DrawRate(380, "开发度倍率", Stats.DevelopmentRateTenths, ExperienceGainPatch.Available && GUI.enabled);
        if (developmentStep != 0)
            Stats.AdjustDevelopmentRate(developmentStep);
        var pleasureStep = DrawRate(415, "快感倍率", Stats.PleasureRateTenths, PleasureGainPatch.Available && GUI.enabled);
        if (pleasureStep != 0)
            Stats.AdjustPleasureRate(pleasureStep);
        var soundStep = DrawRate(450, "声音警戒倍率", Stats.SoundRateTenths, SoundAlertPatch.Available && GUI.enabled);
        if (soundStep != 0)
            Stats.AdjustSoundRate(soundStep);

        var voice = voiceSignal == null ? "—" : Format(voiceSignal.Value);
        var sound = soundSignal == null ? "—" : Format(soundSignal.Value);
        Label(new Rect(17, 483, 484, 25),
            $"发声度 {voice}     听觉度 {sound}     0.0× 关闭声音对警戒值的影响", 12f, Muted);

        Fill(new Rect(17, 516, 484, 1), Border);
        DrawVisualSection(active);

        var hint = !active
            ? "进入游戏后可编辑当前值。"
            : "淫乱度由累计经验决定。修改等级也会调整经验。";
        if (!ExperienceGainPatch.Available)
            hint = "经验倍率未能加载。";
        if (!SoundAlertPatch.Available)
            hint = "声音警戒倍率未能加载。";
        if (!PleasureGainPatch.Available)
            hint = "快感倍率未能加载。";
        Label(new Rect(17, 637, 484, 25), status.Length == 0 ? hint : status, 12f, Muted);

        var currentEvent = Event.current;
        if (currentEvent.isMouse && new Rect(0, 0, PanelLayout.Width, PanelLayout.Height).Contains(currentEvent.mousePosition))
            currentEvent.Use();
    }

    private void ApplyLevel()
    {
        if (!int.TryParse(entries[Level], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value < 0 || value > maxLevel)
        {
            ShowRange(maxLevel);
            return;
        }

        gauge.SetExp(value == 0 ? 0f : gauge.GetRequiredExp(value - 1));
        entries[Level] = "";
        status = "已设定等级及对应累计经验。";
    }

    [HideFromIl2Cpp]
    private bool DrawStat(float y, string label, string current, ref string entry, bool available)
    {
        Label(new Rect(17, y, 129, 27), label, 15f, Color.white);
        Label(new Rect(154, y, 90, 27), current, 12f, Muted);
        var enabled = GUI.enabled;
        try
        {
            GUI.enabled = enabled && available;
            entry = GUI.TextField(new Rect(246, y, 92, 27), entry, 16, inputStyle);
            return Button(new Rect(348, y, 67, 27), "应用");
        }
        finally
        {
            GUI.enabled = enabled;
        }
    }

    private int DrawRate(float y, string label, int rateTenths, bool available)
    {
        Label(new Rect(17, y, 200, 27), label, 15f, Color.white);
        var enabled = GUI.enabled;
        try
        {
            GUI.enabled = available;
            var lower = Button(new Rect(246, y, 58, 27), "-0.1");
            Label(new Rect(318, y, 56, 27), (rateTenths / 10m).ToString("0.0", CultureInfo.InvariantCulture) + "×", 15f, Color.white);
            var raise = Button(new Rect(379, y, 58, 27), "+0.1");
            return lower ? -1 : raise ? 1 : 0;
        }
        finally
        {
            GUI.enabled = enabled;
        }
    }

    private void ShowRange(float max)
    {
        var limit = Format(max);
        status = $"请输入 0 至 {limit} 范围内的数值。";
    }

    private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private void Label(Rect rect, string text, float size, Color color)
    {
        if (textAtlas != null)
            textAtlas.Draw(rect, text, size, color, centered: false);
        else
            GUI.Label(rect, text);
    }

    private bool Button(Rect rect, string text)
    {
        if (textAtlas == null)
            return GUI.Button(rect, text, buttonStyle);

        var clicked = GUI.Button(rect, "", buttonStyle);
        var tint = GUI.enabled ? Color.white : Muted;
        textAtlas.Draw(rect, text, 13f, tint, centered: true);
        return clicked;
    }

    private static void Fill(Rect rect, Color color)
    {
        var saved = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = saved;
    }
}
