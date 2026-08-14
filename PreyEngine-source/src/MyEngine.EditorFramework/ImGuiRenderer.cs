using System.Runtime.InteropServices;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MyEngine.EditorFramework;

/// <summary>
/// ImGui renderer/input-backend for MonoGame (DesktopGL).
///
/// This is adapted directly from the official ImGui.NET MonoGame/XNA sample
/// (github.com/ImGuiNET/ImGui.NET, src/ImGui.NET.SampleProgram.XNA/ImGuiRenderer.cs) rather than
/// written from scratch, specifically to stay in sync with whatever MonoGame/ImGui.NET version
/// quirks that sample already accounts for (e.g. VertexBuffer/IndexBuffer only expose the plain
/// byte[]-based SetData overload — the SetDataOptions overloads belong to DynamicVertexBuffer/
/// DynamicIndexBuffer, not the buffers used here).
///
/// Usage inside a Game:
///   _imGui = new ImGuiRenderer(this);
///   _imGui.RebuildFontAtlas();
///   // each frame:
///   _imGui.BeforeLayout(gameTime);
///   ... ImGui.* calls building your UI ...
///   _imGui.AfterLayout();
/// </summary>
public class ImGuiRenderer
{
    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;

    private readonly Dictionary<IntPtr, Texture2D> _loadedTextures = new();
    private int _textureId;
    private IntPtr? _fontTextureId;

    private BasicEffect? _effect;
    private readonly RasterizerState _rasterizerState;

    private byte[] _vertexData = Array.Empty<byte>();
    private VertexBuffer? _vertexBuffer;
    private int _vertexBufferSize;

    private byte[] _indexData = Array.Empty<byte>();
    private IndexBuffer? _indexBuffer;
    private int _indexBufferSize;

    private int _scrollWheelValue;
    private int _horizontalScrollWheelValue;
    private const float WheelDelta = 120f;
    private static readonly Keys[] AllKeys = Enum.GetValues<Keys>();

    public ImGuiRenderer(Game game)
    {
        var context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);

        _game = game ?? throw new ArgumentNullException(nameof(game));
        _graphicsDevice = game.GraphicsDevice;

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        _rasterizerState = new RasterizerState
        {
            CullMode = CullMode.None,
            DepthBias = 0,
            FillMode = FillMode.Solid,
            MultiSampleAntiAlias = false,
            ScissorTestEnable = true,
            SlopeScaleDepthBias = 0,
        };

