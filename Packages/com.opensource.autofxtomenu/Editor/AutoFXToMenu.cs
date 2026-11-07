using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using Control = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;

// Tools > Auto FX To Menu
// Scans an FX controller for "Category/Item" float parameters and builds the
// VRC Expression Parameters + Menus (paged at 8 controls per menu).
public class AutoFXToMenu : EditorWindow
{
    const int MaxControls = 8;

    enum Kind { Bool, Int, Float }

    class Entry
    {
        public string param;     // full FX parameter name, e.g. "Tops/Hoodie"
        public string category;  // editable
        public string label;     // shown in the menu
        public bool include = true;
        public Kind kind;
        public bool isInt => kind == Kind.Int;
        public int values = 2;   // Int only: number of states (0..values-1)
    }

    AnimatorController fx;
    bool makeIcons;
    GameObject avatar; // needed when makeIcons is on
    GameObject iconRoot; // throwaway clone of avatar the icons are shot from
    VRCExpressionsMenu mainMenu;
    VRCExpressionParameters vrcParams;
    List<Entry> entries = new List<Entry>();
    Vector2 scroll;

    [MenuItem("Tools/Auto FX To Menu")]
    static void Open() => GetWindow<AutoFXToMenu>("Auto FX To Menu");

    void OnGUI()
    {
        fx = (AnimatorController)EditorGUILayout.ObjectField("FX Controller", fx, typeof(AnimatorController), false);
        mainMenu = (VRCExpressionsMenu)EditorGUILayout.ObjectField("Main Menu", mainMenu, typeof(VRCExpressionsMenu), false);
        vrcParams = (VRCExpressionParameters)EditorGUILayout.ObjectField("Parameters", vrcParams, typeof(VRCExpressionParameters), false);

        makeIcons = EditorGUILayout.Toggle(new GUIContent("Generate icons", "Photograph an icon for each Bool toggle from its animation clip."), makeIcons);
        if (makeIcons)
            avatar = (GameObject)EditorGUILayout.ObjectField("Avatar (scene object)", avatar, typeof(GameObject), true);

        using (new EditorGUI.DisabledScope(fx == null))
            if (GUILayout.Button("Scan FX Layer")) Scan();

        if (entries.Count == 0) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Review what the scan found (fix the category or type; set Float for sliders like Hue Shifts if it guessed wrong):", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var e in entries)
        {
            EditorGUILayout.BeginHorizontal();
            e.include = EditorGUILayout.Toggle(e.include, GUILayout.Width(16));
            e.category = EditorGUILayout.TextField(e.category, GUILayout.Width(110));
            EditorGUILayout.LabelField(e.label, GUILayout.MinWidth(80));
            e.kind = (Kind)EditorGUILayout.Popup((int)e.kind, new[] { "Bool", "Int", "Float" }, GUILayout.Width(55));
            if (e.isInt) e.values = Mathf.Clamp(EditorGUILayout.IntField(e.values, GUILayout.Width(40)), 2, 255);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        int cost = entries.Where(e => e.include).Sum(e => e.kind == Kind.Bool ? 1 : 8);
        EditorGUILayout.HelpBox($"New parameter memory: {cost} bits (avatar limit is 256 total).", MessageType.Info);

        if (makeIcons && avatar == null)
            EditorGUILayout.HelpBox("Icons are on but no avatar is set, so no icons will be made.", MessageType.Warning);

        using (new EditorGUI.DisabledScope(mainMenu == null || vrcParams == null))
            if (GUILayout.Button("Generate Menus + Parameters")) Generate();
    }

    void Scan()
    {
        var treeCounts = new Dictionary<string, int>();
        foreach (var layer in fx.layers) CollectTrees(layer.stateMachine, treeCounts);

        entries = fx.parameters
            .Where(p => p.type == AnimatorControllerParameterType.Float && p.name.Contains("/"))
            .Select(p =>
            {
                int i = p.name.IndexOf('/');
                treeCounts.TryGetValue(p.name, out int n);
                // Int values run 1..N, so max threshold = N. >2 states in a 1D tree means a swap, not an on/off toggle
                return new Entry { param = p.name, category = p.name.Substring(0, i), label = p.name.Substring(i + 1), kind = p.name.IndexOf("Hue", StringComparison.OrdinalIgnoreCase) >= 0 ? Kind.Float : n > 2 ? Kind.Int : Kind.Bool, values = Mathf.Max(n, 2) };
            }).ToList();
    }

    static void CollectTrees(AnimatorStateMachine sm, Dictionary<string, int> counts)
    {
        foreach (var s in sm.states) if (s.state.motion is BlendTree bt) CollectTree(bt, counts);
        foreach (var sub in sm.stateMachines) CollectTrees(sub.stateMachine, counts);
    }

    static void CollectTree(BlendTree bt, Dictionary<string, int> counts)
    {
        if (bt.blendType == BlendTreeType.Simple1D)
            counts[bt.blendParameter] = Mathf.Max(counts.TryGetValue(bt.blendParameter, out int c) ? c : 0,
                bt.children.Length == 0 ? 0 : Mathf.RoundToInt(bt.children.Max(ch => ch.threshold)));
        foreach (var ch in bt.children) if (ch.motion is BlendTree sub) CollectTree(sub, counts);
    }

    void Generate()
    {
        var chosen = entries.Where(e => e.include).ToList();
        string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(mainMenu)) + "/Generated";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Path.GetDirectoryName(dir), "Generated");

