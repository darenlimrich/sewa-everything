using Microsoft.AspNetCore.Components;

namespace SewaEverything.Web.Components;

public static class Icons
{
    public static MarkupString Search => Svg("<circle cx='11' cy='11' r='7'/><path d='m21 21-4.35-4.35'/>");
    public static MarkupString User => Svg("<path d='M20 21a8 8 0 0 0-16 0'/><circle cx='12' cy='8' r='4'/>");
    public static MarkupString Shield => Svg("<path d='M12 3l7 3v5c0 4.5-3 7.6-7 9-4-1.4-7-4.5-7-9V6z'/><path d='m9 12 2 2 4-4'/>");
    public static MarkupString Lock => Svg("<rect x='4' y='10' width='16' height='11' rx='2'/><path d='M8 10V7a4 4 0 0 1 8 0v3'/>");
    public static MarkupString Box => Svg("<path d='M21 8 12 3 3 8v8l9 5 9-5V8z'/><path d='M3 8l9 5 9-5'/><path d='M12 13v8'/>");
    public static MarkupString ChevronDown => Svg("<path d='m6 9 6 6 6-6'/>");
    public static MarkupString ChevronLeft => Svg("<path d='m15 18-6-6 6-6'/>");
    public static MarkupString ChevronRight => Svg("<path d='m9 18 6-6-6-6'/>");
    public static MarkupString Calendar => Svg("<rect x='3' y='4' width='18' height='18' rx='2'/><path d='M16 2v4M8 2v4M3 10h18'/>");
    public static MarkupString Grid => Svg("<rect x='3' y='3' width='7' height='7' rx='1'/><rect x='14' y='3' width='7' height='7' rx='1'/><rect x='14' y='14' width='7' height='7' rx='1'/><rect x='3' y='14' width='7' height='7' rx='1'/>");
    public static MarkupString Card => Svg("<rect x='3' y='5' width='18' height='14' rx='2'/><path d='M3 10h18'/>");
    public static MarkupString Check => Svg("<path d='m5 13 4.5 4.5L19 6.5'/>");
    public static MarkupString Plus => Svg("<path d='M12 5v14M5 12h14'/>");
    public static MarkupString Trash => Svg("<path d='M4 7h16'/><path d='M10 11v6M14 11v6'/><path d='M6 7l1 13a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1l1-13'/><path d='M9 7V4h6v3'/>");
    public static MarkupString Image => Svg("<rect x='3' y='3' width='18' height='18' rx='2'/><circle cx='9' cy='9' r='2'/><path d='m21 15-5-5L5 21'/>");
    public static MarkupString Pencil => Svg("<path d='M12 20h9'/><path d='M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z'/>");
    public static MarkupString Alert => Svg("<path d='M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z'/><path d='M12 9v4M12 17h.01'/>");
    public static MarkupString Inbox => Svg("<path d='M22 12h-6l-2 3h-4l-2-3H2'/><path d='M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z'/>");
    public static MarkupString Link => Svg("<path d='M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71'/><path d='M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71'/>");
    public static MarkupString Chart => Svg("<path d='M3 3v18h18'/><path d='M7 15v-4M12 15V7M17 15v-6'/>");
    public static MarkupString Star => Svg("<path d='M12 3.5l2.6 5.3 5.9.9-4.25 4.14 1 5.86L12 16.95 6.75 19.7l1-5.86L3.5 9.7l5.9-.9z'/>");
    public static MarkupString Eye => Svg("<path d='M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7-10-7-10-7z'/><circle cx='12' cy='12' r='3'/>");
    public static MarkupString EyeOff => Svg("<path d='M10.6 6.2A9.8 9.8 0 0 1 12 6c6.4 0 10 6 10 6a17.8 17.8 0 0 1-3.2 3.9'/><path d='M6.6 6.8A17.6 17.6 0 0 0 2 12s3.6 6 10 6a9.6 9.6 0 0 0 4-.8'/><path d='M9.9 9.9a3 3 0 0 0 4.2 4.2'/><path d='m3 3 18 18'/>");
    public static MarkupString Users => Svg("<path d='M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2'/><circle cx='9' cy='7' r='4'/><path d='M22 21v-2a4 4 0 0 0-3-3.87'/><path d='M16 3.13a4 4 0 0 1 0 7.75'/>");

    public static MarkupString Close => Svg("<path d='M18 6 6 18M6 6l12 12'/>");
    public static MarkupString Bell => Svg("<path d='M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9'/><path d='M13.7 21a2 2 0 0 1-3.4 0'/>");
    public static MarkupString Cart => Svg("<circle cx='9' cy='20' r='1.6'/><circle cx='18' cy='20' r='1.6'/><path d='M2 3h2.2l2.5 12.4a1.6 1.6 0 0 0 1.6 1.3h8.9a1.6 1.6 0 0 0 1.6-1.3L21 7H5.3'/>");
    public static MarkupString Clipboard => Svg("<rect x='5' y='4' width='14' height='17' rx='2'/><path d='M9 4V3a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v1'/><path d='M9 10h6M9 14h6'/>");
    public static MarkupString LogOut => Svg("<path d='M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4'/><path d='m16 17 5-5-5-5'/><path d='M21 12H9'/>");

    public static MarkupString Sun => Svg("<circle cx='12' cy='12' r='4'/><path d='M12 2v2'/><path d='M12 20v2'/><path d='m4.9 4.9 1.4 1.4'/><path d='m17.7 17.7 1.4 1.4'/><path d='M2 12h2'/><path d='M20 12h2'/><path d='m4.9 19.1 1.4-1.4'/><path d='m17.7 6.3 1.4-1.4'/>");
    public static MarkupString Filter => Svg("<path d='M3 5h18l-7 8v6l-4 2v-8z'/>");
    public static MarkupString Info => Svg("<circle cx='12' cy='12' r='9'/><path d='M12 11v5M12 8h.01'/>");
    public static MarkupString Moon => Svg("<path d='M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z'/>");

    private static MarkupString Svg(string inner) => new(
        "<svg width='20' height='20' viewBox='0 0 24 24' fill='none' stroke='currentColor' " +
        $"stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round' aria-hidden='true'>{inner}</svg>");
}