        // MonoGame-specific text input -> ImGui characters (needed for text fields to work at all).
        _game.Window.TextInput += (_, e) =>
        {
            if (e.Character == '\t') return;
            io.AddInputCharacter(e.Character);
        };
    }

    // ---------------------------------------------------------------- fonts

    public unsafe void RebuildFontAtlas()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixelData, out int width, out int height, out int bytesPerPixel);

        var pixels = new byte[width * height * bytesPerPixel];
        Marshal.Copy(new IntPtr(pixelData), pixels, 0, pixels.Length);

        var texture = new Texture2D(_graphicsDevice, width, height, false, SurfaceFormat.Color);
        texture.SetData(pixels);

        if (_fontTextureId.HasValue)
            UnbindTexture(_fontTextureId.Value);

        _fontTextureId = BindTexture(texture);
        io.Fonts.SetTexID(_fontTextureId.Value);
        io.Fonts.ClearTexData();
    }

    public IntPtr BindTexture(Texture2D texture)
    {
        var id = new IntPtr(_textureId++);
        _loadedTextures[id] = texture;
        return id;
    }

    public void UnbindTexture(IntPtr textureId) => _loadedTextures.Remove(textureId);

    // ---------------------------------------------------------------- frame

    public void BeforeLayout(GameTime gameTime)
    {
        ImGui.GetIO().DeltaTime = MathF.Max((float)gameTime.ElapsedGameTime.TotalSeconds, 1f / 1000f);
        UpdateInput();
        ImGui.NewFrame();
    }

    public void AfterLayout()
    {
        ImGui.Render();
        unsafe { RenderDrawData(ImGui.GetDrawData()); }
    }

    // ---------------------------------------------------------------- input

    private void UpdateInput()
    {
        if (!_game.IsActive) return;

        var io = ImGui.GetIO();
        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();

        io.AddMousePosEvent(mouse.X, mouse.Y);
        io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(3, mouse.XButton1 == ButtonState.Pressed);
        io.AddMouseButtonEvent(4, mouse.XButton2 == ButtonState.Pressed);

        io.AddMouseWheelEvent(
            (mouse.HorizontalScrollWheelValue - _horizontalScrollWheelValue) / WheelDelta,
            (mouse.ScrollWheelValue - _scrollWheelValue) / WheelDelta);
        _scrollWheelValue = mouse.ScrollWheelValue;
        _horizontalScrollWheelValue = mouse.HorizontalScrollWheelValue;

        foreach (var key in AllKeys)
        {
            if (TryMapKey(key, out var imguiKey))
                io.AddKeyEvent(imguiKey, keyboard.IsKeyDown(key));
        }

        io.DisplaySize = new System.Numerics.Vector2(
            _graphicsDevice.PresentationParameters.BackBufferWidth,
            _graphicsDevice.PresentationParameters.BackBufferHeight);
        io.DisplayFramebufferScale = System.Numerics.Vector2.One;
    }

    private static bool TryMapKey(Keys key, out ImGuiKey imguiKey)
    {
        if (key == Keys.None)
        {
            imguiKey = ImGuiKey.None;
            return false;
        }

        imguiKey = key switch
        {
            Keys.Back => ImGuiKey.Backspace,
            Keys.Tab => ImGuiKey.Tab,
            Keys.Enter => ImGuiKey.Enter,
            Keys.CapsLock => ImGuiKey.CapsLock,
            Keys.Escape => ImGuiKey.Escape,
            Keys.Space => ImGuiKey.Space,
            Keys.PageUp => ImGuiKey.PageUp,
            Keys.PageDown => ImGuiKey.PageDown,
            Keys.End => ImGuiKey.End,
            Keys.Home => ImGuiKey.Home,
            Keys.Left => ImGuiKey.LeftArrow,
            Keys.Right => ImGuiKey.RightArrow,
            Keys.Up => ImGuiKey.UpArrow,
            Keys.Down => ImGuiKey.DownArrow,
            Keys.PrintScreen => ImGuiKey.PrintScreen,
            Keys.Insert => ImGuiKey.Insert,
            Keys.Delete => ImGuiKey.Delete,
            >= Keys.D0 and <= Keys.D9 => ImGuiKey._0 + (key - Keys.D0),
            >= Keys.A and <= Keys.Z => ImGuiKey.A + (key - Keys.A),
            >= Keys.NumPad0 and <= Keys.NumPad9 => ImGuiKey.Keypad0 + (key - Keys.NumPad0),
            Keys.Multiply => ImGuiKey.KeypadMultiply,
            Keys.Add => ImGuiKey.KeypadAdd,
            Keys.Subtract => ImGuiKey.KeypadSubtract,
            Keys.Decimal => ImGuiKey.KeypadDecimal,
            Keys.Divide => ImGuiKey.KeypadDivide,
            >= Keys.F1 and <= Keys.F24 => ImGuiKey.F1 + (key - Keys.F1),
            Keys.NumLock => ImGuiKey.NumLock,
            Keys.Scroll => ImGuiKey.ScrollLock,
            Keys.LeftShift or Keys.RightShift => ImGuiKey.ModShift,
            Keys.LeftControl or Keys.RightControl => ImGuiKey.ModCtrl,
            Keys.LeftAlt or Keys.RightAlt => ImGuiKey.ModAlt,
            Keys.OemSemicolon => ImGuiKey.Semicolon,
            Keys.OemPlus => ImGuiKey.Equal,
            Keys.OemComma => ImGuiKey.Comma,
            Keys.OemMinus => ImGuiKey.Minus,
            Keys.OemPeriod => ImGuiKey.Period,
            Keys.OemQuestion => ImGuiKey.Slash,
            Keys.OemTilde => ImGuiKey.GraveAccent,
            Keys.OemOpenBrackets => ImGuiKey.LeftBracket,
            Keys.OemCloseBrackets => ImGuiKey.RightBracket,
            Keys.OemPipe => ImGuiKey.Backslash,
            Keys.OemQuotes => ImGuiKey.Apostrophe,
            Keys.BrowserBack => ImGuiKey.AppBack,
            Keys.BrowserForward => ImGuiKey.AppForward,
            _ => ImGuiKey.None,
        };

        return imguiKey != ImGuiKey.None;
    }

    // ---------------------------------------------------------------- render

    private unsafe void RenderDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0) return;

        var lastViewport = _graphicsDevice.Viewport;
        var lastScissor = _graphicsDevice.ScissorRectangle;
        var lastRasterizer = _graphicsDevice.RasterizerState;
        var lastDepthStencil = _graphicsDevice.DepthStencilState;
        var lastBlendFactor = _graphicsDevice.BlendFactor;
        var lastBlendState = _graphicsDevice.BlendState;

        _graphicsDevice.BlendFactor = Color.White;
        _graphicsDevice.BlendState = BlendState.NonPremultiplied;
        _graphicsDevice.RasterizerState = _rasterizerState;
        _graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;

        // Handles cases of screen coordinates != framebuffer coordinates (e.g. retina/HiDPI displays).
        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        _graphicsDevice.Viewport = new Viewport(
            0, 0, _graphicsDevice.PresentationParameters.BackBufferWidth, _graphicsDevice.PresentationParameters.BackBufferHeight);

        UpdateBuffers(drawData);
        RenderCommandLists(drawData);

        _graphicsDevice.Viewport = lastViewport;
        _graphicsDevice.ScissorRectangle = lastScissor;
        _graphicsDevice.RasterizerState = lastRasterizer;
        _graphicsDevice.DepthStencilState = lastDepthStencil;
        _graphicsDevice.BlendState = lastBlendState;
        _graphicsDevice.BlendFactor = lastBlendFactor;
    }

    private unsafe void UpdateBuffers(ImDrawDataPtr drawData)
    {
        if (drawData.TotalVtxCount == 0) return;

        if (drawData.TotalVtxCount > _vertexBufferSize)
        {
            _vertexBuffer?.Dispose();
            _vertexBufferSize = (int)(drawData.TotalVtxCount * 1.5f);
            _vertexBuffer = new VertexBuffer(_graphicsDevice, DrawVertDeclaration.Declaration, _vertexBufferSize, BufferUsage.None);
            _vertexData = new byte[_vertexBufferSize * DrawVertDeclaration.Size];
        }

        if (drawData.TotalIdxCount > _indexBufferSize)
        {
            _indexBuffer?.Dispose();
            _indexBufferSize = (int)(drawData.TotalIdxCount * 1.5f);
            _indexBuffer = new IndexBuffer(_graphicsDevice, IndexElementSize.SixteenBits, _indexBufferSize, BufferUsage.None);
            _indexData = new byte[_indexBufferSize * sizeof(ushort)];
        }

        // Copy ImGui's native vertex/index buffers into managed byte arrays first — VertexBuffer/
        // IndexBuffer.SetData only accepts managed arrays, not raw IntPtrs.
        int vtxOffset = 0, idxOffset = 0;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            fixed (void* vtxDstPtr = &_vertexData[vtxOffset * DrawVertDeclaration.Size])
            fixed (void* idxDstPtr = &_indexData[idxOffset * sizeof(ushort)])
            {
                Buffer.MemoryCopy((void*)cmdList.VtxBuffer.Data, vtxDstPtr, _vertexData.Length, cmdList.VtxBuffer.Size * DrawVertDeclaration.Size);
                Buffer.MemoryCopy((void*)cmdList.IdxBuffer.Data, idxDstPtr, _indexData.Length, cmdList.IdxBuffer.Size * sizeof(ushort));
            }

            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }

        _vertexBuffer!.SetData(_vertexData, 0, drawData.TotalVtxCount * DrawVertDeclaration.Size);
        _indexBuffer!.SetData(_indexData, 0, drawData.TotalIdxCount * sizeof(ushort));
    }

    private void RenderCommandLists(ImDrawDataPtr drawData)
    {
        _graphicsDevice.SetVertexBuffer(_vertexBuffer);
        _graphicsDevice.Indices = _indexBuffer;

        int vtxOffset = 0, idxOffset = 0;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            for (int cmdIndex = 0; cmdIndex < cmdList.CmdBuffer.Size; cmdIndex++)
            {
                var cmd = cmdList.CmdBuffer[cmdIndex];
                if (cmd.ElemCount == 0) continue;

                if (!_loadedTextures.TryGetValue(cmd.TextureId, out var texture))
                    throw new InvalidOperationException($"Unbound ImGui texture id: {cmd.TextureId}");

                _graphicsDevice.ScissorRectangle = new Rectangle(
                    (int)cmd.ClipRect.X, (int)cmd.ClipRect.Y,
                    (int)(cmd.ClipRect.Z - cmd.ClipRect.X), (int)(cmd.ClipRect.W - cmd.ClipRect.Y));

                var effect = UpdateEffect(texture);
                foreach (var pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();

                    // The (baseVertex, minVertexIndex, numVertices, startIndex, primitiveCount) overload is
                    // obsolete in modern MonoGame but is what correctly honors ImGui's per-drawcall VtxOffset;
                    // this matches the official ImGui.NET MonoGame sample, hence the pragma.
#pragma warning disable CS0618
                    _graphicsDevice.DrawIndexedPrimitives(
                        primitiveType: PrimitiveType.TriangleList,
                        baseVertex: (int)cmd.VtxOffset + vtxOffset,
                        minVertexIndex: 0,
                        numVertices: cmdList.VtxBuffer.Size,
                        startIndex: (int)cmd.IdxOffset + idxOffset,
                        primitiveCount: (int)cmd.ElemCount / 3);
#pragma warning restore CS0618
                }
            }
            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
    }

    private Effect UpdateEffect(Texture2D texture)
    {
        _effect ??= new BasicEffect(_graphicsDevice);

        var io = ImGui.GetIO();
        _effect.World = Matrix.Identity;
        _effect.View = Matrix.Identity;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0f, io.DisplaySize.X, io.DisplaySize.Y, 0f, -1f, 1f);
        _effect.TextureEnabled = true;
        _effect.Texture = texture;
        _effect.VertexColorEnabled = true;
        return _effect;
    }

    /// <summary>Vertex declaration matching ImGui's ImDrawVert layout (pos, uv, color).</summary>
    private static class DrawVertDeclaration
    {
        public static readonly VertexDeclaration Declaration = new(
            Size,
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
            new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 0));

        public const int Size = 20; // 2 floats pos + 2 floats uv + 4 bytes color
    }
}
