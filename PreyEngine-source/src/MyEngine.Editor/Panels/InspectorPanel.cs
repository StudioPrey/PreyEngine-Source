using System.Numerics;
using ImGuiNET;
using Microsoft.Xna.Framework.Graphics;
using MyEngine.Core.Components;
using MyEngine.Core.ECS;
using MyEngine.Core.SceneSystem;
using MyEngine.Editor.UndoSystem;

namespace MyEngine.Editor.Panels;

public static class InspectorPanel
{
    public static void Draw(EditorState state)
    {
        ImGui.Begin("Inspector");

        if (state.SelectedAsset != null)
            DrawAssetInspector(state.SelectedAsset);
        else if (state.Selected != null)
            DrawGameObjectInspector(state);
        else
            ImGui.TextDisabled("Nothing selected.");

        ImGui.End();
    }

    // ---------------------------------------------------------------- asset inspector

    private static void DrawAssetInspector(AssetInfo asset)
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
            ImGui.EndPopup();
        }
    }

    private static void AddComponent<T>(GameObject go, EditorState state, string description) where T : Component, new()
    {
        var command = AddRemoveComponentCommand<T>.AddNow(description, go);
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
}
