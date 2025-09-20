using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xabbo.GEarth;
using Xabbo;
using Xabbo.Interceptor;
using Xabbo.Messages;

namespace XabboImager
{
    public class Service
    {
        GEarthExtension ext;
        Header renderHdr;
        bool edit;
        bool cap = true;
        bool inj;
        bool activated;
        public event Action? Started;
        public event Action? Stopped;
        JsonArray planes = new();
        JsonArray sprites = new();
        JsonArray filters = new();

        public event Action? NewPhoto;
        public event Action<string>? ServerPreview;
        public event Action<string>? Status;

        public Service()
        {
            ext = new GEarthExtension(new GEarthOptions
            {
                Name = "XabboImager",
                Description = "photo injector",
                Author = "QDave",
                Version = "1.0",
                ShowDeleteButton = true,
                ShowLeaveButton = true
            });

            ext.Activated += () =>
            {
                Started?.Invoke();
                if (!activated)
                {
                    activated = true;
                    ext.Intercepted += OnIntercept;
                    Status?.Invoke("started via G‑Earth");
                }
            };
        }

        public async Task Start()
        {
            Hook();
            Status?.Invoke("connecting to G-Earth...");
            await ext.RunAsync(new GEarthConnectOptions(FileName: string.Empty, Cookie: string.Empty));
        }

        void Hook()
        {
            ext.Connected += e =>
            {
                Status?.Invoke($"connected {e.Session.Hotel} ({e.Session.Client.Type})");
            };
            ext.Initialized += _ => { };
            ext.Disconnected += () =>
            {
                Status?.Invoke("disconnected");
                ext.Intercepted -= OnIntercept;
                activated = false;
                Stopped?.Invoke();
                System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
            };
        }

        public void SetEdit(bool isEdit, bool isCap, bool isInj, JsonArray p, JsonArray s)
        {
            edit = isEdit; cap = isCap; inj = isInj;
            planes = p ?? new JsonArray();
            sprites = s ?? new JsonArray();
            if (planes.Count < 2)
            {
                while (planes.Count < 2)
                {
                    var bp = new JsonObject();
                    var cp = new JsonArray();
                    for (int i = 0; i < 4; i++) cp.Add(new JsonObject { ["x"] = 0, ["y"] = 0 });
                    bp["cornerPoints"] = cp; bp["texCols"] = new JsonArray(); bp["masks"] = new JsonArray(); bp["bottomAligned"] = false; bp["z"] = 0; bp["color"] = 0;
                    planes.Add(bp);
                }
            }
        }

        void OnIntercept(Intercept e)
        {
            try
            {
                if (!activated) return;
                if (!ext.IsConnected) return;
                if (!ext.Messages.TryGetNames(e.Packet.Header, out var names)) return;
                var name = names.GetName(ext.Session.Client.Type);
                if (e.Packet.Header.Direction == Direction.Out)
                {
                    if (name == "RenderRoom") { renderHdr = e.Packet.Header; HandleRender(e, false); }
                    else if (name == "RenderRoomThumbnail") HandleRender(e, true);
                }
                else if (e.Packet.Header.Direction == Direction.In)
                {
                    if (name == "CameraStorageUrl")
                    {
                        var s = e.Packet.Reader().ReadString();
                        if (!string.IsNullOrEmpty(s)) ServerPreview?.Invoke($"https://habbo-stories-content.s3.amazonaws.com/{s}");
                    }
                }
            }
            catch { }
        }

