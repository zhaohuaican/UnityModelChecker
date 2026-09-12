using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/*
 * UIDrawer.cs  ——  布局 / 配色 / 控件  v3（双栏工作区）
 *
 * 作者: 阿灿
 * 创建日期: 2023-06-15
 * UI 改版: 2026-09-12
 *
 * 描述:
 *   仅负责绘制，不含任何检查逻辑（ModelChecker.cs / ReportExporter.cs / DataModels.cs 未改动）。
 *
 *   布局：顶部命令栏 → 配置区（可折叠）→ 左侧摘要栏 + 右侧数据表格 → 底部状态栏
 *   配色：全部走设计 token（冷灰蓝中性色 + 靛蓝主色 + 语义功能色）
 *        控件统一落在"机身色" BgPanel 上，靠明度差形成层次：
 *        输入框（Unity 原生样式，比 BgPanel 暗/更白）→ 看着凹进去；
 *        按钮（3x3 九宫格贴图，比 BgPanel 亮）→ 看着凸起来。
 *   控件：统一高度、完整四态（默认 / 悬停 / 按下 / 禁用）、颜色之外还有文字表意
 *
 *   ⚠ 两条铁律（踩过坑，别违反）：
 *   1) 输入框绝不能加自定义 GUIStyle / 背景 —— 会失去编辑能力，用 EditorGUILayout 默认即可。
 *   2) 按钮绝不能自己接管 GUIUtility.hotControl / Event.current.Use() —— 会抢走输入框的鼠标事件，
 *      表现为"按钮点得动、输入框点不进去"。外观靠 GUIStyle 状态贴图，事件一律交给 Unity。
 *
 *   设计原则：以用户任务为中心、一致性、层级清晰、反馈明确、可访问、可维护。
 */

public static class UIDrawer
{
    /// <summary>命令栏产生的用户意图，由窗口类统一处理</summary>
    public enum UiAction
    {
        None,
        StartCheck,
        ClearResults,
        ExportReport,
        ToggleConfig
    }

    /// <summary>按钮语义：决定面色与文字色（全窗口只有 Primary 一种实心主色）</summary>
    public enum BtnKind
    {
        Default,
        Ghost,
        Danger,
        Primary
    }

    private const string TOOL_NAME = "模型规范检查器";
    private const string VERSION_INFO = "v0.3";
    private const string AUTHOR_INFO = "阿灿";

    #region 设计 Token（间距 4pt 基线 / 8pt 栅格；字号阶梯；尺寸统一）

    public static class Tok
    {
        // 间距阶梯
        public const float S1 = 4f;
        public const float S2 = 8f;
        public const float S3 = 12f;
        public const float S4 = 16f;
        public const float S6 = 24f;

        // 控件尺寸（统一高度）
        public const float TOPBAR_H = 48f;
        public const float STATUS_H = 26f;
        public const float ROW_H = 30f;
        public const float THEAD_H = 26f;
        public const float TITLE_ROW_H = 30f;
        public const float BTN_H = 28f;
        public const float INPUT_H = 24f;
        public const float CHIP_H = 26f;
        public const float SIDEBAR_W = 236f;
        public const float SIDE_ROW_H = 26f;
        public const float CONFIG_ROW_H = 24f;
        public const float FIELD_LABEL_W = 92f;
        public const float PAGER_H = 34f;

        // 表格列宽
        public const float COL_SEV = 62f;
        public const float COL_TYPE = 104f;
        public const float COL_MODEL = 160f;
        public const float COL_ACT = 68f;

        // 字号阶梯
        public const int FS_TINY = 10;
        public const int FS_SMALL = 11;
        public const int FS_BODY = 12;
        public const int FS_H3 = 13;
        public const int FS_H2 = 15;
        public const int FS_NUM = 28;
    }

    /// <summary>颜色 token：中性色 / 主色 / 功能色；暗色模式提升层次、降低饱和，不做简单反色</summary>
    public static class C
    {
        public static bool Pro { get { return EditorGUIUtility.isProSkin; } }

        public static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }

        // 中性色：明度刻意"错位"，拉开与 Unity 原生控件的亮度差。
        // 关键约束：输入框用的是 Unity 原生样式（不能自己画背景），所以
        //   BgPanel 必须明显亮于原生输入框 → 输入框才看得出是"凹进去的输入位"；
        //   按钮面又必须明显亮于 BgPanel → 才看得出"凸起"。
        public static Color BgApp { get { return Pro ? Hex(0x1F2124) : Hex(0xC9CFD6); } }
        public static Color BgSurface { get { return Pro ? Hex(0x2C2F34) : Hex(0xFFFFFF); } }
        public static Color BgSubtle { get { return Pro ? Hex(0x363A41) : Hex(0xEEF1F5); } }
        public static Color BgPanel { get { return Pro ? Hex(0x40464E) : Hex(0xD3D9E0); } }
        public static Color BgRaised { get { return Pro ? Hex(0x4A5058) : Hex(0xDFE4EA); } }
        public static Color Border { get { return Pro ? Hex(0x17191B) : Hex(0xB0B8C2); } }
        public static Color BorderStrong { get { return Pro ? Hex(0x5C636D) : Hex(0x8C97A4); } }
        public static Color TextPrimary { get { return Pro ? Hex(0xDCDEE3) : Hex(0x1F2937); } }
        public static Color TextSecondary { get { return Pro ? Hex(0xAEB3BB) : Hex(0x4E5764); } }
        public static Color TextMuted { get { return Pro ? Hex(0x9AA0A9) : Hex(0x5A626D); } }

        // 控件立体感：凹槽上缘（暗）与下缘（亮）
        public static Color BevelTop { get { return Pro ? Hex(0x17191C) : Hex(0x9DA7B3); } }
        public static Color BevelBottom { get { return Pro ? Hex(0x5A6069) : Hex(0xFFFFFF); } }

        // ---- 按钮面：正常 / 悬停 / 按下三档必须拉得开，点击才有"手感" ----
        public static Color BtnFace { get { return Pro ? Hex(0x545B66) : Hex(0xE9EDF2); } }
        public static Color BtnFaceHover { get { return Pro ? Hex(0x66707D) : Hex(0xF8FBFC); } }
        public static Color BtnFaceActive { get { return Pro ? Hex(0x2C3138) : Hex(0xC1C8D1); } }
        public static Color BtnDangerHover { get { return Pro ? Hex(0x734040) : Hex(0xFDF0F0); } }
        public static Color BtnDangerActive { get { return Pro ? Hex(0x573030) : Hex(0xEFD2D2); } }
        public static Color BtnFaceDisabled { get { return Pro ? Hex(0x3A4048) : Hex(0xDDE2E8); } }

        // 按钮凹凸边（由 3x3 九宫格贴图绘制）
        public static Color BtnEdgeTop { get { return Pro ? Hex(0x7A8390) : Hex(0xFFFFFF); } }
        public static Color BtnEdgeBottom { get { return Pro ? Hex(0x15171A) : Hex(0x9DA7B3); } }
        public static Color BtnEdgeSide { get { return Pro ? Hex(0x23262B) : Hex(0x8C97A4); } }

