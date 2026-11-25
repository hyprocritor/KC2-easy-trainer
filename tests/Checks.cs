using System;
using System.IO;
using KanzenChikan2.ProgressPanel;

try
{
    var checks = 0;
    void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        checks++;
        Console.WriteLine($"OK {description}");
    }

    Check(Stats.RateTenths == 10, "new session starts at 1.0x");
    Check(Stats.ScalePositiveGain(5f, 7f, 10) == 7f, "1.0x leaves a positive gain intact");
    Stats.AdjustRate(1);
    Check(Stats.RateTenths == 11, "UI increments by one tenth");
    Check(Stats.ScalePositiveGain(5f, 7f, Stats.RateTenths) == 7.2f, "1.1x scales only the gain");
    Stats.AdjustRate(-1);
    Stats.AdjustRate(-1);
    Check(Stats.RateTenths == 9, "UI decrements by one tenth");
    Check(Stats.ScalePositiveGain(5f, 7f, Stats.RateTenths) == 6.8f, "0.9x scales only the gain");
    Check(Stats.ScalePositiveGain(5f, 2f, 11) == 2f, "deductions stay unchanged");
    Check(Stats.ScalePositiveGain(5f, 5f, 11) == 5f, "zero gains stay unchanged");
    Check(Stats.ScalePositiveGain(0f, float.MaxValue, 11) == float.MaxValue, "an overflowing adjustment leaves the original finite result");
    Check(Stats.TryReadNumber("12.5", 100f, out var number) && number == 12.5f, "decimal alert values parse");
    Check(Stats.TryReadNumber("0", 100f, out _) && Stats.TryReadNumber("100", 100f, out _), "alert endpoints parse");
    Check(!Stats.TryReadNumber("NaN", 100f, out _) && !Stats.TryReadNumber("100.01", 100f, out _)
        && !Stats.TryReadNumber("-1", 100f, out _), "invalid and out-of-range edit values are rejected");
    for (var i = 0; i < 250; i++)
        Stats.AdjustRate(1);
    Check(Stats.RateTenths == 200, "upper multiplier limit");
    for (var i = 0; i < 250; i++)
        Stats.AdjustRate(-1);
    Check(Stats.RateTenths == 1, "lower multiplier limit");
    Check(Stats.TryReadPercentLock("", 22.5f, out var locked) && locked == 22.5f, "lock defaults to the exact current alert value");
    Check(Stats.TryReadPercentLock("17.5", 22.5f, out locked) && locked == 17.5f, "lock accepts an explicit target");
    Check(!Stats.TryReadPercentLock("101", 22.5f, out _), "lock rejects values above 100");
    Check(!Stats.TryReadPercentLock("", float.NaN, out _), "lock rejects an invalid current value");
    Check(Stats.PleasureLimit(725f) == 725f, "pleasure uses the live game cap");
    Check(Stats.PleasureLimit(0f) == 1000f, "pleasure uses the default cap until the game initializes it");
    Check(Stats.TryReadNumber("999.5", Stats.MeterLimit, out _), "the new meters accept decimal values");
    Check(!Stats.TryReadNumber("1000.01", Stats.MeterLimit, out _), "the new meters reject out-of-range values");
    Check(PanelLayout.Scale(1920f, 1080f) == 1f, "desktop window has normal scale");
    Check(PanelLayout.Scale(320f, 240f) > 0f && PanelLayout.Scale(320f, 240f) < 1f, "panel scales to fit a small window");
    Check(PanelLayout.ClampPosition(-120f, 1920f, PanelLayout.Width) == 8f, "dragging left keeps the panel visible");
    Check(PanelLayout.ClampPosition(2000f, 1920f, PanelLayout.Width) == 1394f, "dragging right keeps the panel visible");
    Check(PanelLayout.ClampPosition(1200f, 1080f, PanelLayout.Height) == 399f, "dragging down keeps the panel visible");
    Check(PanelLayout.ClampPosition(210f, 1920f, PanelLayout.Width) == 210f, "dragging moves the panel within the viewport");
    Check(Stats.SoundRateTenths == 10, "sound alert starts at 1.0x");
    Check(Stats.ScaleDelta(25f, 28f, 10) == 28f, "1.0x leaves sound-driven alert unchanged");
    Check(Stats.ScaleDelta(25f, 28f, 0) == 25f && Stats.ScaleDelta(25f, 22f, 0) == 25f, "0.0x removes sound-driven increases and decreases");
    Check(Stats.ScaleDelta(25f, 28f, 5) == 26.5f && Stats.ScaleDelta(25f, 22f, 5) == 23.5f, "0.5x scales both directions");
    for (var i = 0; i < 20; i++)
        Stats.AdjustSoundRate(-1);
    Check(Stats.SoundRateTenths == 0, "sound-specific rate can reach 0.0x");
    Stats.AdjustSoundRate(1);
    Check(Stats.SoundRateTenths == 1, "sound-specific rate changes by 0.1x");
    var atlas = GlyphAtlasData.Read(typeof(Stats).Assembly);
    Check(atlas.Glyphs.Count >= 180, "embedded atlas contains Chinese and numeric glyphs");
    var uiDirectory = Path.Combine(AppContext.BaseDirectory, "ui");
    var ui = File.ReadAllText(Path.Combine(uiDirectory, "ProgressPanel.cs"))
        + File.ReadAllText(Path.Combine(uiDirectory, "ProgressPanel.Vision.cs"));
    foreach (var character in ui)
    {
        if (character > 126 && !char.IsWhiteSpace(character) && !atlas.Glyphs.ContainsKey(character))
            throw new InvalidOperationException($"Missing atlas glyph U+{(int)character:X4}.");
    }
    Check(true, "embedded atlas covers every non-ASCII character in the panel source");

    int Ink(char character)
    {
        var columns = atlas.Width / atlas.CellWidth;
        var slot = atlas.Glyphs[character].Slot;
        var bottom = atlas.Height - (slot / columns + 1) * atlas.CellHeight;
        var left = (slot % columns) * atlas.CellWidth;
        var coverage = 0;
        for (var y = bottom; y < bottom + atlas.CellHeight; y++)
            for (var x = left; x < left + atlas.CellWidth; x++)
                if (atlas.Pixels[(y * atlas.Width + x) * 4 + 3] != 0)
                    coverage++;
        return coverage;
    }
    Check(Ink('警') > 80 && Ink('验') > 80 && Ink('锁') > 80, "Chinese glyphs contain visible pixels in the Unity texture layout");
    Check(Ink(' ') == 0, "space stays transparent");
    Check(Ink('0') > 20 && Ink('×') > 20 && Ink('。') > 5, "numeric and punctuation glyphs are visible");
    Check(Stats.PleasureRateTenths == 10, "pleasure gain starts at 1.0x");
    Check(Stats.ScalePositiveGain(40f, 50f, 10) == 50f, "1.0x leaves pleasure gains intact");
    for (var i = 0; i < 5; i++)
        Stats.AdjustPleasureRate(1);
    Check(Stats.PleasureRateTenths == 15 && Stats.ScalePositiveGain(40f, 50f, Stats.PleasureRateTenths) == 55f, "pleasure scales the positive delta in tenth increments");
    Check(Stats.ScalePositiveGain(40f, 37f, 15) == 37f, "a pleasure reduction retains its normal size");
    for (var i = 0; i < 20; i++)
        Stats.AdjustPleasureRate(-1);
    Check(Stats.PleasureRateTenths == 0 && Stats.ScalePositiveGain(40f, 50f, 0) == 40f, "0.0x holds current pleasure during a positive gain");
    Check(Stats.VisualSlotCount(3) == 3 && Stats.VisualSlotCount(4) == 4, "the panel supports three or four active meters");
    Check(Stats.VisualSlotCount(5) == 5 && Stats.VisualSlotCount(9) == 5 && Stats.VisualSlotCount(-1) == 0, "active visual slots stay within the five game variables");
    Check(Stats.TryReadPercentLock("12.2", 0f, out var visual) && visual == 12.2f, "percent locks accept the exact visual value");
    Check(!Stats.TryReadPercentLock("100.01", 0f, out _), "visual and alert lock targets reject out-of-range percentages");
    Check(Stats.DevelopmentRateTenths == 10, "development experience starts at 1.0x");
    for (var i = 0; i < 5; i++)
        Stats.AdjustDevelopmentRate(1);
    var developmentNames = new[]
    {
        "耳取得経験値", "口取得経験値", "胸取得経験値", "乳首取得経験値", "クリ取得経験値",
        "膣取得経験値", "子宮取得経験値", "尻取得経験値", "アナル取得経験値", "太股取得経験値"
    };
    foreach (var name in developmentNames)
        if (Stats.EarnedExperienceRate(name) != 15)
            throw new InvalidOperationException($"Missing development route: {name}.");
    Check(true, "all ten confirmed game accumulators use the development rate");
    Check(Stats.ScalePositiveGain(12f, 16f, Stats.EarnedExperienceRate("耳取得経験値")) == 18f, "development scales only newly gained experience");
    Check(Stats.ScalePositiveGain(12f, 8f, Stats.EarnedExperienceRate("耳取得経験値")) == 8f, "development loss follows the game amount");
    Check(Stats.EarnedExperienceRate(Stats.EarnedExperienceName) == Stats.RateTenths && Stats.EarnedExperienceRate(Stats.EarnedExperienceName) != 15, "the total experience rate is independent");
    Check(Stats.EarnedExperienceRate("耳の開発度") == 10 && Stats.EarnedExperienceRate("未確認取得経験値") == 10 && Stats.EarnedExperienceRate(null) == 10, "raw development values and unrelated variables follow the game");
    for (var i = 0; i < 20; i++)
        Stats.AdjustDevelopmentRate(-1);
    Check(Stats.DevelopmentRateTenths == 0 && Stats.ScalePositiveGain(12f, 16f, Stats.DevelopmentRateTenths) == 12f, "0.0x disables new development experience");
    Console.WriteLine($"{checks} checks passed.");
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    Environment.ExitCode = 1;
}
