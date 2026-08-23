using System.Numerics;
using System.Reflection;
using ImGuiNET;
using Microsoft.Xna.Framework.Graphics;
using MyEngine.Core.Components;
using MyEngine.Core.ECS;
using MyEngine.Core.Physics;
using MyEngine.Core.SceneSystem;
using MyEngine.Core.Scripting;
using MyEngine.Editor.UndoSystem;

namespace MyEngine.Editor.Panels;

public static class InspectorPanel
{
    public static void Draw(EditorState state)
    {
        ImGui.Begin("Inspector");

        if (state.SelectedAsset != null)
            DrawAssetInspector(state.SelectedAsset, state);
        else if (state.Selected != null)
            DrawGameObjectInspector(state);
        else
            ImGui.TextDisabled("Nothing selected.");

        ImGui.End();
    }

    // ---------------------------------------------------------------- asset inspector

    private static void DrawAssetInspector(AssetInfo asset, EditorState state)
    {
        ImGui.Text(asset.DisplayName);
        ImGui.TextDisabled(asset.RelativePath);
        ImGui.Separator();

        if (asset.Type == AssetType.Texture && asset.Texture != null)
        {
            ImGui.Text($"{asset.Texture.Width} x {asset.Texture.Height} px");
            var avail = ImGui.GetContentRegionAvail().X;
            var previewSize = MathF.Min(avail, 256);
            var aspect = (float)asset.Texture.Height / asset.Texture.Width;
            ImGui.Image(asset.ThumbnailId, new Vector2(previewSize, previewSize * aspect));
        }
        else if (asset.Type == AssetType.Prefab)
        {
            ImGui.TextDisabled("Prefab asset — drag into the Viewport, or double-click, to instantiate.");
        }
        else if (asset.Type == AssetType.Scene)
        {
            ImGui.TextDisabled("Scene asset — double-click in the Content Browser to open it.");
        }
        else if (asset.Type == AssetType.Script)
        {
            DrawOpenInEditorButton(asset.FullPath, state);
            ImGui.Spacing();
            DrawReadOnlySource(asset.FullPath);
        }
    }

    // ---------------------------------------------------------------- gameobject inspector

    private static void DrawGameObjectInspector(EditorState state)
    {
        var go = state.Selected!;
        var undo = state.Undo;

        bool enabledBefore = go.Enabled;
        bool enabled = enabledBefore;
        if (ImGui.Checkbox("##enabled", ref enabled)) go.Enabled = enabled;
        ImGuiUndo.Track(undo, $"Toggle {go.Name}", enabledBefore, go.Enabled, v => go.Enabled = v);

        ImGui.SameLine();
        string nameBefore = go.Name;
        string name = nameBefore;
        if (ImGui.InputText("##name", ref name, 128)) go.Name = name;
        ImGuiUndo.Track(undo, "Rename GameObject", nameBefore, go.Name, v => go.Name = v);

        if (go.SourcePrefabPath != null)
            DrawPrefabLinkBar(go, state);

        ImGui.Separator();

        if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var posBefore = go.Transform.LocalPosition;
            var pos = new Vector2(posBefore.X, posBefore.Y);
            if (ImGui.DragFloat2("Position", ref pos, 1f))
                go.Transform.LocalPosition = new Microsoft.Xna.Framework.Vector2(pos.X, pos.Y);
            ImGuiUndo.Track(undo, $"Move {go.Name}", posBefore, go.Transform.LocalPosition, v => go.Transform.LocalPosition = v);

            float rotationBefore = go.Transform.LocalRotation;
            float rotationDeg = rotationBefore * (180f / MathF.PI);
            if (ImGui.DragFloat("Rotation", ref rotationDeg, 1f))
                go.Transform.LocalRotation = rotationDeg * (MathF.PI / 180f);
            ImGuiUndo.Track(undo, $"Rotate {go.Name}", rotationBefore, go.Transform.LocalRotation, v => go.Transform.LocalRotation = v);

            var scaleBefore = go.Transform.LocalScale;
            var scale = new Vector2(scaleBefore.X, scaleBefore.Y);
            if (ImGui.DragFloat2("Scale", ref scale, 0.05f))
                go.Transform.LocalScale = new Microsoft.Xna.Framework.Vector2(scale.X, scale.Y);
            ImGuiUndo.Track(undo, $"Scale {go.Name}", scaleBefore, go.Transform.LocalScale, v => go.Transform.LocalScale = v);
        }