        // 主色按钮（明暗主题共用同一套蓝，保证白字对比度 ≥ 4.5:1）
        public static Color PrimaryFace { get { return Hex(0x2563EB); } }
        public static Color PrimaryFaceHover { get { return Hex(0x3B82F6); } }
        public static Color PrimaryFaceActive { get { return Hex(0x1D4ED8); } }
        public static Color PrimaryEdgeTop { get { return Hex(0x8FB4F8); } }
        public static Color PrimaryEdgeBottom { get { return Hex(0x123277); } }
        public static Color PrimaryEdgeSide { get { return Hex(0x1B49B0); } }

        // 主色（全窗口唯一强调色：只给主操作与选中态）
        public static Color Primary { get { return Pro ? Hex(0x60A5FA) : Hex(0x2563EB); } }
        public static Color OnPrimary { get { return Hex(0xFFFFFF); } }

        // 功能色
        public static Color Danger { get { return Pro ? Hex(0xF87171) : Hex(0xDC2626); } }
        public static Color Warning { get { return Pro ? Hex(0xFBBF24) : Hex(0xB45309); } }
        public static Color WarningSeverity { get { return Pro ? Hex(0xFB923C) : Hex(0xEA580C); } }
        public static Color Info { get { return Pro ? Hex(0x38BDF8) : Hex(0x0284C7); } }
        public static Color Success { get { return Pro ? Hex(0x34D399) : Hex(0x059669); } }
    }

    #endregion

    #region 样式（默认 / 悬停 / 聚焦 / 按下 / 禁用）

    public static class Styles
    {
        private static bool _ready;
        private static bool _pro;
        private static readonly Dictionary<string, GUIStyle> _cache = new Dictionary<string, GUIStyle>();

        private static Texture2D _white;

        private static Texture2D MakeTex(float v)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, new Color(v, v, v, 1f));
            t.hideFlags = HideFlags.HideAndDontSave;
            t.Apply();
            return t;
        }

        public static Texture2D TexWhite
        {
            get
            {
                if (_white == null) _white = MakeTex(1f);
                return _white;
            }
        }

        public static void Ensure()
        {
            bool pro = EditorGUIUtility.isProSkin;
            if (_ready && pro == _pro) return;
            _cache.Clear();
            _bevelCache.Clear();     // 按钮贴图颜色依赖明暗主题，换肤时必须重建
            _pro = pro;
            _ready = true;
        }

        private static GUIStyle S(string key, Func<GUIStyle> factory)
        {
            GUIStyle s;
            if (!_cache.TryGetValue(key, out s) || s == null)
            {
                s = factory();
                s.name = key;
                _cache[key] = s;
            }

            return s;
        }

        // ---------- 文字 ----------
        public static GUIStyle H1 =>
            S("h1", () => new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = Tok.FS_H2,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 0f,               // 内置皮肤带了 fixedHeight，改字号后必须清掉，否则字会被裁
                normal = { textColor = C.TextPrimary }
            });

        public static GUIStyle H2Centered =>
            S("h2c", () => new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = Tok.FS_H2,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 0f,
                normal = { textColor = C.TextPrimary }
            });

        public static GUIStyle H2CenteredPrimary =>
            S("h2cp", () => new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = Tok.FS_H2,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 0f,
                normal = { textColor = C.Primary }
            });

        public static GUIStyle H3 =>
            S("h3", () => new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = Tok.FS_H3,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = C.TextPrimary }
            });

        public static GUIStyle Body =>
            S("body", () => new GUIStyle(EditorStyles.label)
            {
                fontSize = Tok.FS_BODY,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = C.TextPrimary }
            });

        public static GUIStyle Small =>
            S("small", () => new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = Tok.FS_SMALL,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = C.TextSecondary }
            });

        public static GUIStyle SmallRight =>
            S("smallRight", () => new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = Tok.FS_SMALL,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = C.TextSecondary }
            });

        public static GUIStyle SmallCenter =>
            S("smallCenter", () => new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = Tok.FS_SMALL,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = C.TextSecondary }
            });

        public static GUIStyle Tiny =>
            S("tiny", () => new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = Tok.FS_TINY,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = C.TextMuted }
            });

        public static GUIStyle TinyRight =>
            S("tinyRight", () => new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = Tok.FS_TINY,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = C.TextMuted }
            });

        // 白字样式：配合 GUI.color 精确着色，避免二次乘色变暗
        public static GUIStyle TintLeft =>
            S("tintLeft", () => new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = Tok.FS_BODY,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white }
            });

        public static GUIStyle TintRight =>
            S("tintRight", () => new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = Tok.FS_SMALL,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Color.white }
            });

        public static GUIStyle TintRightValue =>
            S("tintRightValue", () => new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = Tok.FS_H3,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Color.white }
            });

        public static GUIStyle BigNumber =>
            S("bigNumber", () => new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = Tok.FS_NUM,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 0f,                       // 关键：清掉内置 fixedHeight，否则 28px 大字被 16px 行高裁掉
                clipping = TextClipping.Overflow,
                normal = { textColor = C.TextPrimary }
            });

        public static GUIStyle MetricKey =>
            S("metricKey", () => new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = Tok.FS_SMALL,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = C.TextSecondary }
            });

        // ---------- 容器 ----------
        private static GUIStyle Container(RectOffset padding)
        {
            return new GUIStyle
            {
                normal = { background = TexWhite, textColor = C.TextPrimary },
                padding = padding,
                margin = new RectOffset(0, 0, 0, 0)
            };
        }

        public static GUIStyle TopBar => S("topBar", () => Container(new RectOffset(0, 0, 0, 0)));
        public static GUIStyle Surface => S("surface", () => Container(new RectOffset((int)Tok.S3, (int)Tok.S3, (int)Tok.S3, (int)Tok.S3)));
        public static GUIStyle SurfaceFlat => S("surfaceFlat", () => Container(new RectOffset(0, 0, 0, 0)));

        // ---------- 按钮 ----------
        // 通过 GUIStyle 各状态的 background 提供悬停 / 按下反馈，事件完全交给 Unity 处理。
        // 【绝对不要】自己接管 GUIUtility.hotControl —— 那会和输入框抢鼠标事件，
        // 结果就是"按钮看着正常、输入框点不进去"。
        // 按钮贴图是 3x3 九宫格：四周 1px 分别是上缘/下缘/左右边，中间 1px 面色会被拉伸填充。
        // 注意：只有按钮能自己画背景；输入框必须用 Unity 原生样式，否则会失去编辑能力。

        private static readonly Dictionary<int, Texture2D> _bevelCache = new Dictionary<int, Texture2D>();

        private static int TexKey(Color a, Color b, Color c, Color d)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Pack(a);
                h = h * 31 + Pack(b);
                h = h * 31 + Pack(c);
                h = h * 31 + Pack(d);
                return h;
            }
        }

        private static int Pack(Color c)
        {
            return (Mathf.RoundToInt(c.r * 255f) << 16)
                 | (Mathf.RoundToInt(c.g * 255f) << 8)
                 | Mathf.RoundToInt(c.b * 255f);
        }

        /// <summary>3x3 九宫格按钮面：face 拉伸填充，四周 1px 取 top/bottom/side（Unity 纹理 y 轴向上）</summary>
        private static Texture2D BevelTex(Color face, Color top, Color bottom, Color side)
        {
            int key = TexKey(face, top, bottom, side);
            Texture2D t;
            if (_bevelCache.TryGetValue(key, out t) && t != null) return t;

            t = new Texture2D(3, 3, TextureFormat.RGBA32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Point;
            t.SetPixel(0, 2, top); t.SetPixel(1, 2, top); t.SetPixel(2, 2, top);
            t.SetPixel(0, 1, side); t.SetPixel(1, 1, face); t.SetPixel(2, 1, side);
            t.SetPixel(0, 0, bottom); t.SetPixel(1, 0, bottom); t.SetPixel(2, 0, bottom);
            t.Apply();

            _bevelCache[key] = t;
            return t;
        }

        /// <summary>按语义 + 状态取按钮面贴图：0 默认 / 1 悬停 / 2 按下（按下时凹凸边反转）</summary>
        private static Texture2D FaceTex(BtnKind kind, int state)
        {
            Color face, eTop, eBottom, eSide;
            GetButtonColors(kind, state == 1, state == 2, true, out face, out eTop, out eBottom, out eSide);
            return BevelTex(face, eTop, eBottom, eSide);
        }

        private static Color BtnTextColor(BtnKind kind)
        {
            if (kind == BtnKind.Primary) return C.OnPrimary;
            if (kind == BtnKind.Danger) return C.Danger;
            if (kind == BtnKind.Ghost) return C.TextSecondary;
            return C.TextPrimary;
        }

        public static GUIStyle BtnStyle(BtnKind kind, bool mini)
        {
            string key = "btn_" + kind + (mini ? "_m" : "");
            return S(key, () =>
            {
                var s = new GUIStyle(GUI.skin.button);
                s.border = new RectOffset(1, 1, 1, 1);
                s.padding = new RectOffset(9, 9, 2, 2);
                s.margin = new RectOffset(0, 0, 0, 0);
                s.alignment = TextAnchor.MiddleCenter;
                s.clipping = TextClipping.Clip;
                s.wordWrap = false;
                s.fixedHeight = 0f;         // 高度统一由调用处的 GUILayout.Height 决定
                s.fontSize = mini ? Tok.FS_TINY : (kind == BtnKind.Primary ? Tok.FS_BODY : Tok.FS_SMALL);
                s.fontStyle = kind == BtnKind.Primary ? FontStyle.Bold : FontStyle.Normal;

                Color tc = BtnTextColor(kind);
                s.normal.background = FaceTex(kind, 0);
                s.hover.background = FaceTex(kind, 1);
                s.active.background = FaceTex(kind, 2);
                s.focused.background = FaceTex(kind, 1);
                s.onNormal.background = FaceTex(kind, 0);
                s.onHover.background = FaceTex(kind, 1);
                s.onActive.background = FaceTex(kind, 2);
                s.onFocused.background = FaceTex(kind, 1);

                s.normal.textColor = tc;
                s.hover.textColor = tc;
                s.active.textColor = tc;
                s.focused.textColor = tc;
                s.onNormal.textColor = tc;
                s.onHover.textColor = tc;
                s.onActive.textColor = tc;
                s.onFocused.textColor = tc;
                return s;
            });
        }

        // 输入框：不加任何自定义样式，完全交给 Unity 默认处理
        //（这是唯一 100% 保证不失去编辑能力的做法，别再加 GUIStyle 了）

        // 分区折叠头：原生 Foldout，箭头与文字由皮肤着色
        public static GUIStyle Foldout =>
            S("foldout", () => new GUIStyle(EditorStyles.foldout)
            {
                fontSize = Tok.FS_H3,
                fontStyle = FontStyle.Bold,
                fixedHeight = Tok.SIDE_ROW_H
            });
    }

    #endregion

    #region 基础绘制工具

    private static bool IsRepaint
    {
        get { return Event.current == null || Event.current.type == EventType.Repaint; }
    }

    private static void Fill(Rect r, Color c)
    {
        if (!IsRepaint) return;
        // EditorGUI.DrawRect 会与 GUI.color 相乘，这里强制还原为纯白，保证颜色不被二次染色
        var prev = GUI.color;
        GUI.color = Color.white;
        EditorGUI.DrawRect(r, c);
        GUI.color = prev;
    }

    private static void HLine(Rect r, float y, Color c)
    {
        Fill(new Rect(r.x, y, r.width, 1f), c);
    }

    #region 按钮（外观自绘，事件交给 Unity）

    /*
     * 历史教训（别再踩）：
     *   曾经在这里自己接管 GUIUtility.hotControl / Event.current.Use() 来画按钮，
     *   结果输入框点不进去 —— 自绘按钮抢走了鼠标事件，Unity 的文本编辑器拿不到 MouseDown
     *   就建立不了 TextEditor，表现就是"框还在、字也能显示，但就是输入不了"。
     *
     *   现在只做两件事：① 给 GUIStyle 的 normal / hover / active 三个状态各配一张 3x3 九宫格贴图
     *   （凹凸边 + 面色，按下时凹凸边反转）；② 用 GUILayout.Button / GUI.Button 让 Unity 处理事件。
     *   反馈强度靠贴图颜色差，安全性靠不碰事件。
     */

    /// <summary>布局版按钮：返回是否被点击</summary>
    private static bool Btn(string text, string tip, BtnKind kind, float width, bool mini = false, float height = 0f)
    {
        float h = height > 0f ? height : (mini ? 22f : Tok.BTN_H);
        return GUILayout.Button(new GUIContent(text, tip), Styles.BtnStyle(kind, mini),
            GUILayout.Width(width), GUILayout.Height(h));
    }

    /// <summary>矩形版按钮：用于表格行内等需要手动定位的位置</summary>
    private static bool Btn(Rect rect, GUIContent content, BtnKind kind, bool mini)
    {
        return GUI.Button(rect, content, Styles.BtnStyle(kind, mini));
    }

    /// <summary>面色与凹凸边：悬停提亮、按下压暗并**反转**凹凸边（真正"陷进去"）</summary>
    private static void GetButtonColors(BtnKind kind, bool hover, bool active, bool enabled,
                                        out Color face, out Color eTop, out Color eBottom, out Color eSide)
    {
        if (!enabled)
        {
            face = C.BtnFaceDisabled;
            eTop = C.Border;
            eBottom = C.Border;
            eSide = C.Border;
            return;
        }

        if (kind == BtnKind.Primary)
        {
            face = active ? C.PrimaryFaceActive : (hover ? C.PrimaryFaceHover : C.PrimaryFace);
            eTop = active ? C.PrimaryEdgeBottom : C.PrimaryEdgeTop;
            eBottom = active ? C.PrimaryEdgeTop : C.PrimaryEdgeBottom;
            eSide = C.PrimaryEdgeSide;
            return;
        }

        if (kind == BtnKind.Danger)
            face = active ? C.BtnDangerActive : (hover ? C.BtnDangerHover : C.BtnFace);
        else
            face = active ? C.BtnFaceActive : (hover ? C.BtnFaceHover : C.BtnFace);

        eTop = active ? C.BtnEdgeBottom : C.BtnEdgeTop;
        eBottom = active ? C.BtnEdgeTop : C.BtnEdgeBottom;
        eSide = C.BtnEdgeSide;
    }

    #endregion

    private static Color Blend(Color a, Color b, float t)
    {
        return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, 1f);
    }

    private static Rect BeginRow(Color bg, GUIStyle style, params GUILayoutOption[] opts)
    {
        var prev = GUI.backgroundColor;
        GUI.backgroundColor = bg;
        Rect r = EditorGUILayout.BeginHorizontal(style, opts);
        GUI.backgroundColor = prev;
        return r;
    }

    private static Rect BeginCol(Color bg, GUIStyle style, params GUILayoutOption[] opts)
    {
        var prev = GUI.backgroundColor;
        GUI.backgroundColor = bg;
        Rect r = EditorGUILayout.BeginVertical(style, opts);
        GUI.backgroundColor = prev;
        return r;
    }

    private static void Spacer(float px)
    {
        GUILayout.Space(px);
    }

    /// <summary>整个窗口的统一底色容器</summary>
    public static void BeginApp()
    {
        Styles.Ensure();
        var prev = GUI.backgroundColor;
        GUI.backgroundColor = C.BgApp;
        EditorGUILayout.BeginVertical(Styles.SurfaceFlat, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        GUI.backgroundColor = prev;

        // 顶部留白：命令栏不再紧贴窗口上沿
        GUILayout.Space(Tok.S2);
    }

    public static void EndApp()
    {
        EditorGUILayout.EndVertical();
    }

    /// <summary>手绘文字（用于自绘表格/侧栏，保证列对齐）</summary>
    private static void Text(Rect row, float x, float w, string s, GUIStyle style, Color color)
    {
        Text(row, x, w, new GUIContent(s), style, color);
    }

    private static void Text(Rect row, float x, float w, GUIContent content, GUIStyle style, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUI.Label(new Rect(x, row.y, w, row.height), content, style);
        GUI.color = prev;
    }

    private static void Separator(float topSpace, float bottomSpace)
    {
        GUILayout.Space(topSpace);
        Rect r = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
        Fill(r, C.Border);
        GUILayout.Space(bottomSpace);
    }

    #endregion

    #region 1. 顶部命令栏

    public static UiAction DrawTopBar(ModelStandardChecker window, bool isChecking, bool configOpen)
    {
        Styles.Ensure();

        UiAction act = UiAction.None;
        bool hasResult = window != null && window.issues.Count > 0;

        var bar = BeginRow(C.BgPanel, Styles.TopBar, GUILayout.Height(Tok.TOPBAR_H), GUILayout.ExpandWidth(true));
        HLine(bar, bar.y + bar.height - 1f, C.Border);

        Spacer(Tok.S3);

        // 品牌区（产品名 → 版本 → 作者，字号递减）
        EditorGUILayout.LabelField(TOOL_NAME, Styles.H1, GUILayout.Width(140f));
        EditorGUILayout.LabelField(VERSION_INFO + "  ·  " + AUTHOR_INFO, Styles.Tiny, GUILayout.Width(104f));

        GUILayout.FlexibleSpace();

        // 低频：配置
        if (Btn(configOpen ? "收起配置" : "配置", "展开/收起阈值与检查项设置", BtnKind.Ghost, 72f))
            act = UiAction.ToggleConfig;

        Spacer(Tok.S2);

        // 次要：导出（禁用时明确告知原因）
        using (new EditorGUI.DisabledScope(!hasResult))
        {
            if (Btn("导出报告", hasResult ? "导出 Excel 检查报告" : "暂无可导出内容：请先开始检查",
                    BtnKind.Default, 80f))
                act = UiAction.ExportReport;
        }

        Spacer(Tok.S2);

        // 危险：清空（红字提示 + 点击后二次确认）
        using (new EditorGUI.DisabledScope(!hasResult || isChecking))
        {
            if (Btn("清空", hasResult ? "清空全部结果（需二次确认）" : "暂无结果可清空",
                    BtnKind.Danger, 56f))
                act = UiAction.ClearResults;
        }

        Spacer(Tok.S2);

        // 主操作：全窗口唯一主色实心按钮，检查中进入禁用态（防重复提交）
        using (new EditorGUI.DisabledScope(isChecking))
        {
            if (Btn(isChecking ? "检查中…" : "开始检查",
                    isChecking ? "正在检查场景，请稍候" : "扫描当前场景中的全部模型",
                    BtnKind.Primary, 100f))
                act = UiAction.StartCheck;
        }

        Spacer(Tok.S3);

        EditorGUILayout.EndHorizontal();

        return act;
    }

    #endregion

    #region 2. 配置区（可折叠，三列网格）

    public static void DrawConfigPanel(ref ModelCheckConfig config, ref bool configPanelFoldout, ref int itemsPerPage)
    {
        Styles.Ensure();

        var panel = BeginCol(C.BgPanel, Styles.Surface, GUILayout.ExpandWidth(true));
        HLine(panel, panel.y + panel.height - 1f, C.Border);

        // 分区标题：原生 Foldout（箭头 + 文字由皮肤着色，明暗主题均可读）
        EditorGUILayout.BeginHorizontal(GUILayout.Height(Tok.SIDE_ROW_H));
        Spacer(Tok.S2);
        configPanelFoldout = EditorGUILayout.Foldout(configPanelFoldout, "检查配置", true, Styles.Foldout);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(configPanelFoldout ? "收起" : "展开", Styles.TinyRight, GUILayout.Width(40f));
        Spacer(Tok.S2);
        EditorGUILayout.EndHorizontal();

        if (configPanelFoldout)
        {
            Spacer(Tok.S2);
            EditorGUILayout.LabelField("阈值", Styles.Small);
            Spacer(Tok.S1);

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            IntField("最大三角面数", ref config.maxTriangles, 1000, "单模型三角面数上限");
            IntField("最大材质数", ref config.maxMaterials, 5, "单模型材质槽上限");
            IntField("最大骨骼数", ref config.maxBones, 100, "蒙皮网格骨骼数上限");
            EditorGUILayout.EndVertical();

            Spacer(Tok.S4);

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            IntField("最大贴图尺寸", ref config.maxTextureSize, 1024, "贴图宽高上限");
            FloatField("轴心偏移容差", ref config.pivotTolerance, 1f, "轴心点偏移的容许范围");
            FloatField("UV拉伸比阈值", ref config.uvStretchThreshold, 5f, "超过该拉伸比即视为问题");
            EditorGUILayout.EndVertical();

            Spacer(Tok.S4);

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            IntPairField("DrawCall预算", ref config.minDrawCallBudget, 200, ref config.maxDrawCallBudget, 700,
                "DrawCall 预算区间（最小 / 最大）");
            IntField("每页显示数量", ref itemsPerPage, 10, "结果表格每页条数（5 - 200）");
            EditorGUILayout.LabelField("", Styles.Small);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            Spacer(Tok.S3);
            EditorGUILayout.LabelField("检查项", Styles.Small);
            Spacer(Tok.S1);

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            Toggle("检查子节点命名", ref config.checkChildrenNaming, "检查子节点是否遵循命名规范");
            Toggle("检查轴心偏移", ref config.checkPivotOffset, "检查模型轴心点位置");
            EditorGUILayout.EndVertical();

            Spacer(Tok.S4);

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            Toggle("检查UV重叠", ref config.checkUVOverlap, "检查 UV 坐标是否存在重叠");
            Toggle("检查Lightmap UV", ref config.checkLightmapUV, "检查光照贴图 UV 通道");
            EditorGUILayout.EndVertical();

            Spacer(Tok.S4);

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            Toggle("检查材质使用效率", ref config.checkMaterialUsage, "分析材质使用效率与 DrawCall 建议");
            Toggle("检查UV拉伸", ref config.checkUVStretch, "检测 UV 拉伸比是否超标");
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            Spacer(Tok.S2);
        }

        EditorGUILayout.EndVertical();
    }

    private static void IntField(string label, ref int value, int def, string tip)
    {
        EditorGUILayout.BeginHorizontal(GUILayout.Height(Tok.CONFIG_ROW_H));
        EditorGUILayout.LabelField(new GUIContent(label, tip), Styles.MetricKey,
            GUILayout.Width(Tok.FIELD_LABEL_W), GUILayout.Height(Tok.CONFIG_ROW_H));

        value = EditorGUILayout.IntField(value, GUILayout.Height(Tok.INPUT_H), GUILayout.MinWidth(40f));

        if (Btn("R", "恢复默认值 " + def, BtnKind.Default, 22f, true, Tok.INPUT_H))
            value = def;
        EditorGUILayout.EndHorizontal();
    }

    private static void IntPairField(string label, ref int v1, int d1, ref int v2, int d2, string tip)
    {
        EditorGUILayout.BeginHorizontal(GUILayout.Height(Tok.CONFIG_ROW_H));
        EditorGUILayout.LabelField(new GUIContent(label, tip), Styles.MetricKey,
            GUILayout.Width(Tok.FIELD_LABEL_W), GUILayout.Height(Tok.CONFIG_ROW_H));

        v1 = EditorGUILayout.IntField(v1, GUILayout.Height(Tok.INPUT_H), GUILayout.MinWidth(30f));
        v2 = EditorGUILayout.IntField(v2, GUILayout.Height(Tok.INPUT_H), GUILayout.MinWidth(30f));

        if (Btn("R", "恢复默认值 " + d1 + " / " + d2, BtnKind.Default, 22f, true, Tok.INPUT_H))
        {
            v1 = d1;
            v2 = d2;
        }
        EditorGUILayout.EndHorizontal();
    }

    private static void FloatField(string label, ref float value, float def, string tip)
    {
        EditorGUILayout.BeginHorizontal(GUILayout.Height(Tok.CONFIG_ROW_H));
        EditorGUILayout.LabelField(new GUIContent(label, tip), Styles.MetricKey,
            GUILayout.Width(Tok.FIELD_LABEL_W), GUILayout.Height(Tok.CONFIG_ROW_H));

        value = EditorGUILayout.FloatField(value, GUILayout.Height(Tok.INPUT_H), GUILayout.MinWidth(40f));

        if (Btn("R", "恢复默认值 " + def, BtnKind.Default, 22f, true, Tok.INPUT_H))
            value = def;
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 勾选框紧跟标签（不再把标签拉伸到整列宽）。
    /// 之前用 ExpandWidth(true) 让标签吃掉整列宽度，窗口一宽勾选框就跑到列的右端，
    /// 和文字隔着半个屏幕，完全对不上。
    /// </summary>
    private static void Toggle(string label, ref bool value, string tip)
    {
        EditorGUILayout.BeginHorizontal(GUILayout.Height(Tok.CONFIG_ROW_H));
        value = EditorGUILayout.Toggle(value, GUILayout.Width(16f));
        EditorGUILayout.LabelField(new GUIContent(label, tip), Styles.MetricKey, GUILayout.ExpandWidth(false));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    #endregion

    #region 3. 左侧摘要栏

    /// <summary>
    /// 左侧摘要栏。
    /// 返回 true 表示本帧筛选条件发生了变化，调用方应重置分页与表格滚动位置。
    /// </summary>
    public static bool DrawSidebar(List<ModelIssue> issues, Dictionary<IssueType, int> issueStats,
                                   int totalTriangles, int totalObjects, HashSet<Material> materialSet,
                                   float averageTrianglesPerMaterial, bool isChecking,
                                   ref int severityFilter, ref IssueType? typeFilter, ref Vector2 scroll)
    {
        Styles.Ensure();

        bool changed = false;
        int total = issues == null ? 0 : issues.Count;

        var panel = BeginCol(C.BgSurface, Styles.SurfaceFlat, GUILayout.Width(Tok.SIDEBAR_W), GUILayout.ExpandHeight(true));
        Fill(new Rect(panel.x + panel.width - 1f, panel.y, 1f, panel.height), C.BorderStrong);

        scroll = EditorGUILayout.BeginScrollView(scroll, false, true, GUILayout.ExpandHeight(true));

        // ---- 首屏最重要的结论：问题总数 ----
        Spacer(Tok.S3);
        EditorGUILayout.BeginHorizontal();
        Spacer(Tok.S3);
        EditorGUILayout.LabelField(isChecking ? "当前状态" : "问题总数", Styles.Small);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (isChecking)
        {
            EditorGUILayout.BeginHorizontal();
            Spacer(Tok.S3);
            EditorGUILayout.LabelField("检查中…", Styles.H2CenteredPrimary, GUILayout.Width(Tok.SIDEBAR_W - Tok.S6));
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            // 手绘 + 显式 40px 行高：大字永远不会被行高裁掉
            Rect numRect = GUILayoutUtility.GetRect(0f, 40f, GUILayout.ExpandWidth(true));
            var prev = GUI.color;
            GUI.color = total > 0 ? C.Danger : C.Success;
            GUI.Label(new Rect(numRect.x + Tok.S3, numRect.y, numRect.width - Tok.S3 - Tok.S2, numRect.height),
                total.ToString(), Styles.BigNumber);
            GUI.color = prev;
        }

        // ---- 严重度分布：颜色 + 文字双编码，整行可点击筛选 ----
        if (total > 0 && issueStats != null && issueStats.Count > 0)
        {
            Spacer(Tok.S3);
            EditorGUILayout.BeginHorizontal();
            Spacer(Tok.S3);
            EditorGUILayout.LabelField(
                new GUIContent("严重度 · 点击筛选", "按严重度筛选（会自动清除「问题类型」筛选）；再点一次取消筛选"),
                Styles.Tiny);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            Spacer(Tok.S1);

            for (int lv = 0; lv < 4; lv++)
            {
                int c = CountLevel(issueStats, lv);
                if (c <= 0) continue;
                if (DrawSidebarRow(GetLevelName(lv), c, total, GetLevelColor(lv),
                        severityFilter == lv, lv, null, ref severityFilter, ref typeFilter))
                    changed = true;
            }
        }

        Separator(Tok.S3, Tok.S1);

        // ---- 场景指标：键左值右，数字右对齐 ----
        EditorGUILayout.BeginHorizontal();
        Spacer(Tok.S3);
        EditorGUILayout.LabelField("场景指标", Styles.Small);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        Spacer(Tok.S1);

        MetricRow("三角面数", FormatCompact(totalTriangles), totalTriangles >= 1000000 ? C.Danger : C.TextPrimary);
        MetricRow("游戏对象", FormatCompact(totalObjects), C.TextPrimary);
        MetricRow("涉及模型", issues == null ? "0" : issues.Select(i => i.modelName).Distinct().Count().ToString(), C.TextPrimary);
        MetricRow("材质总数", materialSet.Count.ToString(), C.TextPrimary);
        if (averageTrianglesPerMaterial > 0f)
            MetricRow("平均面数 / 材质", averageTrianglesPerMaterial.ToString("F0"), C.TextPrimary);

        // ---- 问题类型：点击筛选（替代分组折叠，减少跳转）----
        if (issueStats != null && issueStats.Count > 0)
        {
            Separator(Tok.S3, Tok.S1);

            EditorGUILayout.BeginHorizontal();
            Spacer(Tok.S3);
            EditorGUILayout.LabelField(
                new GUIContent("问题类型 · 点击筛选", "按问题类型筛选（会自动清除「严重度」筛选）；再点一次取消筛选"),
                Styles.Tiny);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            Spacer(Tok.S1);

            var sorted = issueStats.OrderByDescending(s => s.Value).ToList();
            int max = sorted.Count > 0 ? sorted[0].Value : 1;

            for (int i = 0; i < sorted.Count; i++)
            {
                var t = sorted[i].Key;
                if (DrawSidebarRow(GetIssueTypeDisplayName(t), sorted[i].Value, max, GetSeverityColor(t),
                        typeFilter.HasValue && typeFilter.Value == t, -1, t, ref severityFilter, ref typeFilter))
                    changed = true;
            }
        }

        Spacer(Tok.S4);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        return changed;
    }

    /// <summary>
    /// 侧栏数据条行：名称 + 数量 + 占比条；整行可点击筛选。
    /// 当前项以主色/语义色 + 左色条 + 加粗三重标识（不依赖单一颜色）。
    /// 返回 true 表示本帧点了这一行、筛选条件已改变。
    /// </summary>
    private static bool DrawSidebarRow(string name, int count, int max, Color color,
                                       bool active, int level, IssueType? type,
                                       ref int severityFilter, ref IssueType? typeFilter)
    {
        Rect row = GUILayoutUtility.GetRect(0f, Tok.SIDE_ROW_H, GUILayout.ExpandWidth(true));

        bool hover = Event.current != null && row.Contains(Event.current.mousePosition);
        float ratio = max > 0 ? Mathf.Clamp01(count / (float)max) : 0f;

        Color baseBg = active ? Blend(C.BgSurface, color, 0.20f) : (hover ? Blend(C.BgSurface, color, 0.09f) : C.BgSurface);
        Fill(row, baseBg);
        Fill(new Rect(row.x + 3f, row.y + 4f, Mathf.Max(2f, (row.width - 6f) * ratio), row.height - 8f),
            Blend(C.BgSurface, color, 0.15f));
        if (active) Fill(new Rect(row.x, row.y, 3f, row.height), color);

        Text(row, row.x + Tok.S3, row.width - Tok.S3 - 56f, name,
            active ? Styles.TintLeft : Styles.Body, active ? color : C.TextPrimary);

        var prev = GUI.color;
        GUI.color = active ? color : C.TextSecondary;
        GUI.Label(new Rect(row.x + row.width - 56f, row.y, 44f, row.height), count.ToString(), Styles.TintRight);
        GUI.color = prev;

        EditorGUIUtility.AddCursorRect(row, MouseCursor.Link);

        if (Event.current != null && Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
        {
            // 严重度与问题类型互斥：点其中一个就清掉另一个。
            // 否则两个条件会叠加（AND），例如「严重度=建议 + 类型=UV拉伸」→ 一条都不剩，
            // 用户会以为筛选坏了 / 数据没了。再点一次同一行 = 取消筛选，回到全部。
            if (type.HasValue)
            {
                typeFilter = (typeFilter.HasValue && typeFilter.Value == type.Value) ? (IssueType?)null : type;
                severityFilter = -1;
            }
            else
            {
                severityFilter = (severityFilter == level) ? -1 : level;
                typeFilter = null;
            }

            Event.current.Use();
            return true;
        }

        return false;
    }

    private static void MetricRow(string key, string value, Color valueColor)
    {
        Rect row = GUILayoutUtility.GetRect(0f, Tok.SIDE_ROW_H, GUILayout.ExpandWidth(true));
        Text(row, row.x + Tok.S3, row.width - Tok.S3 - 100f, key, Styles.MetricKey, C.TextSecondary);

        var prev = GUI.color;
        GUI.color = valueColor;
        GUI.Label(new Rect(row.x + row.width - 104f, row.y, 92f, row.height), value, Styles.TintRightValue);
        GUI.color = prev;
    }

    #endregion

    #region 4. 右侧数据工作区

    public static void DrawWorkspace(List<ModelIssue> rows, int totalCount, bool isChecking,
                                     ref int severityFilter, ref IssueType? typeFilter,
                                     ref int page, ref int itemsPerPage, ref Vector2 scroll)
    {
        Styles.Ensure();

        var panel = BeginCol(C.BgSurface, Styles.SurfaceFlat, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        // ---- 视图状态条：筛选摘要 + 每页条数（就地可改）+ 清除筛选 ----
        EditorGUILayout.BeginHorizontal(GUILayout.Height(Tok.TITLE_ROW_H));
        Spacer(Tok.S3);
        EditorGUILayout.LabelField("检查结果", Styles.H3, GUILayout.Width(64f));
        EditorGUILayout.LabelField(FilterSummary(totalCount, rows.Count, severityFilter, typeFilter), Styles.Small);
        GUILayout.FlexibleSpace();

        if (severityFilter >= 0 || typeFilter.HasValue)
        {
            if (Btn("清除筛选", "恢复显示全部结果", BtnKind.Default, 64f, true))
            {
                severityFilter = -1;
                typeFilter = null;
                page = 0;
            }

            Spacer(Tok.S2);
        }

        // 每页条数：不用回到折叠的配置区，就地就能改；改完自动回到第 1 页
        EditorGUILayout.LabelField(new GUIContent("每页", "每页显示条数，有效范围 5 - 200"), Styles.Small,
            GUILayout.Width(28f));
        int typed = EditorGUILayout.IntField(itemsPerPage,
            GUILayout.Width(48f), GUILayout.Height(Tok.INPUT_H));
        EditorGUILayout.LabelField("条", Styles.Small, GUILayout.Width(18f));

        if (typed != itemsPerPage)
        {
            itemsPerPage = typed;       // 允许自由输入，真正生效范围在分页计算里兜底
            page = 0;
            scroll = Vector2.zero;
        }

        Spacer(Tok.S3);
        EditorGUILayout.EndHorizontal();

        HLine(panel, panel.y + Tok.TITLE_ROW_H, C.Border);

        // ---- 加载态：明确告知正在进行，避免用户误判卡死 ----
        if (isChecking)
        {
            DrawLoadingState();
            EditorGUILayout.EndVertical();
            return;
        }

        // ---- 空状态：说明原因 + 给出下一步 ----
        if (rows == null || rows.Count == 0)
        {
            DrawEmptyState(totalCount);
            EditorGUILayout.EndVertical();
            return;
        }

        // ---- 分页计算（每页条数兜底在 5 - 200）----
        int perPage = Mathf.Clamp(itemsPerPage, 5, 200);
        int totalPages = Mathf.Max(1, Mathf.CeilToInt(rows.Count / (float)perPage));
        page = Mathf.Clamp(page, 0, totalPages - 1);

        // 键盘翻页（可访问性：不依赖鼠标）
        if (Event.current != null && Event.current.type == EventType.KeyDown)
        {
            if (Event.current.keyCode == KeyCode.PageDown && page < totalPages - 1)
            {
                page++;
                scroll = Vector2.zero;
                Event.current.Use();
            }
            else if (Event.current.keyCode == KeyCode.PageUp && page > 0)
            {
                page--;
                scroll = Vector2.zero;
                Event.current.Use();
            }
        }

        int start = page * perPage;
        int end = Mathf.Min(start + perPage, rows.Count);

        // ---- 表头（固定不滚动，保证列对齐参照始终可见）----
        DrawTableHeader();

        // ---- 数据行：滚动区占满剩余高度 ----
        scroll = EditorGUILayout.BeginScrollView(scroll, false, true, GUILayout.ExpandHeight(true));
        for (int i = start; i < end; i++)
        {
            DrawTableRow(rows[i], i - start);
        }

        GUILayout.Space(Tok.S2);
        EditorGUILayout.EndScrollView();

        // ---- 分页条 ----
        DrawPager(page, totalPages, start, end, rows.Count, ref page, ref scroll);

        EditorGUILayout.EndVertical();
    }

    private static string FilterSummary(int totalCount, int shown, int severityFilter, IssueType? typeFilter)
    {
        if (severityFilter < 0 && !typeFilter.HasValue)
            return "共 " + totalCount + " 条";

        string s = "筛选：";
        if (severityFilter >= 0) s += "严重度=" + GetLevelName(severityFilter) + "  ";
        if (typeFilter.HasValue) s += "类型=" + GetIssueTypeDisplayName(typeFilter.Value) + "  ";
        return s + "· " + shown + " / " + totalCount + " 条";
    }

    private static void DrawTableHeader()
    {
        var head = BeginRow(C.BgPanel, Styles.SurfaceFlat, GUILayout.Height(Tok.THEAD_H), GUILayout.ExpandWidth(true));
        HLine(head, head.y + head.height - 1f, C.Border);

        float x = head.x + Tok.S2;
        Text(head, x, Tok.COL_SEV, "严重度", Styles.TintLeft, C.TextSecondary);
        x += Tok.COL_SEV;
        Text(head, x, Tok.COL_TYPE, "类型", Styles.TintLeft, C.TextSecondary);
        x += Tok.COL_TYPE;
        Text(head, x, Tok.COL_MODEL, "模型", Styles.TintLeft, C.TextSecondary);
        x += Tok.COL_MODEL;

        float descW = head.width - (x - head.x) - Tok.COL_ACT - Tok.S2;
        Text(head, x, descW, "描述", Styles.TintLeft, C.TextSecondary);
        Text(head, head.x + head.width - Tok.COL_ACT, Tok.COL_ACT - Tok.S2, "操作", Styles.TintLeft, C.TextSecondary);

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>数据行：斑马纹 + 悬停高亮 + 严重度色条 + 文字表意（不仅靠颜色）</summary>
    private static void DrawTableRow(ModelIssue issue, int indexInPage)
    {
        Rect row = GUILayoutUtility.GetRect(0f, Tok.ROW_H, GUILayout.ExpandWidth(true));

        Color sev = GetSeverityColor(issue.type);
        bool hover = Event.current != null && row.Contains(Event.current.mousePosition);
        Color baseBg = (indexInPage % 2 == 0) ? C.BgSurface : C.BgSubtle;
        Fill(row, hover ? Blend(baseBg, C.Primary, 0.10f) : baseBg);
        HLine(row, row.y + row.height - 1f, C.Border);

        // 严重度左色条（辅助识别，不承担唯一表意）
        Fill(new Rect(row.x, row.y, 2f, row.height), sev);

        float x = row.x + Tok.S2;

        // 严重度：色点 + 文字
        Fill(new Rect(x + 2f, row.y + row.height * 0.5f - 4f, 8f, 8f), sev);
        Text(row, x + 14f, Tok.COL_SEV - 16f, GetLevelName(GetSeverityLevel(issue.type)), Styles.TintLeft, sev);
        x += Tok.COL_SEV;

        // 类型
        Text(row, x, Tok.COL_TYPE - Tok.S2, GetIssueTypeDisplayName(issue.type), Styles.Small, C.TextSecondary);
        x += Tok.COL_TYPE;

        // 模型
        Text(row, x, Tok.COL_MODEL - Tok.S2, new GUIContent(issue.modelName, issue.modelName), Styles.Body, C.TextPrimary);
        x += Tok.COL_MODEL;

        // 描述（超宽截断，悬停显示全文，信息不丢失）
        float descW = row.width - (x - row.x) - Tok.COL_ACT - Tok.S2;
        Text(row, x, descW, new GUIContent(issue.description, issue.description), Styles.Body, C.TextPrimary);

        // 操作：定位
        Rect btn = new Rect(row.x + row.width - Tok.COL_ACT, row.y + (Tok.ROW_H - 22f) * 0.5f, 56f, 22f);
        if (issue.gameObject == null)
        {
            Text(row, btn.x, btn.width, "无对象", Styles.SmallCenter, C.TextMuted);
        }
        else
        {
            if (Btn(btn, new GUIContent("定位", "在 Hierarchy 中选中该对象"), BtnKind.Default, true))
            {
                Selection.activeGameObject = issue.gameObject;
                EditorGUIUtility.PingObject(issue.gameObject);
            }
        }
    }

    private static void DrawPager(int page, int totalPages, int start, int end, int total,
                                 ref int pageRef, ref Vector2 scroll)
    {
        var bar = BeginRow(C.BgPanel, Styles.SurfaceFlat, GUILayout.Height(Tok.PAGER_H), GUILayout.ExpandWidth(true));
        HLine(bar, bar.y, C.Border);

        Spacer(Tok.S3);
        EditorGUILayout.LabelField("第 " + (start + 1) + "-" + end + " 条 / 共 " + total + " 条",
            Styles.Small, GUILayout.Width(170f));

        GUILayout.FlexibleSpace();

        using (new EditorGUI.DisabledScope(page <= 0))
        {
            if (Btn("上一页", "上一页（Page Up）", BtnKind.Default, 64f))
            {
                pageRef = page - 1;
                scroll = Vector2.zero;
            }
        }

        Spacer(Tok.S2);

        using (new EditorGUI.DisabledScope(page >= totalPages - 1))
        {
            if (Btn("下一页", "下一页（Page Down）", BtnKind.Default, 64f))
            {
                pageRef = page + 1;
                scroll = Vector2.zero;
            }
        }

        Spacer(Tok.S2);
        EditorGUILayout.LabelField((page + 1) + " / " + totalPages, Styles.SmallRight, GUILayout.Width(56f));
        Spacer(Tok.S3);

        EditorGUILayout.EndHorizontal();
    }

    private static void DrawEmptyState(int totalCount)
    {
        GUILayout.FlexibleSpace();

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(totalCount > 0 ? "当前筛选条件下没有结果" : "尚未开始检查",
            Styles.H2Centered, GUILayout.Width(320f));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        Spacer(Tok.S2);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(totalCount > 0
                ? "点击右上角「清除筛选」，或点击左侧类型/严重度恢复全部结果"
                : "点击右上角「开始检查」，扫描当前场景的全部模型",
            Styles.SmallCenter, GUILayout.Width(400f));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        Spacer(Tok.S3);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("检查范围：三角面数 · 材质 · 骨骼 · 贴图 · UV · 命名 · 轴心 · 变换",
            Styles.SmallCenter, GUILayout.Width(440f));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        GUILayout.FlexibleSpace();
    }

    private static void DrawLoadingState()
    {
        GUILayout.FlexibleSpace();

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("正在检查场景模型…", Styles.H2CenteredPrimary, GUILayout.Width(320f));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        Spacer(Tok.S2);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("按钮已禁用，检查完成后自动刷新结果", Styles.SmallCenter, GUILayout.Width(320f));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        GUILayout.FlexibleSpace();
    }

    #endregion

    #region 5. 状态栏

    public static void DrawStatusBar(string statusText, bool isChecking, int totalCount, int shownCount)
    {
        var bar = BeginRow(C.BgPanel, Styles.SurfaceFlat, GUILayout.Height(Tok.STATUS_H), GUILayout.ExpandWidth(true));
        HLine(bar, bar.y, C.Border);

        // 状态点：颜色 + 文字双编码
        Color dot = isChecking ? C.Warning : (totalCount > 0 ? C.Danger : C.Success);
        Fill(new Rect(bar.x + Tok.S3, bar.y + Tok.STATUS_H * 0.5f - 3f, 6f, 6f), dot);

        GUI.Label(new Rect(bar.x + Tok.S3 + 14f, bar.y, bar.width * 0.55f, Tok.STATUS_H), statusText, Styles.Small);

        string right = totalCount > 0
            ? "共 " + totalCount + " 个问题 · 当前显示 " + shownCount + " 条 · " + VERSION_INFO
            : VERSION_INFO;
        GUI.Label(new Rect(bar.x + bar.width * 0.55f, bar.y, bar.width * 0.45f - Tok.S3, Tok.STATUS_H),
            right, Styles.SmallRight);

        EditorGUILayout.EndHorizontal();
    }

    #endregion

    #region 颜色 / 分级 / 命名 / 数字

    /// <summary>严重度分级：0 高 / 1 中 / 2 低 / 3 建议</summary>
    public static int GetSeverityLevel(IssueType type)
    {
        switch (type)
        {
            case IssueType.TriangleCount:
            case IssueType.BoneCount:
                return 0;
            case IssueType.MaterialCount:
            case IssueType.TextureSize:
            case IssueType.MaterialUsage:
            case IssueType.MissingMaterial:
                return 1;
            case IssueType.Naming:
                return 3;
            default:
                return 2;
        }
    }

    public static string GetLevelName(int level)
    {
        switch (level)
        {
            case 0: return "高";
            case 1: return "中";
            case 2: return "低";
            case 3: return "建议";
            default: return "其他";
        }
    }

    public static Color GetLevelColor(int level)
    {
        switch (level)
        {
            case 0: return C.Danger;
            case 1: return C.WarningSeverity;
            case 2: return C.Info;
            case 3: return C.Success;
            default: return C.TextSecondary;
        }
    }

    private static int CountLevel(Dictionary<IssueType, int> stats, int level)
    {
        int n = 0;
        foreach (var kv in stats)
        {
            if (GetSeverityLevel(kv.Key) == level) n += kv.Value;
        }

        return n;
    }

    /// <summary>排序权重（供窗口排序使用）</summary>
    public static int GetSeverityOrder(IssueType issueType)
    {
        return GetSeverityLevel(issueType);
    }

    public static Color GetSeverityColor(IssueType issueType)
    {
        return GetLevelColor(GetSeverityLevel(issueType));
    }

    public static string GetIssueTypeDisplayName(IssueType issueType)
    {
        switch (issueType)
        {
            case IssueType.TriangleCount: return "三角面数";
            case IssueType.MaterialCount: return "含有多维材质物体";
            case IssueType.MissingMaterial: return "材质丢失";
            case IssueType.MeshTopology: return "网格拓扑";
            case IssueType.Naming: return "命名规范";
            case IssueType.Transform: return "变换设置";
            case IssueType.EmptyNode: return "空节点";
            case IssueType.TextureSize: return "贴图尺寸";
            case IssueType.TexturePowerOfTwo: return "贴图规格";
            case IssueType.TextureImportSettings: return "贴图导入";
            case IssueType.UVMapping: return "UV映射";
            case IssueType.LightmapUV: return "光照贴图UV";
            case IssueType.ImportSettings: return "导入设置";
            case IssueType.BoneCount: return "骨骼数量";
            case IssueType.MissingBone: return "骨骼丢失";
            case IssueType.Animation: return "动画设置";
            case IssueType.PivotOffset: return "轴心偏移";
            case IssueType.MaterialUsage: return "材质使用效率";
            case IssueType.UVStretch: return "UV拉伸";
            default: return issueType.ToString();
        }
    }

    /// <summary>紧凑数字：12.3K / 1.23M</summary>
    public static string FormatCompact(int number)
    {
        if (number >= 1000000) return (number / 1000000f).ToString("F2") + "M";
        if (number >= 1000) return (number / 1000f).ToString("F1") + "K";
        return number.ToString();
    }

    /// <summary>导出报告用（保持原有格式不变）</summary>
    public static string FormatNumber(int number)
    {
        if (number >= 1000000)
            return (number / 1000000f).ToString("F2") + "M" + "   警告：面数已破百万";
        if (number >= 1000)
            return (number / 1000f).ToString("F1") + "K";
        return number.ToString();
    }

    #endregion
}
