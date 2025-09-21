using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XabboImager
{
    public partial class MainWindow : Window
    {
        static MainWindow? Instance;
        Service svc;
        List<TextItem> items = new();
        bool drag;
        Point dragStart;
        TextItem? hit;
        BitmapImage? srcImg;
        byte[]? pxBuf;
        int pxW;
        int pxH;
        int offX;
        int offY;
        int alphaVal = 0;
        int scaleVal = 100;
        double autoScalePercent = 100;
        bool userScaled = false;
        bool internalScaleSet = false;

        public MainWindow()
        {
            InitializeComponent();

            Instance = this;
            svc = new Service();
            svc.ServerPreview += url => Dispatcher.Invoke(() => LoadPreview(url));
            svc.Status += s => Dispatcher.Invoke(() => status.Text = s);
            svc.NewPhoto += () => Dispatcher.Invoke(() => { items.Clear(); pxBuf = null; srcImg = null; Redraw(); });
            svc.Started += () => Dispatcher.Invoke(() => { if (!IsVisible) Show(); Activate(); TryUpdateCanvasBg(); });
            svc.Stopped += () => Dispatcher.Invoke(() => { Hide(); });
            Closing += MainWindow_Closing;
            try
            {
                alpha.ValueChanged += Alpha_Changed;
                scale.ValueChanged += Scale_Changed;
                _ = svc.Start();
                TryUpdateCanvasBg();
                UpdateServiceMode();
            }
            catch (Exception ex) { status.Text = "failed: " + ex.Message; }

            color.SelectedIndex = 1;
            alpha.Value = 0;
            scale.Value = 100;
            LoadSpritePool();
        }

        void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        void LoadPreview(string url)
        {
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri(url, UriKind.Absolute);
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.EndInit();
                preview.Source = b;
                TryUpdateCanvasBg();
            }
            catch { preview.Source = null; }
        }

        void AddText_Click(object sender, RoutedEventArgs e)
        {
            var t = txt.Text.Trim().ToUpper();
            if (string.IsNullOrEmpty(t)) return;
            var ci = color.SelectedItem as ComboBoxItem;
            var tag = (ci?.Tag as string) ?? "11";
            var chunks = Chunk(t, 3).ToList();
            var x = 10.0;
            var y = 10.0;
            foreach (var c in chunks)
            {
                var url = BadgeUrl(c, tag);
                if (string.IsNullOrEmpty(url)) continue;
                items.Add(new TextItem { text = c, color = tag, x = x, y = y, url = url });
                x += 39;
            }
            txt.Text = "";
            Redraw();
            UpdateOverflow();
        }

        static IEnumerable<string> Chunk(string s, int n)
        {
            for (int i = 0; i < s.Length; i += n) yield return s.Substring(i, Math.Min(n, s.Length - i));
        }

        static string BadgeUrl(string part, string code)
        {
            var map = new Dictionary<char, string>
            {
                ['0']="s68",['1']="s69",['2']="s70",['3']="s71",['4']="s72",
                ['5']="s73",['6']="s74",['7']="s75",['8']="s76",['9']="s77",
                ['A']="s78",['B']="s79",['C']="s80",['D']="s81",['E']="s82",
                ['F']="s83",['G']="s84",['H']="s85",['I']="s86",['J']="s87",
                ['K']="s88",['L']="s89",['M']="s90",['N']="s91",['O']="s92",
                ['P']="s93",['Q']="s94",['R']="s95",['S']="s96",['T']="s97",
                ['U']="s98",['V']="s99",['W']="t00",['X']="t01",['Y']="t02",
                ['Z']="t03",[' ']=""
            };
            var slots = new[] { 3, 4, 5 };
            var sb = new StringBuilder();
            for (int i = 0; i < part.Length && i < 3; i++)
            {
                var ch = part[i];
                if (map.TryGetValue(ch, out var s) && !string.IsNullOrEmpty(s)) sb.Append(s).Append(code).Append(slots[i]);
            }
            var body = sb.ToString();
            if (string.IsNullOrEmpty(body)) return "";
            var hash = Md5(body + "ef2356a4926bf225eb86c75c52309c32");
            return $"https://www.habbo.com/habbo-imaging/badge/{body}{hash}.gif";
        }

        static string Md5(string s)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var bytes = Encoding.UTF8.GetBytes(s);
            var hash = md5.ComputeHash(bytes);
            var sb = new StringBuilder();
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(canvas);
            hit = items.LastOrDefault(i => p.X >= i.x && p.X <= i.x + 39 && p.Y >= i.y && p.Y <= i.y + 39);
            drag = true;
            dragStart = p;
            canvas.CaptureMouse();
        }

        void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!drag) return;
            var p = e.GetPosition(canvas);
            if (hit != null)
            {
                var dx = p.X - dragStart.X;
                var dy = p.Y - dragStart.Y;
                hit.x += dx; hit.y += dy;
                dragStart = p;
                Redraw();
            }
            else if (pxBuf != null)
            {
                var dx = p.X - dragStart.X;
                var dy = p.Y - dragStart.Y;
                offX += (int)Math.Round(dx); offY += (int)Math.Round(dy);
                offX = Math.Clamp(offX, -320, 320); offY = Math.Clamp(offY, -320, 320);
                dragStart = p;
                Redraw();
            }
        }

        void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            drag = false; hit = null; canvas.ReleaseMouseCapture();
        }

        void Redraw()
        {
            if (canvas == null) return;
            canvas.Children.Clear();
            if (pxBuf != null)
            {
                var wb = BitmapSource.Create(pxW, pxH, 96,96, PixelFormats.Pbgra32, null, pxBuf, pxW*4);
                var im = new Image { Source = wb, Width = pxW, Height = pxH, Stretch = Stretch.None };
                Canvas.SetLeft(im, offX); Canvas.SetTop(im, offY);
                canvas.Children.Add(im);
            }
            foreach (var i in items)
            {
                var im = new Image { Width=39, Height=39, Stretch=Stretch.None };
                var b = new BitmapImage();
                b.BeginInit(); b.UriSource = new Uri(i.url, UriKind.Absolute); b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit();
                im.Source = b;
                Canvas.SetLeft(im, i.x); Canvas.SetTop(im, i.y);
                canvas.Children.Add(im);
            }
            UpdateZoom();
        }

        void UpdateZoom()
        {
            if (zoom == null) return;
            if (pxBuf == null) { zoom.Source = null; return; }
            var wb = new WriteableBitmap(pxW, pxH, 96,96, PixelFormats.Pbgra32, null);
            wb.WritePixels(new Int32Rect(0,0,pxW,pxH), pxBuf, pxW*4, 0);
            var zw = wb.PixelWidth*2; var zh = wb.PixelHeight*2;
            var z = new WriteableBitmap(zw, zh, 96,96, PixelFormats.Pbgra32, null);
            var ss = wb.PixelWidth*4; var zs = z.PixelWidth*4;
            var sp = new byte[wb.PixelHeight*ss]; wb.CopyPixels(sp, ss, 0);
            var zp = new byte[z.PixelHeight*zs];
            for (int y=0;y<wb.PixelHeight;y++)
            for (int x=0;x<wb.PixelWidth;x++)
            {
                var si = y*ss + x*4;
                var zi = (y*2)*zs + (x*2)*4;
                for (int dy=0;dy<2;dy++) for(int dx=0;dx<2;dx++)
                {
                    var ii = zi + dy*zs + dx*4;
                    zp[ii+0]=sp[si+0]; zp[ii+1]=sp[si+1]; zp[ii+2]=sp[si+2]; zp[ii+3]=sp[si+3];
                }
            }
            z.WritePixels(new Int32Rect(0,0,zw,zh), zp, zs, 0);
            zoom.Source = z;
        }

        void UpdateOverflow()
        {
            if (pxBuf == null) { if (overflow!=null) overflow.Text = ""; return; }
            var counts = svc.GetNonEditorCounts();
            int availablePlanes = Math.Max(0, MAX_PLANES - counts.planes - 2);
            int availableSprites = Math.Max(0, MAX_SPRITES - counts.sprites - items.Count);
            var est = estimateUsage(pxBuf, pxW, pxH, alphaVal, availablePlanes, availableSprites);
            int over = Math.Max(0, est.over);
            if (overflow != null) overflow.Text = over > 0 ? ($"Pixel Overflow: {over}") : "";
        }

        void Apply_Click(object sender, RoutedEventArgs e)
        {
            var newPlanes = new JsonArray();
            var newSprites = new JsonArray();

            if (pxBuf != null && pxW > 0 && pxH > 0 && HasVisible(pxBuf))
            {
                BuildPixelElements(pxBuf, pxW, pxH, offX, offY, newPlanes, newSprites);
            }

            
            var existingP = svc.GetPlanes();
            var existingS = svc.GetSprites();
            JsonArray finalPlanes = new JsonArray();
            foreach (var n in existingP) if (IsNonEditorPlane(n)) finalPlanes.Add(JsonNode.Parse(n!.ToJsonString())!);
            foreach (var n in newPlanes) finalPlanes.Add(JsonNode.Parse(n!.ToJsonString())!);

            JsonArray finalSprites = new JsonArray();
            foreach (var n in existingS) if (IsNonEditorSprite(n)) finalSprites.Add(JsonNode.Parse(n!.ToJsonString())!);
            foreach (var n in newSprites) finalSprites.Add(JsonNode.Parse(n!.ToJsonString())!);

            double textBaseZ = -450;
            int tCount = 0;
            foreach (var t in items)
            {
                var sObj = new JsonObject
                {
                    ["x"] = (int)Math.Round(t.x),
                    ["y"] = (int)Math.Round(t.y),
                    ["z"] = textBaseZ - tCount*0.00001,
                    ["name"] = t.url,
                    ["type"] = "badge"
                };
                finalSprites.Add(sObj);
                tCount++;
            }

            RecalcZ(finalPlanes, finalSprites);
            
            edit.IsChecked = true;
            modeInj.IsChecked = true;
            modeCap.IsChecked = false;
            svc.SetEdit(true, false, true, finalPlanes, finalSprites);
            status.Text = $"applied planes:{finalPlanes.Count} sprites:{finalSprites.Count}";
        }

        void Clear_Click(object sender, RoutedEventArgs e)
        {
            items.Clear(); pxBuf = null; srcImg = null; Redraw();
        }

        static JsonObject BasePlane()
        {
            var p = new JsonObject();
            var cp = new JsonArray();
            for (int i = 0; i < 4; i++) cp.Add(new JsonObject{["x"]=0,["y"]=0});
            p["cornerPoints"] = cp; p["texCols"] = new JsonArray(); p["masks"] = new JsonArray(); p["bottomAligned"] = false; p["z"] = 0; p["color"] = 0;
            return p;
        }

        const int MAX_PLANES = 289;
        const int MAX_SPRITES = 676;
        List<string> spritePool = new();

        static void BuildPixelElements(byte[] buf, int w, int h, int ox, int oy, JsonArray planes, JsonArray sprites)
        {
            int stride = w * 4;
            bool[] used = new bool[w * h];
            double baseZ = -350;
            int planeCount = 0;
            int planeLimit = Math.Max(0, MAX_PLANES - 2);
            int tol = 48;

            for (int y = 0; y <= h - 3 && planeCount < planeLimit; y++)
            {
                for (int x = 0; x <= w - 3 && planeCount < planeLimit; x++)
                {
                    bool ok = true;
                    int sumR = 0, sumG = 0, sumB = 0;
                    for (int dy = 0; dy < 3 && ok; dy++)
                    for (int dx = 0; dx < 3 && ok; dx++)
                    {
                        int xx = x + dx; int yy = y + dy; int idx = yy * stride + xx * 4; int pi = yy * w + xx;
                        if (used[pi]) { ok = false; break; }
                        if (buf[idx + 3] == 0) { ok = false; break; }
                        int b = buf[idx + 0]; int g = buf[idx + 1]; int r = buf[idx + 2];
                        sumR += r; sumG += g; sumB += b;
                    }
                    if (!ok) continue;
                    int avgR = sumR / 9; int avgG = sumG / 9; int avgB = sumB / 9;
                    for (int dy = 0; dy < 3 && ok; dy++)
                    for (int dx = 0; dx < 3 && ok; dx++)
                    {
                        int xx = x + dx; int yy = y + dy; int idx = yy * stride + xx * 4;
                        int b = buf[idx + 0]; int g = buf[idx + 1]; int r = buf[idx + 2];
                        if (Math.Abs(r - avgR) > tol || Math.Abs(g - avgG) > tol || Math.Abs(b - avgB) > tol) { ok = false; break; }
                    }
                    if (!ok) continue;
                    int color = (avgR << 16) | (avgG << 8) | avgB;
                    var plane = new JsonObject
                    {
                        ["color"] = color,
                        ["z"] = baseZ + planeCount * 0.00001,
                        ["cornerPoints"] = new JsonArray
                        {
                            new JsonObject{["x"]=ox + x + 3,["y"]=oy + y + 3},
                            new JsonObject{["x"]=ox + x,["y"]=oy + y + 3},
                            new JsonObject{["x"]=ox + x + 3,["y"]=oy + y},
                            new JsonObject{["x"]=ox + x,["y"]=oy + y}
                        },
                        ["texCols"] = new JsonArray(),
                        ["masks"] = new JsonArray(),
                        ["bottomAligned"] = false,
                        ["type"] = "pixel_art_plane"
                    };
                    planes.Add(plane);
                    for (int dy = 0; dy < 3; dy++)
                    for (int dx = 0; dx < 3; dx++)
                    {
                        int xx = x + dx; int yy = y + dy; int pi = yy * w + xx;
                        used[pi] = true;
                    }
                    planeCount++;
                }
            }

            for (int y = 0; y < h && planeCount < planeLimit; y++)
            for (int x = 0; x < w && planeCount < planeLimit; x++)
            {
                int idx = y * stride + x * 4; int pi = y * w + x;
                if (used[pi]) continue; if (buf[idx + 3] == 0) continue;
                int b = buf[idx + 0]; int g = buf[idx + 1]; int r = buf[idx + 2];
                int color = (r << 16) | (g << 8) | b;
                var plane = new JsonObject
                {
                    ["color"] = color,
                    ["z"] = baseZ + planeCount * 0.00001,
                    ["cornerPoints"] = new JsonArray
                    {
                        new JsonObject{["x"]=ox + x + 1,["y"]=oy + y + 1},
                        new JsonObject{["x"]=ox + x,["y"]=oy + y + 1},
                        new JsonObject{["x"]=ox + x + 1,["y"]=oy + y},
                        new JsonObject{["x"]=ox + x,["y"]=oy + y}
                    },
                    ["texCols"] = new JsonArray(),
                    ["masks"] = new JsonArray(),
                    ["bottomAligned"] = false,
                    ["type"] = "pixel_art_plane"
                };
                planes.Add(plane);
                used[pi] = true;
                planeCount++;
            }

            int spriteCount = 0; int spriteNameIndex = 0; double spriteBaseZ = baseZ - 100;
            for (int y = 0; y < h && spriteCount < MAX_SPRITES; y++)
            for (int x = 0; x < w && spriteCount < MAX_SPRITES; x++)
            {
                int idx = y * stride + x * 4; int pi = y * w + x;
                if (used[pi]) continue; if (buf[idx + 3] == 0) continue;
                int b = buf[idx + 0]; int g = buf[idx + 1]; int r = buf[idx + 2];
                int color = (r << 16) | (g << 8) | b;
                var name = "pixel";
                var pool = Instance?.spritePool;
                if (pool != null && pool.Count > 0) name = pool[spriteNameIndex % pool.Count];
                var sp = new JsonObject
                {
                    ["flipH"] = false,
                    ["x"] = ox + x,
                    ["y"] = oy + y,
                    ["z"] = spriteBaseZ - spriteCount * 0.00001,
                    ["color"] = color,
                    ["name"] = name,
                    ["type"] = "pixel_art_sprite"
                };
                sprites.Add(sp);
                spriteCount++;
                spriteNameIndex++;
            }
        }

        static bool HasVisible(byte[] buf)
        {
            for (int i=3;i<buf.Length;i+=4) if (buf[i]>0) return true;
            return false;
        }

        static bool IsNonEditorPlane(JsonNode? node)
        {
            if (node is JsonObject jo)
            {
                if (jo.TryGetPropertyValue("type", out var t) && t is JsonValue tv && tv.TryGetValue(out string? ts))
                {
                    if (string.Equals(ts, "pixel_art_plane", StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            return true;
        }

        static bool IsNonEditorSprite(JsonNode? node)
        {
            if (node is JsonObject jo)
            {
                if (jo.TryGetPropertyValue("type", out var t) && t is JsonValue tv && tv.TryGetValue(out string? ts))
                {
                    if (ts == "badge" || ts == "image_sprite" || ts == "pixel_art_sprite") return false;
                }
                if (jo.TryGetPropertyValue("name", out var n) && n is JsonValue nv && nv.TryGetValue(out string? ns) && ns != null && ns.Contains("habbo-imaging/badge/")) return false;
            }
            return true;
        }

        

        static void RecalcZ(JsonArray planes, JsonArray sprites)
        {
            if (planes.Count < 2)
            {
                while (planes.Count < 2) planes.Add(BasePlane());
            }
            double spriteMaxZ = 0;
            foreach (var n in sprites)
            {
                if (n is JsonObject o && o.TryGetPropertyValue("z", out var zNode) && zNode is JsonValue zv && zv.TryGetValue<double>(out var vz)) if (vz > spriteMaxZ) spriteMaxZ = vz;
            }
            double planeMaxZExFirstTwo = 0;
            for (int i=2;i<planes.Count;i++)
            {
                if (planes[i] is JsonObject o && o.TryGetPropertyValue("z", out var zNode) && zNode is JsonValue zv && zv.TryGetValue<double>(out var vz)) if (vz > planeMaxZExFirstTwo) planeMaxZExFirstTwo = vz;
            }
            double overall = Math.Max(spriteMaxZ, planeMaxZExFirstTwo);
            if (planes[0] is JsonObject p0) p0["z"] = (((planes.Count - 1) * 2.31743) + (sprites.Count * 1.776104)) + overall;
            if (planes[1] is JsonObject p1) p1["z"] = overall;
        }

        class TextItem { public string text=""; public string color=""; public string url=""; public double x; public double y; }

        void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dlg.ShowDialog()==true)
            {
                var b = new BitmapImage(); b.BeginInit(); b.UriSource=new Uri(dlg.FileName, UriKind.Absolute); b.CacheOption=BitmapCacheOption.OnLoad; b.EndInit();
                srcImg = b; offX = 0; offY = 0; AutoScaleToLimits(true); UpdateProcessed();
            }
        }

        void Optimize_Click(object sender, RoutedEventArgs e)
        {
            AutoScaleToLimits(true);
            UpdateProcessed();
        }

        void Alpha_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            alphaVal = (int)e.NewValue; AutoScaleToLimits(!userScaled || Math.Abs(scale.Value - autoScalePercent) < 0.5); UpdateProcessed(); UpdateOverflow();
        }

        void Scale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (internalScaleSet) return; userScaled = true; scaleVal = (int)e.NewValue; UpdateProcessed(); UpdateOverflow();
        }

        void UpdateProcessed()
        {
            if (srcImg == null) { pxBuf = null; Redraw(); return; }
            double effPercent = PercentFromSlider(scale.Value);
            var sw = Math.Max(1, (int)Math.Round(srcImg.PixelWidth * (effPercent/100.0)));
            var sh = Math.Max(1, (int)Math.Round(srcImg.PixelHeight * (effPercent/100.0)));
            var tb = new TransformedBitmap(srcImg, new ScaleTransform(sw/(double)srcImg.PixelWidth, sh/(double)srcImg.PixelHeight));
            RenderOptions.SetBitmapScalingMode(tb, BitmapScalingMode.NearestNeighbor);
            var fc = new FormatConvertedBitmap(tb, PixelFormats.Pbgra32, null, 0);
            var wb = new WriteableBitmap(fc);
            pxW = wb.PixelWidth; pxH = wb.PixelHeight;
            var stride = pxW*4;
            var buf = new byte[pxH*stride];
            wb.CopyPixels(buf, stride, 0);
            for (int y=0;y<pxH;y++)
            for (int x=0;x<pxW;x++)
            {
                int i = y*stride + x*4;
                if (buf[i+3] < alphaVal) { buf[i]=0; buf[i+1]=0; buf[i+2]=0; buf[i+3]=0; }
            }
            pxBuf = buf;
            Redraw();
            UpdateOverflow();
        }

        void AutoScaleToLimits(bool applyToSlider = true)
        {
            if (srcImg == null) return;
            var counts = svc.GetNonEditorCounts();
            int availablePlanes = Math.Max(0, MAX_PLANES - counts.planes - 2);
            int availableSprites = Math.Max(0, MAX_SPRITES - counts.sprites - items.Count);
            double lo = 1.0, hi = 100.0, best = 1.0;
            for (int it = 0; it < 9; it++)
            {
                double mid = (lo + hi) / 2.0;
                int sw = Math.Max(1, (int)Math.Floor(srcImg.PixelWidth * (mid / 100.0)));
                int sh = Math.Max(1, (int)Math.Floor(srcImg.PixelHeight * (mid / 100.0)));
                var tb = new TransformedBitmap(srcImg, new ScaleTransform(sw/(double)srcImg.PixelWidth, sh/(double)srcImg.PixelHeight));
                RenderOptions.SetBitmapScalingMode(tb, BitmapScalingMode.NearestNeighbor);
                var fc = new FormatConvertedBitmap(tb, PixelFormats.Pbgra32, null, 0);
                var wb = new WriteableBitmap(fc);
                int stride = wb.PixelWidth * 4;
                byte[] tmp = new byte[wb.PixelHeight * stride];
                wb.CopyPixels(tmp, stride, 0);
                for (int y=0;y<wb.PixelHeight;y++) for (int x=0;x<wb.PixelWidth;x++) { int i=y*stride + x*4; if (tmp[i+3] <= alphaVal) { tmp[i]=0; tmp[i+1]=0; tmp[i+2]=0; tmp[i+3]=0; } }
                var est = estimateUsage(tmp, sw, sh, alphaVal, availablePlanes, availableSprites);
                if (est.over > 0) hi = mid; else { best = mid; lo = mid; }
            }
            autoScalePercent = Math.Clamp(best, 1.0, 100.0);
            if (applyToSlider)
            {
                internalScaleSet = true; scale.Value = SliderFromPercent(autoScalePercent); internalScaleSet = false; userScaled = false;
            }
        }

        static (int planes, int sprites, int over) estimateUsage(byte[] buf, int w, int h, int alpha, int availPlanes, int availSprites)
        {
            int stride = w * 4;
            bool[] used = new bool[w * h];
            int tol = 48;
            int planes = 0;
            for (int y = 0; y <= h - 3 && planes < availPlanes; y++)
            {
                for (int x = 0; x <= w - 3 && planes < availPlanes; x++)
                {
                    bool ok = true;
                    int sumR = 0, sumG = 0, sumB = 0;
                    for (int dy = 0; dy < 3 && ok; dy++)
                    for (int dx = 0; dx < 3 && ok; dx++)
                    {
                        int xx = x + dx; int yy = y + dy; int idx = yy * stride + xx * 4; int pi = yy * w + xx;
                        if (used[pi]) { ok = false; break; }
                        if (buf[idx + 3] <= alpha) { ok = false; break; }
                        int b = buf[idx + 0]; int g = buf[idx + 1]; int r = buf[idx + 2];
                        sumR += r; sumG += g; sumB += b;
                    }
                    if (!ok) continue;
                    int avgR = sumR / 9; int avgG = sumG / 9; int avgB = sumB / 9;
                    for (int dy = 0; dy < 3 && ok; dy++)
                    for (int dx = 0; dx < 3 && ok; dx++)
                    {
                        int xx = x + dx; int yy = y + dy; int idx = yy * stride + xx * 4;
                        int b = buf[idx + 0]; int g = buf[idx + 1]; int r = buf[idx + 2];
                        if (Math.Abs(r - avgR) > tol || Math.Abs(g - avgG) > tol || Math.Abs(b - avgB) > tol) { ok = false; break; }
                    }
                    if (!ok) continue;
                    for (int dy = 0; dy < 3; dy++)
                    for (int dx = 0; dx < 3; dx++)
                    {
                        int xx = x + dx; int yy = y + dy; int pi = yy * w + xx;
                        used[pi] = true;
                    }
                    planes++;
                }
            }
            int singles = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = y * stride + x * 4; int pi = y * w + x;
                if (used[pi]) continue;
                if (buf[idx + 3] > alpha) singles++;
            }
            int planeRemain = Math.Max(0, availPlanes - planes);
            int planeSingles = Math.Min(planeRemain, singles);
            singles -= planeSingles;
            int sprites = Math.Min(availSprites, Math.Max(0, singles));
            int over = Math.Max(0, singles - sprites);
            return (planes + planeSingles, sprites, over);
        }

        static double PercentFromSlider(double controlVal)
        {
            var v = Math.Max(1.0, Math.Min(100.0, controlVal));
            return Math.Round((v * v) / 100.0, 2);
        }

        static double SliderFromPercent(double percent)
        {
            var p = Math.Max(1.0, Math.Min(100.0, percent));
            return Math.Sqrt(p * 100.0);
        }

        void TryUpdateCanvasBg()
        {
            if (serverBgInCanvas == null) return;
            var src = preview?.Source as ImageSource;
            bool on = bgSwitch != null && Math.Round(bgSwitch.Value) == 1;
            if (src != null && on)
            {
                serverBgInCanvas.Source = src;
                serverBgInCanvas.Opacity = 1.0;
                serverBgInCanvas.Visibility = Visibility.Visible;
                if (canvas != null) canvas.Background = Brushes.Transparent;
            }
            else
            {
                serverBgInCanvas.Visibility = Visibility.Collapsed;
                if (canvas != null)
                {
                    var obj = new BrushConverter().ConvertFromString("#111318");
                    if (obj is SolidColorBrush sb) canvas.Background = sb;
                    else canvas.Background = Brushes.Black;
                }
            }
        }

        void BgSwitch_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { TryUpdateCanvasBg(); }

        void Title_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.OriginalSource is DependencyObject d)
            {
                var parent = d;
                while (parent != null)
                {
                    if (parent is Button) return;
                    parent = VisualTreeHelper.GetParent(parent) as DependencyObject;
                }
            }
            DragMove();
        }

        void Min_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        void Mode_Changed(object sender, RoutedEventArgs e)
        {
            UpdateServiceMode();
        }

        void UpdateServiceMode()
        {
            if (svc == null) return;
            bool eon = edit != null && edit.IsChecked == true;
            bool cap = modeCap != null && modeCap.IsChecked == true;
            bool inj = modeInj != null && modeInj.IsChecked == true;
            svc.SetEdit(eon, cap, inj, svc.GetPlanes(), svc.GetSprites());
            if (status != null) status.Text = cap ? "mode: capture" : (inj ? "mode: inject" : "mode: none");
        }

        

        void LoadSpritePool()
        {
            try
            {
                using var s = typeof(MainWindow).Assembly.GetManifestResourceStream("XabboImager.Resources.sprites.txt");
                if (s != null)
                {
                    using var r = new System.IO.StreamReader(s);
                    var lines = r.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines) if (!string.IsNullOrWhiteSpace(line)) spritePool.Add(line.Trim());
                }
            }
            catch { }
        }

        
    }
}
