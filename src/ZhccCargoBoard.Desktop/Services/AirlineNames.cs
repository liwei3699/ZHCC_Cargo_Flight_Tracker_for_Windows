namespace ZhccCargoBoard.Desktop.Services;

/// <summary>
/// 航司二字码 → 中文名（只用于界面显示；数据里的 agency 是地面代理，不能当航司名）。
/// 未收录的代码只显示代码本身。
/// </summary>
public static class AirlineNames
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // 国内客运
        ["CA"] = "中国国际航空",
        ["MU"] = "中国东方航空",
        ["CZ"] = "中国南方航空",
        ["HU"] = "海南航空",
        ["3U"] = "四川航空",
        ["ZH"] = "深圳航空",
        ["MF"] = "厦门航空",
        ["SC"] = "山东航空",
        ["FM"] = "上海航空",
        ["GS"] = "天津航空",
        ["PN"] = "西部航空",
        ["8L"] = "祥鹏航空",
        ["JD"] = "首都航空",
        ["GJ"] = "长龙航空",
        ["HO"] = "吉祥航空",
        ["9C"] = "春秋航空",
        ["EU"] = "成都航空",
        ["QW"] = "青岛航空",
        ["DR"] = "瑞丽航空",
        ["DZ"] = "东海航空",
        ["GT"] = "桂林航空",
        ["GY"] = "多彩贵州航空",
        ["KY"] = "昆明航空",
        ["RY"] = "江西航空",
        ["TV"] = "西藏航空",
        ["UQ"] = "乌鲁木齐航空",
        ["FU"] = "福州航空",
        ["G5"] = "华夏航空",
        ["AQ"] = "九元航空",
        ["OQ"] = "重庆航空",
        ["NS"] = "河北航空",
        ["BK"] = "奥凯航空",
        ["KN"] = "中国联合航空",

        // 货运
        ["O3"] = "顺丰航空",
        ["Y8"] = "金鹏航空",
        ["K4"] = "圆通航空",
        ["CF"] = "中国邮政航空",
        ["CK"] = "中国货运航空",
        ["GI"] = "中原龙浩航空",
        ["HT"] = "天津货运航空",
        ["5X"] = "UPS",
        ["5Y"] = "阿特拉斯航空",
        ["VI"] = "伏尔加-第聂伯",
        ["CV"] = "卢森堡国际货运",

        // 境外
        ["CX"] = "国泰航空",
        ["KE"] = "大韩航空",
        ["OZ"] = "韩亚航空",
        ["NH"] = "全日空",
        ["TK"] = "土耳其航空",
        ["ET"] = "埃塞俄比亚航空",
        ["EK"] = "阿联酋航空",
        ["QR"] = "卡塔尔航空",
        ["EY"] = "阿提哈德航空",
        ["SV"] = "沙特航空",
        ["TR"] = "酷航",
        ["OD"] = "马印航空",
        ["SQ"] = "新加坡航空",
        ["BR"] = "长荣航空",
        ["CI"] = "中华航空",
        ["LH"] = "汉莎航空",
        ["AF"] = "法国航空",
        ["KL"] = "荷兰皇家航空",
        ["SU"] = "俄罗斯航空"
    };

    public static string Of(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        return Map.TryGetValue(code.Trim(), out var name) ? name : "";
    }
}