        void HandleRender(Intercept e, bool thumb)
        {
            var buf = e.Packet.Buffer.Span;
            if (buf.Length < 4) { e.Block(); return; }
            var len = BinaryPrimitives.ReadInt32BigEndian(buf.Slice(0, 4));
            if (len < 0 || 4 + len > buf.Length) { e.Block(); return; }
            var data = buf.Slice(4, len).ToArray();
            byte[] raw;
            try { raw = Inflate(data); }
            catch { e.Block(); return; }
            var txt = Encoding.UTF8.GetString(raw);
            var photo = new Photo(txt);

            if (cap)
            {
                planes = photo.Planes.DeepClone().AsArray();
                sprites = photo.Sprites.DeepClone().AsArray();
                filters = photo.Filters.DeepClone().AsArray();
                NewPhoto?.Invoke();
            }

            if (inj && edit)
            {
                var p = planes.DeepClone().AsArray();
                var s = sprites.DeepClone().AsArray();
                RecalcZ(p, s);
                photo.Planes = p;
                photo.Sprites = s;
                photo.Filters = filters.DeepClone().AsArray();
                var newJson = photo.Build();
                var comp = Deflate(Encoding.UTF8.GetBytes(newJson));
                var payload = new byte[4 + comp.Length];
                BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), comp.Length);
                Buffer.BlockCopy(comp, 0, payload, 4, comp.Length);
                var pb = new PacketBuffer(payload);
                var np = new Packet(e.Packet.Header, ext.Session.Client.Type, pb);
                e.Packet = np;
            }
        }

        static void RecalcZ(JsonArray planes, JsonArray sprites)
        {
            if (planes.Count < 2)
            {
                while (planes.Count < 2)
                {
                    var bp = new JsonObject();
                    var cp = new JsonArray();
                    for (int i = 0; i < 4; i++) cp.Add(new JsonObject { ["x"] = 0, ["y"] = 0 });
                    bp["cornerPoints"] = cp; bp["texCols"] = new JsonArray(); bp["masks"] = new JsonArray(); bp["bottomAligned"] = false; bp["z"] = 0; bp["color"] = 0;
                    planes.Add(bp);
                }
            }
            double spriteMaxZ = 0;
            foreach (var n in sprites)
            {
                if (n is JsonObject o && o.TryGetPropertyValue("z", out var zNode) && zNode is JsonValue zv && zv.TryGetValue<double>(out var vz)) if (vz > spriteMaxZ) spriteMaxZ = vz;
            }
            double planeMaxZExFirstTwo = 0;
            for (int i = 2; i < planes.Count; i++)
            {
                if (planes[i] is JsonObject o && o.TryGetPropertyValue("z", out var zNode) && zNode is JsonValue zv && zv.TryGetValue<double>(out var vz)) if (vz > planeMaxZExFirstTwo) planeMaxZExFirstTwo = vz;
            }
            double overall = Math.Max(spriteMaxZ, planeMaxZExFirstTwo);
            if (planes[0] is JsonObject p0) p0["z"] = (((planes.Count - 1) * 2.31743) + (sprites.Count * 1.776104)) + overall;
            if (planes[1] is JsonObject p1) p1["z"] = overall;
        }

        static byte[] Inflate(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var outms = new MemoryStream();
                using var z = new ZLibStream(ms, CompressionMode.Decompress);
                z.CopyTo(outms); return outms.ToArray();
            }
            catch
            {
                try
                {
                    using var ms = new MemoryStream(data);
                    using var outms = new MemoryStream();
                    ms.Position = 2;
                    using var z = new DeflateStream(ms, CompressionMode.Decompress, true);
                    z.CopyTo(outms); return outms.ToArray();
                }
                catch
                {
                    using var ms = new MemoryStream(data);
                    using var outms = new MemoryStream();
                    using var z = new GZipStream(ms, CompressionMode.Decompress);
                    z.CopyTo(outms); return outms.ToArray();
                }
            }
        }

        static byte[] Deflate(byte[] data)
        {
            using var outms = new MemoryStream();
            using (var z = new ZLibStream(outms, CompressionMode.Compress, true)) z.Write(data, 0, data.Length);
            return outms.ToArray();
        }

        static bool IsNonEditorItem(JsonNode? node, params string[] editorTypes)
        {
            if (node is JsonObject jo)
            {
                if (jo.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonValue typeVal && typeVal.TryGetValue(out string? typeStr))
                {
                    foreach (var t in editorTypes) if (string.Equals(t, typeStr, StringComparison.OrdinalIgnoreCase)) return false;
                }
                if (editorTypes.Contains("badge") && jo.TryGetPropertyValue("name", out var nameNode) && nameNode is JsonValue nameVal && nameVal.TryGetValue(out string? nameStr) && nameStr != null && nameStr.Contains("habbo-imaging/badge/"))
                {
                    return false;
                }
            }
            return true;
        }

        public (int planes, int sprites) GetNonEditorCounts()
        {
            int p = 0, s = 0;
            foreach (var n in planes) if (IsNonEditorItem(n, "pixel_art_plane")) p++;
            foreach (var n in sprites) if (IsNonEditorItem(n, "badge", "image_sprite", "pixel_art_sprite")) s++;
            return (p, s);
        }

        public JsonArray GetPlanes() => planes.DeepClone().AsArray();
        public JsonArray GetSprites() => sprites.DeepClone().AsArray();
        public JsonArray GetFilters() => filters.DeepClone().AsArray();
    }
}