        // Parameters: add missing, fix type on existing
        var list = vrcParams.parameters.ToList();
        foreach (var e in chosen)
        {
            var type = e.kind == Kind.Float ? VRCExpressionParameters.ValueType.Float : e.isInt ? VRCExpressionParameters.ValueType.Int : VRCExpressionParameters.ValueType.Bool;
            int idx = list.FindIndex(p => p.name == e.param);
            if (idx >= 0) list[idx].valueType = type;
            else list.Add(new VRCExpressionParameters.Parameter { name = e.param, valueType = type, saved = true, networkSynced = true });
        }
        vrcParams.parameters = list.ToArray();
        EditorUtility.SetDirty(vrcParams);

        var icons = new Dictionary<string, Texture2D>();
        if (makeIcons && avatar != null)
        {
            var onClips = new Dictionary<string, AnimationClip>();
            foreach (var layer in fx.layers) FindOnClips(layer.stateMachine, onClips);
            string iconDir = dir + "/Icons";
            if (!AssetDatabase.IsValidFolder(iconDir)) AssetDatabase.CreateFolder(dir, "Icons");
            var bools = chosen.Where(e => e.kind == Kind.Bool).ToList();
            Debug.Log($"[AutoFXToMenu] Icons: {bools.Count} Bool toggles, {onClips.Count} Direct blend tree clips found, {bools.Count(e => onClips.ContainsKey(e.param))} matched.");
            // work on a far-away clone with every toggleable thing off, so the real avatar is never touched
            iconRoot = Instantiate(avatar, avatar.transform.position + Vector3.right * 1000, avatar.transform.rotation);
            try
            {
                foreach (var clip in onClips.Values.Distinct()) AllOff(clip);
                foreach (var e in bools.Where(e => onClips.ContainsKey(e.param)))
                {
                    var icon = CaptureIcon(onClips[e.param], $"{iconDir}/{e.param.Replace("/", "_")}.png");
                    if (icon != null) icons[e.param] = icon;
                }
            }
            finally { DestroyImmediate(iconRoot); }
        }

        // One submenu per category, replacing any same-named control on the main menu
        var categories = chosen.GroupBy(e => e.category).ToList();
        mainMenu.controls.RemoveAll(c => c.type == Control.ControlType.SubMenu && categories.Any(g => g.Key == c.name));
        foreach (var g in categories)
        {
            var controls = g.Select(e => e.kind == Kind.Int ? IntSubMenu(e, dir) : e.kind == Kind.Float ? Radial(e) : Toggle(e.label, e.param, 1, icons.TryGetValue(e.param, out var ic) ? ic : null)).ToList();
            mainMenu.controls.Add(new Control { name = g.Key, type = Control.ControlType.SubMenu, subMenu = BuildPaged(g.Key, controls, dir) });
        }
        EditorUtility.SetDirty(mainMenu);
        AssetDatabase.SaveAssets();

