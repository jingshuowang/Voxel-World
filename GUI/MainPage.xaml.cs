#nullable disable
using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Microsoft.UI;
using Windows.UI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Brushes;
using System.IO;
using System.Linq;

namespace GUI;

public sealed partial class MainPage : Page
{
    private const float GridSpacing = 4f;
    private Point? startPoint;
    private Point? currentPoint;
    private bool isDrawing = false;
    private bool isMoving = false;
    private bool isResizing = false;
    private bool isWiring = false;
    private Component selectedComponent = null;
    private Component activeSlider = null;
    private Component activeTextEdit = null;
    private Point dragOffset;
    private Point currentMousePos;
    private Point fixedCorner;

    // Panning State
    private bool isPanning = false;
    private Point panStartPos;
    private Vector2 canvasOffset = Vector2.Zero;
    private Vector2 panStartOffset = Vector2.Zero;

    // Palette Always Visible
    private string selectedTypeToPlace = "Base";
    private Rect paletteRect;
    private List<PaletteFolder> folders = new List<PaletteFolder>();
    private List<PaletteTarget> paletteTargets = new List<PaletteTarget>();

    // Left Explorer & Slider Frame Targets
    private string selectedFile = "w.prop";
    private string selectedFileText = "w";
    private bool isEditingFileName = false;
    private Variable selectedVar = null;
    private bool isEditingVarName = false;
    private List<SidebarTarget> sidebarTargets = new List<SidebarTarget>();
    private List<SliderPlusTarget> sliderPlusTargets = new List<SliderPlusTarget>();
    private bool isSidebarFocused = false;
    private float sidebarScrollOffset = 0f;
    private bool isSpacePressed = false;
    private DateTime lastLoadedFileWriteTime = DateTime.MinValue;
    private bool hasOverwriteConflict = false;

    // Tap Combo Detector
    private int currentTapCount = 0;
    private DispatcherTimer tapResetTimer;

    // Scroll dragging state
    private Component activeScrollComponent = null;
    private double activeScrollStartOffset = 0.0;
    private double activeScrollStartMouseY = 0.0;

    // GUI Data
    private List<Component> components = new List<Component>();
    private List<Variable> variables = new List<Variable>();

    public MainPage()
    {
        this.InitializeComponent();

        // Initialize tapResetTimer to decay tap combos after 300ms of inactivity
        tapResetTimer = new DispatcherTimer();
        tapResetTimer.Interval = TimeSpan.FromMilliseconds(300);
        tapResetTimer.Tick += (s, args) =>
        {
            currentTapCount = 0;
            canvas.Invalidate();
            tapResetTimer.Stop();
        };

        // Initialize Hierarchical Folder Tree Palette
        folders.Add(new PaletteFolder { Name = "Frame", Items = new List<string> { "Base", "Fill", "Scroll" } });
        folders.Add(new PaletteFolder { Name = "Edit", Items = new List<string> { "Slider", "Text", "Draw" } });

        LoadVariablesFromFile(selectedFile);
    }

