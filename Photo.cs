using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XabboImager
{
    public class Photo
    {
        JsonArray planes;
        JsonArray sprites;
        JsonArray filters;
        JsonObject mods;
        int room;
        float zoom;

        public Photo(string json)
        {
            var node = JsonNode.Parse(json) as JsonObject;
            planes = node?["planes"]?.AsArray()?.DeepClone().AsArray() ?? new JsonArray();
            sprites = node?["sprites"]?.AsArray()?.DeepClone().AsArray() ?? new JsonArray();
            filters = node?["filters"]?.AsArray()?.DeepClone().AsArray() ?? new JsonArray();
            mods = node?["modifiers"]?.AsObject()?.DeepClone().AsObject() ?? new JsonObject();
            room = node?["roomid"]?.GetValue<int>() ?? 0;
            zoom = node != null && node.ContainsKey("zoom") ? (node["zoom"]?.GetValue<float>() ?? -1) : -1;
        }

        long StatusFromTs(long ts) => (ts / 100) % 23;
        long Checksum(long mod, long key) => (mod + 13) * (key + 29);
        long MixTs(string body, long ts, long key) => ts + Score(Encoding.UTF8.GetBytes(body), key, room);
        long Score(byte[] data, long key, int rid)
        {
            long a = key, b = rid;
            foreach (var d in data)
            {
                a = (a + ((d + 256) % 256)) % 255;
                b = (a + b) % 255;
            }
            return (a + b) % 100;
        }

        public string Build()
        {
            var obj = new JsonObject
            {
                ["planes"] = planes.DeepClone(),
                ["sprites"] = sprites.DeepClone(),
                ["modifiers"] = mods.DeepClone(),
                ["filters"] = filters.DeepClone(),
                ["roomid"] = room
            };
            if (zoom != -1) obj["zoom"] = zoom;

            var json = obj.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            json = json.Substring(0, json.Length - 1);
            json = json.Replace("\"planes\":", "\"planes\" : ")
                       .Replace("\"sprites\":", "\"sprites\" : ")
                       .Replace("\"modifiers\":", "\"modifiers\" : ")
                       .Replace("\"filters\":", "\"filters\" : ")
                       .Replace("\"roomid\":", "\"roomid\" : ")
                       .Replace("\"zoom\":", "\"zoom\" : ");
            json = "{ " + json.Substring(1);

            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var mod = now % 100; now -= mod;
            json += ",\"status\" : " + StatusFromTs(now);
            var key = (json.Length + now / 100 * 17) % 1493;
            json += ",\"timestamp\" : " + MixTs(json, now, key);
            json += ",\"checksum\" : " + Checksum(mod, key) + " }";
            return json;
        }

        public void ForceZoom(float z)
        {
            zoom = z;
        }

        public JsonArray Planes { get => planes; set => planes = value ?? new JsonArray(); }
        public JsonArray Sprites { get => sprites; set => sprites = value ?? new JsonArray(); }
        public JsonArray Filters { get => filters; set => filters = value ?? new JsonArray(); }
    }
}
