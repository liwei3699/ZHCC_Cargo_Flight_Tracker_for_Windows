namespace ZhccCargoBoard.Desktop.Services;

/// <summary>
/// 机型代码 → 大类 / 短标签。
/// 源数据用的是 ICAO 风格（B738、A320、B744…），这里按航司习惯显示成 738 / 320 / 744，
/// 大类则归成「波音737」「空客A320」这种，方便两级筛选。
/// </summary>
public static class AircraftTypes
{
    private static readonly string[] FamilyOrder =
    {
        "波音737", "波音747", "波音757", "波音767", "波音777", "波音787",
        "空客A320", "空客A330", "空客A340", "空客A350", "空客A380",
        "中国商飞ARJ21", "中国商飞C919", "巴航工业E系列", "庞巴迪CRJ",
        "图波列夫Tu-204", "伊尔-76", "其他"
    };

    public static string FamilyOf(string? craftType)
    {
        var t = (craftType ?? "").Trim().ToUpperInvariant();
        if (t.Length < 3) return "其他";

        if (t.StartsWith("B73")) return "波音737";
        if (t.StartsWith("B74")) return "波音747";
        if (t.StartsWith("B75")) return "波音757";
        if (t.StartsWith("B76")) return "波音767";
        if (t.StartsWith("B77")) return "波音777";
        if (t.StartsWith("B78")) return "波音787";

        if (t.StartsWith("A32")) return "空客A320";
        if (t.StartsWith("A33")) return "空客A330";
        if (t.StartsWith("A34")) return "空客A340";
        if (t.StartsWith("A35")) return "空客A350";
        if (t.StartsWith("A38")) return "空客A380";

        if (t.StartsWith("ARJ")) return "中国商飞ARJ21";
        if (t.StartsWith("C919")) return "中国商飞C919";
        if (t.StartsWith("E1") || t.StartsWith("E2")) return "巴航工业E系列";
        if (t.StartsWith("CRJ")) return "庞巴迪CRJ";
        if (t.StartsWith("TU2")) return "图波列夫Tu-204";
        if (t.StartsWith("IL7") || t.StartsWith("IL8")) return "伊尔-76";

        return "其他";
    }

    /// <summary>B738 → 738，A320NEO → 320NEO；其它（E190/CRJ900/ARJ217…）原样返回。</summary>
    public static string LabelOf(string? craftType)
    {
        var t = (craftType ?? "").Trim().ToUpperInvariant();
        if (t.Length >= 4 && (t[0] == 'B' || t[0] == 'A') && char.IsDigit(t[1]))
        {
            return t[1..];
        }
        return t;
    }

    public static int FamilyRank(string family)
    {
        var index = Array.IndexOf(FamilyOrder, family);
        return index >= 0 ? index : FamilyOrder.Length;
    }
}