        if (mainMenu.controls.Count > MaxControls)
            EditorUtility.DisplayDialog("Main menu too big", $"Main menu now has {mainMenu.controls.Count} controls; VRChat shows only {MaxControls}. Move some by hand.", "OK");
    }

    // Direct blend tree children: the clip is the "on" state of its direct parameter
    static void FindOnClips(AnimatorStateMachine sm, Dictionary<string, AnimationClip> clips)
    {
        foreach (var st in sm.states) if (st.state.motion is BlendTree bt) FindOnClips(bt, clips);
        foreach (var sub in sm.stateMachines) FindOnClips(sub.stateMachine, clips);
    }

    static void FindOnClips(BlendTree bt, Dictionary<string, AnimationClip> clips)
    {
        // 1D tree on the item's own float: the clip at the highest threshold is "on"
        var onChild = bt.blendType == BlendTreeType.Simple1D
            ? bt.children.Where(ch => ch.motion is AnimationClip).OrderByDescending(ch => ch.threshold).FirstOrDefault()
            : default;
        if (onChild.motion is AnimationClip onClip) clips[bt.blendParameter] = onClip;

        foreach (var ch in bt.children)
        {
            if (ch.motion is AnimationClip c && bt.blendType == BlendTreeType.Direct && !string.IsNullOrEmpty(ch.directBlendParameter))
                clips[ch.directBlendParameter] = c;
            if (ch.motion is BlendTree sub) FindOnClips(sub, clips);
        }
    }

    static bool Visible(Renderer r) => r.gameObject.activeInHierarchy && r.enabled && r.bounds.size != Vector3.zero;

    // Switches off what the clip would switch on, on the clone.
    void AllOff(AnimationClip clip)
    {
        foreach (var b in AnimationUtility.GetCurveBindings(clip))
        {
            var t = b.path == "" ? null : iconRoot.transform.Find(b.path);
            if (t == null) continue;
            if (b.type == typeof(GameObject)) t.gameObject.SetActive(false);
            else if (b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalScale")) t.localScale = Vector3.zero;
            else if (typeof(Renderer).IsAssignableFrom(b.type) && b.propertyName == "m_Enabled" && t.GetComponent(b.type) is Renderer r) r.enabled = false;
        }
    }

    // Poses the avatar with the clip, shows only the renderers it touches, renders them front-on.
    // shortcut: front view, orthographic, scene lighting, clips that only swap materials give no icon; upgrade with a per-category camera angle.
    Texture2D CaptureIcon(AnimationClip clip, string path)
    {
        var targets = new HashSet<Renderer>();
        foreach (var b in AnimationUtility.GetCurveBindings(clip))
        {
            var t = b.path == "" ? null : iconRoot.transform.Find(b.path);
            if (t != null) targets.UnionWith(t.GetComponentsInChildren<Renderer>(true));
        }
        if (targets.Count == 0) { Debug.LogWarning($"[AutoFXToMenu] {clip.name}: no clip path matches a renderer under '{iconRoot.name}'."); return null; }

        var all = iconRoot.GetComponentsInChildren<Renderer>(true);
        var wasEnabled = all.ToDictionary(r => r, r => r.enabled);
        var rt = RenderTexture.GetTemporary(256, 256, 24, RenderTextureFormat.ARGB32);
        var cam = new GameObject("IconCam") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
        var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        var bones = iconRoot.GetComponentsInChildren<Transform>(true);
        var rest = bones.ToDictionary(t => t, t => (t.localPosition, t.localRotation, t.localScale));
        var animated = new HashSet<string>(AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform)).Select(b => b.path));
        var visibleBefore = new HashSet<Renderer>(targets.Where(Visible));
        bool asyncWas = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false; // async compile shows cyan placeholder shaders
        AnimationMode.StartAnimationMode();
        try
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(iconRoot, clip, 0);
            AnimationMode.EndSampling();

            // sampling re-poses every bone (bicycle pose); keep the ones the clip doesn't animate where they were
            foreach (var t in bones)
                if (!animated.Contains(AnimationUtility.CalculateTransformPath(t, iconRoot.transform)))
                    (t.localPosition, t.localRotation, t.localScale) = rest[t];

            // scale-0 and inactive renderers (hidden by the clip) have no useful bounds
            var shown = targets.Where(Visible).ToList();
            // only what this toggle turns on; shortcut: if it is already on in the scene, everything the clip touches shows
            var added = shown.Where(r => !visibleBefore.Contains(r)).ToList();
            if (added.Count > 0) shown = added;
            if (shown.Count == 0) { Debug.LogWarning($"[AutoFXToMenu] {clip.name}: {targets.Count} renderers, none visible after posing."); return null; }
            foreach (var r in all) r.enabled = shown.Contains(r);

            var bounds = shown[0].bounds;
            foreach (var r in shown) bounds.Encapsulate(r.bounds);

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.1f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100;
            cam.transform.position = bounds.center + iconRoot.transform.forward * 50;
            cam.transform.rotation = Quaternion.LookRotation(-iconRoot.transform.forward, Vector3.up);
            cam.targetTexture = rt;
            cam.Render(); // warm-up
            cam.Render();

            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }
        finally
        {
            foreach (var kv in wasEnabled) if (kv.Key) kv.Key.enabled = kv.Value;
            AnimationMode.StopAnimationMode();
            ShaderUtil.allowAsyncCompilation = asyncWas;
            RenderTexture.ReleaseTemporary(rt);
            DestroyImmediate(cam.gameObject);
            DestroyImmediate(tex);
        }

        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.maxTextureSize = 256;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Control Toggle(string name, string param, float value, Texture2D icon = null) => new Control
    {
        name = name, icon = icon, type = Control.ControlType.Toggle, value = value,
        parameter = new Control.Parameter { name = param }
    };

    // Float (e.g. Hue Shift): radial puppet driving the parameter 0-1
    static Control Radial(Entry e) => new Control
    {
        name = e.label, type = Control.ControlType.RadialPuppet,
        subParameters = new[] { new Control.Parameter { name = e.param } }
    };

    static Control IntSubMenu(Entry e, string dir)
    {
        var buttons = Enumerable.Range(0, e.values).Select(i => Toggle($"{e.label} {i + 1}", e.param, i + 1)).ToList();
        return new Control { name = e.label, type = Control.ControlType.SubMenu, subMenu = BuildPaged(e.category + "_" + e.label, buttons, dir) };
    }

    // <=8 controls: one menu. Otherwise 7 + a "Page N" link, chained.
    // Child pages are built first: creating an asset can reload unsaved ones, so each menu is filled in last.
    static VRCExpressionsMenu BuildPaged(string name, List<Control> controls, string dir, int page = 1)
    {
        var shown = controls.Take(controls.Count <= MaxControls ? MaxControls : MaxControls - 1).ToList();
        if (controls.Count > MaxControls)
            shown.Add(new Control
            {
                name = $"Page {page + 1}", type = Control.ControlType.SubMenu,
                subMenu = BuildPaged(name, controls.Skip(MaxControls - 1).ToList(), dir, page + 1)
            });

        string path = $"{dir}/Menu_{name.Replace("/", "_")}{(page > 1 ? "_P" + page : "")}.asset";
        var menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(path);
        if (menu == null) { menu = CreateInstance<VRCExpressionsMenu>(); AssetDatabase.CreateAsset(menu, path); }
        menu.controls = shown;
        EditorUtility.SetDirty(menu);
        AssetDatabase.SaveAssetIfDirty(menu); // on disk before the next CreateAsset can discard it
        return menu;
    }
}