    private string GetActualTestFolderPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        DirectoryInfo di = new DirectoryInfo(baseDir);
        while (di != null)
        {
            string candidate = Path.Combine(di.FullName, "testfolder");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(di.FullName, "GUI")))
            {
                return candidate;
            }
            di = di.Parent;
        }
        string fallback = Path.Combine(Directory.GetCurrentDirectory(), "testfolder");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private bool IsGuiPathName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string lower = name.ToLower();
        return lower.Contains(":") ||
               lower.Contains("||") ||
               lower.StartsWith("base") ||
               lower.StartsWith("fill") ||
               lower.StartsWith("scroll") ||
               lower.StartsWith("slider") ||
               lower.StartsWith("text") ||
               lower.StartsWith("draw");
    }

    private void LoadVariablesFromFile(string fileName)
    {
        variables.Clear();
        components.Clear();
        selectedComponent = null;
        activeSlider = null;
        activeTextEdit = null;

        string filePath = Path.Combine(GetActualTestFolderPath(), fileName);
        if (!File.Exists(filePath)) return;

        lastLoadedFileWriteTime = File.GetLastWriteTime(filePath);
        hasOverwriteConflict = false;

        string ext = Path.GetExtension(fileName).ToLower();
        if (ext == ".json")
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var loaded = System.Text.Json.JsonSerializer.Deserialize<List<Variable>>(json);
                if (loaded != null)
                {
                    variables = loaded;
                    float startY = 180;
                    foreach (var v in variables)
                    {
                        v.Position = new Vector2(30, startY);
                        startY += 30;
                    }
                }
            }
            catch { }
        }
        else if (ext == ".prop")
        {
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                int nextId = 1;
                float startY = 180;
                List<Color> paletteColors = new List<Color> { Colors.Red, Colors.Orange, Colors.Green, Colors.DodgerBlue, Colors.Violet, Colors.Yellow };

                bool hasBlocks = lines.Any(l => l.Trim() == "//" || l.Trim() == "*");

                if (!hasBlocks)
                {
                    // Fallback to old format: parse all lines as variables
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith("#")) continue;

                        if (trimmed.Contains("="))
                        {
                            var parts = trimmed.Split(new[] { '=' }, 2);
                            string namePart = parts[0].Trim();
                            string valPart = parts[1].Trim();

                            if (!valPart.EndsWith(";"))
                            {
                                System.Diagnostics.Debug.WriteLine($"[Syntax Error] Missing semicolon in variable declaration on line: {line}");
                                continue;
                            }
                            valPart = valPart.Substring(0, valPart.Length - 1).Trim();

                            string typePart = "";
                            int openParen = namePart.IndexOf('(');
                            int closeParen = namePart.IndexOf(')');
                            if (openParen >= 0 && closeParen > openParen)
                            {
                                typePart = namePart.Substring(openParen + 1, closeParen - openParen - 1).Trim().ToLower();
                                namePart = namePart.Substring(0, openParen).Trim();
                            }

                            if (!string.IsNullOrEmpty(typePart) && typePart != "int" && typePart != "list" && typePart != "float")
                            {
                                System.Diagnostics.Debug.WriteLine($"[Parse Error] Unrecognized type '{typePart}' on line: {line}");
                                return;
                            }

                            if (IsGuiPathName(namePart)) continue;

                            if (typePart == "list" || (string.IsNullOrEmpty(typePart) && valPart.StartsWith("[") && valPart.EndsWith("]")))
                            {
                                string content = (valPart.StartsWith("[") && valPart.EndsWith("]")) ? valPart.Substring(1, valPart.Length - 2) : valPart;
                                var nums = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                  .Select(s => int.TryParse(s.Trim(), out int n) ? n : 0)
                                                  .ToList();
                                variables.Add(new Variable
                                {
                                    Id = nextId++,
                                    Name = namePart,
                                    IsList = true,
                                    ListValues = nums,
                                    Type = "list",
                                    Color = paletteColors[(nextId - 2) % paletteColors.Count],
                                    Position = new Vector2(30, startY)
                                });
                            }
                            else
                            {
                                float val = 0;
                                if (valPart != "null") float.TryParse(valPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val);
                                variables.Add(new Variable
                                {
                                    Id = nextId++,
                                    Name = namePart,
                                    Value = val,
                                    IsList = false,
                                    Type = string.IsNullOrEmpty(typePart) ? ((valPart.Contains(".")) ? "float" : "int") : typePart,
                                    Color = paletteColors[(nextId - 2) % paletteColors.Count],
                                    Position = new Vector2(30, startY)
                                });
                            }
                            startY += 30;
                        }
                    }
                }
                else
                {
                    var pathMap = new Dictionary<string, Component>();
                    var rootComponents = new List<Component>();
                    var boundVarRefs = new Dictionary<Component, string>();

                    bool inGuiBlock = false;
                    bool inVarBlock = false;

                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (trimmed == "//")
                        {
                            inGuiBlock = !inGuiBlock;
                            continue;
                        }
                        if (trimmed == "*")
                        {
                            inVarBlock = !inVarBlock;
                            continue;
                        }

                        if (inVarBlock)
                        {
                            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith("#") || trimmed.StartsWith("*")) continue;

                            if (trimmed.Contains("="))
                            {
                                var parts = trimmed.Split(new[] { '=' }, 2);
                                string namePart = parts[0].Trim();
                                string valPart = parts[1].Trim();

                                if (!valPart.EndsWith(";"))
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Syntax Error] Missing semicolon in variable declaration on line: {line}");
                                    continue;
                                }
                                valPart = valPart.Substring(0, valPart.Length - 1).Trim();

                                string typePart = "";
                                int openParen = namePart.IndexOf('(');
                                int closeParen = namePart.IndexOf(')');
                                if (openParen >= 0 && closeParen > openParen)
                                {
                                    typePart = namePart.Substring(openParen + 1, closeParen - openParen - 1).Trim().ToLower();
                                    namePart = namePart.Substring(0, openParen).Trim();
                                }

                                if (!string.IsNullOrEmpty(typePart) && typePart != "int" && typePart != "list" && typePart != "float")
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Parse Error] Unrecognized type '{typePart}' on line: {line}");
                                    return;
                                }

                                if (IsGuiPathName(namePart)) continue;

                                if (typePart == "list" || (string.IsNullOrEmpty(typePart) && valPart.StartsWith("[") && valPart.EndsWith("]")))
                                {
                                    string content = (valPart.StartsWith("[") && valPart.EndsWith("]")) ? valPart.Substring(1, valPart.Length - 2) : valPart;
                                    var nums = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                      .Select(s => int.TryParse(s.Trim(), out int n) ? n : 0)
                                                      .ToList();
                                    variables.Add(new Variable
                                    {
                                        Id = nextId++,
                                        Name = namePart,
                                        IsList = true,
                                        ListValues = nums,
                                        Type = "list",
                                        Color = paletteColors[(nextId - 2) % paletteColors.Count],
                                        Position = new Vector2(30, startY)
                                    });
                                }
                                else
                                {
                                    float val = 0;
                                    if (valPart != "null") float.TryParse(valPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val);
                                    variables.Add(new Variable
                                    {
                                        Id = nextId++,
                                        Name = namePart,
                                        Value = val,
                                        IsList = false,
                                        Type = string.IsNullOrEmpty(typePart) ? ((valPart.Contains(".")) ? "float" : "int") : typePart,
                                        Color = paletteColors[(nextId - 2) % paletteColors.Count],
                                        Position = new Vector2(30, startY)
                                    });
                                }
                                startY += 30;
                            }
                        }
                        else if (inGuiBlock)
                        {
                            if (string.IsNullOrEmpty(trimmed) || !trimmed.Contains("=")) continue;

                            var parts = trimmed.Split(new[] { '=' }, 2);
                            string key = parts[0].Trim();
                            string val = parts[1].Trim();

                            if (val.Contains("||"))
                            {
                                // New diagonal corners format: base1 = x1,y1 || x2,y2 || props
                                var segments = val.Split(new[] { "||" }, StringSplitOptions.None)
                                                  .Select(s => s.Trim())
                                                  .ToArray();

                                if (segments.Length >= 2)
                                {
                                    Component comp = GetOrCreateComponentPath(key, pathMap, rootComponents);

                                    // Top-left
                                    var p1Parts = segments[0].Split(',');
                                    double x1 = 0, y1 = 0;
                                    if (p1Parts.Length == 2)
                                    {
                                        double.TryParse(p1Parts[0], out x1);
                                        double.TryParse(p1Parts[1], out y1);
                                    }

                                    // Bottom-right
                                    var p2Parts = segments[1].Split(',');
                                    double x2 = 0, y2 = 0;
                                    if (p2Parts.Length == 2)
                                    {
                                        double.TryParse(p2Parts[0], out x2);
                                        double.TryParse(p2Parts[1], out y2);
                                    }

                                    comp.Rect = new Rect(x1 * 4.0, y1 * 4.0, Math.Max(0, x2 - x1) * 4.0, Math.Max(0, y2 - y1) * 4.0);

                                    // Properties
                                    if (segments.Length >= 3)
                                    {
                                        var props = segments[2].Split('|');
                                        if (props.Length >= 4)
                                        {
                                            comp.EditType = props[0];
                                            comp.Text = props[1];
                                            float.TryParse(props[2], out float sliderVal);
                                            comp.SliderValue = sliderVal;
                                            string varRef = props[3];
                                            boundVarRefs[comp] = varRef;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Old 3-line format: base1:1 = x,y
                                int lastColon = key.LastIndexOf(':');
                                if (lastColon < 0) continue;

                                string path = key.Substring(0, lastColon);
                                string propId = key.Substring(lastColon + 1);

                                Component comp = GetOrCreateComponentPath(path, pathMap, rootComponents);

                                if (propId == "1")
                                {
                                    var coords = val.Split(',');
                                    if (coords.Length == 2)
                                    {
                                        double.TryParse(coords[0], out double x);
                                        double.TryParse(coords[1], out double y);
                                        comp.Rect = new Rect(x, y, comp.Rect.Width, comp.Rect.Height);
                                    }
                                }
                                else if (propId == "2")
                                {
                                    var size = val.Split(',');
                                    if (size.Length == 2)
                                    {
                                        double.TryParse(size[0], out double w);
                                        double.TryParse(size[1], out double h);
                                        comp.Rect = new Rect(comp.Rect.X, comp.Rect.Y, w, h);
                                    }
                                }
                                else if (propId == "3")
                                {
                                    var props = val.Split('|');
                                    if (props.Length >= 4)
                                    {
                                        comp.EditType = props[0];
                                        comp.Text = props[1];
                                        float.TryParse(props[2], out float sliderVal);
                                        comp.SliderValue = sliderVal;
                                        string varRef = props[3];
                                        boundVarRefs[comp] = varRef;
                                    }
                                }
                            }
                        }
                    }

                    // Resolve variable bindings by name or ID
                    foreach (var kvp in boundVarRefs)
                    {
                        var comp = kvp.Key;
                        string varRef = kvp.Value;
                        if (int.TryParse(varRef, out int parsedId))
                        {
                            comp.BoundVariableId = parsedId;
                        }
                        else
                        {
                            var matchingVar = variables.FirstOrDefault(v => v.Name.Equals(varRef, StringComparison.OrdinalIgnoreCase));
                            if (matchingVar != null)
                            {
                                comp.BoundVariableId = matchingVar.Id;
                            }
                        }
                    }

                    // If we loaded components successfully, assign them
                    if (rootComponents.Count > 0)
                    {
                        components = rootComponents;
                        SyncAllScrollsRecursive(components);
                    }
                }
            }
            catch { }
        }
    }

    private Component GetOrCreateComponentPath(string path, Dictionary<string, Component> pathMap, List<Component> rootComponents)
    {
        if (pathMap.ContainsKey(path)) return pathMap[path];

        int lastColon = path.LastIndexOf(':');
        string currentName = lastColon < 0 ? path : path.Substring(lastColon + 1);

        string typeStr = new string(currentName.Where(c => char.IsLetter(c)).ToArray());
        string type = "Base";
        if (typeStr.Equals("scroll", StringComparison.OrdinalIgnoreCase)) type = "Scroll";
        else if (typeStr.Equals("fill", StringComparison.OrdinalIgnoreCase)) type = "Fill";

        var comp = new Component { Type = type };
        pathMap[path] = comp;

        if (lastColon < 0)
        {
            rootComponents.Add(comp);
        }
        else
        {
            string parentPath = path.Substring(0, lastColon);
            var parent = GetOrCreateComponentPath(parentPath, pathMap, rootComponents);
            parent.Children.Add(comp);
        }

        return comp;
    }

    private void SyncScrollChildren(Component scroll, Variable listVar)
    {
        if (scroll == null || listVar == null || !listVar.IsList) return;

        int count = listVar.ListValues.Count;

        var listItems = scroll.Children.Where(c => c.BoundVariableId == scroll.BoundVariableId).ToList();
        var nonListItems = scroll.Children.Where(c => c.BoundVariableId != scroll.BoundVariableId).ToList();

        // Sort existing list items by Y center, then X center
        listItems = listItems
            .OrderBy(c => ((c.Rect.Y + c.Rect.Bottom) / 2.0) - scroll.Rect.Y)
            .ThenBy(c => ((c.Rect.X + c.Rect.Right) / 2.0) - scroll.Rect.X)
            .ToList();

        for (int i = 0; i < Math.Min(listItems.Count, count); i++)
        {
            var child = listItems[i];
            child.Type = "Fill";
            if (child.EditType == "None") child.EditType = "Text";
            child.Text = listVar.ListValues[i].ToString();
        }

        while (listItems.Count > count)
        {
            listItems.RemoveAt(listItems.Count - 1);
        }

        while (listItems.Count < count)
        {
            int newIndex = listItems.Count;
            double nextX = scroll.Rect.X + 6.0;
            double nextY = scroll.Rect.Y + 8.0;
            
            var last = listItems.LastOrDefault();
            if (last != null)
            {
                nextX = last.Rect.Right + 4.0;
                nextY = last.Rect.Y;
                
                if (nextX + 16.0 > scroll.Rect.Right - 16.0)
                {
                    nextX = scroll.Rect.X + 6.0;
                    nextY = last.Rect.Bottom + 4.0;
                }
            }

            var child = new Component
            {
                Type = "Fill",
                EditType = "Text",
                Text = listVar.ListValues[newIndex].ToString(),
                BoundVariableId = scroll.BoundVariableId,
                Rect = new Rect(nextX, nextY, 16.0, 16.0)
            };
            listItems.Add(child);
        }

        scroll.Children = listItems.Concat(nonListItems).ToList();
    }

    private void SyncAllScrollsRecursive(List<Component> list)
    {
        if (list == null) return;
        foreach (var comp in list)
        {
            if (comp.Type == "Scroll")
            {
                var listVar = GetVariable(comp.BoundVariableId);
                if (listVar != null && listVar.IsList)
                {
                    SyncScrollChildren(comp, listVar);
                }
            }
            SyncAllScrollsRecursive(comp.Children);
        }
    }

    private Component FindScrollComponentAtMouse(Point p, List<Component> list)
    {
        if (list == null) return null;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var comp = list[i];
            Point childP = comp.Type == "Scroll" ? new Point(p.X, p.Y + comp.ScrollOffset * 40.0) : p;
            var found = FindScrollComponentAtMouse(childP, comp.Children);
            if (found != null) return found;
            if (comp.Type == "Scroll" && comp.Rect.Contains(p)) return comp;
        }
        return null;
    }

    private void Grid_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var ptr = e.GetCurrentPoint(canvas);
        Point pos = ptr.Position;

        Rect leftSidebarRect = new Rect(10, 50, 200, canvas.ActualHeight - 70);
        if (leftSidebarRect.Contains(pos))
        {
            int delta = ptr.Properties.MouseWheelDelta;
            if (delta > 0)
            {
                sidebarScrollOffset = Math.Max(0f, sidebarScrollOffset - 28f);
            }
            else
            {
                float viewportTop = 142f;
                float viewportBottom = (float)leftSidebarRect.Bottom - 45f;
                float viewportHeight = viewportBottom - viewportTop;
                float totalContentHeight = variables.Count * 28f + 40f;
                float maxSidebarScroll = Math.Max(0f, totalContentHeight - viewportHeight);
                sidebarScrollOffset = Math.Min(maxSidebarScroll, sidebarScrollOffset + 28f);
            }
            canvas.Invalidate();
            e.Handled = true;
            return;
        }

        Point workspacePos = new Point(pos.X - canvasOffset.X, pos.Y - canvasOffset.Y);

        var hit = FindScrollComponentAtMouse(workspacePos, components);
        if (hit != null && hit.Type == "Scroll")
        {
            int delta = ptr.Properties.MouseWheelDelta;
            double contentHeight = hit.Children.Count > 0 ? (hit.Children.Max(c => c.Rect.Bottom) - hit.Rect.Y) : 0;
            double maxScroll = Math.Max(0, (contentHeight - (hit.Rect.Height - 8.0)) / 40.0 + 1.0);

            if (maxScroll > 0)
            {
                if (delta > 0)
                {
                    hit.ScrollOffset = Math.Max(0, hit.ScrollOffset - 0.5);
                }
                else
                {
                    hit.ScrollOffset = Math.Min(maxScroll, hit.ScrollOffset + 0.5);
                }
                var listVar = GetVariable(hit.BoundVariableId);
                if (listVar != null && listVar.IsList)
                {
                    SyncScrollChildren(hit, listVar);
                }
                canvas.Invalidate();
                e.Handled = true;
            }
        }
    }

    private void SaveVariablesToFile(string fileName, bool force = false)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        string filePath = Path.Combine(GetActualTestFolderPath(), fileName);

        if (!force && File.Exists(filePath))
        {
            var diskTime = File.GetLastWriteTime(filePath);
            if (diskTime > lastLoadedFileWriteTime.AddSeconds(1))
            {
                hasOverwriteConflict = true;
                canvas.Invalidate();
                System.Diagnostics.Debug.WriteLine($"[Conflict Warning] External modifications detected on {fileName}!");
                return;
            }
        }
        string ext = Path.GetExtension(fileName).ToLower();

        if (ext == ".json")
        {
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                var json = System.Text.Json.JsonSerializer.Serialize(variables, options);
                File.WriteAllText(filePath, json);
                lastLoadedFileWriteTime = File.GetLastWriteTime(filePath);
                hasOverwriteConflict = false;
            }
            catch { }
        }
        else if (ext == ".prop")
        {
            try
            {
                List<string> lines = new List<string>();

                // 1. GUI Block
                lines.Add("//");
                var childTypeCounts = new Dictionary<string, int>();
                foreach (var comp in components)
                {
                    SerializeComponentPropLine(comp, "", childTypeCounts, lines);
                }
                lines.Add("//");

                // 2. Variables Block
                lines.Add("*");
                foreach (var v in variables)
                {
                    if (v.IsList)
                    {
                        string listStr = string.Join(", ", v.ListValues);
                        lines.Add($"{v.Name}(list) = [{listStr}];");
                    }
                    else
                    {
                        string typeStr = v.Type;
                        if (string.IsNullOrEmpty(typeStr) || (typeStr != "int" && typeStr != "float"))
                        {
                            typeStr = (v.Value % 1 == 0) ? "int" : "float";
                        }
                        string valStr = v.Value == 0 ? "null" : v.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        lines.Add($"{v.Name}({typeStr}) = {valStr};");
                    }
                }
                lines.Add("*");

                File.WriteAllLines(filePath, lines);
                lastLoadedFileWriteTime = File.GetLastWriteTime(filePath);
                hasOverwriteConflict = false;
            }
            catch { }
        }
    }

    private void SerializeComponentPropLine(Component comp, string parentPath, Dictionary<string, int> typeCountsPerLevel, List<string> lines)
    {
        string typeKey = comp.Type.ToLower();
        if (!typeCountsPerLevel.ContainsKey(typeKey))
        {
            typeCountsPerLevel[typeKey] = 1;
        }
        int index = typeCountsPerLevel[typeKey]++;

        string currentPath = (string.IsNullOrEmpty(parentPath) ? "" : parentPath + ":") + $"{typeKey}{index}";

        int x1 = (int)Math.Round(comp.Rect.X / 4.0);
        int y1 = (int)Math.Round(comp.Rect.Y / 4.0);
        int x2 = (int)Math.Round((comp.Rect.X + comp.Rect.Width) / 4.0);
        int y2 = (int)Math.Round((comp.Rect.Y + comp.Rect.Height) / 4.0);

        string varRef = "0";
        var boundVar = GetVariable(comp.BoundVariableId);
        if (boundVar != null)
        {
            varRef = boundVar.Name;
        }
        string propsStr = $"{comp.EditType}|{comp.Text}|{comp.SliderValue}|{varRef}";

        lines.Add($"{currentPath} = {x1},{y1} || {x2},{y2} || {propsStr}");

        var childTypeCounts = new Dictionary<string, int>();
        foreach (var child in comp.Children)
        {
            SerializeComponentPropLine(child, currentPath, childTypeCounts, lines);
        }
    }

    private bool IsScrollNameTaken(string name, List<Component> list)
    {
        foreach (var c in list)
        {
            if (c.Type == "Scroll")
            {
                var v = GetVariable(c.BoundVariableId);
                if (v != null && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (IsScrollNameTaken(name, c.Children)) return true;
        }
        return false;
    }

    private Point SnapToGrid(Point p)
    {
        double x = Math.Round(p.X / GridSpacing) * GridSpacing;
        double y = Math.Round(p.Y / GridSpacing) * GridSpacing;
        return new Point(x, y);
    }

    private Variable GetVariable(int id)
    {
        foreach (var v in variables) if (v.Id == id) return v;
        return null;
    }

    private Variable GetVariableAt(Point p)
    {
        foreach (var v in variables)
        {
            Rect bounds = new Rect(v.Position.X, v.Position.Y, 150, 25);
            if (bounds.Contains(p)) return v;
        }
        return null;
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var session = args.DrawingSession;

        // Background
        session.Clear(Color.FromArgb(255, 20, 20, 25));

        // Draw Infinite Grid Lines shifted by fractional canvasOffset (before transform)
        float gridVisualSpacing = 4f;
        float startX = canvasOffset.X % gridVisualSpacing;
        if (startX > 0) startX -= gridVisualSpacing;
        for (float x = startX; x < sender.ActualWidth; x += gridVisualSpacing)
        {
            session.DrawLine(x, 0, x, (float)sender.ActualHeight, Color.FromArgb(10, 255, 255, 255), 1);
        }

        float startY = canvasOffset.Y % gridVisualSpacing;
        if (startY > 0) startY -= gridVisualSpacing;
        for (float y = startY; y < sender.ActualHeight; y += gridVisualSpacing)
        {
            session.DrawLine(0, y, (float)sender.ActualWidth, y, Color.FromArgb(10, 255, 255, 255), 1);
        }

        var textFormat = new CanvasTextFormat { FontSize = 14, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 }, FontFamily = "Segoe UI" };

        // ==================== WORKSPACE LAYER (TRANSLATED) ====================
        session.Transform = Matrix3x2.CreateTranslation(canvasOffset.X, canvasOffset.Y);

        // Draw World Origin X at (0, 0)
        session.DrawLine(-15, -15, 15, 15, Color.FromArgb(220, 255, 50, 50), 3);
        session.DrawLine(-15, 15, 15, -15, Color.FromArgb(220, 255, 50, 50), 3);
        session.DrawText("Origin (0,0)", new Vector2(18, 5), Color.FromArgb(200, 255, 50, 50), new CanvasTextFormat { FontSize = 10, FontFamily = "Segoe UI", FontWeight = new Windows.UI.Text.FontWeight { Weight = 800 } });

        // Draw components, selection handles, wire lines
        foreach (var comp in components) DrawComponent(session, comp, textFormat);

        // Draw dashed drawing box
        if (isDrawing && startPoint.HasValue && currentPoint.HasValue)
        {
            var rect = GetRect(startPoint.Value, currentPoint.Value);
            session.DrawRectangle((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, Colors.Cyan, 2, new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash });
        }

        // Draw active wiring line
        if (isWiring && selectedComponent != null)
        {
            Vector2 start = new Vector2((float)(selectedComponent.Rect.X + selectedComponent.Rect.Width / 2 + 15), (float)selectedComponent.Rect.Bottom + 5);
            Vector2 end = new Vector2((float)currentMousePos.X, (float)currentMousePos.Y);
            session.DrawLine(start, end, Colors.Orange, 2, new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dot });
        }

        // ==================== SCREEN SPACE LAYER (FIXED UI) ====================
        session.Transform = Matrix3x2.Identity;

        // Clear dynamic targets
        sliderPlusTargets.Clear();
        sidebarTargets.Clear();

        // Draw Left Sidebar Panel
        float sidebarX = isSidebarFocused ? 10f : -180f;
        Rect leftSidebarRect = new Rect(sidebarX, 50, 200, sender.ActualHeight - 70);
        session.FillRoundedRectangle((float)leftSidebarRect.X, (float)leftSidebarRect.Y, (float)leftSidebarRect.Width, (float)leftSidebarRect.Height, 8, 8, Color.FromArgb(220, 25, 25, 30));
        session.DrawRoundedRectangle((float)leftSidebarRect.X, (float)leftSidebarRect.Y, (float)leftSidebarRect.Width, (float)leftSidebarRect.Height, 8, 8, isSidebarFocused ? Colors.Cyan : Colors.DimGray, isSidebarFocused ? 2f : 1f);

        if (!isSidebarFocused)
        {
            // Draw a vertical glowing cyan handle on the visible right edge of the collapsed sidebar
            session.FillRoundedRectangle(12, 55, 6, (float)leftSidebarRect.Height - 10, 3, 3, Color.FromArgb(180, 0, 255, 255));
        }

        float sbY = (float)leftSidebarRect.Y + 15;

        // 1. Select File Section Header
        session.DrawText("📂 SELECT FILE", new Vector2((float)leftSidebarRect.X + 10, sbY), Colors.LightGray, new CanvasTextFormat { FontSize = 11, FontWeight = new Windows.UI.Text.FontWeight { Weight = 700 } });

        sbY += 22;

        Rect fileInputRect = new Rect(leftSidebarRect.X + 10, sbY - 2, leftSidebarRect.Width - 20, 26);
        sidebarTargets.Add(new SidebarTarget { Rect = fileInputRect, Type = "SelectFileBox" });

        if (isEditingFileName)
        {
            session.FillRoundedRectangle((float)fileInputRect.X, (float)fileInputRect.Y, (float)fileInputRect.Width, (float)fileInputRect.Height, 4, 4, Color.FromArgb(60, 255, 165, 0));
            session.DrawRoundedRectangle((float)fileInputRect.X, (float)fileInputRect.Y, (float)fileInputRect.Width, (float)fileInputRect.Height, 4, 4, Colors.Orange, 1.5f);
            session.DrawText(selectedFileText + "|", new Vector2((float)fileInputRect.X + 8, (float)fileInputRect.Y + 4), Colors.Orange, textFormat);
        }
        else
        {
            session.FillRoundedRectangle((float)fileInputRect.X, (float)fileInputRect.Y, (float)fileInputRect.Width, (float)fileInputRect.Height, 4, 4, Color.FromArgb(40, 255, 255, 255));
            session.DrawRoundedRectangle((float)fileInputRect.X, (float)fileInputRect.Y, (float)fileInputRect.Width, (float)fileInputRect.Height, 4, 4, Colors.LightGray, 1);
            session.DrawText(selectedFileText, new Vector2((float)fileInputRect.X + 8, (float)fileInputRect.Y + 4), Colors.White, textFormat);
        }

        sbY += 34;

        session.DrawLine(new Vector2((float)leftSidebarRect.X + 10, sbY), new Vector2((float)leftSidebarRect.Right - 10, sbY), Colors.DimGray, 1);
        sbY += 12;

        // 2. Variables Section Header
        session.DrawText("🔑 VARIABLES & LISTS", new Vector2((float)leftSidebarRect.X + 10, sbY), Colors.LightGray, new CanvasTextFormat { FontSize = 11, FontWeight = new Windows.UI.Text.FontWeight { Weight = 700 } });
        sbY += 22;

        if (string.IsNullOrEmpty(selectedFile))
        {
            session.DrawText("Select a file above", new Vector2((float)leftSidebarRect.X + 15, sbY), Colors.Gray, textFormat);
            sbY += 25;
        }
        else
        {
            float viewportTop = sbY;
            float viewportBottom = (float)leftSidebarRect.Bottom - 45f;
            float viewportHeight = viewportBottom - viewportTop;

            float totalContentHeight = 40f;
            foreach (var v in variables)
            {
                totalContentHeight += 28f;
                if (v.IsList && v.IsExpanded)
                {
                    totalContentHeight += v.ListValues.Count * 24f + 26f;
                }
            }
            float maxSidebarScroll = Math.Max(0f, totalContentHeight - viewportHeight);
            sidebarScrollOffset = Math.Max(0f, Math.Min(sidebarScrollOffset, maxSidebarScroll));

            // Set up clipping region so variables and buttons don't draw outside viewport
            using (var layer = session.CreateLayer(1.0f, new Rect(leftSidebarRect.X + 5, viewportTop, leftSidebarRect.Width - 10, viewportHeight)))
            {
                float currentY = viewportTop - sidebarScrollOffset;

                foreach (var v in variables)
                {
                    Rect vRect = new Rect(leftSidebarRect.X + 10, currentY - 2, leftSidebarRect.Width - 20, 24);
                    sidebarTargets.Add(new SidebarTarget { Rect = vRect, Type = "Variable", Var = v });

                    bool isSel = selectedVar == v;
                    if (isSel)
                    {
                        session.FillRoundedRectangle((float)vRect.X, (float)vRect.Y, (float)vRect.Width, (float)vRect.Height, 4, 4, Color.FromArgb(40, v.Color.R, v.Color.G, v.Color.B));
                        session.DrawRoundedRectangle((float)vRect.X, (float)vRect.Y, (float)vRect.Width, (float)vRect.Height, 4, 4, Colors.White, 1);
                    }

                    // If in wiring mode, draw a beautiful pulsing glow!
                    if (isWiring)
                    {
                        float pulse = (float)(Math.Sin(DateTime.Now.TimeOfDay.TotalMilliseconds * 0.005) + 1.0) / 2.0f;
                        Color glowColor = Color.FromArgb((byte)(100 + pulse * 155), 255, 215, 0); // Gold glow
                        session.DrawRoundedRectangle((float)vRect.X - 2, (float)vRect.Y - 2, (float)vRect.Width + 4, (float)vRect.Height + 4, 6, 6, glowColor, 1.5f + pulse * 1.5f);
                    }

                    string prefix = v.IsList ? (v.IsExpanded ? "▼ 📊 " : "▶ 📊 ") : "🔸 ";
                    string valStr = v.IsList ? $"[{string.Join(",", v.ListValues.Take(2))}{(v.ListValues.Count > 2 ? "..." : "")}]" : (v.Value == 0 ? "null" : v.Value.ToString());
                    string nameDisplay = (isSel && isEditingVarName) ? v.Name + "|" : v.Name;
                    session.DrawText(prefix + $"{nameDisplay}: {valStr}", new Vector2((float)vRect.X + 5, (float)vRect.Y + 2), isSel ? Colors.Cyan : v.Color, textFormat);

                    if (isSel)
                    {
                        Rect delBtn = new Rect(vRect.Right - 22, vRect.Y + 2, 18, 18);
                        sidebarTargets.Add(new SidebarTarget { Rect = delBtn, Type = "DeleteVar", Var = v });
                        session.FillRoundedRectangle((float)delBtn.X, (float)delBtn.Y, (float)delBtn.Width, (float)delBtn.Height, 2, 2, Colors.Red);
                        session.DrawText("-", new Vector2((float)delBtn.X + 5, (float)delBtn.Y - 1), Colors.White, textFormat);
                    }

                    v.Position = new Vector2((float)vRect.X + 5, (float)vRect.Y + 2);
                    currentY += 28f;

                    if (v.IsList && v.IsExpanded)
                    {
                        for (int i = 0; i < v.ListValues.Count; i++)
                        {
                            Rect childRect = new Rect(leftSidebarRect.X + 25, currentY - 2, leftSidebarRect.Width - 35, 22);
                            sidebarTargets.Add(new SidebarTarget { Rect = childRect, Type = "ListItem", Var = v, ListIndex = i });
                            
                            string childText = $"🔸 {v.Name}[{i}]: {v.ListValues[i]}";
                            session.DrawText(childText, new Vector2((float)childRect.X + 5, (float)childRect.Y + 2), Color.FromArgb(200, v.Color.R, v.Color.G, v.Color.B), new CanvasTextFormat { FontSize = 10 });
                            currentY += 24f;
                        }

                        // Add a "+" button to add item to the list
                        Rect plusRect = new Rect(leftSidebarRect.X + 25, currentY - 2, 85, 20);
                        sidebarTargets.Add(new SidebarTarget { Rect = plusRect, Type = "AddListItem", Var = v });
                        session.FillRoundedRectangle((float)plusRect.X, (float)plusRect.Y, (float)plusRect.Width, (float)plusRect.Height, 3, 3, Color.FromArgb(50, 0, 255, 0));
                        session.DrawRoundedRectangle((float)plusRect.X, (float)plusRect.Y, (float)plusRect.Width, (float)plusRect.Height, 3, 3, Colors.Green, 1);
                        session.DrawText("+ Add Item", new Vector2((float)plusRect.X + 12, (float)plusRect.Y + 2), Colors.LightGreen, new CanvasTextFormat { FontSize = 9, FontWeight = new Windows.UI.Text.FontWeight { Weight = 700 } });
                        currentY += 26f;
                    }
                }

                currentY += 5f;

                // Add Var and Add List buttons
                Rect addVarBtn = new Rect(leftSidebarRect.X + 10, currentY, 85, 24);
                sidebarTargets.Add(new SidebarTarget { Rect = addVarBtn, Type = "AddVar" });
                session.FillRoundedRectangle((float)addVarBtn.X, (float)addVarBtn.Y, (float)addVarBtn.Width, (float)addVarBtn.Height, 4, 4, Colors.DarkGreen);
                session.DrawText("+ Var", new Vector2((float)addVarBtn.X + 15, (float)addVarBtn.Y + 3), Colors.White, textFormat);

                Rect addListBtn = new Rect(leftSidebarRect.X + 105, currentY, 85, 24);
                sidebarTargets.Add(new SidebarTarget { Rect = addListBtn, Type = "AddList" });
                session.FillRoundedRectangle((float)addListBtn.X, (float)addListBtn.Y, (float)addListBtn.Width, (float)addListBtn.Height, 4, 4, Colors.DarkBlue);
                session.DrawText("+ List", new Vector2((float)addListBtn.X + 15, (float)addListBtn.Y + 3), Colors.White, textFormat);
            }

            // Draw visual scrollbar outside of clip region if overflow exists!
            if (maxSidebarScroll > 0)
            {
                float scrollbarX = (float)leftSidebarRect.Right - 8f;
                float scrollbarHeight = (viewportHeight / totalContentHeight) * viewportHeight;
                float scrollbarY = viewportTop + (sidebarScrollOffset / maxSidebarScroll) * (viewportHeight - scrollbarHeight);
                session.FillRoundedRectangle(scrollbarX, scrollbarY, 4f, scrollbarHeight, 2f, 2f, Color.FromArgb(120, 255, 255, 255));
            }

            sbY += 34;
        }

        // Save Button at bottom
        Rect saveBtn = new Rect(leftSidebarRect.X + 10, leftSidebarRect.Bottom - 35, leftSidebarRect.Width - 20, 26);
        sidebarTargets.Add(new SidebarTarget { Rect = saveBtn, Type = "Save" });
        session.FillRoundedRectangle((float)saveBtn.X, (float)saveBtn.Y, (float)saveBtn.Width, (float)saveBtn.Height, 4, 4, Colors.Teal);
        session.DrawText("💾 Save Project (S)", new Vector2((float)saveBtn.X + 22, (float)saveBtn.Y + 4), Colors.White, textFormat);

        session.DrawText($"Active Tool: {selectedTypeToPlace}", new Vector2(20, 20), Colors.White, textFormat);
        session.DrawText("R-Click: Select/Move/Wire | Space + R-Click Drag: Create Frame | L-Click: Interact", new Vector2(20, 45), Colors.LightGray, new CanvasTextFormat { FontSize = 12 });
        session.DrawText("Ctrl + R-Click Drag: Pan Viewport", new Vector2(20, 65), Colors.Yellow, new CanvasTextFormat { FontSize = 12 });

        // Draw Tap Combo Counter at Top-Right
        if (currentTapCount > 0)
        {
            float comboX = (float)sender.ActualWidth - 360f;
            float comboY = 15f;
            string tapText = currentTapCount == 1 ? "1 TAP" : $"{currentTapCount} TAPS!";
            Color comboColor = Colors.White;
            if (currentTapCount == 2) comboColor = Colors.Cyan;
            else if (currentTapCount == 3) comboColor = Colors.Orange;
            else if (currentTapCount >= 4) comboColor = Colors.Magenta;

            session.FillRoundedRectangle(comboX, comboY, 140, 26, 6, 6, Color.FromArgb(180, 20, 20, 25));
            session.DrawRoundedRectangle(comboX, comboY, 140, 26, 6, 6, comboColor, 1.5f);
            session.DrawText($"⚡ {tapText}", new Vector2(comboX + 15, comboY + 4), comboColor, new CanvasTextFormat { FontSize = 12, FontWeight = new Windows.UI.Text.FontWeight { Weight = 700 } });
        }

        if (hasOverwriteConflict)
        {
            float bannerW = (float)sender.ActualWidth - 240f;
            session.FillRoundedRectangle(220, 10, bannerW, 36, 4, 4, Color.FromArgb(240, 255, 69, 0));
            session.DrawRoundedRectangle(220, 10, bannerW, 36, 4, 4, Colors.Red, 2f);
            session.DrawText($"⚠️ EDIT CONFLICT DETECTED: {selectedFile} was modified externally. Press 'F' to Force Save, or 'L' to reload.", new Vector2(240, 20), Colors.White, new CanvasTextFormat { FontSize = 11, FontWeight = new Windows.UI.Text.FontWeight { Weight = 800 } });
        }

        paletteRect = new Rect(sender.ActualWidth - 190, 50, 180, 250);
        session.FillRoundedRectangle((float)paletteRect.X, (float)paletteRect.Y, (float)paletteRect.Width, (float)paletteRect.Height, 8, 8, Color.FromArgb(220, 30, 30, 35));
        session.DrawRoundedRectangle((float)paletteRect.X, (float)paletteRect.Y, (float)paletteRect.Width, (float)paletteRect.Height, 8, 8, Colors.DimGray, 1);

        paletteTargets.Clear();
        float palY = (float)paletteRect.Y + 15;

        // Draw Select item
        Rect selectRect = new Rect(paletteRect.X + 10, palY - 4, paletteRect.Width - 20, 24);
        paletteTargets.Add(new PaletteTarget { Rect = selectRect, Type = "Select", Name = "Select" });
        Color selectColor = selectedTypeToPlace == "Select" ? Colors.Cyan : Colors.White;
        session.DrawText("🔍 Select", new Vector2((float)selectRect.X, (float)selectRect.Y + 2), selectColor, textFormat);
        palY += 30;

        foreach (var folder in folders)
        {
            Rect folderRect = new Rect(paletteRect.X + 10, palY - 4, paletteRect.Width - 20, 24);
            paletteTargets.Add(new PaletteTarget { Rect = folderRect, Type = "Folder", Name = folder.Name, Folder = folder });
            string folderPrefix = folder.IsExpanded ? "▼ 📁 " : "▶ 📁 ";
            session.DrawText(folderPrefix + folder.Name, new Vector2((float)folderRect.X, (float)folderRect.Y + 2), Colors.LightGray, textFormat);
            palY += 28;

            if (folder.IsExpanded)
            {
                foreach (var item in folder.Items)
                {
                    Rect itemRect = new Rect(paletteRect.X + 25, palY - 2, paletteRect.Width - 35, 22);
                    paletteTargets.Add(new PaletteTarget { Rect = itemRect, Type = "Item", Name = item });
                    Color c = selectedTypeToPlace == item ? Colors.Cyan : Colors.White;
                    if (item == "Draw") c = selectedTypeToPlace == item ? Colors.Cyan : Colors.Red;
                    session.DrawText("📄 " + item, new Vector2((float)itemRect.X, (float)itemRect.Y + 2), c, textFormat);
                    palY += 24;
                }
                palY += 4;
            }
        }
        paletteRect.Height = Math.Max(220, palY - paletteRect.Y + 10);
    }

    private void DrawComponent(CanvasDrawingSession session, Component comp, CanvasTextFormat textFormat)
    {
        if (comp.Rect.Width <= 0 || comp.Rect.Height <= 0) return;

        Color boundColor = Colors.White;
        var boundVar = GetVariable(comp.BoundVariableId);
        if (boundVar != null) boundColor = boundVar.Color;

        if (comp.Type == "Base")
        {
            session.FillRoundedRectangle((float)comp.Rect.X, (float)comp.Rect.Y, (float)comp.Rect.Width, (float)comp.Rect.Height, 4, 4, Color.FromArgb(100, 40, 40, 45));
            session.DrawRoundedRectangle((float)comp.Rect.X, (float)comp.Rect.Y, (float)comp.Rect.Width, (float)comp.Rect.Height, 4, 4, Color.FromArgb(200, 80, 80, 90), 2);
        }
        else if (comp.Type == "Scroll")
        {
            var listVar = GetVariable(comp.BoundVariableId);
            if (listVar != null && listVar.IsList)
            {
                SyncScrollChildren(comp, listVar);
            }

            if (listVar != null)
            {
                string displayName = comp == activeTextEdit ? comp.Text + "|" : listVar.Name;
                session.DrawText($"📜 {displayName}", new Vector2((float)comp.Rect.X + 4, (float)comp.Rect.Y - 16), listVar.Color, new CanvasTextFormat { FontSize = 11, FontWeight = new Windows.UI.Text.FontWeight { Weight = 700 } });
            }

            // Sleek dark grey container with a refined grey border (not blue!)
            session.FillRoundedRectangle((float)comp.Rect.X, (float)comp.Rect.Y, (float)comp.Rect.Width, (float)comp.Rect.Height, 6, 6, Color.FromArgb(255, 20, 20, 25));
            session.DrawRoundedRectangle((float)comp.Rect.X, (float)comp.Rect.Y, (float)comp.Rect.Width, (float)comp.Rect.Height, 6, 6, Color.FromArgb(120, 80, 80, 90), 1.5f);

            // Draw Scrollbar track
            double trackX = comp.Rect.Right - 16;
            double trackY = comp.Rect.Y + 4;
            double trackW = 10;
            double trackH = comp.Rect.Height - 8;
            session.FillRoundedRectangle((float)trackX, (float)trackY, (float)trackW, (float)trackH, 3, 3, Color.FromArgb(40, 255, 255, 255));

            // Calculate Scroll thumb (scroll block)
            double contentHeight = comp.Children.Count > 0 ? (comp.Children.Max(c => c.Rect.Bottom) - comp.Rect.Y) : 0;
            double maxScroll = Math.Max(0, (contentHeight - (comp.Rect.Height - 8.0)) / 40.0 + 1.0);
            double thumbH = trackH;
            double thumbY = trackY;
            if (maxScroll > 0)
            {
                thumbH = Math.Max(20.0, (comp.Rect.Height - 8.0) / (contentHeight + 40.0) * trackH);
                thumbY = trackY + (comp.ScrollOffset / maxScroll) * (trackH - thumbH);
            }
            // Draw Scroll thumb block (refined light-grey block)
            session.FillRoundedRectangle((float)trackX, (float)thumbY, (float)trackW, (float)thumbH, 3f, 3f, Color.FromArgb(180, 255, 255, 255));
        }
        else if (comp.Type == "Fill")
        {
            // ALWAYS draw Fill container background and thin outline to keep borders intact
            session.FillRoundedRectangle((float)comp.Rect.X, (float)comp.Rect.Y, (float)comp.Rect.Width, (float)comp.Rect.Height, 2, 2, Color.FromArgb(20, 255, 255, 255));
            session.DrawRoundedRectangle((float)comp.Rect.X, (float)comp.Rect.Y, (float)comp.Rect.Width, (float)comp.Rect.Height, 2, 2, Color.FromArgb(80, 255, 255, 255), 1);

            if (comp.EditType == "None")
            {
                session.DrawRoundedRectangle((float)comp.Rect.X, (float)comp.Rect.Y, (float)comp.Rect.Width, (float)comp.Rect.Height, 2, 2, Color.FromArgb(120, 255, 255, 255), 1, new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dot });
            }
            else if (comp.EditType == "Text")
            {
                if (comp == activeTextEdit)
                {
                    session.FillRoundedRectangle((float)comp.Rect.X, (float)comp.Rect.Y, (float)comp.Rect.Width, (float)comp.Rect.Height, 2, 2, Color.FromArgb(60, 0, 120, 215));
                    session.DrawRoundedRectangle((float)comp.Rect.X, (float)comp.Rect.Y, (float)comp.Rect.Width, (float)comp.Rect.Height, 2, 2, Colors.Cyan, 1);
                }

                // Check if inside a Scroll bound to a List
                var parentScroll = FindParentComponent(comp, components);
                Variable parentListVar = null;
                int childIndex = -1;
                if (parentScroll != null && parentScroll.Type == "Scroll")
                {
                    parentListVar = GetVariable(parentScroll.BoundVariableId);
                    if (parentListVar != null && parentListVar.IsList)
                    {
                        childIndex = GetListChildIndex(parentScroll, comp);
                    }
                }

                string txt = comp.Text;
                if (parentListVar != null && childIndex >= 0 && childIndex < parentListVar.ListValues.Count)
                {
                    if (comp != activeTextEdit)
                    {
                        txt = parentListVar.ListValues[childIndex].ToString();
                    }
                }
                else if (boundVar != null && comp != activeTextEdit)
                {
                    if (boundVar.IsList)
                    {
                        txt = "[" + string.Join(", ", boundVar.ListValues) + "]";
                    }
                    else
                    {
                        txt = boundVar.Value == 0 ? "null" : boundVar.Value.ToString();
                    }
                }
                if (comp == activeTextEdit) txt += "|";

                session.DrawText(txt, new Vector2((float)comp.Rect.X + 8, (float)comp.Rect.Y + 6), Colors.White, textFormat);

                // Draw spatial index inside Scroll
                if (parentScroll != null && parentScroll.Type == "Scroll" && childIndex >= 0)
                {
                    string label = parentListVar != null ? $"{parentListVar.Name}[{childIndex}]" : $"{childIndex}";
                    session.DrawText(label, new Vector2((float)comp.Rect.X + 4, (float)comp.Rect.Y + (float)comp.Rect.Height - 16), Colors.DeepSkyBlue, new CanvasTextFormat { FontSize = 10, FontWeight = new Windows.UI.Text.FontWeight { Weight = 700 } });
                }
            }
            else if (comp.EditType == "Slider")
            {
                float sliderVal = comp.SliderValue;
                var parentScroll = FindParentComponent(comp, components);
                Variable parentListVar = null;
                int childIndex = -1;
                if (parentScroll != null && parentScroll.Type == "Scroll")
                {
                    parentListVar = GetVariable(parentScroll.BoundVariableId);
                    if (parentListVar != null && parentListVar.IsList)
                    {
                        childIndex = GetListChildIndex(parentScroll, comp);
                        if (childIndex >= 0 && childIndex < parentListVar.ListValues.Count)
                        {
                            sliderVal = parentListVar.ListValues[childIndex] / 100f;
                            comp.SliderValue = sliderVal;
                        }
                    }
                }

                Color sColor = boundVar != null ? boundColor : (parentListVar != null ? parentListVar.Color : Colors.Cyan);
                session.FillRoundedRectangle((float)comp.Rect.X + 6, (float)comp.Rect.Y + (float)comp.Rect.Height / 2 - 3, (float)comp.Rect.Width - 12, 6, 3, 3, Color.FromArgb(40, 255, 255, 255));
                session.FillRoundedRectangle((float)comp.Rect.X + 6, (float)comp.Rect.Y + (float)comp.Rect.Height / 2 - 3, (float)((comp.Rect.Width - 12) * sliderVal), 6, 3, 3, sColor);
                session.FillCircle((float)(comp.Rect.X + 6 + (comp.Rect.Width - 12) * sliderVal), (float)comp.Rect.Y + (float)comp.Rect.Height / 2, 7, Colors.White);
                session.DrawCircle((float)(comp.Rect.X + 6 + (comp.Rect.Width - 12) * sliderVal), (float)comp.Rect.Y + (float)comp.Rect.Height / 2, 7, sColor, 1.5f);

                // Draw spatial index inside Scroll
                if (parentScroll != null && parentScroll.Type == "Scroll" && childIndex >= 0)
                {
                    string label = parentListVar != null ? $"{parentListVar.Name}[{childIndex}]" : $"{childIndex}";
                    session.DrawText(label, new Vector2((float)comp.Rect.X + 4, (float)comp.Rect.Y + (float)comp.Rect.Height - 16), Colors.DeepSkyBlue, new CanvasTextFormat { FontSize = 10, FontWeight = new Windows.UI.Text.FontWeight { Weight = 700 } });
                }
            }
            else if (comp.EditType == "Draw")
            {
                // Draw type fills it red as requested, padded inside the border
                session.FillRoundedRectangle((float)comp.Rect.X + 2, (float)comp.Rect.Y + 2, (float)comp.Rect.Width - 4, (float)comp.Rect.Height - 4, 2, 2, Color.FromArgb(150, 255, 50, 50));
                session.DrawRoundedRectangle((float)comp.Rect.X + 2, (float)comp.Rect.Y + 2, (float)comp.Rect.Width - 4, (float)comp.Rect.Height - 4, 2, 2, Colors.Red, 1.5f);
                session.DrawText("DRAW", new Vector2((float)comp.Rect.X + 8, (float)comp.Rect.Y + 6), Colors.White, textFormat);
            }
        }

        if (comp.Type == "Scroll")
        {
            var prevTransform = session.Transform;
            using (session.CreateLayer(1.0f, new Rect((float)comp.Rect.X, (float)comp.Rect.Y + 4, (float)comp.Rect.Width, (float)comp.Rect.Height - 8)))
            {
                session.Transform = System.Numerics.Matrix3x2.CreateTranslation(0, -(float)(comp.ScrollOffset * 40.0)) * prevTransform;
                foreach (var child in comp.Children) DrawComponent(session, child, textFormat);
                session.Transform = prevTransform;
            }
        }
        else
        {
            foreach (var child in comp.Children) DrawComponent(session, child, textFormat);
        }

        if (comp == selectedComponent)
        {
            session.DrawRectangle((float)comp.Rect.X - 2, (float)comp.Rect.Y - 2, (float)comp.Rect.Width + 4, (float)comp.Rect.Height + 4, Colors.Magenta, 2);
            float r = 6;
            session.FillRectangle((float)comp.Rect.X - r, (float)comp.Rect.Y - r, r * 2, r * 2, Colors.White);
            session.FillRectangle((float)comp.Rect.Right - r, (float)comp.Rect.Y - r, r * 2, r * 2, Colors.White);
            session.FillRectangle((float)comp.Rect.X - r, (float)comp.Rect.Bottom - r, r * 2, r * 2, Colors.White);
            session.FillRectangle((float)comp.Rect.Right - r, (float)comp.Rect.Bottom - r, r * 2, r * 2, Colors.White);

            // Delete handle (Red) - to the left of move handle
            session.FillRoundedRectangle((float)(comp.Rect.X + comp.Rect.Width / 2 - 40), (float)comp.Rect.Bottom + 4, 20, 12, 4, 4, Colors.Red);
            session.DrawText("x", new Vector2((float)(comp.Rect.X + comp.Rect.Width / 2 - 34), (float)comp.Rect.Bottom - 1), Colors.White, new CanvasTextFormat { FontSize = 10, FontWeight = new Windows.UI.Text.FontWeight { Weight = 800 } });

            // Clear EditType '-' handle (OrangeRed) - ONLY if it's a Fill and has an edit!
            if (comp.Type == "Fill" && comp.EditType != "None")
            {
                session.FillRoundedRectangle((float)(comp.Rect.X + comp.Rect.Width / 2 - 65), (float)comp.Rect.Bottom + 4, 20, 12, 4, 4, Colors.OrangeRed);
                session.DrawText("-", new Vector2((float)(comp.Rect.X + comp.Rect.Width / 2 - 58), (float)comp.Rect.Bottom - 1), Colors.White, new CanvasTextFormat { FontSize = 10, FontWeight = new Windows.UI.Text.FontWeight { Weight = 800 } });
            }

            // Move handle (Blue)
            session.FillRoundedRectangle((float)(comp.Rect.X + comp.Rect.Width / 2 - 15), (float)comp.Rect.Bottom + 4, 30, 12, 4, 4, Colors.DodgerBlue);

            // Wire handle (Orange) - ONLY draw if not a Base!
            if (comp.Type != "Base")
            {
                session.FillCircle((float)(comp.Rect.X + comp.Rect.Width / 2 + 25), (float)comp.Rect.Bottom + 10, 8, Colors.Orange);
            }
        }

        if (boundVar != null)
        {
            Vector2 start = new Vector2(boundVar.Position.X + 150, boundVar.Position.Y + 12);
            Vector2 end = new Vector2((float)comp.Rect.X, (float)comp.Rect.Y + (float)comp.Rect.Height / 2);
            session.DrawLine(start, end, Color.FromArgb(150, boundColor.R, boundColor.G, boundColor.B), 2, new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash });
        }
    }

    private Component GetComponentAt(Point p, List<Component> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var comp = list[i];
            if (comp.Type == "Scroll")
            {
                var listVar = GetVariable(comp.BoundVariableId);
                if (listVar != null && listVar.IsList)
                {
                    SyncScrollChildren(comp, listVar);
                }
                
                if (comp.Rect.Contains(p))
                {
                    Point childP = new Point(p.X, p.Y + comp.ScrollOffset * 40.0);
                    var hitChild = GetComponentAt(childP, comp.Children);
                    if (hitChild != null) return hitChild;
                    return comp;
                }
            }
            else
            {
                var hitChild = GetComponentAt(p, comp.Children);
                if (hitChild != null) return hitChild;
                if (comp.Rect.Contains(p)) return comp;
            }
        }
        return null;
    }

    private void UpdateSliderValue(Component comp, Point pos)
    {
        float val = (float)((pos.X - comp.Rect.X) / comp.Rect.Width);
        comp.SliderValue = Math.Max(0, Math.Min(1, val));
        var boundVar = GetVariable(comp.BoundVariableId);
        if (boundVar != null) boundVar.Value = (int)(comp.SliderValue * 100);

        var parentScroll = FindParentComponent(comp, components);
        if (parentScroll != null && parentScroll.Type == "Scroll")
        {
            var listVar = GetVariable(parentScroll.BoundVariableId);
            if (listVar != null && listVar.IsList)
            {
                int childIndex = GetListChildIndex(parentScroll, comp);
                if (childIndex >= 0 && childIndex < listVar.ListValues.Count)
                {
                    listVar.ListValues[childIndex] = (int)(comp.SliderValue * 100);
                }
            }
        }
    }

    private void Grid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        RootGrid.Focus(FocusState.Programmatic); // Force focus to RootGrid so Tab key always works instantly!

        var ptr = e.GetCurrentPoint(canvas);
        if (ptr.Properties.IsRightButtonPressed)
        {
            currentTapCount++;
            tapResetTimer.Stop();
            tapResetTimer.Start();
            canvas.Invalidate();
        }
        Point pos = ptr.Position;
        Point workspacePos = new Point(pos.X - canvasOffset.X, pos.Y - canvasOffset.Y);

        // Check scrollbar thumb dragging click
        Component clickedScrollComp = FindScrollComponentAtMouse(workspacePos, components);
        if (clickedScrollComp != null)
        {
            double trackY = clickedScrollComp.Rect.Y + 4;
            double trackH = clickedScrollComp.Rect.Height - 8;
            double contentHeight = clickedScrollComp.Children.Count > 0 ? (clickedScrollComp.Children.Max(c => c.Rect.Bottom) - clickedScrollComp.Rect.Y) : 0;
            double maxScroll = Math.Max(0, (contentHeight - (clickedScrollComp.Rect.Height - 8.0)) / 40.0 + 1.0);

            if (maxScroll > 0)
            {
                double thumbH = Math.Max(20.0, (clickedScrollComp.Rect.Height - 8.0) / (contentHeight + 40.0) * trackH);
                double thumbY = trackY + (clickedScrollComp.ScrollOffset / maxScroll) * (trackH - thumbH);
                Rect thumbRect = new Rect(clickedScrollComp.Rect.Right - 16, thumbY, 10, thumbH);

                if (thumbRect.Contains(workspacePos))
                {
                    activeScrollComponent = clickedScrollComp;
                    activeScrollStartOffset = clickedScrollComp.ScrollOffset;
                    activeScrollStartMouseY = pos.Y;
                    e.Handled = true;
                    return;
                }
            }
        }

        Point workspaceSnapped = SnapToGrid(workspacePos);

        var keyModifiers = e.KeyModifiers;
        bool isCtrl = (keyModifiers & Windows.System.VirtualKeyModifiers.Control) == Windows.System.VirtualKeyModifiers.Control;
        if (ptr.Properties.IsRightButtonPressed && isCtrl)
        {
            isPanning = true;
            panStartPos = pos;
            panStartOffset = canvasOffset;
            e.Handled = true;
            canvas.Invalidate();
            return;
        }

        if (ptr.Properties.IsLeftButtonPressed)
        {
            // 1. Sidebar clicks
            Rect leftSidebarRect = new Rect(10, 50, 200, canvas.ActualHeight - 70);

            bool clickedSelectFile = false;
            if (leftSidebarRect.Contains(pos))
            {
                isSidebarFocused = true;
                foreach (var target in sidebarTargets)
                {
                    if (target.Rect.Contains(pos) && target.Type == "SelectFileBox")
                    {
                        clickedSelectFile = true;
                        break;
                    }
                }
            }

            if (!clickedSelectFile && isEditingFileName)
            {
                isEditingFileName = false;
                CommitSelectFile();
            }

            if (leftSidebarRect.Contains(pos))
            {
                // If in wiring mode and we clicked a variable, link it!
                if (isWiring && selectedComponent != null)
                {
                    var target = sidebarTargets.FirstOrDefault(t => t.Rect.Contains(pos));
                    if (target != null && target.Type == "Variable" && target.Var != null)
                    {
                        selectedComponent.BoundVariableId = target.Var.Id;
                        if (selectedComponent.Type == "Scroll" && target.Var.IsList)
                        {
                            foreach (var child in selectedComponent.Children)
                            {
                                if (child.BoundVariableId == 0) child.BoundVariableId = target.Var.Id;
                            }
                            SyncScrollChildren(selectedComponent, target.Var);
                        }
                        isWiring = false;
                        canvas.Invalidate();
                        if (!string.IsNullOrEmpty(selectedFile)) SaveVariablesToFile(selectedFile);
                        return;
                    }
                }

                if (!isWiring)
                {
                    selectedComponent = null;
                }

                foreach (var target in sidebarTargets)
                {
                    if (target.Rect.Contains(pos))
                    {
                        if (target.Type == "SelectFileBox")
                        {
                            isEditingFileName = true;
                            if (activeTextEdit != null) activeTextEdit = null;
                        }
                        else if (target.Type == "Variable")
                        {
                            selectedVar = target.Var;
                            isEditingVarName = false;
                        }
                        else if (target.Type == "ListItem" && target.Var != null)
                        {
                            selectedVar = target.Var;
                            isEditingVarName = false;
                        }
                        else if (target.Type == "AddListItem" && target.Var != null)
                        {
                            target.Var.ListValues.Add(0);
                            foreach (var comp in components)
                            {
                                if (comp.Type == "Scroll" && comp.BoundVariableId == target.Var.Id)
                                {
                                    SyncScrollChildren(comp, target.Var);
                                }
                            }
                            if (!string.IsNullOrEmpty(selectedFile)) SaveVariablesToFile(selectedFile);
                        }
                        else if (target.Type == "AddVar")
                        {
                            int suffix = 0;
                            string autoName = $"var{suffix}";
                            while (variables.Any(v => v.Name.Equals(autoName, StringComparison.OrdinalIgnoreCase)))
                            {
                                suffix++;
                                autoName = $"var{suffix}";
                            }
                            var paletteColors = new List<Color> { Colors.Red, Colors.Orange, Colors.Green, Colors.DodgerBlue, Colors.Violet, Colors.Yellow };
                            variables.Add(new Variable
                            {
                                Id = variables.Count > 0 ? variables.Max(v => v.Id) + 1 : 1,
                                Name = autoName,
                                Value = 0,
                                Type = "int",
                                Color = paletteColors[variables.Count % paletteColors.Count],
                                Position = new Vector2(30, 180 + variables.Count * 30)
                            });
                            if (!string.IsNullOrEmpty(selectedFile)) SaveVariablesToFile(selectedFile);
                        }
                        else if (target.Type == "AddList")
                        {
                            int suffix = 0;
                            string autoName = $"list{suffix}";
                            while (variables.Any(v => v.Name.Equals(autoName, StringComparison.OrdinalIgnoreCase)))
                            {
                                suffix++;
                                autoName = $"list{suffix}";
                            }
                            var paletteColors = new List<Color> { Colors.Red, Colors.Orange, Colors.Green, Colors.DodgerBlue, Colors.Violet, Colors.Yellow };
                            variables.Add(new Variable
                            {
                                Id = variables.Count > 0 ? variables.Max(v => v.Id) + 1 : 1,
                                Name = autoName,
                                IsList = true,
                                ListValues = new List<int> { 1, 2, 3, 4, 5 },
                                Type = "list",
                                Color = paletteColors[variables.Count % paletteColors.Count],
                                Position = new Vector2(30, 180 + variables.Count * 30)
                            });
                            if (!string.IsNullOrEmpty(selectedFile)) SaveVariablesToFile(selectedFile);
                        }
                        else if (target.Type == "DeleteVar" && target.Var != null)
                        {
                            variables.Remove(target.Var);
                            selectedVar = null;
                            if (!string.IsNullOrEmpty(selectedFile)) SaveVariablesToFile(selectedFile);
                        }
                        else if (target.Type == "Save")
                        {
                            if (!string.IsNullOrEmpty(selectedFile))
                            {
                                SaveVariablesToFile(selectedFile);
                            }
                        }
                        canvas.Invalidate();
                        break;
                    }
                }
                return;
            }

            // 2. Slider Frame '+' click checks
            foreach (var target in sliderPlusTargets)
            {
                if (target.Rect.Contains(pos))
                {
                    var newFill = new Component { Type = "Fill", Rect = new Rect(0, 0, 70, target.Slider.Rect.Height - 30) };
                    target.Slider.Children.Insert(target.InsertIndex, newFill);

                    var listVar = GetVariable(target.Slider.BoundVariableId);
                    if (listVar != null && listVar.IsList)
                    {
                        listVar.ListValues.Insert(target.InsertIndex, 0);
                        if (target.Slider.Type == "Scroll")
                        {
                            SyncScrollChildren(target.Slider, listVar);
                        }
                    }

                    canvas.Invalidate();
                    return;
                }
            }

            if (paletteRect.Contains(pos))
            {
                foreach (var target in paletteTargets)
                {
                    if (target.Rect.Contains(pos))
                    {
                        if (target.Type == "Folder")
                        {
                            target.Folder.IsExpanded = !target.Folder.IsExpanded;
                        }
                        else if (target.Type == "Item" || target.Type == "Select")
                        {
                            selectedTypeToPlace = target.Name;
                        }
                        canvas.Invalidate();
                        break;
                    }
                }
                return;
            }

            // Check if they clicked wire handle of selected component!
            if (selectedComponent != null && selectedComponent.Type != "Base")
            {
                Rect wireHandle = new Rect(selectedComponent.Rect.X + selectedComponent.Rect.Width / 2 + 17, selectedComponent.Rect.Bottom + 2, 16, 16);
                if (wireHandle.Contains(workspacePos))
                {
                    isWiring = !isWiring;
                    canvas.Invalidate();
                    e.Handled = true;
                    return;
                }
            }

            var hit = GetComponentAt(workspacePos, components);
            if (hit != null)
            {
                if (isWiring)
                {
                    isWiring = false; // Cancel wiring if clicking another component
                }
                selectedComponent = hit;

                if (hit.Type == "Fill" && hit.EditType == "Slider")
                {
                    activeSlider = hit;
                    UpdateSliderValue(hit, workspacePos);
                }
                else if (hit.Type == "Fill" && hit.EditType == "Text")
                {
                    activeTextEdit = hit; // Highlight immediately

                    var parentScroll = FindParentComponent(hit, components);
                    if (parentScroll != null && parentScroll.Type == "Scroll")
                    {
                        var listVar = GetVariable(parentScroll.BoundVariableId);
                        if (listVar != null && listVar.IsList)
                        {
                            int childIndex = GetListChildIndex(parentScroll, hit);
                            if (childIndex >= 0 && childIndex < listVar.ListValues.Count)
                            {
                                hit.Text = listVar.ListValues[childIndex].ToString();
                            }
                        }
                    }

                    RootGrid.Focus(FocusState.Programmatic);
                }
                else if (hit.Type == "Scroll")
                {
                    activeTextEdit = null;
                    isMoving = true;
                    dragOffset = new Point(workspacePos.X - selectedComponent.Rect.X, workspacePos.Y - selectedComponent.Rect.Y);
                }
                else
                {
                    isMoving = true;
                    dragOffset = new Point(workspacePos.X - selectedComponent.Rect.X, workspacePos.Y - selectedComponent.Rect.Y);
                }
                canvas.Invalidate();
                return;
            }

            // Clicked empty space or non-text:
            if (activeTextEdit != null)
            {
                activeTextEdit = null;
                canvas.Invalidate();
            }
            if (isWiring)
            {
                isWiring = false;
            }
            selectedComponent = null;
            selectedVar = null;
            isEditingVarName = false;
        }
        else if (ptr.Properties.IsRightButtonPressed)
        {
            Rect leftSidebarRect = new Rect(10, 50, 200, canvas.ActualHeight - 70);
            if (leftSidebarRect.Contains(pos))
            {
                foreach (var target in sidebarTargets)
                {
                    if (target.Rect.Contains(pos) && target.Type == "Variable" && target.Var != null && target.Var.IsList)
                    {
                        target.Var.IsExpanded = !target.Var.IsExpanded;
                        canvas.Invalidate();
                        break;
                    }
                }
                return;
            }

            if (selectedComponent != null)
            {
                Rect minusHandle = new Rect(selectedComponent.Rect.X + selectedComponent.Rect.Width / 2 - 65, selectedComponent.Rect.Bottom + 4, 20, 12);
                Rect deleteHandle = new Rect(selectedComponent.Rect.X + selectedComponent.Rect.Width / 2 - 40, selectedComponent.Rect.Bottom + 4, 20, 12);
                Rect moveHandle = new Rect(selectedComponent.Rect.X + selectedComponent.Rect.Width / 2 - 15, selectedComponent.Rect.Bottom + 4, 30, 12);
                Rect wireHandle = new Rect(selectedComponent.Rect.X + selectedComponent.Rect.Width / 2 + 17, selectedComponent.Rect.Bottom + 2, 16, 16);
                float r = 6;
                Rect[] corners = new Rect[] {
                    new Rect(selectedComponent.Rect.X - r, selectedComponent.Rect.Y - r, r*2, r*2),
                    new Rect(selectedComponent.Rect.Right - r, selectedComponent.Rect.Y - r, r*2, r*2),
                    new Rect(selectedComponent.Rect.X - r, selectedComponent.Rect.Bottom - r, r*2, r*2),
                    new Rect(selectedComponent.Rect.Right - r, selectedComponent.Rect.Bottom - r, r*2, r*2)
                };

                if (selectedComponent.Type == "Fill" && selectedComponent.EditType != "None" && minusHandle.Contains(workspacePos))
                {
                    selectedComponent.EditType = "None";
                    if (activeTextEdit == selectedComponent)
                    {
                        activeTextEdit = null;
                    }
                    if (activeSlider == selectedComponent) activeSlider = null;
                    canvas.Invalidate();
                    return;
                }

                if (deleteHandle.Contains(workspacePos))
                {
                    DeleteSelectedComponent();
                    return;
                }

                for (int i = 0; i < corners.Length; i++)
                {
                    if (corners[i].Contains(workspacePos))
                    {
                        isResizing = true;
                        if (i == 0) fixedCorner = new Point(selectedComponent.Rect.Right, selectedComponent.Rect.Bottom);
                        else if (i == 1) fixedCorner = new Point(selectedComponent.Rect.X, selectedComponent.Rect.Bottom);
                        else if (i == 2) fixedCorner = new Point(selectedComponent.Rect.Right, selectedComponent.Rect.Y);
                        else if (i == 3) fixedCorner = new Point(selectedComponent.Rect.X, selectedComponent.Rect.Y);
                        return;
                    }
                }
                if (moveHandle.Contains(workspacePos))
                {
                    isMoving = true;
                    dragOffset = new Point(workspacePos.X - selectedComponent.Rect.X, workspacePos.Y - selectedComponent.Rect.Y);
                    return;
                }
                else if (wireHandle.Contains(workspacePos) && selectedComponent.Type != "Base") { isWiring = !isWiring; canvas.Invalidate(); return; }
            }

            var hit = GetComponentAt(workspacePos, components);
            selectedComponent = hit;

            if (hit != null && hit.Type == "Fill")
            {
                if (selectedTypeToPlace == "Slider" || selectedTypeToPlace == "Text" || selectedTypeToPlace == "Draw")
                {
                    if (hit.EditType == "None")
                    {
                        hit.EditType = selectedTypeToPlace;
                    }
                    canvas.Invalidate();
                    return;
                }
            }

            if (isSpacePressed && selectedTypeToPlace != "Select" && selectedTypeToPlace != "Slider" && selectedTypeToPlace != "Text" && selectedTypeToPlace != "Draw")
            {
                isDrawing = true; startPoint = workspaceSnapped; currentPoint = workspaceSnapped;
            }
            canvas.Invalidate();
        }
    }

    private void MoveComponentRecursive(Component comp, double dx, double dy)
    {
        comp.Rect.X += dx;
        comp.Rect.Y += dy;
        foreach (var child in comp.Children) MoveComponentRecursive(child, dx, dy);
    }

    private void Grid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var ptr = e.GetCurrentPoint(canvas);
        Point pos = ptr.Position;
        Point workspacePos = new Point(pos.X - canvasOffset.X, pos.Y - canvasOffset.Y);
        currentMousePos = workspacePos;
        canvas.Invalidate(); // Ensure smooth redrawing on hover

        if (activeScrollComponent != null)
        {
            double dy = pos.Y - activeScrollStartMouseY;
            double trackH = activeScrollComponent.Rect.Height - 8;
            double contentHeight = activeScrollComponent.Children.Count > 0 ? (activeScrollComponent.Children.Max(c => c.Rect.Bottom) - activeScrollComponent.Rect.Y) : 0;
            double maxScroll = Math.Max(0, (contentHeight - (activeScrollComponent.Rect.Height - 8.0)) / 40.0 + 1.0);

            if (maxScroll > 0)
            {
                double thumbH = Math.Max(20.0, (activeScrollComponent.Rect.Height - 8.0) / (contentHeight + 40.0) * trackH);
                double trackRangeY = trackH - thumbH;
                if (trackRangeY > 0)
                {
                    double rowDelta = (dy / trackRangeY) * maxScroll;
                    activeScrollComponent.ScrollOffset = Math.Max(0, Math.Min(maxScroll, activeScrollStartOffset + rowDelta));
                    canvas.Invalidate();
                }
            }
            e.Handled = true;
            return;
        }

        if (isPanning)
        {
            double dx = pos.X - panStartPos.X;
            double dy = pos.Y - panStartPos.Y;
            canvasOffset = new Vector2((float)(panStartOffset.X + dx), (float)(panStartOffset.Y + dy));
            canvas.Invalidate();
            e.Handled = true;
            return;
        }

        if (isDrawing) { currentPoint = SnapToGrid(workspacePos); canvas.Invalidate(); }
        else if (isMoving && selectedComponent != null)
        {
            Point p = new Point(workspacePos.X - dragOffset.X, workspacePos.Y - dragOffset.Y);
            Point snapped = SnapToGrid(p);
            double dx = snapped.X - selectedComponent.Rect.X;
            double dy = snapped.Y - selectedComponent.Rect.Y;

            if (selectedComponent.Type == "Fill" || selectedComponent.Type == "Scroll")
            {
                var parent = FindParentComponent(selectedComponent, components);
                if (parent != null)
                {
                    Rect newRect = new Rect(selectedComponent.Rect.X + dx, selectedComponent.Rect.Y + dy, selectedComponent.Rect.Width, selectedComponent.Rect.Height);
                    if (newRect.X < parent.Rect.X) newRect.X = parent.Rect.X;
                    if (newRect.Right > parent.Rect.Right) newRect.X = parent.Rect.Right - newRect.Width;
                    if (newRect.Y < parent.Rect.Y) newRect.Y = parent.Rect.Y;
                    if (newRect.Bottom > parent.Rect.Bottom) newRect.Y = parent.Rect.Bottom - newRect.Height;

                    if (IsOverlappingAny(newRect, parent.Children, selectedComponent))
                    {
                        dx = 0;
                        dy = 0;
                    }
                    else
                    {
                        Point constrained = SnapToGrid(new Point(newRect.X, newRect.Y));
                        dx = constrained.X - selectedComponent.Rect.X;
                        dy = constrained.Y - selectedComponent.Rect.Y;
                    }
                }
            }

            MoveComponentRecursive(selectedComponent, dx, dy);
            canvas.Invalidate();
        }
        else if (isResizing && selectedComponent != null)
        {
            Point snapped = SnapToGrid(workspacePos);
            Rect newRect = GetRect(fixedCorner, snapped);

            if (selectedComponent.Type == "Fill" || selectedComponent.Type == "Scroll")
            {
                var parent = FindParentComponent(selectedComponent, components);
                if (parent != null)
                {
                    if (newRect.X < parent.Rect.X) newRect.X = parent.Rect.X;
                    if (newRect.Right > parent.Rect.Right) newRect.Width = parent.Rect.Right - newRect.X;
                    if (newRect.Y < parent.Rect.Y) newRect.Y = parent.Rect.Y;
                    if (newRect.Bottom > parent.Rect.Bottom) newRect.Height = parent.Rect.Bottom - newRect.Y;

                    if (IsOverlappingAny(newRect, parent.Children, selectedComponent))
                    {
                        newRect = selectedComponent.Rect;
                    }
                }
            }

            selectedComponent.Rect = newRect;
            canvas.Invalidate();
        }
        else if (isWiring) canvas.Invalidate();

        if (ptr.Properties.IsLeftButtonPressed && activeSlider != null)
        {
            UpdateSliderValue(activeSlider, workspacePos);
            canvas.Invalidate();
        }
    }

    private void Grid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        RootGrid.Focus(FocusState.Programmatic);
        var ptr = e.GetCurrentPoint(canvas);

        if (activeScrollComponent != null)
        {
            activeScrollComponent = null;
            canvas.Invalidate();
            e.Handled = true;
            return;
        }

        if (isPanning)
        {
            isPanning = false;
            canvas.Invalidate();
            e.Handled = true;
            return;
        }

        bool wasInteracting = isDrawing || isWiring || isMoving || isResizing || activeSlider != null;

        if (!ptr.Properties.IsRightButtonPressed)
        {
            if (isDrawing && startPoint.HasValue && currentPoint.HasValue)
            {
                Rect rect = GetRect(startPoint.Value, currentPoint.Value);
                if (rect.Width >= GridSpacing && rect.Height >= GridSpacing)
                {
                    var parent = GetContainerAt(startPoint.Value, components);

                    if (selectedTypeToPlace == "Fill")
                    {
                        if (parent == null || (parent.Type != "Base" && parent.Type != "Scroll") || !IsRectFullyInside(rect, parent.Rect) || IsOverlappingAny(rect, parent.Children))
                        {
                            isDrawing = false; startPoint = null; currentPoint = null;
                            canvas.Invalidate();
                            return;
                        }
                    }
                    else if (selectedTypeToPlace == "Scroll")
                    {
                        if (parent == null || parent.Type != "Base" || !IsRectFullyInside(rect, parent.Rect) || IsOverlappingAny(rect, parent.Children))
                        {
                            isDrawing = false; startPoint = null; currentPoint = null;
                            canvas.Invalidate();
                            return;
                        }
                    }

                    var newComp = new Component { Type = selectedTypeToPlace, Rect = rect };
                    if (parent != null && (parent.Type == "Base" || parent.Type == "Scroll") && selectedTypeToPlace == "Fill")
                    {
                        parent.Children.Add(newComp);
                    }
                    else if (parent != null && parent.Type == "Base" && selectedTypeToPlace == "Scroll")
                    {
                        string autoScrollName = "scroll0";
                        int suffix = 0;
                        while (variables.Any(v => v.Name.Equals(autoScrollName, StringComparison.OrdinalIgnoreCase)) || IsScrollNameTaken(autoScrollName, components))
                        {
                            suffix++;
                            autoScrollName = $"scroll{suffix}";
                        }

                        int nextId = variables.Count > 0 ? variables.Max(v => v.Id) + 1 : 1;
                        var paletteColors = new List<Color> { Colors.Red, Colors.Orange, Colors.Green, Colors.DodgerBlue, Colors.Violet, Colors.Yellow };
                        Color col = paletteColors[nextId % paletteColors.Count];

                        var newVar = new Variable
                        {
                            Id = nextId,
                            Name = autoScrollName,
                            IsList = true,
                            ListValues = new List<int>(), // Question 1: Start completely empty!
                            Type = "list",
                            Color = col,
                            Position = new Vector2(30, 180 + variables.Count * 30)
                        };
                        variables.Add(newVar);
                        newComp.BoundVariableId = newVar.Id;

                        parent.Children.Add(newComp);

                        if (!string.IsNullOrEmpty(selectedFile))
                        {
                            SaveVariablesToFile(selectedFile);
                        }
                    }
                    else components.Add(newComp);
                    selectedComponent = newComp;
                }
                isDrawing = false; startPoint = null; currentPoint = null;
            }
            if (isWiring && selectedComponent != null)
            {
                var v = GetVariableAt(ptr.Position);
                if (v != null)
                {
                    selectedComponent.BoundVariableId = v.Id;
                    if (selectedComponent.Type == "Scroll" && v.IsList)
                    {
                        SyncScrollChildren(selectedComponent, v);
                    }
                }
                isWiring = false;
            }
            isMoving = false; isResizing = false;
            canvas.Invalidate();
        }
        if (!ptr.Properties.IsLeftButtonPressed)
        {
            activeSlider = null;
        }

        if (wasInteracting)
        {
            if (!string.IsNullOrEmpty(selectedFile))
            {
                SaveVariablesToFile(selectedFile);
            }
        }
    }

    private Rect GetRect(Point p1, Point p2)
    {
        return new Rect(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Max(GridSpacing, Math.Abs(p1.X - p2.X)), Math.Max(GridSpacing, Math.Abs(p1.Y - p2.Y)));
    }

    private void CommitSelectFile()
    {
        if (!string.IsNullOrWhiteSpace(selectedFileText))
        {
            if (!string.IsNullOrEmpty(selectedFile))
            {
                SaveVariablesToFile(selectedFile);
            }

            string cleanName = selectedFileText.Trim();
            if (!cleanName.EndsWith(".prop", StringComparison.OrdinalIgnoreCase))
            {
                cleanName += ".prop";
            }
            selectedFile = cleanName;
            LoadVariablesFromFile(selectedFile);
            selectedFileText = Path.GetFileNameWithoutExtension(selectedFile);
        }
        canvas.Invalidate();
    }

    private void Grid_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        if (isEditingFileName)
        {
            char c = args.Character;
            if (!char.IsControl(c) && c != '/' && c != '\\' && c != ':' && c != '*' && c != '?' && c != '"' && c != '<' && c != '>' && c != '|')
            {
                selectedFileText += c;
                canvas.Invalidate();
                args.Handled = true;
            }
            return;
        }

        if (activeTextEdit != null)
        {
            char c = args.Character;
            if (!char.IsControl(c))
            {
                if (activeTextEdit.Type == "Fill")
                {
                    if (!char.IsDigit(c) && c != '-')
                    {
                        args.Handled = true;
                        return;
                    }
                }
                activeTextEdit.Text += c;
                if (activeTextEdit.Type == "Scroll")
                {
                    var v = GetVariable(activeTextEdit.BoundVariableId);
                    if (v != null)
                    {
                        v.Name = activeTextEdit.Text;
                    }
                }
                else
                {
                    var v = GetVariable(activeTextEdit.BoundVariableId);
                    if (v != null)
                    {
                        if (float.TryParse(activeTextEdit.Text, out float f)) v.Value = f;
                    }

                    var parentScroll = FindParentComponent(activeTextEdit, components);
                    if (parentScroll != null && parentScroll.Type == "Scroll")
                    {
                        var listVar = GetVariable(parentScroll.BoundVariableId);
                        if (listVar != null && listVar.IsList)
                        {
                            int childIndex = GetListChildIndex(parentScroll, activeTextEdit);
                            if (childIndex >= 0 && childIndex < listVar.ListValues.Count)
                            {
                                if (int.TryParse(activeTextEdit.Text, out int parsedVal))
                                {
                                    listVar.ListValues[childIndex] = parsedVal;
                                }
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(selectedFile))
                {
                    SaveVariablesToFile(selectedFile);
                }
                canvas.Invalidate();
                args.Handled = true;
            }
        }

        if (selectedVar != null && isEditingVarName)
        {
            char c = args.Character;
            if (!char.IsControl(c) && c != ' ' && c != '=' && c != ';')
            {
                selectedVar.Name += c;
                if (!string.IsNullOrEmpty(selectedFile))
                {
                    SaveVariablesToFile(selectedFile);
                }
                canvas.Invalidate();
                args.Handled = true;
            }
            return;
        }
    }

    private void Grid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (hasOverwriteConflict)
        {
            if (e.Key == Windows.System.VirtualKey.F)
            {
                SaveVariablesToFile(selectedFile, force: true);
                canvas.Invalidate();
                e.Handled = true;
                return;
            }
            else if (e.Key == Windows.System.VirtualKey.L)
            {
                LoadVariablesFromFile(selectedFile);
                canvas.Invalidate();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Windows.System.VirtualKey.R && !isEditingFileName && activeTextEdit == null && !isEditingVarName)
        {
            if (selectedComponent != null && selectedComponent.Type == "Scroll")
            {
                activeTextEdit = selectedComponent;
                var listVar = GetVariable(selectedComponent.BoundVariableId);
                if (listVar != null)
                {
                    selectedComponent.Text = listVar.Name;
                }
                canvas.Invalidate();
                e.Handled = true;
                return;
            }
            else if (selectedVar != null)
            {
                isEditingVarName = true;
                canvas.Invalidate();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Windows.System.VirtualKey.Space && !isEditingFileName && activeTextEdit == null)
        {
            isSpacePressed = true;
            e.Handled = true;
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Tab)
        {
            isSidebarFocused = !isSidebarFocused;
            canvas.Invalidate();
            e.Handled = true;
            return;
        }

        if (isEditingFileName)
        {
            if (e.Key == Windows.System.VirtualKey.Back)
            {
                if (selectedFileText.Length > 0)
                {
                    selectedFileText = selectedFileText.Substring(0, selectedFileText.Length - 1);
                    canvas.Invalidate();
                }
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Enter)
            {
                isEditingFileName = false;
                CommitSelectFile();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                isEditingFileName = false;
                selectedFileText = Path.GetFileNameWithoutExtension(selectedFile);
                canvas.Invalidate();
                e.Handled = true;
            }
            return;
        }

        if (activeTextEdit != null)
        {
            if (e.Key == Windows.System.VirtualKey.Back)
            {
                if (activeTextEdit.Text.Length > 0)
                {
                    activeTextEdit.Text = activeTextEdit.Text.Substring(0, activeTextEdit.Text.Length - 1);
                    if (activeTextEdit.Type == "Scroll")
                    {
                        var v = GetVariable(activeTextEdit.BoundVariableId);
                        if (v != null)
                        {
                            v.Name = activeTextEdit.Text;
                        }
                    }
                    else
                    {
                        var v = GetVariable(activeTextEdit.BoundVariableId);
                        if (v != null)
                        {
                            if (float.TryParse(activeTextEdit.Text, out float f)) v.Value = f;
                            else if (activeTextEdit.Text == "") v.Value = 0;
                        }

                        var parentScroll = FindParentComponent(activeTextEdit, components);
                        if (parentScroll != null && parentScroll.Type == "Scroll")
                        {
                            var listVar = GetVariable(parentScroll.BoundVariableId);
                            if (listVar != null && listVar.IsList)
                            {
                                int childIndex = GetListChildIndex(parentScroll, activeTextEdit);
                                if (childIndex >= 0 && childIndex < listVar.ListValues.Count)
                                {
                                    if (int.TryParse(activeTextEdit.Text, out int parsedVal))
                                    {
                                        listVar.ListValues[childIndex] = parsedVal;
                                    }
                                    else if (activeTextEdit.Text == "")
                                    {
                                        listVar.ListValues[childIndex] = 0;
                                    }
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(selectedFile))
                    {
                        SaveVariablesToFile(selectedFile);
                    }
                    canvas.Invalidate();
                }
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Enter || e.Key == Windows.System.VirtualKey.Escape)
            {
                activeTextEdit = null;
                canvas.Invalidate();
                e.Handled = true;
            }
            return;
        }

        if (selectedVar != null && isEditingVarName)
        {
            if (e.Key == Windows.System.VirtualKey.Back)
            {
                if (selectedVar.Name.Length > 0)
                {
                    selectedVar.Name = selectedVar.Name.Substring(0, selectedVar.Name.Length - 1);
                    if (!string.IsNullOrEmpty(selectedFile))
                    {
                        SaveVariablesToFile(selectedFile);
                    }
                    canvas.Invalidate();
                }
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Enter || e.Key == Windows.System.VirtualKey.Escape)
            {
                isEditingVarName = false;
                canvas.Invalidate();
                e.Handled = true;
            }
            return;
        }

        if (selectedComponent != null)
        {
            if (e.Key == Windows.System.VirtualKey.Back || e.Key == Windows.System.VirtualKey.Delete)
            {
                DeleteSelectedComponent();
                canvas.Invalidate();
                e.Handled = true;
                return;
            }

            float step = GridSpacing;
            Rect newRect = selectedComponent.Rect;

            if (e.Key == Windows.System.VirtualKey.Up)
            {
                newRect.Y -= step;
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down)
            {
                newRect.Y += step;
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Left)
            {
                newRect.X -= step;
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Right)
            {
                newRect.X += step;
                e.Handled = true;
            }

            if (e.Handled)
            {
                var parent = FindParentComponent(selectedComponent, components);
                if (parent != null && (parent.Type == "Base" || parent.Type == "Scroll"))
                {
                    double clampedX = Math.Max(parent.Rect.X, Math.Min(parent.Rect.Right - newRect.Width, newRect.X));
                    double clampedY = Math.Max(parent.Rect.Y, Math.Min(parent.Rect.Bottom - newRect.Height, newRect.Y));
                    newRect.X = clampedX;
                    newRect.Y = clampedY;
                }

                selectedComponent.Rect = newRect;
                canvas.Invalidate();
                return;
            }
        }

        if (e.Key == Windows.System.VirtualKey.Subtract || (int)e.Key == 189)
        {
            if (selectedComponent != null && selectedComponent.Type == "Fill")
            {
                selectedComponent.EditType = "None";
                if (activeTextEdit == selectedComponent)
                {
                    activeTextEdit = null;
                }
                if (activeSlider == selectedComponent) activeSlider = null;
                canvas.Invalidate();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Windows.System.VirtualKey.S)
        {
            if (!string.IsNullOrEmpty(selectedFile))
            {
                SaveVariablesToFile(selectedFile);
            }
            e.Handled = true;
        }
    }

    private void Grid_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Space)
        {
            isSpacePressed = false;
            e.Handled = true;
        }
    }

    private bool DeleteComponent(Component target, List<Component> list)
    {
        if (list.Contains(target))
        {
            list.Remove(target);
            return true;
        }
        foreach (var comp in list)
        {
            if (DeleteComponent(target, comp.Children)) return true;
        }
        return false;
    }

    private void DeleteSelectedComponent()
    {
        if (selectedComponent != null)
        {
            // Parent scroll list index sync
            var parentScroll = FindParentComponent(selectedComponent, components);
            if (parentScroll != null && parentScroll.Type == "Scroll")
            {
                var listVar = GetVariable(parentScroll.BoundVariableId);
                if (listVar != null && listVar.IsList)
                {
                    int childIndex = GetListChildIndex(parentScroll, selectedComponent);
                    if (childIndex >= 0 && childIndex < listVar.ListValues.Count)
                    {
                        listVar.ListValues.RemoveAt(childIndex);
                    }
                }
            }

            DeleteComponent(selectedComponent, components);
            if (activeTextEdit == selectedComponent)
            {
                activeTextEdit = null;
            }
            if (activeSlider == selectedComponent) activeSlider = null;
            selectedComponent = null;

            if (!string.IsNullOrEmpty(selectedFile))
            {
                SaveVariablesToFile(selectedFile);
            }
            canvas.Invalidate();
        }
    }

    private Component FindParentComponent(Component child, List<Component> list)
    {
        foreach (var comp in list)
        {
            if (comp.Children.Contains(child)) return comp;
            var found = FindParentComponent(child, comp.Children);
            if (found != null) return found;
        }
        return null;
    }


    private Component GetContainerAt(Point p, List<Component> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var comp = list[i];
            if (comp.Type == "Scroll" && comp.Rect.Contains(p))
            {
                Point childP = new Point(p.X, p.Y + comp.ScrollOffset * 40.0);
                var innerContainer = GetContainerAt(childP, comp.Children);
                if (innerContainer != null) return innerContainer;
                return comp;
            }
            if (comp.Type == "Base" && comp.Rect.Contains(p))
            {
                var innerContainer = GetContainerAt(p, comp.Children);
                if (innerContainer != null) return innerContainer;
                return comp;
            }
        }
        return null;
    }

    private bool IsRectFullyInside(Rect inner, Rect outer)
    {
        return inner.X >= outer.X &&
               inner.Y >= outer.Y &&
               inner.Right <= outer.Right &&
               inner.Bottom <= outer.Bottom;
    }

    private bool IsOverlappingAny(Rect rect, List<Component> siblings, Component self = null)
    {
        foreach (var sibling in siblings)
        {
            if (sibling == self) continue;
            if (rect.X < sibling.Rect.Right &&
                rect.Right > sibling.Rect.X &&
                rect.Y < sibling.Rect.Bottom &&
                rect.Bottom > sibling.Rect.Y)
            {
                return true;
            }
        }
        return false;
    }

    private int GetListChildIndex(Component parentScroll, Component comp)
    {
        if (parentScroll == null || comp == null) return -1;
        var listItemsOnly = parentScroll.Children.Where(c => c.BoundVariableId == parentScroll.BoundVariableId).ToList();
        listItemsOnly = listItemsOnly
            .OrderBy(c => ((c.Rect.Y + c.Rect.Bottom) / 2.0) - parentScroll.Rect.Y)
            .ThenBy(c => ((c.Rect.X + c.Rect.Right) / 2.0) - parentScroll.Rect.X)
            .ToList();
        return listItemsOnly.IndexOf(comp);
    }
}

public class Variable
{
    public int Id { get; set; }
    public string Name { get; set; }
    public float Value { get; set; }
    public Color Color { get; set; }
    public Vector2 Position { get; set; }
    public bool IsList { get; set; } = false;
    public List<int> ListValues { get; set; } = new List<int>();
    public string Type { get; set; } = "int"; // "int", "float", "list"
    public bool IsExpanded { get; set; } = false;
}

public class SidebarTarget
{
    public Rect Rect;
    public string Type; // "File", "Variable", "AddVar", "AddList", "DeleteVar", "Save", "ListItem", "AddListItem"
    public string FileName;
    public Variable Var;
    public int ListIndex = -1;
}

public class SliderPlusTarget
{
    public Rect Rect;
    public Component Slider;
    public int InsertIndex;
}

public class Component
{
    public Rect Rect;
    public string Type;
    public string EditType = "None";
    public string Text = "Value";
    public float SliderValue = 0.5f;
    public int BoundVariableId = 0;
    public List<Component> Children = new List<Component>();
    public double ScrollOffset { get; set; } = 0.0;
}

public class PaletteFolder
{
    public string Name;
    public bool IsExpanded = true;
    public List<string> Items = new List<string>();
}

public class PaletteTarget
{
    public Rect Rect;
    public string Type; // "Folder", "Item", "Select"
    public string Name; // Name of item/folder
    public PaletteFolder Folder; // Folder reference
}