        foreach (var component in go.Components.ToArray())
        {
            switch (component)
            {
                case SpriteRenderer sr:
                    DrawSpriteRenderer(sr, state);
                    break;
                case Camera2D cam:
                    DrawCamera(cam, state);
                    break;
                case Rigidbody2D rb:
                    DrawRigidbody(rb, state);
                    break;
                case BoxCollider2D box:
                    DrawBoxCollider(box, state);
                    break;
                case CircleCollider2D circle:
                    DrawCircleCollider(circle, state);
                    break;
                case Script script:
                    DrawScript(script, state);
                    break;
            }
        }

        ImGui.Separator();
        if (ImGui.Button("+ Add Component"))
            ImGui.OpenPopup("AddComponentPopup");

        if (ImGui.BeginPopup("AddComponentPopup"))
        {
            if (go.GetComponent<SpriteRenderer>() == null && ImGui.MenuItem("Sprite Renderer"))
                AddComponent<SpriteRenderer>(go, state, "Add Sprite Renderer");
            if (go.GetComponent<Camera2D>() == null && ImGui.MenuItem("Camera 2D"))
                AddComponent<Camera2D>(go, state, "Add Camera 2D");

            ImGui.Separator();
            if (go.GetComponent<Rigidbody2D>() == null && ImGui.MenuItem("Rigidbody 2D"))
                AddComponent<Rigidbody2D>(go, state, "Add Rigidbody 2D");
            // Multiple colliders per GameObject are supported (compound shapes), so unlike the
            // single-instance components above, these stay available even after one's been added —
            // matching Unity's "Add Component" always offering another Box/Circle Collider 2D.
            if (ImGui.MenuItem("Box Collider 2D"))
                AddComponent<BoxCollider2D>(go, state, "Add Box Collider 2D");
            if (ImGui.MenuItem("Circle Collider 2D"))
                AddComponent<CircleCollider2D>(go, state, "Add Circle Collider 2D");

            var scriptTypes = ScriptRegistry.AllTypes.OrderBy(t => t.Name).ToArray();
            if (scriptTypes.Length > 0)
            {
                ImGui.Separator();
                if (ImGui.BeginMenu("Scripts"))
                {
                    foreach (var scriptType in scriptTypes)
                    {
                        bool alreadyAdded = go.Components.Any(c => c.GetType() == scriptType);
                        if (!alreadyAdded && ImGui.MenuItem(scriptType.Name))
                            AddScriptComponent(go, state, scriptType);
                    }
                    ImGui.EndMenu();
                }
            }

            ImGui.EndPopup();
        }
    }

    private static void AddComponent<T>(GameObject go, EditorState state, string description) where T : Component, new()
    {
        var command = AddRemoveComponentCommand<T>.AddNow(description, go);
        state.Undo.Push(command);
    }

    private static void AddScriptComponent(GameObject go, EditorState state, Type scriptType)
    {
        var command = AddScriptComponentCommand.AddNow($"Add {scriptType.Name}", go, scriptType);
        state.Undo.Push(command);
    }

    private static void DrawPrefabLinkBar(GameObject go, EditorState state)
    {
        var prefabName = Path.GetFileNameWithoutExtension(go.SourcePrefabPath!);
        ImGui.TextColored(new Vector4(0.55f, 0.75f, 1f, 1f), $"Prefab Instance: {prefabName}");

        if (ImGui.SmallButton("Apply to Prefab"))
        {
            try
            {
                PrefabSerializer.ApplyToPrefab(go);
                state.LogMessage($"Applied '{go.Name}' back to its prefab file.");
            }
            catch (Exception ex) { state.LogMessage($"ERROR applying prefab: {ex.Message}"); }
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Revert"))
        {
            try
            {
                var reverted = PrefabSerializer.RevertToPrefab(go, state.ActiveScene);
                state.Assets.ResolveSceneTextures(state.ActiveScene);
                state.SelectGameObject(reverted);
                state.LogMessage($"Reverted '{reverted.Name}' to match its prefab.");
            }
            catch (Exception ex) { state.LogMessage($"ERROR reverting prefab: {ex.Message}"); }
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Unlink")) go.SourcePrefabPath = null;
    }

    private static void DrawSpriteRenderer(SpriteRenderer sr, EditorState state)
    {
        if (!ImGui.CollapsingHeader("Sprite Renderer", ImGuiTreeNodeFlags.DefaultOpen)) return;
        ImGui.PushID("sprite");
        var undo = state.Undo;

        // texture slot: shows a thumbnail if assigned, and accepts drag & drop from the Content Browser
        ImGui.Text("Texture");
        ImGui.SameLine();
        if (sr.Texture != null)
        {
            var asset = state.Assets.Find(sr.TexturePath ?? "");
            if (asset != null) ImGui.Image(asset.ThumbnailId, new Vector2(32, 32));
            else ImGui.TextDisabled("(missing)");
            ImGui.SameLine();
            ImGui.TextUnformatted(sr.TexturePath ?? "(runtime texture)");
        }
        else
        {
            ImGui.Button("Drop a texture here", new Vector2(160, 24));
        }

        if (ImGui.BeginDragDropTarget())
        {
            var dropped = DragDropPayloads.AcceptTarget(DragDropPayloads.Texture);
            if (dropped != null) SetTexture(sr, dropped, state, "Assign Texture");
            ImGui.EndDragDropTarget();
        }

        if (sr.Texture != null && ImGui.SmallButton("Clear Texture"))
            SetTexture(sr, null, state, "Clear Texture");

        var sizeBefore = sr.Size;
        var size = new Vector2(sizeBefore.X, sizeBefore.Y);
        if (ImGui.DragFloat2("Size", ref size, 1f, 1f, 4096f))
            sr.Size = new Microsoft.Xna.Framework.Vector2(size.X, size.Y);
        ImGuiUndo.Track(undo, "Resize Sprite", sizeBefore, sr.Size, v => sr.Size = v);

        var colorBefore = sr.Color;
        var color = new Vector4(colorBefore.R / 255f, colorBefore.G / 255f, colorBefore.B / 255f, colorBefore.A / 255f);
        if (ImGui.ColorEdit4("Color", ref color))
            sr.Color = new Microsoft.Xna.Framework.Color(color.X, color.Y, color.Z, color.W);
        ImGuiUndo.Track(undo, "Change Sprite Color", colorBefore, sr.Color, v => sr.Color = v);

        int sortingBefore = sr.SortingOrder;
        int sorting = sortingBefore;
        if (ImGui.DragInt("Sorting Order", ref sorting)) sr.SortingOrder = sorting;
        ImGuiUndo.Track(undo, "Change Sorting Order", sortingBefore, sr.SortingOrder, v => sr.SortingOrder = v);

        ImGui.PopID();
    }

    private static void SetTexture(SpriteRenderer sr, string? path, EditorState state, string description)
    {
        var oldValue = (sr.TexturePath, sr.Texture);
        var newTexture = state.Assets.GetTexture(path);
        var newValue = (path, newTexture);

        sr.TexturePath = path;
        sr.Texture = newTexture;

        if (!oldValue.Equals(newValue))
        {
            state.Undo.Push(new PropertyChangeCommand<(string? Path, Texture2D? Texture)>(
                description,
                v => { sr.TexturePath = v.Path; sr.Texture = v.Texture; },
                oldValue, newValue));
        }
    }

    private static void DrawCamera(Camera2D cam, EditorState state)
    {
        if (!ImGui.CollapsingHeader("Camera 2D", ImGuiTreeNodeFlags.DefaultOpen)) return;
        ImGui.PushID("camera");
        var undo = state.Undo;

        bool activeBefore = cam.IsActive;
        bool active = activeBefore;
        if (ImGui.Checkbox("Active", ref active)) cam.IsActive = active;
        ImGuiUndo.Track(undo, "Toggle Camera Active", activeBefore, cam.IsActive, v => cam.IsActive = v);

        float zoomBefore = cam.Zoom;
        float zoom = zoomBefore;
        if (ImGui.DragFloat("Zoom", ref zoom, 0.05f, 0.05f, 10f)) cam.Zoom = zoom;
        ImGuiUndo.Track(undo, "Change Camera Zoom", zoomBefore, cam.Zoom, v => cam.Zoom = v);

        var bgBefore = cam.BackgroundColor;
        var bg = new Vector3(bgBefore.R / 255f, bgBefore.G / 255f, bgBefore.B / 255f);
        if (ImGui.ColorEdit3("Background", ref bg))
            cam.BackgroundColor = new Microsoft.Xna.Framework.Color(bg.X, bg.Y, bg.Z);
        ImGuiUndo.Track(undo, "Change Camera Background", bgBefore, cam.BackgroundColor, v => cam.BackgroundColor = v);

        ImGui.Separator();
        ImGui.TextDisabled("Follow");

        string followName = cam.FollowTarget?.Name ?? "(none)";
        ImGui.TextUnformatted($"Target: {followName}");
        if (cam.FollowTarget != null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear")) SetFollowTarget(cam, null, undo);
        }

        var candidates = state.ActiveScene.GameObjects.Where(g => g != cam.Owner).ToArray();
        if (candidates.Length > 0 && ImGui.BeginCombo("Set Target", "Choose a GameObject..."))
        {
            foreach (var candidate in candidates)
            {
                if (ImGui.Selectable(candidate.Name, candidate == cam.FollowTarget))
                    SetFollowTarget(cam, candidate, undo);
            }
            ImGui.EndCombo();
        }

        float smoothingBefore = cam.FollowSmoothing;
        float smoothing = smoothingBefore;
        if (ImGui.SliderFloat("Smoothing", ref smoothing, 0.01f, 1f)) cam.FollowSmoothing = smoothing;
        ImGuiUndo.Track(undo, "Change Follow Smoothing", smoothingBefore, cam.FollowSmoothing, v => cam.FollowSmoothing = v);

        ImGui.PopID();
    }

    private static void SetFollowTarget(Camera2D cam, GameObject? target, UndoStack undo)
    {
        var old = cam.FollowTarget;
        if (old == target) return;
        cam.FollowTarget = target;
        undo.Push(new PropertyChangeCommand<GameObject?>("Change Follow Target", v => cam.FollowTarget = v, old, target));
    }

    // ---------------------------------------------------------------- physics components

    private static readonly string[] BodyTypeNames = { "Dynamic", "Kinematic", "Static" };

    private static void DrawRigidbody(Rigidbody2D rb, EditorState state)
    {
        if (!ImGui.CollapsingHeader("Rigidbody 2D", ImGuiTreeNodeFlags.DefaultOpen)) return;
        ImGui.PushID("rigidbody2d");
        var undo = state.Undo;

        var bodyTypeBefore = rb.BodyType;
        int bodyTypeIndex = (int)bodyTypeBefore;
        if (ImGui.Combo("Body Type", ref bodyTypeIndex, BodyTypeNames, BodyTypeNames.Length))
            rb.BodyType = (BodyType2D)bodyTypeIndex;
        ImGuiUndo.Track(undo, "Change Body Type", bodyTypeBefore, rb.BodyType, v => rb.BodyType = v);

        float massBefore = rb.Mass;
        float mass = massBefore;
        if (ImGui.DragFloat("Mass", ref mass, 0.05f, 0.01f, 1000f)) rb.Mass = mass;
        ImGuiUndo.Track(undo, "Change Mass", massBefore, rb.Mass, v => rb.Mass = v);

        float gravityScaleBefore = rb.GravityScale;
        float gravityScale = gravityScaleBefore;
        if (ImGui.DragFloat("Gravity Scale", ref gravityScale, 0.05f)) rb.GravityScale = gravityScale;
        ImGuiUndo.Track(undo, "Change Gravity Scale", gravityScaleBefore, rb.GravityScale, v => rb.GravityScale = v);

        float linearDampingBefore = rb.LinearDamping;
        float linearDamping = linearDampingBefore;
        if (ImGui.DragFloat("Linear Damping", ref linearDamping, 0.01f, 0f, 100f)) rb.LinearDamping = linearDamping;
        ImGuiUndo.Track(undo, "Change Linear Damping", linearDampingBefore, rb.LinearDamping, v => rb.LinearDamping = v);

        float angularDampingBefore = rb.AngularDamping;
        float angularDamping = angularDampingBefore;
        if (ImGui.DragFloat("Angular Damping", ref angularDamping, 0.01f, 0f, 100f)) rb.AngularDamping = angularDamping;
        ImGuiUndo.Track(undo, "Change Angular Damping", angularDampingBefore, rb.AngularDamping, v => rb.AngularDamping = v);

        bool freezeBefore = rb.FreezeRotation;
        bool freeze = freezeBefore;
        if (ImGui.Checkbox("Freeze Rotation", ref freeze)) rb.FreezeRotation = freeze;
        ImGuiUndo.Track(undo, "Toggle Freeze Rotation", freezeBefore, rb.FreezeRotation, v => rb.FreezeRotation = v);

        ImGui.PopID();
    }

    private static void DrawBoxCollider(BoxCollider2D box, EditorState state)
    {
        if (!ImGui.CollapsingHeader("Box Collider 2D", ImGuiTreeNodeFlags.DefaultOpen)) return;
        ImGui.PushID("boxCollider2d");
        var undo = state.Undo;

        var sizeBefore = box.Size;
        var size = new Vector2(sizeBefore.X, sizeBefore.Y);
        if (ImGui.DragFloat2("Size", ref size, 1f, 0.01f, 8192f))
            box.Size = new Microsoft.Xna.Framework.Vector2(size.X, size.Y);
        ImGuiUndo.Track(undo, "Resize Box Collider", sizeBefore, box.Size, v => box.Size = v);

        DrawColliderCommonFields(box, undo);

        ImGui.PopID();
    }

    private static void DrawCircleCollider(CircleCollider2D circle, EditorState state)
    {
        if (!ImGui.CollapsingHeader("Circle Collider 2D", ImGuiTreeNodeFlags.DefaultOpen)) return;
        ImGui.PushID("circleCollider2d");
        var undo = state.Undo;

        float radiusBefore = circle.Radius;
        float radius = radiusBefore;
        if (ImGui.DragFloat("Radius", ref radius, 0.5f, 0.01f, 4096f)) circle.Radius = radius;
        ImGuiUndo.Track(undo, "Resize Circle Collider", radiusBefore, circle.Radius, v => circle.Radius = v);

        DrawColliderCommonFields(circle, undo);

        ImGui.PopID();
    }

    /// <summary>Offset/IsTrigger/Friction/Restitution — identical across every Collider2D subtype, so both
    /// DrawBoxCollider and DrawCircleCollider share this rather than repeating it.</summary>
    private static void DrawColliderCommonFields(Collider2D collider, UndoStack undo)
    {
        var offsetBefore = collider.Offset;
        var offset = new Vector2(offsetBefore.X, offsetBefore.Y);
        if (ImGui.DragFloat2("Offset", ref offset, 1f))
            collider.Offset = new Microsoft.Xna.Framework.Vector2(offset.X, offset.Y);
        ImGuiUndo.Track(undo, "Move Collider Offset", offsetBefore, collider.Offset, v => collider.Offset = v);

        bool triggerBefore = collider.IsTrigger;
        bool trigger = triggerBefore;
        if (ImGui.Checkbox("Is Trigger", ref trigger)) collider.IsTrigger = trigger;
        ImGuiUndo.Track(undo, "Toggle Is Trigger", triggerBefore, collider.IsTrigger, v => collider.IsTrigger = v);

        float frictionBefore = collider.Friction;
        float friction = frictionBefore;
        if (ImGui.SliderFloat("Friction", ref friction, 0f, 1f)) collider.Friction = friction;
        ImGuiUndo.Track(undo, "Change Friction", frictionBefore, collider.Friction, v => collider.Friction = v);

        float restitutionBefore = collider.Restitution;
        float restitution = restitutionBefore;
        if (ImGui.SliderFloat("Restitution", ref restitution, 0f, 1f)) collider.Restitution = restitution;
        ImGuiUndo.Track(undo, "Change Restitution", restitutionBefore, collider.Restitution, v => collider.Restitution = v);

        float densityBefore = collider.Density;
        float density = densityBefore;
        if (ImGui.DragFloat("Density", ref density, 0.05f, 0.01f, 1000f)) collider.Density = density;
        ImGuiUndo.Track(undo, "Change Density", densityBefore, collider.Density, v => collider.Density = v);
    }

    // ---------------------------------------------------------------- script components

    private static void DrawScript(Script script, EditorState state)
    {
        var type = script.GetType();
        if (!ImGui.CollapsingHeader($"{type.Name} (Script)", ImGuiTreeNodeFlags.DefaultOpen)) return;
        ImGui.PushID(type.Name);

        var fields = ScriptSerialization.GetEditableFields(type).ToArray();
        if (fields.Length == 0)
            ImGui.TextDisabled("No exposed fields.");
        else
            foreach (var field in fields)
                DrawScriptField(script, field, state.Undo);

        ImGui.Spacing();

        var asset = state.Assets.FindScriptAsset(type.Name);
        if (asset != null)
        {
            DrawOpenInEditorButton(asset.FullPath, state);
            ImGui.Spacing();
            DrawReadOnlySource(asset.FullPath);
        }
        else
        {
            ImGui.TextDisabled("(source file not found — was it renamed or deleted?)");
        }

        ImGui.PopID();
    }

    /// <summary>Renders one exposed field with the ImGui widget matching its type, wired into Undo the
    /// same way every other Inspector field is. Supported types must match ScriptSerialization exactly —
    /// they're read from the same place so the Inspector and scene-persistence never disagree about which
    /// fields are exposed.</summary>
    private static void DrawScriptField(Script script, FieldInfo field, UndoStack undo)
    {
        string label = field.Name;

        if (field.FieldType == typeof(float))
        {
            float before = (float)(field.GetValue(script) ?? 0f);
            float v = before;
            if (ImGui.DragFloat(label, ref v, 0.1f)) field.SetValue(script, v);
            ImGuiUndo.Track(undo, $"Change {field.Name}", before, (float)field.GetValue(script)!, x => field.SetValue(script, x));
        }
        else if (field.FieldType == typeof(int))
        {
            int before = (int)(field.GetValue(script) ?? 0);
            int v = before;
            if (ImGui.DragInt(label, ref v)) field.SetValue(script, v);
            ImGuiUndo.Track(undo, $"Change {field.Name}", before, (int)field.GetValue(script)!, x => field.SetValue(script, x));
        }
        else if (field.FieldType == typeof(bool))
        {
            bool before = (bool)(field.GetValue(script) ?? false);
            bool v = before;
            if (ImGui.Checkbox(label, ref v)) field.SetValue(script, v);
            ImGuiUndo.Track(undo, $"Change {field.Name}", before, (bool)field.GetValue(script)!, x => field.SetValue(script, x));
        }
        else if (field.FieldType == typeof(string))
        {
            string before = (string)(field.GetValue(script) ?? "");
            string v = before;
            if (ImGui.InputText(label, ref v, 512)) field.SetValue(script, v);
            ImGuiUndo.Track(undo, $"Change {field.Name}", before, (string)(field.GetValue(script) ?? ""), x => field.SetValue(script, x));
        }
    }

    private static void DrawOpenInEditorButton(string filePath, EditorState state)
    {
        if (!ImGui.SmallButton("Open in Editor")) return;
        var error = ExternalEditorLauncher.Open(filePath);
        if (error != null) state.LogMessage($"ERROR: {error}");
    }

    // Cached so the Inspector doesn't re-read a script's source file from disk every single frame it's
    // shown — invalidated automatically whenever the file's last-write-time changes (e.g. saved in VS Code),
    // so the preview stays live without needing an explicit refresh.
    private static readonly Dictionary<string, (string Content, DateTime WriteTimeUtc)> SourceCache = new();

    private static void DrawReadOnlySource(string filePath)
    {
        string source;
        try
        {
            var writeTime = File.GetLastWriteTimeUtc(filePath);
            if (SourceCache.TryGetValue(filePath, out var cached) && cached.WriteTimeUtc == writeTime)
            {
                source = cached.Content;
            }
            else
            {
                source = File.ReadAllText(filePath);
                SourceCache[filePath] = (source, writeTime);
            }
        }
        catch (Exception ex)
        {
            ImGui.TextDisabled($"(could not read file: {ex.Message})");
            return;
        }

        ImGui.TextDisabled("Source (read-only):");
        ImGui.InputTextMultiline(
            "##source", ref source, (uint)(source.Length + 1),
            new Vector2(-1, 220), ImGuiInputTextFlags.ReadOnly);
    }
}
